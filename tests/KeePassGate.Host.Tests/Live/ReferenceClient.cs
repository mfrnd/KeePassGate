using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// PyKeePassGate run as a program, through `uv` from the repository, the way the Client task
    /// runs it.
    /// </summary>
    /// <remarks>
    /// **One copy, for the two fixtures that run it.** `LiveReferenceClientTests` holds the client
    /// to the plugin, and `DocumentationScreenshots` films it for the moving picture, and both have
    /// to start it, feed it a line, read both streams to the end and stop it if it hangs. Two
    /// copies of that would be two places to get the order of those wrong in.
    ///
    /// **Through `uv run --frozen`**, so what runs is what the lockfile pins and not a copy.
    /// </remarks>
    internal static class ReferenceClient
    {
        /// <summary>
        /// One program `uv` runs, from start to exit, with one line on standard input if given and
        /// nothing otherwise.
        /// </summary>
        /// <param name="environment">
        /// Variables to set for the program, over the ones this process has, or null for none.
        /// </param>
        public static Run Execute(
            string arguments, TimeSpan timeout, string input, IDictionary<string, string> environment = null)
        {
            Process process;
            try
            {
                process = Start(arguments, environment);
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                Assert.Ignore("uv was not found, and PyKeePassGate is run through it: " + error.Message);
                throw;
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (input != null)
                {
                    process.StandardInput.WriteLine(input);
                }

                process.StandardInput.Close();

                try
                {
                    return Finish(process, output, errors, timeout);
                }
                finally
                {
                    End(process);
                }
            }
        }

        /// <summary>
        /// `uv run --frozen` and what it is to run, from the repository, with every stream
        /// redirected.
        /// </summary>
        public static Process Start(string arguments, IDictionary<string, string> environment = null)
        {
            var info = new ProcessStartInfo
            {
                FileName = "uv",
                Arguments = "run --frozen " + arguments,
                WorkingDirectory = Repository.Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (environment != null)
            {
                foreach (var variable in environment)
                {
                    info.EnvironmentVariables[variable.Key] = variable.Value;
                }
            }

            return Process.Start(info);
        }

        /// <summary>Waits for a program to exit, and answers with what it printed.</summary>
        public static Run Finish(Process process, Task<string> output, Task<string> errors, TimeSpan timeout)
        {
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                return new Run(int.MinValue, string.Empty, "still running after " + timeout.TotalSeconds + "s");
            }

            // The overload without a timeout, which is the one that waits for the redirected
            // streams to be read to their end as well.
            process.WaitForExit();
            return new Run(process.ExitCode, output.Result, errors.Result);
        }

        /// <summary>
        /// Ends a program that is still running, which only a failure leaves, and every process it
        /// started.
        /// </summary>
        /// <remarks>
        /// **The program is `uv`, and the client is its child**, which `Process.Kill` leaves running
        /// on this framework: measured, the Python process outlived the kill of the `uv` above it,
        /// holding its connection to KeePass and the pipes this process is still reading to their
        /// end. So the whole tree is ended, by `taskkill`, which the framework's own `Kill` cannot
        /// do before .NET Core 3, and the `uv` process alone only where that could not be run.
        /// </remarks>
        // cspell:ignore taskkill
        public static void End(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    if (!EndTree(process.Id))
                    {
                        process.Kill();
                    }

                    process.WaitForExit(10000);
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
            {
                // Gone already, or going.
            }
        }

        /// <summary>
        /// Ends a process and every process under it, and answers whether `taskkill` could be run.
        /// </summary>
        private static bool EndTree(int processId)
        {
            var info = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/T /F /PID " + processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var killing = Process.Start(info))
                {
                    killing.StandardOutput.ReadToEnd();
                    killing.StandardError.ReadToEnd();
                    return killing.WaitForExit(10000);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        /// <summary>What one run of a program printed, and how it ended.</summary>
        internal sealed class Run
        {
            public Run(int exitCode, string output, string errors)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
                Errors = errors ?? string.Empty;
            }

            public int ExitCode { get; }

            public string Output { get; }

            public string Errors { get; }

            public override string ToString()
            {
                return "exit " + ExitCode + Environment.NewLine + Output + Errors;
            }
        }
    }
}
