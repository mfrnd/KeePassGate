using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;

// What the desktop tests do to a menu, done inside a child domain that is then unloaded: find the
// File menu, take its expand pattern, open it, read every menu item on the desktop, close it, and
// optionally take every automation event handler away before the unload.
public static class MenuQuestion
{
    public static int Ask(string scenario, IntPtr handle)
    {
        var domain = Program.ChildDomain("menu-" + scenario);
        var worker = Program.Worker<MenuWorker>(domain);
        Console.WriteLine(scenario + ": " + worker.Run(scenario, handle));
        Console.WriteLine("  static threads and queues inside: " + worker.Threads());

        string said;
        var unloaded = Program.Unload(domain, out said);
        Console.WriteLine("  unload: " + said);
        return unloaded ? 0 : 1;
    }
}

public sealed class MenuWorker : MarshalByRefObject
{
    public string Run(string scenario, IntPtr handle)
    {
        var main = AutomationElement.FromHandle(handle);
        var menu = main.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
            .Cast<AutomationElement>()
            .First(e => (e.Current.Name ?? "").Replace("&", "") == "File");

        if (scenario == "find")
        {
            return "found " + menu.Current.Name + " key " + menu.Current.AccessKey;
        }

        object pattern;
        if (!menu.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
        {
            return "no expand pattern";
        }

        if (scenario == "pattern")
        {
            return "got the pattern, state " + ((ExpandCollapsePattern)pattern).Current.ExpandCollapseState;
        }

        ((ExpandCollapsePattern)pattern).Expand();
        Thread.Sleep(500);

        var items = 0;
        if (scenario == "expand-read" || scenario == "expand-read-remove")
        {
            items = AutomationElement.RootElement.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)).Count;
        }

        ((ExpandCollapsePattern)pattern).Collapse();
        Thread.Sleep(300);

        if (scenario == "expand-read-remove")
        {
            Automation.RemoveAllEventHandlers();
        }

        return "expanded and collapsed, " + items + " menu items seen";
    }

    /// <summary>Every static field of a UI automation assembly loaded here that holds a thread or a
    /// queue processor, and whether it is set.</summary>
    public string Threads()
    {
        var found = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (!name.StartsWith("UIAutomation", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType.Name.Contains("QueueProcessor") || field.FieldType == typeof(Thread))
                    {
                        object value = null;
                        try
                        {
                            value = field.GetValue(null);
                        }
                        catch (Exception)
                        {
                            // A field that cannot be read is reported as unset, which is all this
                            // listing is for.
                        }

                        found.Add(name + ":" + type.Name + "." + field.Name + "=" + (value == null ? "null" : "SET"));
                    }
                }
            }
        }

        return string.Join("; ", found);
    }
}
