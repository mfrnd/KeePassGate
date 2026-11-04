using System;
using KeePass.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Has every save KeePass makes of a database carry the standing answers owed to it, whoever
    /// asked for the save.
    /// </summary>
    /// <remarks>
    /// **KeePass's own events either side of the save**: `FileSavingPre`, which `SaveDatabase` and
    /// `SaveDatabaseAs` raise before anything is written, and `FileSaved`, which reports whether
    /// the save landed. A synchronize saves through `SaveDatabase` once it has merged, and writes
    /// the other copy's file from the same database afterwards, so the answers reach both files.
    /// What is written and what is settled is <see cref="DatabaseWriter"/>'s, in
    /// <see cref="DatabaseWriter.BeforeSave"/> and <see cref="DatabaseWriter.AfterSave"/>; this
    /// only listens, and keeps a defect in either from reaching KeePass's save.
    /// </remarks>
    public sealed class OwedAnswerSaves : IDisposable
    {
        private readonly MainForm _mainWindow;
        private readonly DatabaseWriter _writer;
        private readonly GateIncidents _incidents;
        private bool _started;

        public OwedAnswerSaves(MainForm mainWindow, DatabaseWriter writer, GateIncidents incidents)
        {
            if (mainWindow == null)
            {
                throw new ArgumentNullException(nameof(mainWindow));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            _mainWindow = mainWindow;
            _writer = writer;
            _incidents = incidents;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _mainWindow.FileSavingPre += OnSaving;
            _mainWindow.FileSaved += OnSaved;
            _started = true;
        }

        public void Dispose()
        {
            if (!_started)
            {
                return;
            }

            _mainWindow.FileSavingPre -= OnSaving;
            _mainWindow.FileSaved -= OnSaved;
            _started = false;
        }

        private void OnSaving(object sender, FileSavingEventArgs e)
        {
            try
            {
                if (e != null)
                {
                    _writer.BeforeSave(e.Database, e.Copy);
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // A kind of catch-all `DESIGN.md` §141 lists, and this is why: it runs on KeePass's
                // own thread from KeePass's own event, raised outside the try `SaveDatabase` keeps
                // around the save, so an exception escaping here would stop the operator's save
                // before anything was written and then reach WinForms' own error dialog. The
                // answers stay owed.
                Record("the standing answers owed to a database could not be written before its save", error);
            }
        }

        private void OnSaved(object sender, FileSavedEventArgs e)
        {
            try
            {
                if (e != null)
                {
                    _writer.AfterSave(e.Database, e.Success);
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // As above, at the other end of the same save.
                Record("the standing answers a save carried could not be settled", error);
            }
        }

        private void Record(string what, Exception error)
        {
            if (_incidents != null)
            {
                _incidents.Record(what, error);
            }
        }
    }
}
