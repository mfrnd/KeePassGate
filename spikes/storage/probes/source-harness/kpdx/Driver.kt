// Drives KeePassDX 4.5.8's database module as the app's `app` module does, from a script of steps.
// Written for this harness. The app module is not compiled: each step below restates the body of
// the runnable or the screen it names, calling the database module's own code; what a step cannot
// stand for (a dialog, a setting, when the app reads or writes) is left to the script.
//
//   harness.cmd <script> [<script> ...]     run each script file
//   harness.cmd batch <jobs> [<from>]       run every job from job number <from> (0-based); a job
//                                           that does not end in KPDX_JOB_TIMEOUT seconds of its
//                                           own processor time (120 by default) is named, `RESUME <n>` printed, and the
//                                           process ends with code 3 (common.run_kpdx resumes)
//   harness.cmd batch <jobs>                run every job of a jobs file ("job <name>" ... "end")
//
// The script language is the one k2a\Driver.cs reads:
//   password <text>                 the password for every later open and merge (default the bases')
//   open <file>                     LoadDatabaseRunnable: clearAndClose, then loadData(stream,
//                                   MasterCredential(password), ..., readOnly false,
//                                   fixDuplicateUUID false, limits that refuse nothing)
//   view <entry>                    TouchEntryRunnable, which EntryActivity starts when an entry is
//                                   shown: touch(modified = false, touchParents = true)
//   view-group <group>              TouchGroupRunnable, which GroupActivity starts when a group is
//                                   shown, the root among them right after the database opens
//   edit <entry> <field> append|set <text>
//                                   EntryEditViewModel and TemplateEditView: the EntryInfo
//                                   buildEntryInfoFrom makes (getEntryInfoById, raw), one field
//                                   changed, the expiry read back from the expiry view (never
//                                   expires when the box is not checked); then UpdateEntryRunnable
//   add <group> <title>             buildNewEntryInfo, the same screen, then AddEntryRunnable
//   rename-group <group> <suffix>   GroupEditDialogFragment.retrieveGroupInfoFromViews with the name
//                                   changed, then UpdateGroupRunnable
//   group-notes <group> append|set <text>   the same dialog with the notes changed (KeePassDX only)
//   recycle <entry> | purge <entry> DeleteNodesRunnable for the entry: to the bin when it can be
//                                   recycled, deleted with a deletion record when it cannot (inside
//                                   the bin); the two names are the same step
//   empty-bin                       GroupViewModel.emptyRecycleBin: DeleteNodesRunnable for every
//                                   group and entry in the bin
//   move-entry <entry> <group> | move-group <group> <group>   MoveNodesRunnable
//   save <out>                      SaveDatabaseRunnable: checkVersion, then saveData through a
//                                   cache file, into <out>
//   merge <file> <out>              MergeDatabaseRunnable: mergeData from <file> with its own
//                                   credentials (the password), then the save, into <out>
//   exec <program> | <arg> | ...    runs another program and waits for it
// An <entry> or <group> is a probe alias (the 'KPG'-prefixed identifiers of the bases), a title
// prefixed by '=', 32 hexadecimal digits, or Root.
package sourceharness.kpdx

import com.kunzisoft.keepass.database.crypto.kdf.Limits
import com.kunzisoft.keepass.database.element.DateInstant
import com.kunzisoft.keepass.database.element.Entry
import com.kunzisoft.keepass.database.element.Field
import com.kunzisoft.keepass.database.element.Group
import com.kunzisoft.keepass.database.element.MasterCredential
import com.kunzisoft.keepass.database.element.security.ProtectedString
import com.kunzisoft.keepass.database.element.icon.IconImage
import com.kunzisoft.keepass.database.element.icon.IconImageStandard
import com.kunzisoft.keepass.hardware.HardwareKey
import com.kunzisoft.keepass.model.DatabaseInfo
import com.kunzisoft.keepass.model.EntryInfo
import com.kunzisoft.keepass.model.GroupInfo
import com.kunzisoft.keepass.utils.bytes16ToUuid
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.nio.file.Files
import java.util.UUID

const val DEFAULT_PASSWORD = "dummy-password-for-a-throwaway-file"
val JOB_TIMEOUT_S: Long = System.getenv("KPDX_JOB_TIMEOUT")?.toLongOrNull() ?: 120L

