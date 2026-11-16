// Measurement 5's logging plugin: a probe for a throwaway KeePass, not part of the product. It
// appends a line per event to a log file: every trigger event KeePass raises (through
// EcasTriggerSystem.RaisingEvent, by the name of its identifier) and every file event of the main
// window, each with a timestamp, the database's path, PwDatabase.Modified, a fingerprint of the
// database in memory (entries, deletion records, entries of the group KeePassGate) and the length
// and SHA-256 prefix of the file on disk at that moment.
//
// Settings are read at load from DeskLog.ini beside the DLL, one key=value per line:
//   log=<path>      the log file (required)
//   marker=true     at the first FileSavingPre that comes while a synchronize is running (between
//                   the trigger events SynchronizingDatabaseFile and SynchronizedDatabaseFile, or
//                   nested in another save's FileSavingPre), add one entry "KPG marker" with the
//                   string KPG.Marker to the group KeePassGate of that database, and log it
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using KeePass.Ecas;
using KeePass.Forms;
using KeePass.Plugins;
using KeePassLib;
using KeePassLib.Security;

namespace DeskLog
{
    public sealed class DeskLogExt : Plugin
    {
        private IPluginHost _host;
        private string _log;
        private bool _marker;
        private bool _inSync;
        private int _saveDepth;
        private bool _markerAdded;
        private readonly Dictionary<PwUuid, string> _names = new Dictionary<PwUuid, string>();

        public override bool Initialize(IPluginHost host)
        {
            _host = host;
            var ini = Path.Combine(Path.GetDirectoryName(typeof(DeskLogExt).Assembly.Location), "DeskLog.ini");
            if (File.Exists(ini))
            {
                foreach (var line in File.ReadAllLines(ini))
                {
                    var i = line.IndexOf('=');
                    if (i < 0) continue;
                    var k = line.Substring(0, i).Trim();
                    var v = line.Substring(i + 1).Trim();
                    if (k == "log") _log = v;
                    if (k == "marker") _marker = v == "true";
                }
            }
            if (_log == null) return false;

            var ids = typeof(EcasEvent).Assembly.GetType("KeePass.Ecas.EcasEventIDs");
            if (ids != null)
                foreach (var f in ids.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType == typeof(PwUuid)) _names[(PwUuid)f.GetValue(null)] = f.Name;

            KeePass.Program.TriggerSystem.RaisingEvent += OnTrigger;
            var mf = host.MainWindow;
            mf.FileCreated += (s, e) => Write("FileCreated", e.Database, "");
            mf.FileOpened += (s, e) => Write("FileOpened", e.Database, "");
            mf.FileClosingPre += (s, e) => Write("FileClosingPre", e.Database, "flags=" + e.Flags);
            mf.FileClosingPost += (s, e) => Write("FileClosingPost", e.Database, "flags=" + e.Flags);
            mf.FileClosed += (s, e) => Write("FileClosed", null, "path=" + e.IOConnectionInfo.Path + " flags=" + e.Flags);
            mf.FileSavingPre += OnSavingPre;
            mf.FileSaving += (s, e) => Write("FileSaving", e.Database, "saveAs=" + e.SaveAs + " copy=" + e.Copy + " guid=" + Short(e.EventGuid));
            mf.FileSaved += (s, e) =>
            {
                _saveDepth = Math.Max(0, _saveDepth - 1);
                Write("FileSaved", e.Database, "success=" + e.Success + " guid=" + Short(e.EventGuid));
            };
            Write("Initialize", null, "marker=" + _marker + " triggers.enabled=" + KeePass.Program.TriggerSystem.Enabled);
            return true;
        }

        private void OnTrigger(object sender, EcasRaisingEventArgs e)
        {
            string name;
            if (!_names.TryGetValue(e.Event.Type, out name)) name = e.Event.Type.ToHexString();
            if (name == "SynchronizingDatabaseFile") { _inSync = true; _markerAdded = false; }
            PwDatabase pd = null;
            try { pd = e.Properties.Get<PwDatabase>(EcasProperty.Database); } catch (Exception) { }
            Write("Trigger." + name, pd, "");
            if (name == "SynchronizedDatabaseFile") { _inSync = false; _saveDepth = 0; }
        }

        private void OnSavingPre(object sender, FileSavingEventArgs e)
        {
            _saveDepth++;
            Write("FileSavingPre", e.Database, "saveAs=" + e.SaveAs + " copy=" + e.Copy + " guid=" + Short(e.EventGuid) + " depth=" + _saveDepth + " inSync=" + _inSync);
            if (_marker && !_markerAdded && (_inSync || _saveDepth > 1))
            {
                _markerAdded = true;
                var pd = e.Database;
                var g = FindGroup(pd.RootGroup, "KeePassGate") ?? pd.RootGroup;
                var pe = new PwEntry(true, true);
                pe.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "KPG marker"));
                pe.Strings.Set("KPG.Marker", new ProtectedString(false, "added at FileSavingPre " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
                g.AddEntry(pe, true, true);
                pd.Modified = true;
                Write("MarkerAdded", pd, "group=" + g.Name);
            }
        }

        private static PwGroup FindGroup(PwGroup root, string name)
        {
            if (root == null) return null;
            if (root.Name == name) return root;
            return root.GetGroups(true).FirstOrDefault(x => x.Name == name);
        }

        private static string Short(Guid g) { return g.ToString("N").Substring(0, 8); }

        private void Write(string what, PwDatabase pd, string extra)
        {
            try
            {
                if (pd == null && _host != null && _host.MainWindow != null) pd = _host.MainWindow.ActiveDatabase;
                string fp = "db=none";
                if (pd != null)
                {
                    var path = pd.IOConnectionInfo != null ? pd.IOConnectionInfo.Path : "";
                    if (pd.IsOpen)
                    {
                        var kg = FindGroup(pd.RootGroup, "KeePassGate");
                        fp = "db=" + Path.GetFileName(path) + " open=1 modified=" + (pd.Modified ? 1 : 0)
                            + " entries=" + pd.RootGroup.GetEntries(true).UCount
                            + " records=" + pd.DeletedObjects.UCount
                            + " kpg=" + (kg != null ? kg.Entries.UCount.ToString(CultureInfo.InvariantCulture) : "-")
                            + " disk=" + Disk(path);
                    }
                    else fp = "db=" + Path.GetFileName(path) + " open=0 disk=" + Disk(path);
                }
                var line = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + what + " " + fp
                    + (extra.Length > 0 ? " " + extra : "");
                File.AppendAllText(_log, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(_log, "log failed: " + ex.Message + Environment.NewLine); } catch (Exception) { }
            }
        }

        private static string Disk(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
                var b = File.ReadAllBytes(path);
                using (var s = SHA256.Create())
                    return b.Length + ":" + BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").Substring(0, 10).ToLowerInvariant();
            }
            catch (IOException) { return "unreadable"; }
        }

        public override void Terminate()
        {
            Write("Terminate", null, "");
        }
    }
}
