using System;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Builds real KeePass dialogs in the test process. KeePass constructs its settings dialog
    /// without touching the running application, which is what lets these tests exercise the real
    /// control tree without a KeePass on the desktop.
    /// </summary>
    internal static class KeePassDialogs
    {
        public static Form NewDatabaseSettingsForm()
        {
            var type = typeof(Plugin).Assembly.GetType(KeePassContract.DatabaseSettingsFormTypeName, false);
            if (type == null)
            {
                throw new InvalidOperationException(
                    KeePassContract.DatabaseSettingsFormTypeName + " is not in this KeePass build.");
            }

            return (Form)Activator.CreateInstance(type);
        }

        /// <summary>
        /// Tells the dialog which database it is editing, the way KeePass does before showing it.
        /// </summary>
        public static void InitializeFor(Form dialog, PwDatabase database)
        {
            var initialize = dialog.GetType().GetMethod("InitEx", new[] { typeof(bool), typeof(PwDatabase) });
            if (initialize == null)
            {
                throw new InvalidOperationException(
                    "The settings dialog no longer takes a database through InitEx(bool, PwDatabase).");
            }

            initialize.Invoke(dialog, new object[] { false, database });
        }

        /// <summary>The dialog's own OK handler, which is where its pages are written back.</summary>
        private const string OkHandlerName = "OnBtnOK";

        /// <summary>Closes the dialog as its OK does, which is what commits the tab.</summary>
        public static void Accept(Form dialog)
        {
            Close(dialog, DialogResult.OK);
        }

        /// <summary>
        /// Runs KeePass's own OK handler, which is what writes the dialog's own pages back.
        /// </summary>
        /// <remarks>
        /// A different gesture from `Accept`, and both are real. `Accept` raises `FormClosed`,
        /// which is what the plugin's tab waits for; this is what KeePass does first, and it is
        /// where the recycle bin choice, the history limits and the cipher reach the database.
        ///
        /// Invoked by reflection because it is private, and safe on a dialog that was never shown:
        /// it reads its own controls and writes to the database, and calls nothing that puts a
        /// window on the desktop. A dialog that was never loaded therefore reads exactly as one an
        /// operator opened on a database with none of these things set, which is the state under
        /// test.
        /// </remarks>
        public static void PressOk(Form dialog)
        {
            var handler = dialog.GetType().GetMethod(
                OkHandlerName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (handler == null)
            {
                throw new InvalidOperationException(
                    dialog.GetType().FullName + " no longer writes its pages back in " + OkHandlerName + ".");
            }

            handler.Invoke(dialog, new object[] { null, EventArgs.Empty });
        }

        /// <summary>Closes the dialog as its Cancel does, which is what throws the tab away.</summary>
        public static void Cancel(Form dialog)
        {
            Close(dialog, DialogResult.Cancel);
        }

        /// <summary>
        /// Sets the result and raises the closing event, without showing anything.
        /// </summary>
        /// <remarks>
        /// **`Form.Close` is not usable here.** A form that was never displayed is disposed by it
        /// without `FormClosed` ever being raised, and `FormClosed` is what the tab watches for.
        /// These dialogs are deliberately never shown: `Show` throws inside a message pump outside
        /// a running KeePass, and WinForms answers with a dialog that blocks the run until
        /// something kills it. So the event is raised directly, which is the one thing about this
        /// that is simulated. The tier below, which starts a real KeePass and presses the real
        /// button, is where nothing is.
        /// </remarks>
        private static void Close(Form dialog, DialogResult result)
        {
            dialog.DialogResult = result;

            var raise = typeof(Form).GetMethod(
                "OnFormClosed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(FormClosedEventArgs) },
                null);

            if (raise == null)
            {
                throw new InvalidOperationException("Form.OnFormClosed is gone from this framework.");
            }

            raise.Invoke(dialog, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
        }
    }
}