class Session {
    var db: DatabaseInfo? = null
    var password: String = DEFAULT_PASSWORD
    val cacheDir: File = Files.createTempDirectory("kpdx-harness").toFile()
}

// The app's Limits answer whether the device has memory to spare; the harness's always have.
val limits = Limits(isMemorySufficient = { _, _ -> true }, parallelism = Runtime.getRuntime().availableProcessors().toLong())
val noHardwareKey: (HardwareKey, ByteArray?) -> ByteArray = { _, _ -> throw IllegalStateException("no hardware key in the harness") }

fun main(args: Array<String>) {
    if (args.isEmpty()) {
        System.err.println("harness.cmd <script>... | batch <jobs>")
        System.exit(2)
    }
    val t0 = System.currentTimeMillis()
    val jobs: List<Pair<String, List<String>>> =
        if (args[0] == "batch") readJobs(File(args[1]).readLines(Charsets.UTF_8))
        else args.map { it to File(it).readLines(Charsets.UTF_8) }
    // batch <jobs> <from>: start at job number <from> (0-based), as a resumed run does.
    val from = if (args[0] == "batch" && args.size > 2) args[2].toInt() else 0
    var failed = 0
    for ((index, job) in jobs.withIndex()) {
        if (index < from) continue
        val (name, lines) = job
        // Every job runs on a thread of its own and is given JOB_TIMEOUT_S seconds of that
        // thread's own processor time: a merge was seen not to end
        // (DatabaseKDBXMerger.firstNotDeletedParent), and that is a result to report, not a
        // reason to lose the batch. Processor time and not the clock, since a job waiting on a
        // slow disk is not a job that does not end. A job that does not end is named, with where
        // it was, and the process ends with code 3 and the number of the next job, so that the
        // caller starts a fresh JVM there: a thread cannot be stopped without leaving the
        // module's state behind it unsound.
        var error: Throwable? = null
        val t = Thread { try { run(lines) } catch (e: Throwable) { error = e } }
        t.isDaemon = true
        t.start()
        val cpu = java.lang.management.ManagementFactory.getThreadMXBean()
        while (t.isAlive && cpu.getThreadCpuTime(t.id) < JOB_TIMEOUT_S * 1_000_000_000L) t.join(500)
        if (t.isAlive) {
            val where = t.stackTrace.take(3).joinToString(" < ") { "${it.className.substringAfterLast('.')}.${it.methodName}:${it.lineNumber}" }
            println("FAILED $name: TIMEOUT: did not end in $JOB_TIMEOUT_S s of processor time, at $where")
            println("RESUME ${index + 1}")
            System.out.flush()
            System.err.println("kpdx: stopped after job ${index + 1} of ${jobs.size}, ${System.currentTimeMillis() - t0} ms")
            Runtime.getRuntime().halt(3)
        }
        error?.let { e ->
            failed++
            // The exception and every cause under it, since the module wraps the reader's reason.
            val chain = generateSequence(e) { it.cause }.take(4)
                .joinToString(" <- ") { "${it.javaClass.simpleName}: ${(it.message ?: "").take(160)}" }
            println("FAILED $name: ${chain.replace('\n', ' ').replace('\r', ' ')}")
            if (System.getenv("KPDX_LOG") != null) e.printStackTrace()
        }
    }
    System.out.flush()
    System.err.println("kpdx: ${jobs.size} jobs, $failed failed, ${System.currentTimeMillis() - t0} ms")
    System.exit(if (failed == 0) 0 else 1)
}

fun readJobs(lines: List<String>): List<Pair<String, List<String>>> {
    val out = mutableListOf<Pair<String, List<String>>>()
    var cur = mutableListOf<String>()
    var name: String? = null
    for (raw in lines) {
        val l = raw.trim()
        if (l.startsWith("job ")) { name = l.substring(4); cur = mutableListOf(); continue }
        if (l == "end") { out.add((name ?: "?") to cur); cur = mutableListOf(); name = null; continue }
        cur.add(l)
    }
    if (cur.isNotEmpty()) out.add((name ?: "?") to cur)
    return out
}

// The line split into at most n parts at spaces, the last part keeping its spaces.
fun split(l: String, n: Int): List<String> {
    val parts = mutableListOf<String>()
    var rest = l.trim()
    while (parts.size < n - 1) {
        val i = rest.indexOf(' ')
        if (i < 0) break
        parts.add(rest.substring(0, i))
        rest = rest.substring(i + 1).trimStart()
    }
    if (rest.isNotEmpty()) parts.add(rest)
    return parts
}

