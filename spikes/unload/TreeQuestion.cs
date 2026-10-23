using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// Prints a window's accessibility tree, with or without the managed client-side providers, and then
// unloads the domain it ran in. `empty` registers an empty set of client-side providers before any
// other automation call, which keeps the managed defaults from being used. Naming a menu opens it
// first and walks the whole desktop instead, since an open menu is a window of its own.
public static class TreeQuestion
{
    public static int Ask(string mode, IntPtr handle, string expandFirst)
    {
        var domain = Program.ChildDomain("tree-" + mode);
        var worker = Program.Worker<TreeWorker>(domain);
        Console.Write(worker.Dump(mode, handle, expandFirst));

        string said;
        var unloaded = Program.Unload(domain, out said);
        Console.WriteLine("unload: " + said);
        return unloaded ? 0 : 1;
    }
}

public sealed class TreeWorker : MarshalByRefObject
{
    public string Dump(string mode, IntPtr handle, string expandFirst)
    {
        if (mode == "empty")
        {
            ClientSettings.RegisterClientSideProviders(new ClientSideProviderDescription[0]);
        }

        var text = new StringBuilder();
        var root = AutomationElement.FromHandle(handle);

        if (expandFirst != null)
        {
            foreach (AutomationElement item in root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
            {
                if ((item.Current.Name ?? "").Replace("&", "") == expandFirst)
                {
                    ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    Thread.Sleep(500);
                    break;
                }
            }

            root = AutomationElement.RootElement;
        }

        Walk(root, 0, text, expandFirst == null ? 6 : 3);

        if (expandFirst != null)
        {
            foreach (AutomationElement item in AutomationElement.RootElement.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
            {
                text.AppendLine("menu item: " + Line(item));
            }
        }

        return text.ToString();
    }

    private static void Walk(AutomationElement element, int depth, StringBuilder text, int most)
    {
        if (depth > most)
        {
            return;
        }

        text.Append(new string(' ', depth * 2)).AppendLine(Line(element));

        var child = TreeWalker.RawViewWalker.GetFirstChild(element);
        var count = 0;
        while (child != null && count < 60)
        {
            Walk(child, depth + 1, text, most);
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
            count++;
        }
    }

    private static string Line(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            var patterns = string.Join(",", Array.ConvertAll(element.GetSupportedPatterns(),
                p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "")));
            return current.ControlType.ProgrammaticName.Replace("ControlType.", "") +
                   " name=\"" + current.Name + "\" id=\"" + current.AutomationId + "\" key=\"" + current.AccessKey +
                   "\" class=\"" + current.ClassName + "\" [" + patterns + "]";
        }
        catch (Exception error)
        {
            return "(unreadable: " + error.GetType().Name + ")";
        }
    }
}
