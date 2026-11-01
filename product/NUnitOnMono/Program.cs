using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using NUnit;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace NUnitOnMono
{
    /// <summary>
    /// `NUnitOnMono.exe &lt;tests.dll&gt; &lt;results.tsv&gt; [name regex]`: every test case outside
    /// the `Live` and `Screenshots` categories, one line each as it finishes, so that a run the
    /// runtime takes down still leaves every answer up to the test that did it.
    /// </summary>
    internal static class Program
    {
        // Long enough for the slowest host test on .NET Framework many times over, short enough
        // that a test waiting for an event this runtime never raises costs minutes rather than the run.
        private const int TestTimeout = 120000;

        private static int Main(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("usage: NUnitOnMono <tests.dll> <results.tsv> [name regex]");
                return 2;
            }

            var assembly = Path.GetFullPath(args[0]);
            var results = Path.GetFullPath(args[1]);
            var filter = "<filter><not><or><cat>Live</cat><cat>Screenshots</cat></or></not>"
                + (args.Length == 3 ? "<test re=\"1\">" + SecurityElement.Escape(args[2]) + "</test>" : string.Empty)
                + "</filter>";

            var settings = new Dictionary<string, object>
            {
                { FrameworkPackageSettings.NumberOfTestWorkers, 0 },
                { FrameworkPackageSettings.DefaultTimeout, TestTimeout },
                { FrameworkPackageSettings.WorkDirectory, Path.GetDirectoryName(results) },
            };

            // NUnit reads the signature of every method of every type while it builds fixtures,
            // and one type whose signatures name something this runtime lacks makes the whole
            // assembly one it will not run. Such a type is named here and left out, and only then is the
            // rest loaded. No type is left out unless one fails.
            var resolvable = new List<string>();
            var unresolvable = 0;
            foreach (var type in Assembly.LoadFrom(assembly).GetTypes())
            {
                var why = Unresolvable(type);
                if (why == null)
                {
                    resolvable.Add(type.FullName);
                }
                else
                {
                    unresolvable++;
                    Console.WriteLine("left out " + type.FullName + ": " + why);
                }
            }

            if (unresolvable > 0)
            {
                settings[FrameworkPackageSettings.LOAD] = resolvable;
            }

            var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
            var loaded = runner.Load(assembly, settings);
            Console.WriteLine("loaded " + loaded.TestCaseCount + " test cases from " + assembly);
            if (loaded.RunState != RunState.Runnable)
            {
                // An assembly NUnit could not build is a suite that is not runnable and holds no
                // cases, and the reason is in its properties rather than thrown.
                Console.WriteLine("not runnable: " + loaded.RunState + ": " + loaded.Properties.Get(PropertyNames.SkipReason));
                Console.WriteLine(loaded.Properties.Get(PropertyNames.ProviderStackTrace));
            }

            using (var writer = new StreamWriter(results, false) { AutoFlush = true })
            {
                writer.WriteLine("test\toutcome\tseconds\tmessage\twhere");
                var outcome = runner.Run(new Listener(writer), TestFilter.FromXml(filter));
                Console.WriteLine(
                    "passed " + outcome.PassCount + ", failed " + outcome.FailCount + ", skipped " + outcome.SkipCount
                    + ", inconclusive " + outcome.InconclusiveCount + ", warnings " + outcome.WarningCount);
            }

            return 0;
        }

        private static string Unresolvable(Type type)
        {
            const BindingFlags Every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                       | BindingFlags.Static | BindingFlags.DeclaredOnly;
            try
            {
                foreach (var method in type.GetMethods(Every))
                {
                    method.GetParameters();
                    var returned = method.ReturnType;
                }

                return null;
            }
            catch (TypeLoadException error)
            {
                return error.GetType().Name + ": " + error.Message.Split('\n')[0].Trim();
            }
        }

        private sealed class Listener : ITestListener
        {
            private readonly StreamWriter _writer;

            internal Listener(StreamWriter writer)
            {
                _writer = writer;
            }

            public void TestStarted(ITest test)
            {
                if (!test.IsSuite)
                {
                    // On the console rather than in the results, so the last of these names the
                    // test that was running when a run ended without finishing.
                    Console.WriteLine("started " + test.FullName);
                }
            }

            public void TestFinished(ITestResult result)
            {
                if (result.Test.IsSuite)
                {
                    return;
                }

                var state = result.ResultState.Status.ToString()
                    + (string.IsNullOrEmpty(result.ResultState.Label) ? string.Empty : ":" + result.ResultState.Label);
                _writer.WriteLine(string.Join("\t", result.FullName, state,
                    result.Duration.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    OneLine(result.Message), Where(result.StackTrace)));
            }

            public void TestOutput(TestOutput output)
            {
            }

            public void SendMessage(TestMessage message)
            {
            }

            // The whole message, which for a test with several failures is where each of them is,
            // on one line and cut short only when it runs long.
            private const int MessageLength = 2000;

            private static string OneLine(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return string.Empty;
                }

                var lines = text.Replace("\t", " ").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var joined = string.Join(" | ", Array.ConvertAll(lines, line => line.Trim()));
                return joined.Length <= MessageLength ? joined : joined.Substring(0, MessageLength) + " ...";
            }

            // The first frame in the plugin rather than in its tests, which is where the runtime
            // was found missing something.
            private static string Where(string stackTrace)
            {
                if (string.IsNullOrEmpty(stackTrace))
                {
                    return string.Empty;
                }

                foreach (var line in stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var frame = line.Trim();
                    if (frame.StartsWith("at KeePassGate.", StringComparison.Ordinal)
                        && !frame.StartsWith("at KeePassGate.Tests.", StringComparison.Ordinal)
                        && !frame.StartsWith("at KeePassGate.Host.Tests.", StringComparison.Ordinal))
                    {
                        return frame.Replace("\t", " ");
                    }
                }

                return string.Empty;
            }
        }
    }
}