fun run(lines: List<String>) {
    val s = Session()
    try {
        for (raw in lines) {
            val l = raw.trim()
            if (l.isEmpty() || l.startsWith("#")) continue
            when (l.split(' ')[0]) {
                "password" -> s.password = split(l, 2).getOrElse(1) { "" }
                "open" -> open(s, split(l, 2)[1])
                "view" -> split(l, 2).let { p -> val e = entry(s, p[1]); e.touch(modified = false, touchParents = true); db(s).updateEntry(e) }
                "view-group" -> split(l, 2).let { p -> val g = group(s, p[1]); g.touch(modified = false, touchParents = true); db(s).updateGroup(g) }
                "edit" -> split(l, 5).let { p -> edit(s, entry(s, p[1]), p[2], p[3], p.getOrElse(4) { "" }) }
                "add" -> split(l, 3).let { p -> add(s, group(s, p[1]), p[2]) }
                "rename-group" -> split(l, 3).let { p -> editGroup(s, group(s, p[1]), "title", "append", p[2]) }
                "group-notes" -> split(l, 4).let { p -> editGroup(s, group(s, p[1]), "notes", p[2], p.getOrElse(3) { "" }) }
                "recycle", "purge" -> split(l, 2).let { p -> delete(s, listOf(), listOf(entry(s, p[1]))) }
                "empty-bin" -> db(s).recycleBin?.let { bin -> delete(s, bin.getChildGroups(), bin.getChildEntries()) }
                "move-entry" -> split(l, 3).let { p -> move(s, group(s, p[2]), listOf(), listOf(entry(s, p[1]))) }
                "move-group" -> split(l, 3).let { p -> move(s, group(s, p[2]), listOf(group(s, p[1])), listOf()) }
                "save" -> save(s, split(l, 2)[1])
                "merge" -> split(l, 3).let { p -> merge(s, p[1]); save(s, p[2]) }
                "exec" -> exec(l.substring(5))
                else -> throw IllegalArgumentException("unknown step: $l")
            }
        }
    } finally {
        s.db?.clearAndClose(s.cacheDir)
        s.cacheDir.deleteRecursively()
    }
}

fun db(s: Session): DatabaseInfo = s.db ?: throw IllegalStateException("no database open")

fun credential(s: Session) = MasterCredential(s.password.toCharArray())

// LoadDatabaseRunnable.onActionRun.
fun open(s: Session, file: String) {
    s.db?.clearAndClose(s.cacheDir)
    val database = DatabaseInfo()
    database.clearAndClose(s.cacheDir)
    database.loadData(
        databaseStream = FileInputStream(file),
        masterCredential = credential(s),
        challengeResponseRetriever = noHardwareKey,
        readOnly = false,
        allowUserVerification = false,
        cacheDirectory = s.cacheDir,
        limits = limits,
        fixDuplicateUUID = false,
        progressTaskUpdater = null
    )
    s.db = database
}

// SaveDatabaseRunnable.onActionRun, save = true, no new credential, no copy.
fun save(s: Session, out: String) {
    val database = db(s)
    database.checkVersion()
    File(out).absoluteFile.parentFile.mkdirs()
    database.saveData(
        cacheFile = File.createTempFile("db_", ".tmp", s.cacheDir),
        databaseOutputStream = { FileOutputStream(out) },
        masterCredential = null,
        challengeResponseRetriever = noHardwareKey,
        limits = limits
    )
}

// MergeDatabaseRunnable.onActionRun, before its save.
fun merge(s: Session, file: String) {
    db(s).mergeData(
        databaseToMergeStream = FileInputStream(file),
        databaseToMergeMasterCredential = credential(s),
        databaseToMergeChallengeResponseRetriever = noHardwareKey,
        limits = limits,
        progressTaskUpdater = null
    )
}

fun exec(rest: String) {
    val parts = rest.split('|').map { it.trim() }.filter { it.isNotEmpty() }
    val p = ProcessBuilder(parts).inheritIO().start()
    val code = p.waitFor()
    if (code != 0) throw IllegalStateException("exec exited $code: $rest")
}

// ---------------------------------------------------------------------- finding nodes

