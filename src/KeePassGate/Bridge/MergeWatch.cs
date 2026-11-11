using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Whether the calling thread is inside a merge KeePass is running: a synchronize, an import,
    /// XML Replace, or one another plugin asked for.
    /// </summary>
    /// <remarks>
    /// **Every merge runs inside one method**, `PwDatabase.MergeIn`: File then Synchronize, File
    /// then Import, the import a trigger runs and XML Replace all end there, and so does a merge a
    /// plugin makes of its own. While it runs the tree is neither the one before nor the one
    /// after: groups and entries arrive first, then are moved, reordered and deleted in passes of
    /// their own, and the status logger it is given pumps messages between the steps.
    ///
    /// **Nothing KeePass announces is that question.** `MainForm.UIIsInteractionBlocked` is raised
    /// around a synchronize and an import, and around every save as well, during which the tree
    /// stands still and a read is welcome. The progress window is XML Replace's alone. A merge a
    /// plugin runs raises neither, and `PwDatabase` keeps no flag for the passes that add and
    /// overwrite.
    ///
    /// **The thread's own stack is**, because the method cannot be compiled away: its body is 435
    /// bytes of IL in KeePass 2.61.1, several times what the just-in-time compiler will inline, so
    /// a thread inside it has its frame. A look costs twenty to fifty microseconds, measured at
    /// fifty to a hundred and eighty frames, `FINDINGS.md` section 22.
    /// `ReadsDuringAMergeTests` drives KeePass's own method and asks from inside it.
    ///
    /// **What it cannot see.** A merge running on another thread, which none of KeePass's own
    /// does. And a merge that ended early: Cancel on XML Replace's progress window stops the
    /// method part way, and what it leaves, partly merged, is from then on simply the database.
    /// </remarks>
    public static class MergeWatch
    {
        /// <summary>
        /// The question, for a thread to ask about itself, or null when the type has no method of
        /// that name, so that nothing would ever answer yes.
        /// </summary>
        /// <param name="database">KeePass's `PwDatabase`.</param>
        /// <param name="method">
        /// The method's name, <see cref="KeePassContract.DatabaseMergeMethod"/>. Every overload is
        /// meant: the short one calls the long one.
        /// </param>
        public static Func<bool> Over(Type database, string method)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            if (database.GetMember(method, MemberTypes.Method, Declared).Length == 0)
            {
                return null;
            }

            return () => Inside(database, method);
        }

        private static bool Inside(Type database, string method)
        {
            // No file information, which is the expensive half of a stack trace and names nothing
            // asked about here.
            var frames = new StackTrace(false).GetFrames();
            if (frames == null)
            {
                return false;
            }

            return frames
                .Select(frame => frame.GetMethod())
                .Any(running => running != null
                                && running.DeclaringType == database
                                && string.Equals(running.Name, method, StringComparison.Ordinal));
        }
    }
}
