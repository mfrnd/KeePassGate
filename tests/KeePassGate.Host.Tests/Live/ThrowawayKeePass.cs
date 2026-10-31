using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A complete, disposable KeePass: its own copy of the program, its own configuration, its own
    /// database, and the plugin under test in its plugins directory.
    /// </summary>
    /// <remarks>
    /// Nothing here touches the developer's KeePass or the developer's database. The copy exists
    /// so that the test cannot inherit a language, a policy or a setting from a real installation,
    /// and so that a failed run leaves nothing behind but one directory under
    /// `CommonApplicationData`. Not `%TEMP%`, which carries the account name: the pictures the
    /// documentation is built from show the open database's path, so where the session lives is
    /// what makes them publishable.
    /// </remarks>
    internal sealed class ThrowawayKeePass : IDisposable
    {
        /// <summary>Invented, and only ever passed on the command line of a throwaway database.</summary>
        public const string DatabasePassword = "not-a-real-password-9c1f";

        private static readonly string[] RequiredFiles =
        {
            "KeePass.exe",
            "KeePass.exe.config"
        };

        /// <summary>Present in a normal installation, and KeePass starts without any of them.</summary>
        private static readonly string[] OptionalFiles =
        {
            "KeePass.XmlSerializers.dll",
            "KeePassLibN.x32.dll",
            "KeePassLibN.x64.dll",
            "KeePassLibN.a64.dll"
        };

        private readonly string _root;
        private int _port;
        private TcpListener _reservation;
        // codeql[cs/missed-using-statement] a field, cleared as it is disposed so that nothing reads it disposed
        private Process _process;

        private ThrowawayKeePass(string root, TcpListener reservation)
        {
            _root = root;
            _reservation = reservation;
            _port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        }

        /// <summary>
        /// The port this KeePass is told to listen on: the documented one when nothing holds it,
        /// and anything free when something does, so that what is published does not change for a
        /// reason nobody chose and a test still never collides with a real instance.
        /// </summary>
        public int Port
        {
            get { return _port; }
        }

        /// <summary>
        /// From the next start on, the port the plugin listens on when nobody chose another, or
        /// anything free when something holds it.
        /// </summary>
        /// <remarks>
        /// For the moving picture on the README, where the command typed is the one a reader
        /// types, and a reader whose KeePass nobody reconfigured leaves `--port` out. The stills
        /// and the examples in `docs/` keep <see cref="Documented"/>.
        /// </remarks>
        public void UseTheDefaultPort()
        {
            DropReservation();
            _reservation = Reserve(KeePassGate.Transport.GateOptions.DefaultPort);
            _port = ((IPEndPoint)_reservation.LocalEndpoint).Port;
        }

        public string ExePath
        {
            get { return Paths.Join(_root, "KeePass.exe"); }
        }

        public string DatabasePath
        {
            get { return Paths.Join(_root, "throwaway.kdbx"); }
        }

        /// <summary>
        /// Where the plugin under test writes its audit, which is inside the installation that
        /// `Dispose` deletes.
        /// </summary>
        /// <remarks>
        /// Named, rather than left to `JsonLinesAudit.DefaultPath`. That default is this user's own
        /// application data, so a fixture that says nothing has every end to end run appending its
        /// invented databases to the audit of whoever ran the tests, accumulating across runs with
        /// nothing to clean it up. `KEEPASSGATE_AUDIT_FILE` exists for exactly this and was read by
        /// nothing.
        /// </remarks>
        public string AuditPath
        {
            get { return Paths.Join(_root, "audit.jsonl"); }
        }

        public string PluginLogPath
        {
            get { return Paths.Join(_root, "keepassgate.log"); }
        }

        /// <summary>
        /// Where each KeePass of this installation that had to be ended rather than closing is
        /// written down, with what it was showing then.
        /// </summary>
        public string EndedPath
        {
            get { return Paths.Join(_root, "ended.txt"); }
        }

        public Process Process
        {
            get { return _process; }
        }

        /// <summary>
        /// Builds the installation. The KeePass to copy is the one this test assembly was built
        /// against, so the live test and the in process tests can never drift onto two builds.
        /// </summary>
        public static ThrowawayKeePass Create(string workingDirectory)
        {
            return Create(workingDirectory, false);
        }

        /// <summary>
        /// Builds the installation, optionally with an authority already in the database, which is
        /// what a test needs when it is the gate it wants to reach rather than the tab.
        /// </summary>
        public static ThrowawayKeePass Create(string workingDirectory, bool withAuthority)
        {
            return Create(workingDirectory, withAuthority, 0);
        }

        /// <summary>
        /// The same, locking after <paramref name="lockAfterSeconds"/> of KeePass inactivity, or
        /// never for zero, which is what every other session runs with.
        /// </summary>
        public static ThrowawayKeePass Create(string workingDirectory, bool withAuthority, int lockAfterSeconds)
        {
            // No identifier here: the working directory this sits in is already one per session.
            var root = Paths.Join(workingDirectory, "keepass");
            Directory.CreateDirectory(root);

            var source = KeePassDev.Directory;
            foreach (var name in RequiredFiles)
            {
                var from = Paths.Join(source, name);
                if (!File.Exists(from))
                {
                    throw new FileNotFoundException("The test copy of KeePass is incomplete.", from);
                }

                File.Copy(from, Paths.Join(root, name));
            }

            foreach (var name in OptionalFiles)
            {
                var from = Paths.Join(source, name);
                if (File.Exists(from))
                {
                    File.Copy(from, Paths.Join(root, name));
                }
            }

            WriteConfiguration(root, lockAfterSeconds);

            // Given back if anything below throws. Nothing else holds the instance until it is
            // returned, so a failure here left the reserved port bound until a collection found
            // the socket, and the next fixture in the same run quietly took another port.
            // codeql[cs/missed-using-statement] disposed only when it is not kept, which no `using` can do
            var installation = new ThrowawayKeePass(root, Reserve());
            var built = false;
            try
            {
                installation.InstallPlugin();
                installation.CreateDatabase(withAuthority);
                built = true;
                return installation;
            }
            finally
            {
                if (!built)
                {
                    installation.Dispose();
                }
            }
        }

        public Process Start()
        {
            var info = new ProcessStartInfo
            {
                FileName = ExePath,
                // The password reaches the process list here. That is only ever acceptable because
                // this database is invented, empty and deleted at the end of the test.
                Arguments = "\"" + DatabasePath + "\" -pw:" + DatabasePassword,
                WorkingDirectory = _root,
                UseShellExecute = false
            };
            info.EnvironmentVariables[TraceGateLog.LogFileVariable] = PluginLogPath;
            info.EnvironmentVariables[KeePassGate.Audit.JsonLinesAudit.AuditFileVariable] = AuditPath;
            info.EnvironmentVariables[KeePassGate.Transport.GateOptions.PortVariable] =
                _port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // The one before it is dealt with first. This fixture is started twice, once to
            // create the authority and once to read it back after a restart, and simply
            // overwriting the field orphaned the first KeePass whenever it had failed to
            // exit. That orphan holds the database and the copied KeePass open, so both
            // deletes in `Dispose` fail, and its window then swallows the keystrokes the
            // next fixture sends: one real failure turned into a run of unrelated ones.
            Release();

            // The port goes back a moment before the process that wants it is launched, rather
            // than when it was chosen. Nothing else on the machine gets a window to take it.
            DropReservation();

            _process = Process.Start(info);
            return _process;
        }

        /// <summary>Gives the reserved port up. Safe to call when there is nothing to give up.</summary>
        private void DropReservation()
        {
            var reserved = _reservation;
            _reservation = null;

            if (reserved == null)
            {
                return;
            }

            try
            {
                reserved.Stop();
            }
            catch (SocketException)
            {
                // Being given up either way: the listener is never used again, and a port that
                // stayed taken is reported by the gate when it tries to listen on it.
            }
        }

        public string ReadPluginLog()
        {
            if (!File.Exists(PluginLogPath))
            {
                return string.Empty;
            }

            using (var stream = new FileStream(PluginLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Kills whatever this fixture last started, and lets go of it.
        /// </summary>
        /// <remarks>
        /// Its own method because `Start` needs it as much as `Dispose` does: the field
        /// holds one process, and starting a second without ending the first leaves a
        /// KeePass nothing will ever close.
        /// </remarks>
        private void Release()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    WriteDownWhatItShows();
                    _process.Kill();
                    _process.WaitForExit(10000);
                }
            }
            catch (Exception error) when (
                error is InvalidOperationException || error is Win32Exception)
            {
                // Already gone, or going. Either way there is nothing left to kill.
            }
            finally
            {
                if (_process != null)
                {
                    _process.Dispose();
                    _process = null;
                }
            }
        }

        /// <summary>
        /// Writes down which windows a KeePass about to be ended is showing, and whether it still
        /// answers, beside the plugin's log.
        /// </summary>
        /// <remarks>
        /// **The last moment any of it can be seen.** A KeePass that did not exit when it was asked
        /// to was showing something, a question, a message box or a locked workspace, and the kill
        /// takes all of that with it. A run once saw three fixtures in a row time out that way and
        /// nothing it kept said why. A fixture that failed keeps this file with its log, and
        /// `build.ps1` prints both; a KeePass a fixture ends on purpose between two sessions is
        /// written down too, which costs nothing when nothing failed.
        /// </remarks>
        private void WriteDownWhatItShows()
        {
            try
            {
                var answering = _process.Responding ? "answering" : "not answering";
                File.AppendAllText(
                    EndedPath,
                    DateTime.UtcNow.ToString("u", System.Globalization.CultureInfo.InvariantCulture) +
                    " ended while still running and " + answering + ", showing " +
                    NativeWindows.Titles(_process) + Environment.NewLine);
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is InvalidOperationException || error is Win32Exception)
            {
                // Evidence, and nothing waits on it. A KeePass that exits meanwhile answers no
                // longer, and a file that cannot be written has nothing to add to the failure.
            }
        }

        /// <summary>
        /// Writes a fixed authority and one fixed admitted client into the file.
        /// </summary>
        /// <remarks>
        /// **Written rather than minted, because a minted one is different every time.** The plugin
        /// signs with .NET's `CertificateRequest`, whose ECDSA picks a random k, so the same
        /// certificate made twice has different bytes and a different fingerprint. Any picture
        /// taken of this database shows that fingerprint and the dates beside it, so a
        /// database that mints its own is a database that changes the pictures on every
        /// regeneration.
        ///
        /// **Only before KeePass starts**, for the reason `Admit` gives below: a running KeePass
        /// serves the database it holds in memory and would overwrite this on its next save.
        /// </remarks>
        public void SeedAuthority()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                // Through `CustomDataStore`, not the dictionary underneath it, so that a seeded
                // database is written exactly the way the plugin writes one.
                var store = new CustomDataStore(database);
                string fingerprint;
                using (var authority = CertificateBlob.Load(SeededAuthority.Authority))
                {
                    fingerprint = CertificateDigest.Of(authority);
                }

                store.Set(
                    AuthorityKeys.AuthorityKey(fingerprint),
                    AuthorityDocument.Save(
                        SeededAuthority.Authority, SeededAuthority.Host, SeededAuthority.SniKey));

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>Writes the one admission record the pictures of the tab show, as its own step.</summary>
        /// <remarks>
        /// Separate from the authority, and written between two sessions rather than beside it,
        /// because the two are true at different moments. A database whose authority has just been
        /// made has admitted nobody, and the tab counts admission records: seeding both at once
        /// put a one under a picture captioned as the tab the instant the authority exists.
        ///
        /// Written rather than paired for, for the same reason the authority is: a real pairing
        /// stamps the moment it happened, and a picture of a date that moves is a picture that
        /// changes on every regeneration.
        ///
        /// **Only before KeePass starts**, like everything else here.
        /// </remarks>
        public void SeedAdmittedClient()
        {
            SeedAdmittedClient(DatabasePath, SeededAuthority.ClientKey, SeededAuthority.ClientRecord);
        }

        /// <summary>The same for any written down record, into one copy of the database.</summary>
        public void SeedAdmittedClient(string path, string key, string record)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), NewKey(), null);

                var store = new CustomDataStore(database);
                store.Set(key, record);

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Replaces the authority in one copy of the database with a written down one, as Recreate
        /// would with one it had minted, and answers with its digest.
        /// </summary>
        /// <remarks>
        /// Written rather than minted, for the reason <see cref="SeedAuthority"/> gives. What goes is
        /// what Recreate takes: the old authority retired, its document removed, and every
        /// admission record with it, through `AuthorityStore` so that the copy holds exactly what
        /// the plugin would have left.
        ///
        /// **Only before KeePass starts.**
        /// </remarks>
        public string SeedRecreatedAuthority(string path, string authority, string host, string sniKey)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), NewKey(), null);

                var store = new CustomDataStore(database);
                new AuthorityStore(store, NullGateLog.Instance).DeleteEverything();

                string fingerprint;
                using (var certificate = CertificateBlob.Load(authority))
                {
                    fingerprint = CertificateDigest.Of(certificate);
                }

                store.Set(AuthorityKeys.AuthorityKey(fingerprint), AuthorityDocument.Save(authority, host, sniKey));

                database.Save(null);
                return fingerprint;
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Writes one decision into this installation's audit, at a moment that does not move, as
        /// a request from the client with this admission key would have left it.
        /// </summary>
        /// <remarks>
        /// The authority chooser shows, beside each client, when the audit last wrote it down, and
        /// a real request writes the moment it happened: a picture of that is a picture that changes
        /// on every regeneration. So the line is written instead, through `JsonLinesAudit` and
        /// `AuditEntry`, which is how the gate writes one: a listing of the root group, permitted by
        /// the rule on it. Its client key is the admission key, as it is for a client that paired
        /// and never renewed, which every seeded record is.
        ///
        /// **Only before KeePass starts**, so that the plugin finds it there when it first reads the
        /// file.
        /// </remarks>
        public void SeedLastRequest(string admissionKey, string databaseId, DateTimeOffset at)
        {
            string root;
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                root = database.RootGroup.Uuid.ToHexString();
            }
            finally
            {
                database.Close();
            }

            new KeePassGate.Audit.JsonLinesAudit(AuditPath, NullGateLog.Instance).Record(
                new KeePassGate.Audit.AuditEntry(
                    at, databaseId, admissionKey, admissionKey, "listEntries", root, root, true,
                    "a rule here grants it"));
        }

        /// <summary>
        /// Ends this KeePass without ending the session it belongs to, so another can be
        /// started against the same file.
        /// </summary>
        /// <remarks>
        /// Delegates rather than repeats. The first version of this was a copy of `Release`
        /// with `Win32Exception` dropped from the filter, which is exactly what `Kill` throws
        /// when the target is already terminating.
        /// </remarks>
        public void Stop()
        {
            Release();
        }

        /// <summary>
        /// Ends this KeePass and gives the port back, and leaves every file where it is.
        /// </summary>
        /// <remarks>
        /// What a session whose fixture failed ends with instead of <see cref="Dispose"/>: the
        /// plugin's log, the database and the audit stay in the session directory for `build.ps1`
        /// to report.
        /// </remarks>
        public void Keep()
        {
            DropReservation();
            Release();
        }

        public void Dispose()
        {
            Keep();

            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // A file still open in a KeePass that has not finished exiting. The next session
                // of this fixture ends any such KeePass and deletes what is left before it begins.
            }
            catch (UnauthorizedAccessException)
            {
                // The copied KeePass still running, whose program Windows will not delete. Left
                // for that same next session.
            }
        }

        /// <summary>
        /// A local configuration beside the program is what makes KeePass ignore the developer's
        /// own settings, including any language they have selected. It answers four things.
        /// </summary>
        /// <remarks>
        /// The update question is asked on a first start and blocks behind a modal dialog before
        /// any plugin is loaded. Single instance handling matters more: with it left on, a KeePass
        /// that is already running takes over the command line and this process exits at once, so
        /// the test would drive somebody else's KeePass or nothing at all. The sections follow the
        /// order KeePass declares them in, which its own serializer does not require: measured on
        /// 2.61.1, a section and a setting out of that order are read alike. What it does not do
        /// is say anything about a setting it did not take, which is what
        /// `ThrowawayConfigurationTests` is for.
        ///
        /// **And Escape does nothing to the main window.** KeePass 2.61.1 locks the whole
        /// workspace on an Escape that reaches its main window with no menu open, which is its
        /// default. A test sends Escape to close a dialog or a menu, and one that arrives after
        /// the thing it was for has gone, or when a menu did not open, would lock the database
        /// every later step works on, and a failure about one menu would then read as a run of
        /// failures about something else.
        /// </remarks>
        private static void WriteConfiguration(string root, int lockAfterSeconds)
        {
            File.WriteAllText(
                Paths.Join(root, "KeePass.config.xml"), ConfigurationLockingAfter(lockAfterSeconds), new UTF8Encoding(false));
        }

        /// <summary>
        /// <see cref="Configuration"/>, with KeePass's own inactivity lock set to fire after this
        /// many seconds, or exactly it for zero.
        /// </summary>
        /// <remarks>
        /// For the one fixture that runs KeePass's own lock timer. Every other session runs with
        /// none, since a lock falling due in the middle of a fixture that did not ask for one would
        /// read as a failure about something else.
        /// </remarks>
        internal static string ConfigurationLockingAfter(int lockAfterSeconds)
        {
            if (lockAfterSeconds <= 0)
            {
                return Configuration;
            }

            return Configuration.Replace(
                "  <Integration>\r\n",
                "  <Security>\r\n" +
                "    <WorkspaceLocking>\r\n" +
                "      <LockAfterTime>" + lockAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "</LockAfterTime>\r\n" +
                "    </WorkspaceLocking>\r\n" +
                "  </Security>\r\n" +
                "  <Integration>\r\n");
        }

        /// <summary>
        /// What <see cref="WriteConfiguration"/> writes, where a test can read it the way KeePass
        /// does.
        /// </summary>
        internal const string Configuration =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<Configuration>\r\n" +
                "  <Application>\r\n" +
                "    <Start>\r\n" +
                "      <CheckForUpdate>false</CheckForUpdate>\r\n" +
                "      <CheckForUpdateConfigured>true</CheckForUpdateConfigured>\r\n" +
                "    </Start>\r\n" +
                "  </Application>\r\n" +
                "  <MainWindow>\r\n" +
                "    <EscAction>None</EscAction>\r\n" +
                "  </MainWindow>\r\n" +
                "  <Integration>\r\n" +
                "    <LimitToSingleInstance>false</LimitToSingleInstance>\r\n" +
                "  </Integration>\r\n" +
                "</Configuration>\r\n";

        private void InstallPlugin()
        {
            var pluginAssembly = typeof(KeePassGateExt).Assembly;
            var pluginPath = new Uri(pluginAssembly.CodeBase).LocalPath;
            // In a directory of its own under `plugins`, which is where `build.ps1 -Task Deploy`
            // and the operator's documentation put it, so that the live tier loads the layout an
            // installation has rather than one nobody ships.
            var plugins = Paths.Join(_root, "plugins", "KeePassGate");
            Directory.CreateDirectory(plugins);
            File.Copy(pluginPath, Paths.Join(plugins, Path.GetFileName(pluginPath)));

            // And what it needs, which is how the live test proves that a plugin with dependencies
            // actually loads: KeePass uses Assembly.LoadFrom, which probes the plugin's own
            // directory.
            //
            // **What it needs, followed through, rather than everything beside it.** The directory
            // this runs from is a test host's: it also holds NUnit, the test adapter and
            // `System.Runtime.CompilerServices.Unsafe`, which is the one assembly the plugin may
            // never ship because it wants a binding redirect only KeePass's own configuration could
            // give it. Copying the directory put all of that beside the plugin, so the live tier
            // proved something deployment never produces.
            //
            // **Followed through, and not one level deep.** A dependency that brings one of its own
            // is not named by the plugin at all: `Microsoft.OpenApi.Readers` brings `SharpYaml`, and
            // the plugin names neither. A plugin missing a transitive dependency fails to load with
            // nothing in the log to say why.
            var beside = Path.GetDirectoryName(pluginPath);
            foreach (var dependency in NeededBy(pluginAssembly, beside))
            {
                File.Copy(dependency, Paths.Join(plugins, Path.GetFileName(dependency)), true);
            }

            // And their notices, which each of their licenses asks to go with every copy, and which
            // `build.ps1 -Task Deploy` puts beside them as well.
            var notices = Paths.Join(Repository.Root, "THIRD-PARTY-NOTICES.md");
            File.Copy(notices, Paths.Join(plugins, Path.GetFileName(notices)));

            var symbols = Path.ChangeExtension(pluginPath, ".pdb");
            if (File.Exists(symbols))
            {
                File.Copy(symbols, Paths.Join(plugins, Path.GetFileName(symbols)));
            }
        }

        /// <summary>
        /// Every assembly the plugin references that is found beside it, and whatever those
        /// reference in turn.
        /// </summary>
        /// <remarks>
        /// Followed transitively rather than taken one level deep, because a dependency that brings
        /// one of its own is not named by the plugin at all and a plugin missing one fails to load
        /// with nothing in the log to say why. Anything not found beside the plugin is the
        /// framework or KeePass, both of which the host already has.
        /// </remarks>
        private static IEnumerable<string> NeededBy(Assembly plugin, string beside)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Assembly>();
            pending.Enqueue(plugin);

            while (pending.Count > 0)
            {
                foreach (var referenced in pending.Dequeue().GetReferencedAssemblies())
                {
                    if (!seen.Add(referenced.Name))
                    {
                        continue;
                    }

                    var path = Paths.Join(beside, referenced.Name + ".dll");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    found.Add(path);

                    try
                    {
                        pending.Enqueue(Assembly.ReflectionOnlyLoadFrom(path));
                    }
                    catch (BadImageFormatException)
                    {
                        // Not managed, so it names nothing further. Copied anyway.
                    }
                    catch (FileLoadException)
                    {
                        // Already loaded for reflection under another path. Its references are
                        // reachable from the copy that is, so nothing is lost by stopping here.
                    }
                }
            }

            return found;
        }

        private void CreateDatabase(bool withAuthority)
        {
            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(DatabasePath), NewKey());
            database.Name = "KeePassGate throwaway";
            database.RootGroup.Name = "Throwaway";

            if (withAuthority)
            {
                var created = new AuthorityService(
                    new CustomDataStore(database), NullGateLog.Instance).Create();
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException("The throwaway database got no authority: " + created.Message);
                }
            }

            database.Save(null);
            database.Close();
        }

        /// <summary>
        /// A second copy of the database file, beside the first, as a second machine holding the
        /// same database has one. Answers with its path.
        /// </summary>
        /// <remarks>
        /// **Copied rather than created**, because a copy is what makes the two one database to
        /// KeePass: the same root group, which is what a synchronize matches groups by, and the
        /// same master key, which is what a synchronize opens the other file with before it asks
        /// for one. Each copy can then change on its own, as two machines do between merges.
        ///
        /// **Only before KeePass starts**, like everything else here.
        /// </remarks>
        public string CopyDatabase(string fileName)
        {
            var path = Paths.Join(_root, fileName);
            File.Copy(DatabasePath, path);
            return path;
        }

        /// <summary>
        /// Puts a copy <see cref="CopyDatabase"/> made back in place of the database, and removes
        /// the copy.
        /// </summary>
        /// <remarks>
        /// For a stretch of the fixture that changes the database for its own sake and has to
        /// leave it as it found it for what follows. **Only once KeePass has exited**, because a
        /// running KeePass would save what it holds over this.
        /// </remarks>
        public void RestoreDatabase(string copy)
        {
            File.Copy(copy, DatabasePath, true);
            File.Delete(copy);
        }

        /// <summary>
        /// Replaces the authority in one copy of the database, as its Recreate button does once the
        /// settings dialog is accepted, and answers with the new authority's digest.
        /// </summary>
        /// <remarks>
        /// Through `AuthorityService`, so the copy holds exactly what the plugin leaves: the old
        /// authority retired, its document and every admission record gone, and the new one under
        /// a key of its own. Minted rather than written down, so it differs on every run, which
        /// costs nothing to a test that reads back the digest it is given.
        ///
        /// **Only before KeePass starts.**
        /// </remarks>
        public string RecreateAuthority(string path)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), NewKey(), null);

                var authority = new AuthorityService(new CustomDataStore(database), NullGateLog.Instance);
                var recreated = authority.Recreate();
                if (!recreated.Succeeded)
                {
                    throw new InvalidOperationException(
                        "The copy's authority was not recreated: " + recreated.Message);
                }

                database.Save(null);

                using (var reading = authority.Read())
                {
                    return reading.Material.AuthorityDigest;
                }
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Opens the database the way anybody holding the file could, which is how a test issues
        /// itself a client certificate and works out a name the gate will answer to.
        /// </summary>
        public AuthorityReading ReadAuthority()
        {
            return ReadAuthority(DatabasePath);
        }

        /// <summary>The same, for one copy of the database among several.</summary>
        public AuthorityReading ReadAuthority(string path)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), NewKey(), null);
                return new AuthorityStore(new CustomDataStore(database), NullGateLog.Instance).Read();
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// What the root group holds under a custom data key, read out of the file after KeePass
        /// has gone. The value rather than the group, because a group outlives the database it came
        /// from only by accident.
        /// </summary>
        public string ReadRootCustomData(string key)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return database.RootGroup.CustomData.Get(key);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>A name this database's gate will answer to, fresh each time.</summary>
        public string NewGateName()
        {
            using (var reading = ReadAuthority())
            {
                return SniName.For(reading.Material.SniKey);
            }
        }

        /// <summary>
        /// A client certificate issued by the database's own authority, loaded so that TLS can
        /// present it. Pairing issues one the same way, from inside the plugin.
        /// </summary>
        public X509Certificate2 IssueClientCertificate(string subject)
        {
            return IssueClientCertificate(DatabasePath, subject);
        }

        /// <summary>The same, from the authority one copy of the database holds.</summary>
        public X509Certificate2 IssueClientCertificate(string path, string subject)
        {
            using (var reading = ReadAuthority(path))
            using (var issued = new AuthorityIssuer().IssueClient(reading.Material.Authority, subject, null))
            using (KeePassGate.Testing.PrivateKeyFiles.Lock())
            {
                // With a key container, because the client in these tests is an SslStream and
                // SChannel will not present an ephemeral key. The plugin itself no longer asks for
                // one anywhere.
                return CertificateBlob.Load(
                    CertificateBlob.Save(issued), System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);
            }
        }

        /// <summary>
        /// Writes an admission record into the file, which is what pairing does from inside the
        /// plugin, through `DatabaseWriter.WriteAdmission`.
        /// </summary>
        /// <remarks>
        /// **Only before KeePass starts.** A running KeePass holds the database in memory and
        /// serves from that, so a record written to the file underneath it is not seen, and would
        /// be overwritten by its next save. That is not a limitation to work around: it is why
        /// pairing writes through the plugin rather than through the file.
        /// </remarks>
        public void Admit(X509Certificate2 certificate, params string[] profiles)
        {
            Admit(DatabasePath, certificate, profiles);
        }

        /// <summary>
        /// Admits a client whose row reads the same on every run, answering with its admission
        /// key: issued by the database's own authority at that moment, for the year a client that
        /// asks for nothing is given, and admitted at the same moment.
        /// </summary>
        /// <remarks>
        /// For the pictures, which show the name, the profiles and both dates and nothing else of a
        /// client: the certificate's own bytes differ every run, since the authority signs with a
        /// random k, and nothing in a picture shows them. **Only before KeePass starts**, for the
        /// reason <see cref="Admit(X509Certificate2, string[])"/> gives.
        /// </remarks>
        public string AdmitAsOf(string subject, DateTimeOffset at, params string[] profiles)
        {
            X509Certificate2 issued;
            using (var reading = ReadAuthority())
            {
                issued = new AuthorityIssuer(() => at).IssueClient(reading.Material.Authority, subject, null);
            }

            using (issued)
            {
                var database = new PwDatabase();
                try
                {
                    database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                    new AdmissionStore(new CustomDataStore(database), NullGateLog.Instance)
                        .Write(AdmissionRecord.For(issued, at, profiles));
                    database.Save(null);
                }
                finally
                {
                    database.Close();
                }

                return CertificateDigest.Of(issued);
            }
        }

        /// <summary>
        /// Writes a standing answer to renewing one certificate into the file, as the renewal
        /// dialog's Refuse menu has the plugin write one.
        /// </summary>
        /// <remarks>**Only before KeePass starts**, like everything else here.</remarks>
        public void RefuseRenewal(string admissionKey, RenewalRefusal refusal)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                new AdmissionStore(new CustomDataStore(database), NullGateLog.Instance)
                    .RefuseRenewal(admissionKey, refusal);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>The same, into one copy of the database among several.</summary>
        public void Admit(string path, X509Certificate2 certificate, params string[] profiles)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(path), NewKey(), null);

                new AdmissionStore(new CustomDataStore(database), NullGateLog.Instance)
                    .Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles));

                // Named as well, because pairing names what it approves in the same write as the
                // record, and a client holds only the names the database still lists: a record
                // written without them would be a client reaching nothing.
                var names = ProfileNames.Read(database.CustomData.Get(ProfileNames.CustomDataKey));
                foreach (var refused in profiles.Select(names.Add))
                {
                    if (refused != null)
                    {
                        throw new ArgumentException(refused, nameof(profiles));
                    }
                }

                var document = names.Write();
                if (document != null)
                {
                    database.CustomData.Set(ProfileNames.CustomDataKey, document);
                }

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Writes a grant on the root group, the way the KPG tab does, before KeePass is
        /// started.
        /// </summary>
        /// <remarks>
        /// Before, and it has to be: a running KeePass serves the database it holds in memory, so a
        /// rule written to the file underneath it would be invisible and then overwritten by its
        /// next save. That is the same reason a record is admitted before the process starts.
        /// </remarks>
        public void Grant(string document)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, document);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Puts a small invented tree in the database: two groups and four entries.
        /// </summary>
        /// <remarks>
        /// For the documentation, which screenshots these dialogs. An empty database makes a
        /// picture that shows the plugin working on nothing, and the group and entry dialogs cannot
        /// be reached at all without something to open them on.
        ///
        /// **Every name here is invented and has to stay that way.** These pictures are published,
        /// so a title, a user name or a group taken from anywhere real would be published with
        /// them. Nothing carries a password, because a screenshot of a password field is a habit
        /// worth not starting.
        /// </remarks>
        public void FillWithInventedContent()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                // Named for the pictures rather than for the tests, which is why this is a separate
                // method: a screenshot of a group called "Throwaway" teaches the reader nothing
                // about where their own rules would go.
                database.RootGroup.Name = "Invented Vault";

                var automation = new PwGroup(true, true, "Automation", PwIcon.Folder) { Uuid = Identified(AutomationUuid) };
                var services = new PwGroup(true, true, InventedServices, PwIcon.Folder) { Uuid = Identified(ServicesUuid) };
                database.RootGroup.AddGroup(automation, true);
                database.RootGroup.AddGroup(services, true);

                // One in the root as well, because the entry list shows the selected group and the
                // root is what a freshly opened KeePass selects. Without it the list is empty and
                // there is no entry dialog to reach.
                database.RootGroup.AddEntry(
                    Entry(MailboxUuid, "Shared Mailbox", "mail-invented", "https://mail.invalid"), true);

                automation.AddEntry(Entry(BuildAgentUuid, "Build agent", "agent-invented", "https://build.invalid"), true);
                services.AddEntry(Entry(FictitiousApiUuid, "Fictitious API", "api-fictitious", "https://api.invalid"), true);
                services.AddEntry(Entry(NonexistentMailUuid, "Nonexistent Mail", "mail-nonexistent", "https://mail.invalid"), true);

                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// Writes the profiles this database names, which is what the grant editor picks from.
        /// </summary>
        /// <remarks>
        /// Written into the file before KeePass opens it, because a rule can only be made for a
        /// profile the database already names and the editor offers no way to invent one. Pairing
        /// is the other way they arrive, and it is a whole test of its own.
        /// </remarks>
        public void NameProfiles(params string[] names)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                var profiles = ProfileNames.Read(null);
                foreach (var refused in names.Select(profiles.Add))
                {
                    // Answered rather than discarded. A name this plugin would refuse is a name the
                    // operator could not have typed, so a test seeding one is testing a state the
                    // product cannot reach. This used to do that silently, then hand `Write`'s
                    // null to `Set`, which throws about an argument instead.
                    if (refused != null)
                    {
                        throw new ArgumentException(refused, nameof(names));
                    }
                }

                var document = profiles.Write();
                if (document == null)
                {
                    throw new ArgumentException("no profile name was given", nameof(names));
                }

                database.CustomData.Set(ProfileNames.CustomDataKey, document);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>The name of the second invented group, which the moving picture keeps reader out of.</summary>
        public const string InventedServices = "Invented Services";

        /// <summary>The group the build agent is filed in.</summary>
        /// <remarks>
        /// **Every identifier in the invented tree is written down rather than minted**, for the
        /// reason the authority is: the moving picture types commands that name an entry and
        /// prints what the gate answers, a group's identifier among it, and an identifier minted
        /// by `PwUuid` is new every run. Patterned so that nobody takes one for a real identifier:
        /// `AA` for a group and `EE` for an entry, then a count.
        /// </remarks>
        public const string AutomationUuid = "AA000000000000000000000000000001";

        /// <summary>The group <see cref="InventedServices"/>.</summary>
        public const string ServicesUuid = "AA000000000000000000000000000002";

        /// <summary>The entry in the root group, which the moving picture moves into Automation.</summary>
        public const string MailboxUuid = "EE000000000000000000000000000001";

        /// <summary>The entry in the Automation group.</summary>
        public const string BuildAgentUuid = "EE000000000000000000000000000002";

        /// <summary>The first entry in <see cref="InventedServices"/>.</summary>
        public const string FictitiousApiUuid = "EE000000000000000000000000000003";

        /// <summary>The second entry in <see cref="InventedServices"/>.</summary>
        public const string NonexistentMailUuid = "EE000000000000000000000000000004";

        private static PwUuid Identified(string hex)
        {
            return new PwUuid(MemUtil.HexStringToByteArray(hex));
        }

        /// <summary>
        /// When every invented entry was made and last changed, written down for the reason the
        /// identifiers are: KeePass's main window shows both for the selected entry, and the moving
        /// picture shows the main window.
        /// </summary>
        private static readonly DateTime Invented = new DateTime(2026, 8, 22, 8, 30, 0, DateTimeKind.Utc);

        private static PwEntry Entry(string uuid, string title, string user, string url)
        {
            var entry = new PwEntry(true, true)
            {
                Uuid = Identified(uuid),
                CreationTime = Invented,
                LastModificationTime = Invented,
                LastAccessTime = Invented
            };
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, user));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, url));
            return entry;
        }

        /// <summary>
        /// Moves the entry in the root into the Automation group, before KeePass is started.
        /// </summary>
        /// <remarks>
        /// For the moving picture, which is about two folders, one a client may read and one it
        /// may not. An entry outside both is a third answer the picture would have to explain, and
        /// the root has it only so that a freshly opened KeePass lists something, which the picture
        /// shows by searching instead.
        /// </remarks>
        public void MoveRootEntryToAutomation()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                var entry = database.RootGroup.FindEntry(Identified(MailboxUuid), false);
                var automation = database.RootGroup.FindGroup(Identified(AutomationUuid), false);
                if (entry == null || automation == null)
                {
                    throw new InvalidOperationException("the database is not the invented one");
                }

                database.RootGroup.Entries.Remove(entry);
                automation.AddEntry(entry, true);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>The invented password the moving picture reads back off the Build agent entry.</summary>
        public const string BuildAgentPassword = "not-a-real-password-build-agent";

        /// <summary>The invented password the moving picture's client sets on that entry in its place.</summary>
        public const string RotatedPassword = "not-a-real-password-rotated";

        /// <summary>
        /// Gives the Build agent entry an invented password, before KeePass is started.
        /// </summary>
        /// <remarks>
        /// For the moving picture, which shows a client reading a password its rule opens to it.
        /// Only there: the stills list these entries too, and a password column with something in
        /// it would change pictures that are about something else.
        /// </remarks>
        public void GiveTheBuildAgentAPassword()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                var entry = database.RootGroup.FindEntry(Identified(BuildAgentUuid), true);
                if (entry == null)
                {
                    throw new InvalidOperationException("the database is not the invented one");
                }

                entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, BuildAgentPassword));
                database.Save(null);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>Every entry in the file, read after KeePass has exited.</summary>
        public IReadOnlyList<PwEntry> ReadEntries()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);

                var found = new List<PwEntry>();
                foreach (var entry in database.RootGroup.GetEntries(true))
                {
                    found.Add(entry.CloneDeep());
                }

                return found;
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// The admission records in the file, read after KeePass has exited. This is the only way to
        /// find out whether pairing really made one durable, because everything KeePass reports
        /// about a save it reports to itself.
        /// </summary>
        public IReadOnlyList<AdmissionRecord> ReadAdmissions()
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return new AdmissionStore(
                    new CustomDataStore(database), NullGateLog.Instance).All().Records;
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// One key of the database's own custom data as the file holds it, read after KeePass has
        /// exited, or null when the file has no such key: the raw value, for a test that has to
        /// tell an emptied record from a revocation rather than ask the store what they add up to.
        /// </summary>
        public string ReadCustomData(string key)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return new CustomDataStore(database).Get(key);
            }
            finally
            {
                database.Close();
            }
        }

        /// <summary>
        /// The standing answer to renewing one certificate that the file holds, read after KeePass
        /// has exited, or null when it holds none.
        /// </summary>
        public RenewalRefusal ReadStandingRefusal(string admissionKey)
        {
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(DatabasePath), NewKey(), null);
                return new AdmissionStore(
                    new CustomDataStore(database), NullGateLog.Instance).StandingRefusal(admissionKey);
            }
            finally
            {
                database.Close();
            }
        }

        public string DatabaseId()
        {
            using (var reading = ReadAuthority())
            {
                return reading.Material.AuthorityDigest;
            }
        }

        private static CompositeKey NewKey()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(DatabasePassword));
            return key;
        }

        /// <summary>The port examples and pictures use, or any free one.</summary>
        public const int Documented = 45678;

        /// <summary>A port nothing is using, held until the moment KeePass is told to take it.</summary>
        /// <remarks>
        /// **One port when it is free, rather than a fresh one every time.** Anything published or
        /// compared between runs then does not change for a reason nobody chose. Anything else free
        /// will do when something on the machine already holds it.
        ///
        /// **Held rather than let go.** Choosing it in `Create` and binding it in `Start` leaves
        /// seconds of copying and key generation in between, and the number is a fixed one, so a
        /// previous run's KeePass that has not finished exiting is the likeliest thing to take it.
        /// The reservation is dropped in `Start`, immediately before the process that wants it is
        /// launched.
        /// </remarks>
        private static TcpListener Reserve(int preferred = Documented)
        {
            foreach (var listener in new[] { preferred, 0 }
                         .Select(port => new TcpListener(IPAddress.Loopback, port)))
            {
                try
                {
                    listener.Start();
                    return listener;
                }
                catch (SocketException)
                {
                    // The documented one is taken. Anything free will do.
                }
            }

            throw new InvalidOperationException("No loopback port could be reserved for the gate.");
        }

    }
}
