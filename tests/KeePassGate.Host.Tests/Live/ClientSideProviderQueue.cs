using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace KeePassGate.Host.Tests.Live
{
    // cspell:ignore Clientside

    /// <summary>
    /// The thread UI automation's managed client-side providers leave in the domain that used them,
    /// and its end.
    /// </summary>
    /// <remarks>
    /// **What it is.** `System.Windows.Automation` answers for a window's Win32 and WinForms controls
    /// through the providers in `UIAutomationClientsideProviders`, loaded into the domain that asks.
    /// The first walk into a window's controls has their `WinEventTracker` start a queue of its own,
    /// a `QueueProcessor` on a background thread, which waits in `MsgWaitForMultipleObjects` with no
    /// timeout and has no way to be told to stop: it leaves its loop only when that wait fails.
    /// Finding a window by its handle and reading its name starts nothing; one `FindAll` over its
    /// descendants starts it every time.
    ///
    /// **Why it matters.** A thread waiting in native code takes no abort until it returns, so
    /// `AppDomain.Unload` gives up on this one after fifteen seconds with
    /// `CannotUnloadAppDomainException`. NUnit runs the test assembly in a domain of its own and
    /// unloads it at the end, so every end to end run that read a dialog ended with "Exception
    /// encountered unloading application domain", fifteen seconds after its last test. `FINDINGS.md`
    /// has it under "Driving KeePass through the desktop".
    ///
    /// **How it ends.** The queue is handed one work item the way the framework hands it one, and
    /// that item ends the thread it runs on. The field holding the queue is cleared under the lock
    /// the framework guards it with, so anything asking for the queue afterwards starts a new one the
    /// way the first was started. The WinEvent hooks the queue installed belong to that thread's
    /// message queue, so nothing can call into the domain through them once the thread has gone.
    /// What the tracker remembers about those hooks is left as it is, which is why this is for the
    /// end of a run rather than the middle of one.
    ///
    /// **Every name here is private to .NET Framework 4.8.x**, which is serviced rather than
    /// developed, and they were read off 4.8.1, file version 4.8.10008.0 of the providers. Nothing
    /// promises a servicing build keeps them, so what this rests on is not the framework standing
    /// still: `ClientSideProviderQueueTests` pins them in the host tier, on every run of the gate,
    /// against whatever build is installed, and a name that has gone throws here rather than
    /// leaving a run to end the old way unnoticed.
    ///
    /// **Not `RunConfiguration.DisableAppDomain`**, which was measured as the alternative and does
    /// take the unload away, since the tests then run in the test host's own domain, which is never
    /// unloaded. Set for the whole host assembly it fails `GrantTabFitsTests`, whose themed child
    /// domain no longer finds `System.Buffers` 4.0.5.0, and set for the end to end runs alone it
    /// puts their code under the test host's binding redirects, which is the reasoning
    /// `Directory.Build.props` gives for having none. `spikes/automation-unload` holds the probe and
    /// what it measured.
    /// </remarks>
    internal static class ClientSideProviderQueue
    {
        /// <summary>The framework's own assembly, as it is loaded into a domain that used automation.</summary>
        internal const string AssemblyName = "UIAutomationClientsideProviders";

        internal const string AssemblyFullName =
            AssemblyName + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";

        internal const string TrackerName = "MS.Internal.AutomationProxies.WinEventTracker";

        private const string ItemName = "MS.Internal.AutomationProxies.QueueItem+MSAAWinEventItem";

        private const BindingFlags Shared = BindingFlags.Static | BindingFlags.NonPublic;

        private const BindingFlags Own = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>
        /// Ends the queue's thread, when one was started in this domain, and says whether it did.
        /// </summary>
        /// <remarks>
        /// Nothing is loaded to find out: a domain that never walked into a window has not loaded the
        /// providers, and that is answered as having nothing to end.
        /// </remarks>
        public static bool End(TimeSpan patience)
        {
            var providers = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == AssemblyName);
            if (providers == null)
            {
                return false;
            }

            var tracker = providers.GetType(TrackerName, true);
            var held = Field(tracker, "_callbackQueue", Shared);
            var gate = Field(tracker, "_queueLock", Shared).GetValue(null);

            object queue;
            lock (gate)
            {
                queue = held.GetValue(null);
                held.SetValue(null, null);
            }

            if (queue == null)
            {
                return false;
            }

            var items = (Queue)Field(queue.GetType(), "_q", Own).GetValue(queue);
            var wake = (EventWaitHandle)Field(queue.GetType(), "_ev", Own).GetValue(queue);
            var item = providers.GetType(ItemName, true).GetConstructors(Own | BindingFlags.Public).Single();

            var ending = new Ending();
            var work = Delegate.CreateDelegate(item.GetParameters()[0].ParameterType, ending, nameof(Ending.Run));

            // As `QueueProcessor.PostSyncWorkItem` posts one, without its wait for the item to finish,
            // since this one finishes the thread instead.
            items.Enqueue(item.Invoke(new object[] { work }));
            wake.Set();

            if (!ending.Reached.WaitOne(patience))
            {
                // Left undisposed, since the queue's thread may yet reach the item and set it.
                throw new InvalidOperationException(
                    "The client-side providers' queue thread had not taken the item that ends it after " +
                    patience.TotalSeconds + " seconds, and the test domain cannot unload while it runs.");
            }

            ending.Reached.Dispose();

            if (!ending.Thread.Join(patience))
            {
                throw new InvalidOperationException(
                    "The client-side providers' queue thread took the item that ends it and was still " +
                    "running after " + patience.TotalSeconds + " seconds, and the test domain cannot " +
                    "unload while it runs.");
            }

            return true;
        }

        /// <summary>One private member of the framework's, or a failure naming it.</summary>
        private static FieldInfo Field(Type type, string name, BindingFlags flags)
        {
            var field = type.GetField(name, flags);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, name);
            }

            return field;
        }

        /// <summary>The work item's body, run on the queue's own thread.</summary>
        /// <remarks>
        /// <see cref="Reached"/> is disposed by <see cref="End"/> once it has been set, which is the
        /// last thing anything does with it.
        /// </remarks>
        private sealed class Ending
        {
            public readonly ManualResetEvent Reached = new ManualResetEvent(false);

            public Thread Thread;

            /// <summary>
            /// Ends the thread it runs on, which is the queue's. An abort of the current thread
            /// passes through the queue's own handler, which catches every exception and cannot keep
            /// this one, and the thread leaves its loop for good.
            /// </summary>
            public void Run()
            {
                Thread = Thread.CurrentThread;
                Reached.Set();
                Thread.CurrentThread.Abort();
            }
        }
    }
}