fun aliasUuid(alias: String): UUID {
    if (alias.length == 32 && alias.all { it.isDigit() || it.lowercaseChar() in 'a'..'f' }) {
        return bytes16ToUuid(alias.chunked(2).map { it.toInt(16).toByte() }.toByteArray())
    }
    val b = ByteArray(16)
    b[0] = 0x4b; b[1] = 0x50; b[2] = 0x47
    alias.toByteArray(Charsets.US_ASCII).copyInto(b, 3)
    // As the reader turns a node's 16 bytes into a UUID.
    return bytes16ToUuid(b)
}

fun allGroups(g: Group): List<Group> = listOf(g) + g.getChildGroups().flatMap { allGroups(it) }

fun entry(s: Session, ref: String): Entry {
    val root = db(s).rootGroup ?: throw IllegalStateException("no root")
    val all = allGroups(root).flatMap { it.getChildEntries() }
    return if (ref.startsWith("=")) all.first { it.title == ref.substring(1) }
    else { val id = aliasUuid(ref); all.first { it.nodeId.id == id } }
}

fun group(s: Session, ref: String): Group {
    val root = db(s).rootGroup ?: throw IllegalStateException("no root")
    if (ref == "Root" || ref == "=") return root
    val all = allGroups(root)
    return if (ref.startsWith("=")) all.first { it.title == ref.substring(1) }
    else { val id = aliasUuid(ref); all.first { it.nodeId.id == id } }
}

// ---------------------------------------------------------------------- the app's operations

fun changed(old: String, how: String, text: String) = if (how == "append") old + text else text

// The expiry the edit screen hands back: DateTimeEditFieldView answers its default, never
// expires, when its box is not checked.
fun EntryInfo.expiryFromScreen() { if (!expires) expiryTime = DateInstant.NEVER_EXPIRES }

// EntryEditViewModel (buildEntryInfoFrom) and the edit screen, then UpdateEntryRunnable.
fun edit(s: Session, oldEntry: Entry, field: String, how: String, text: String) {
    val database = db(s)
    val info = database.getEntryInfoById(oldEntry.nodeId, raw = true) ?: throw IllegalStateException("no entry info")
    when (field) {
        "Title" -> info.title = changed(info.title, how, text)
        "UserName" -> info.username = changed(info.username, how, text)
        "Password" -> info.password = changed(String(info.password), how, text).toCharArray()
        "URL" -> info.url = changed(info.url, how, text)
        "Notes" -> info.notes = changed(info.notes, how, text)
        else -> {
            val i = info.customFields.indexOfFirst { it.name == field }
            if (i >= 0) {
                val f = info.customFields[i]
                info.customFields[i] = Field(f.name, ProtectedString(f.protectedValue.isProtected, changed(f.protectedValue.toString(), how, text)))
            } else {
                info.customFields.add(Field(field, ProtectedString(false, text)))
            }
        }
    }
    info.expiryFromScreen()
    updateEntry(database, oldEntry, info)
}

// UpdateEntryRunnable: init and nodeAction.
fun updateEntry(database: DatabaseInfo, oldEntry: Entry, newEntryInfo: EntryInfo) {
    val newEntry = database.updateEntry(Entry(oldEntry), newEntryInfo)
    newEntry.addParentFrom(oldEntry)
    newEntry.clearHistory()
    oldEntry.getHistory().forEach { newEntry.addEntryToHistory(it) }
    val oldEntryAttachments = oldEntry.getAttachments(database.attachmentPool, true)
    val newEntryAttachments = newEntry.getAttachments(database.attachmentPool, true)
    val attachmentsToRemove = oldEntryAttachments.toMutableList()
    newEntryAttachments.forEach { newAttachment ->
        oldEntryAttachments.forEach { oldAttachment ->
            if (oldAttachment.name == newAttachment.name && oldAttachment.binaryData == newAttachment.binaryData)
                attachmentsToRemove.remove(oldAttachment)
        }
    }
    newEntry.touch(modified = true, touchParents = true)
    newEntry.addEntryToHistory(Entry(oldEntry, copyHistory = false))
    database.removeOldestEntryHistory(newEntry, database.attachmentPool)
    database.updateEntry(newEntry)
    attachmentsToRemove.forEach { database.removeAttachmentIfNotUsed(it) }
}

