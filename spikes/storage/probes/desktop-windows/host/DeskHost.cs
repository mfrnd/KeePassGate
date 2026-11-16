// Runs one of the family's PowerShell scripts inside a managed process, on an STA thread:
//
//   DeskHost.exe <script.ps1> [name value]...
//
// Each pair after the script is passed to it as a named parameter. What the script writes to its
// output, information and error streams is printed as it arrives; the exit code is 1 when the
// script ended in an error.
using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

static class DeskHost
{
    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("DeskHost.exe <script.ps1> [name value]..."); return 2; }
        int code = 0;
        var t = new Thread(() => code = Run(args));
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return code;
    }

    static int Run(string[] args)
    {
        var iss = InitialSessionState.CreateDefault();
        iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        using (var rs = RunspaceFactory.CreateRunspace(iss))
        {
            rs.ApartmentState = ApartmentState.STA;
            rs.ThreadOptions = PSThreadOptions.UseCurrentThread;
            rs.Open();
            using (var ps = PowerShell.Create())
            {
                ps.Runspace = rs;
                var cmd = new Command(args[0], false);
                for (int i = 1; i + 1 < args.Length; i += 2)
                {
                    string name = args[i].TrimStart('-');
                    string value = args[i + 1];
                    if (value == "$true") cmd.Parameters.Add(name, true);
                    else cmd.Parameters.Add(name, value);
                }
                ps.Commands.AddCommand(cmd);
                ps.Streams.Information.DataAdded += (s, e) => Console.WriteLine(ps.Streams.Information[e.Index].MessageData);
                ps.Streams.Error.DataAdded += (s, e) => Console.Error.WriteLine("ERROR: " + ps.Streams.Error[e.Index]);
                ps.Streams.Warning.DataAdded += (s, e) => Console.WriteLine("WARNING: " + ps.Streams.Warning[e.Index]);
                var output = new PSDataCollection<PSObject>();
                output.DataAdded += (s, e) => Console.WriteLine(output[e.Index]);
                try
                {
                    ps.Invoke(null, output);
                }
                catch (RuntimeException ex)
                {
                    Console.Error.WriteLine("FAILED: " + ex.Message);
                    Console.Error.WriteLine(ex.ErrorRecord != null ? ex.ErrorRecord.ScriptStackTrace : "");
                    return 1;
                }
                return ps.HadErrors ? 1 : 0;
            }
        }
    }
}
