// Measurement 3's writing plugin: a probe for a throwaway KeePass, not part of the product. A timer
// on KeePass's own thread looks every 300 ms for a command file beside the DLL, DeskWrite.cmd, runs
// the one line it holds and writes what happened to DeskWrite.done. The timer runs while a modal
// dialog is open, since the dialog's message loop runs it, which is how a write lands on a live
// entry underneath an open entry dialog. Commands, the entry named by its title in the active
// database:
//
//   set|<title>|<name>|<value>        entry.Strings.Set(name, plain value), the entry touched as
//                                    modified, the database marked modified, the list refreshed
//   setsave|<title>|<name>|<value>    the same, then saved through MainForm.SaveDatabase
//   dump|<title>|<file>               what the entry holds in memory, and the database's Modified
//   save                             MainForm.SaveDatabase on the active database
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassLib;
using KeePassLib.Security;

namespace DeskWrite
{
    public sealed class DeskWriteExt : Plugin
    {
        private IPluginHost _host;
        private Timer _timer;
        private string _dir;

        public override bool Initialize(IPluginHost host)
        {
            _host = host;
            _dir = Path.GetDirectoryName(typeof(DeskWriteExt).Assembly.Location);
            _timer = new Timer { Interval = 300 };
            _timer.Tick += OnTick;
            _timer.Start();
            return true;
        }

        public override void Terminate()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
        }

        private void OnTick(object sender, EventArgs e)
        {
            var cmd = Path.Combine(_dir, "DeskWrite.cmd");
            if (!File.Exists(cmd)) return;
            string line;
            try { line = File.ReadAllText(cmd).Trim(); File.Delete(cmd); }
            catch (IOException) { return; }
            string result;
            try { result = Run(line); }
            catch (Exception ex) { result = "FAILED " + ex.GetType().Name + ": " + ex.Message; }
            File.WriteAllText(Path.Combine(_dir, "DeskWrite.done"),
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + line + " => " + result);
        }

        private PwEntry Find(PwDatabase pd, string title)
        {
            return pd.RootGroup.GetEntries(true).FirstOrDefault(x => x.Strings.ReadSafe(PwDefs.TitleField) == title);
        }

        private string Run(string line)
        {
            var p = line.Split('|');
            var mf = _host.MainWindow;
            var pd = mf.ActiveDatabase;
            switch (p[0])
            {
                case "set":
                case "setsave":
                {
                    var pe = Find(pd, p[1]);
                    if (pe == null) return "no entry " + p[1];
                    bool existed = pe.Strings.Exists(p[2]);
                    pe.Strings.Set(p[2], new ProtectedString(false, p[3]));
                    pe.Touch(true, false);
                    pd.Modified = true;
                    mf.UpdateUI(false, null, false, null, true, null, true);
                    if (p[0] == "setsave") mf.SaveDatabase(pd, null);
                    return (existed ? "changed " : "added ") + p[2] + " on " + p[1] + "; modified=" + pd.Modified;
                }
                case "dump":
                {
                    var pe = Find(pd, p[1]);
                    if (pe == null) return "no entry " + p[1];
                    File.WriteAllText(p[2], Describe(pe) + "database modified=" + pd.Modified + Environment.NewLine);
                    return "dumped";
                }
                case "save":
                    mf.SaveDatabase(pd, null);
                    return "saved; modified=" + pd.Modified;
            }
            return "unknown command";
        }

        private static string Describe(PwEntry pe)
        {
            var sb = new StringBuilder();
            foreach (var kv in pe.Strings) sb.AppendLine("S " + kv.Key + " p=" + (kv.Value.IsProtected ? 1 : 0) + " " + kv.Value.ReadString());
            foreach (var kv in pe.Binaries) sb.AppendLine("A " + kv.Key + " len=" + kv.Value.Length);
            sb.AppendLine("tags [" + string.Join(";", pe.Tags) + "]");
            foreach (var kv in pe.CustomData) sb.AppendLine("CD " + kv.Key + " = " + kv.Value);
            sb.AppendLine("history " + pe.History.UCount);
            sb.AppendLine("modified " + pe.LastModificationTime.ToString("o", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
