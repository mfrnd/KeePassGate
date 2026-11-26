using System;
using KeePass.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Hands the writer KeePass's word about each save it makes, whoever asked for it.
    /// </summary>
    /// <remarks>
    /// **KeePass's own event after a save**, `FileSaved`, which reports whether the save landed,
    /// and is raised after a save that failed on a database closed inside it as well. The writer
    /// keeps that word for its own save alone, for the single ending in which the file cannot be
    /// asked: a database closed inside the save, <see cref="DatabaseWriter.AfterSave"/>. This only
    /// listens, and keeps a defect there from reaching KeePass's save.
    /// </remarks>
    public sealed class SaveReports : IDisposable
    {
        private readonly MainForm _mainWindow;
        private readonly DatabaseWriter _writer;
        private readonly GateIncidents _incidents;
        private bool _started;

        public SaveReports(MainForm mainWindow, DatabaseWriter writer, GateIncidents incidents)
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

            _mainWindow.FileSaved += OnSaved;
            _started = true;
        }

        public void Dispose()
        {
            if (!_started)
            {
                return;
            }

            _mainWindow.FileSaved -= OnSaved;
            _started = false;
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
                // A kind of catch-all `DESIGN.md` §141 lists, and this is why: it runs on KeePass's
                // own thread from KeePass's own event, raised outside the try `SaveDatabase` keeps
                // around the save, so an exception escaping here would reach WinForms' own error
                // dialog at the end of the operator's save.
                if (_incidents != null)
                {
                    _incidents.Record("KeePass's report of a save could not be kept", error);
                }
            }
        }
    }
}