// ContextualDatabase.buildNewEntryInfo, the edit screen with the title typed, AddEntryRunnable.
fun add(s: Session, parent: Group, title: String) {
    val database = db(s)
    val entry = database.createEntry()?.apply {
        val parentIcon = parent.icon
        if (parentIcon.custom.isUnknown && parentIcon.standard.id != IconImageStandard.FOLDER_ID) {
            icon = IconImage(parentIcon.standard)
        }
        if (!parentIcon.custom.isUnknown) {
            icon = IconImage(parentIcon.custom)
        }
        username = database.defaultUsername
    } ?: throw IllegalStateException("createEntry failed")
    val info = database.getEntryInfoFrom(entry = entry, raw = true)
    info.title = title
    info.expiryFromScreen()
    val newEntry = database.createEntry(info) ?: throw IllegalStateException("createEntry(info) failed")
    newEntry.touch(modified = true, touchParents = true)
    parent.touch(modified = true, touchParents = true)
    database.addEntryTo(newEntry, parent)
}

// GroupEditDialogFragment.retrieveGroupInfoFromViews, then UpdateGroupRunnable.
fun editGroup(s: Session, oldGroup: Group, field: String, how: String, text: String) {
    val database = db(s)
    val info: GroupInfo = database.getGroupInfoFrom(oldGroup)
    // The dialog's views give back what they were given, the notes as text and the expiry as the
    // expiry view answers it.
    info.notes = info.notes ?: ""
    if (!info.expires) info.expiryTime = DateInstant.NEVER_EXPIRES
    when (field) {
        "title" -> info.title = changed(info.title, how, text)
        "notes" -> info.notes = changed(info.notes ?: "", how, text)
    }
    val newGroup = database.updateGroup(Group(oldGroup), info)
    if (oldGroup.nodeId == newGroup.nodeId) {
        newGroup.addParentFrom(oldGroup)
        newGroup.addChildrenFrom(oldGroup)
        newGroup.touch(modified = true, touchParents = true)
        if (database.rootGroup == oldGroup) database.rootGroup = newGroup
        database.updateGroup(newGroup)
    }
}

// DeleteNodesRunnable.nodeAction. The bin's title is used only when the bin has to be made.
fun delete(s: Session, groups: List<Group>, entries: List<Entry>) {
    val database = db(s)
    val recyclerBinTitle = "Recycle bin"
    for (nodeToDelete in groups.toList()) {
        val oldParent = nodeToDelete.parent
        nodeToDelete.touch(modified = true, touchParents = true)
        if (database.canRecycle(nodeToDelete)) {
            database.recycle(nodeToDelete, recyclerBinTitle)
            nodeToDelete.setPreviousParentGroup(oldParent)
            nodeToDelete.touch(modified = true, touchParents = true)
        } else {
            database.deleteGroup(nodeToDelete)
        }
    }
    for (nodeToDelete in entries.toList()) {
        val oldParent = nodeToDelete.parent
        nodeToDelete.touch(modified = true, touchParents = true)
        if (database.canRecycle(nodeToDelete)) {
            database.recycle(nodeToDelete, recyclerBinTitle)
            nodeToDelete.setPreviousParentGroup(oldParent)
            nodeToDelete.touch(modified = true, touchParents = true)
        } else {
            database.deleteEntry(nodeToDelete)
        }
        nodeToDelete.getAttachments(database.attachmentPool).forEach { database.removeAttachmentIfNotUsed(it) }
    }
}

// MoveNodesRunnable.nodeAction.
fun move(s: Session, newParent: Group, groups: List<Group>, entries: List<Entry>) {
    val database = db(s)
    for (nodeToMove in groups) {
        val oldParent = nodeToMove.parent ?: continue
        nodeToMove.touch(modified = true, touchParents = true)
        if (oldParent != newParent && nodeToMove != newParent && !newParent.isContainedIn(nodeToMove)) {
            database.moveGroupTo(nodeToMove, newParent)
            nodeToMove.setPreviousParentGroup(oldParent)
            nodeToMove.touch(modified = true, touchParents = true)
        } else throw IllegalStateException("MoveGroupDatabaseException")
    }
    for (nodeToMove in entries) {
        val oldParent = nodeToMove.parent ?: continue
        nodeToMove.touch(modified = true, touchParents = true)
        if (oldParent != newParent && (newParent != database.rootGroup || database.rootCanContainsEntry())) {
            database.moveEntryTo(nodeToMove, newParent)
            nodeToMove.setPreviousParentGroup(oldParent)
            nodeToMove.touch(modified = true, touchParents = true)
        } else throw IllegalStateException("MoveEntryDatabaseException")
    }
}
