using System;
using System.Collections.Generic;

namespace MonoProbe
{
    /// <summary>
    /// `MonoProbe.exe &lt;probe&gt;`, one probe per process, so that a probe that takes the runtime
    /// down costs only its own lines. `MonoProbe.exe list` names them in the order the scripts run
    /// them.
    /// </summary>
    internal static class Program
    {
        private static readonly Dictionary<string, Action> Probes = new Dictionary<string, Action>
        {
            { "runtime", RuntimeProbe.Run },
            { "issuance", IssuanceProbe.Run },
            { "ecdsa", EcdsaProbe.Run },
            { "bouncy", BouncyIssuanceProbe.Run },
            { "chain", ChainProbe.Run },
            { "tls", TlsProbe.Run },
            { "audit", AuditFileProbe.Run },
            { "native", NativeProbe.Run },
            { "culture", CultureProbe.Run },
            { "forms", FormsProbe.Run },
        };

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("usage: MonoProbe <probe> | list");
                return 2;
            }

            if (args[0] == "list")
            {
                foreach (var name in Probes.Keys)
                {
                    Console.WriteLine(name);
                }

                return 0;
            }

            Action probe;
            if (!Probes.TryGetValue(args[0], out probe))
            {
                Console.Error.WriteLine("no probe named " + args[0]);
                return 2;
            }

            try
            {
                probe();
                return 0;
            }
            catch (Exception error)
            {
                // A probe catches what each measurement throws, so reaching here is a defect in the
                // probe rather than an answer, and it says so.
                Report.Line(args[0] + ".probe-defect", Report.Failure(error));
                return 1;
            }
        }
    }
}
