using System;
using System.IO;
using System.Security.AccessControl;
using System.Text;

namespace MonoProbe
{
    /// <summary>
    /// What `JsonLinesAudit` does to its file: append through a stream that asks for
    /// `FileSystemRights.AppendData` alone, read it while that stream is open, and roll it by
    /// deleting the previous `.1` and moving the file onto that name, with a reader still holding
    /// the file open with `FileShare.Delete`.
    /// </summary>
    internal static class AuditFileProbe
    {
        internal static void Run()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MonoProbe-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "audit.jsonl");
            try
            {
                Report.Step("audit.append-data-stream", () =>
                {
                    using (var stream = new FileStream(
                               path, FileMode.Append, FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
                    {
                        var line = Encoding.UTF8.GetBytes("{\"line\":1}\n");
                        stream.Write(line, 0, line.Length);
                    }
                });
                Report.Measure("audit.length-after-append", () => new FileInfo(path).Length);

                Report.Step("audit.append-data-stream-twice", () =>
                {
                    using (var stream = new FileStream(
                               path, FileMode.Append, FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
                    {
                        var line = Encoding.UTF8.GetBytes("{\"line\":2}\n");
                        stream.Write(line, 0, line.Length);
                    }
                });
                Report.Measure("audit.length-after-second", () => new FileInfo(path).Length);

                if (!File.Exists(path))
                {
                    // The plugin's way failed; write the file another way so the roll can still be asked.
                    File.WriteAllText(path, "{\"line\":1}\n");
                }

                Report.Measure("audit.roll-with-reader-open", () =>
                {
                    var previous = path + ".1";
                    File.WriteAllText(previous, "older\n");
                    using (var reader = new FileStream(
                               path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        File.Delete(previous);
                        File.Move(path, previous);
                        return "moved, reader still reads " + new StreamReader(reader).ReadLine();
                    }
                });

                // Whether this runtime reports a file's access rules at all, which the plugin does not
                // ask for but an operator sealing the directory, as `docs/` describes, relies on.
                Report.Measure("audit.get-access-control", () => File.GetAccessControl(path + ".1"),
                    security => security.GetType().FullName);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                    // Left in the temporary directory, which is where it was made.
                }
            }
        }
    }
}
