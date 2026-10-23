using System;
using System.Linq;
using System.Reflection;
using System.Windows.Automation;

// The smallest automation call that leaves the domain unable to unload, and what it leaves loaded.
// A scenario with the `empty-` prefix first registers an empty set of client-side providers, which
// is the documented way to keep the managed default providers from being used.
public static class MinimalQuestion
{
    public static int Ask(string scenario, IntPtr handle)
    {
        var domain = Program.ChildDomain("minimal-" + scenario);
        var worker = Program.Worker<MinimalWorker>(domain);
        var said = worker.Run(scenario, handle);

        string unload;
        Program.Unload(domain, out unload);
        Console.WriteLine(scenario.PadRight(14) + " unload " + unload.PadRight(42) + said);
        return 0;
    }
}

public sealed class MinimalWorker : MarshalByRefObject
{
    public string Run(string scenario, IntPtr handle)
    {
        if (scenario.StartsWith("empty-", StringComparison.Ordinal))
        {
            ClientSettings.RegisterClientSideProviders(new ClientSideProviderDescription[0]);
            scenario = scenario.Substring("empty-".Length);
        }

        if (scenario != "nothing")
        {
            var element = AutomationElement.FromHandle(handle);

            if (scenario == "name" || scenario == "findall")
            {
                var name = element.Current.Name;
            }

            if (scenario == "id")
            {
                var id = element.Current.AutomationId;
            }

            if (scenario == "findall")
            {
                var all = element.FindAll(TreeScope.Descendants, Condition.TrueCondition).Count;
            }
        }

        return State();
    }

    /// <summary>Which UI automation assemblies are loaded here, and whether the providers' queue
    /// has been started.</summary>
    private static string State()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Where(n => n.StartsWith("UIAutomation", StringComparison.Ordinal))
            .OrderBy(n => n);

        var queue = "not loaded";
        var providers = Program.LoadedProviders();
        if (providers != null)
        {
            var tracker = providers.GetType("MS.Internal.AutomationProxies.WinEventTracker");
            var field = tracker.GetField("_callbackQueue", BindingFlags.Static | BindingFlags.NonPublic);
            queue = field.GetValue(null) == null ? "queue null" : "queue SET";
        }

        return "[" + string.Join(" ", loaded) + "] " + queue;
    }
}
