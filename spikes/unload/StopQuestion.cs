using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;

// One `FindAll` over the window, then the client-side providers' queue thread ended, then the
// unload. The queue has no way to be told to stop, so the thread is handed a work item of the kind
// it already runs, whose body records which thread it is and aborts it, and the static field that
// holds the queue is cleared under the lock that guards it so that the next caller starts a new one.
//
//   findall      the call alone, for comparison
//   stop         the call, then the thread ended
//   stop-again   the call, the thread ended, a second call, and that queue's thread ended too
public static class StopQuestion
{
    public static int Ask(string scenario, IntPtr handle)
    {
        var domain = Program.ChildDomain("stop-" + scenario);
        var worker = Program.Worker<StopWorker>(domain);
        var said = worker.Run(scenario, handle);

        string unload;
        Program.Unload(domain, out unload);
        Console.WriteLine(scenario.PadRight(22) + " unload " + unload.PadRight(42) + said);
        return 0;
    }
}

public sealed class StopWorker : MarshalByRefObject
{
    private const string TrackerName = "MS.Internal.AutomationProxies.WinEventTracker";

    public string Run(string scenario, IntPtr handle)
    {
        var said = "";
        var element = AutomationElement.FromHandle(handle);
        element.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        said += "hooks " + Hooks() + "; ";

        if (scenario.StartsWith("stop", StringComparison.Ordinal))
        {
            said += Stop() + "; ";
        }

        if (scenario == "stop-again")
        {
            element.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            said += "again: " + QueueState() + "; " + Stop() + "; ";
        }

        return said + QueueState();
    }

    private static Type Tracker()
    {
        return Program.LoadedProviders().GetType(TrackerName, true);
    }

    private static string QueueState()
    {
        var queue = Tracker().GetField("_callbackQueue", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return queue == null ? "queue null" : "queue SET";
    }

    /// <summary>How many WinEvent hook registrations the tracker holds across its tables.</summary>
    private static string Hooks()
    {
        var tables = (Hashtable[])Tracker().GetField("_ahp", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return tables.Sum(t => t == null ? 0 : t.Count).ToString();
    }

    private static string Stop()
    {
        var providers = Program.LoadedProviders();
        var tracker = providers.GetType(TrackerName, true);
        var queueField = tracker.GetField("_callbackQueue", BindingFlags.Static | BindingFlags.NonPublic);
        var queueLock = tracker.GetField("_queueLock", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        object queue;
        lock (queueLock)
        {
            queue = queueField.GetValue(null);
            queueField.SetValue(null, null);
        }

        if (queue == null)
        {
            return "nothing to stop";
        }

        // What `QueueProcessor.PostSyncWorkItem` does, less its two second wait: enqueue the item
        // and signal the event the thread waits on beside its message queue.
        var processor = queue.GetType();
        var items = (Queue)processor.GetField("_q", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);
        var signal = (AutoResetEvent)processor.GetField("_ev", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(queue);
        var itemType = providers.GetType("MS.Internal.AutomationProxies.QueueItem+MSAAWinEventItem", true);
        var constructor = itemType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();

        var ending = new Ending();
        var work = Delegate.CreateDelegate(constructor.GetParameters()[0].ParameterType, ending, "End");
        items.Enqueue(constructor.Invoke(new object[] { work }));
        signal.Set();

        var watch = Stopwatch.StartNew();
        if (!ending.Reached.WaitOne(5000))
        {
            return "the queue never ran the item";
        }

        return ending.Thread.Join(5000)
            ? "stopped in " + watch.ElapsedMilliseconds + " ms"
            : "the thread did not end";
    }

    private sealed class Ending
    {
        public readonly ManualResetEvent Reached = new ManualResetEvent(false);

        public Thread Thread;

        public void End()
        {
            Thread = Thread.CurrentThread;
            Reached.Set();
            Thread.CurrentThread.Abort();
        }
    }
}
