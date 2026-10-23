using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

// Why a test domain that has driven KeePass through UI automation cannot be unloaded, asked one
// question per process so that no process-wide state is left over from another.
//
// The questions that drive KeePass take the main window's handle, in decimal, as their last
// argument, and each runs its automation inside a child application domain that it then unloads:
//
//   menu <scenario> <handle>          find, open and read the File menu (find, pattern, expand,
//                                     expand-read, expand-read-remove)
//   minimal <scenario> <handle>       the smallest call that leaves the domain unable to unload
//                                     (nothing, handle, name, id, findall, and each of those with
//                                     an empty- prefix, which registers no client-side providers)
//   tree <default|empty> <handle> [menu]
//                                     the accessibility tree with and without the managed
//                                     client-side providers, optionally with one menu opened
//   stop <scenario> <handle>          end the providers' queue thread before the unload
//                                     (findall, stop, stop-again)
//
// The questions that read the providers' assembly need no KeePass and run in this domain:
//
//   members                           what `WinEventTracker` and `QueueProcessor` declare
//   items                             what a queue item is and what each kind takes
//   il                                the IL of the methods that start, wait on and feed the queue
public static class Program
{
    /// <summary>The assembly that starts the queue thread. It is in the GAC on every Windows that
    /// carries .NET Framework 4, and nothing here references it, so it is loaded by name.</summary>
    public const string ProvidersName =
        "UIAutomationClientsideProviders, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: Probe <menu|minimal|tree|stop> <scenario> <handle> | <members|items|il>");
            return 2;
        }

        switch (args[0])
        {
            case "menu":
                return MenuQuestion.Ask(args[1], Handle(args[2]));
            case "minimal":
                return MinimalQuestion.Ask(args[1], Handle(args[2]));
            case "tree":
                return TreeQuestion.Ask(args[1], Handle(args[2]), args.Length > 3 ? args[3] : null);
            case "stop":
                return StopQuestion.Ask(args[1], Handle(args[2]));
            case "members":
                ProvidersReflection.Members();
                return 0;
            case "items":
                ProvidersReflection.Items();
                return 0;
            case "il":
                ProvidersReflection.Il();
                return 0;
            default:
                Console.Error.WriteLine("no question named " + args[0]);
                return 2;
        }
    }

    /// <summary>A child domain set up as this one is, which is how the originals made theirs.</summary>
    public static AppDomain ChildDomain(string name)
    {
        return AppDomain.CreateDomain(name, null, AppDomain.CurrentDomain.SetupInformation);
    }

    /// <summary>Makes a worker inside the child domain and hands back its proxy.</summary>
    public static T Worker<T>(AppDomain domain) where T : MarshalByRefObject
    {
        return (T)domain.CreateInstanceAndUnwrap(typeof(T).Assembly.FullName, typeof(T).FullName);
    }

    /// <summary>Unloads the domain and says how it went and how long it took.</summary>
    public static bool Unload(AppDomain domain, out string said)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            AppDomain.Unload(domain);
            said = "ok " + watch.ElapsedMilliseconds + " ms";
            return true;
        }
        catch (Exception error)
        {
            said = error.GetType().Name + " " + watch.ElapsedMilliseconds + " ms";
            return false;
        }
    }

    public static Assembly LoadedProviders()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "UIAutomationClientsideProviders")
            {
                return assembly;
            }
        }

        return null;
    }

    private static IntPtr Handle(string text)
    {
        return (IntPtr)long.Parse(text, CultureInfo.InvariantCulture);
    }
}
