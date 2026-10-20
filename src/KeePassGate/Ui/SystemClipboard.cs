using System.IO;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>The real one.</summary>
    internal sealed class SystemClipboard : IClipboard
    {
        public static readonly IClipboard Instance = new SystemClipboard();

        /// <summary>
        /// Asks Windows not to keep this copy in clipboard history or send it to another machine.
        /// </summary>
        /// <remarks>
        /// The presence of the format is the whole of it; what is under it is never read, so the
        /// plugin's name goes there the way KeePass puts its own product name there.
        /// See https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats
        /// </remarks>
        private const string NoMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";

        /// <summary>
        /// The older convention, honored by third-party clipboard managers rather than by Windows.
        /// </summary>
        private const string ViewerIgnore = "Clipboard Viewer Ignore";

        /// <summary>Windows 10 and later: a `DWORD` of zero keeps it out of the history.</summary>
        private const string NoHistory = "CanIncludeInClipboardHistory";

        /// <summary>And out of the clipboard shared between a person's machines.</summary>
        private const string NoCloud = "CanUploadToCloudClipboard";

        private const int Retries = 10;

        private const int PauseBetweenRetries = 100;

        private SystemClipboard()
        {
        }

        /// <remarks>
        /// `SetDataObject` rather than `SetText`, for the two arguments it takes. `copy: true`
        /// renders the text onto the clipboard now instead of leaving a promise to produce it when
        /// somebody asks, so what was copied survives KeePass exiting. The retries are because the
        /// clipboard is one system-wide lock that any process can be holding at the moment we ask,
        /// and the documented answer to that is to ask again.
        /// See https://learn.microsoft.com/dotnet/api/system.windows.forms.clipboard.setdataobject
        /// </remarks>
        public void Set(string text)
        {
            Clipboard.SetDataObject(Excluded(text), true, Retries, PauseBetweenRetries);
        }

        /// <summary>
        /// The text, with the formats that ask Windows and any clipboard manager to let it pass.
        /// </summary>
        /// <remarks>
        /// **What this buys is narrow and worth stating.** Clipboard history keeps the last
        /// twenty-five entries until they are cleared by hand, and the cloud clipboard sends them
        /// to a person's other machines, so a code copied out of this box outlived its ten minutes
        /// in two places nobody looks at. Excluding it is what KeePass does with everything it
        /// copies, and these are the same four format names KeePass uses.
        ///
        /// **What it does not buy is any defense against a program running as this user.** The
        /// clipboard is readable by all of them and `DESIGN.md` §125 concedes that throughout. This
        /// stops the copy being written down where nobody asked it to be, and nothing more.
        ///
        /// Built apart from the copy so a test can read the formats without taking the developer's
        /// clipboard away, which is the same reason the panel takes its clipboard through a seam.
        /// </remarks>
        internal static DataObject Excluded(string text)
        {
            var data = new DataObject();
            data.SetText(text ?? string.Empty);

            // The two that need only be present, so the value under them is never read.
            data.SetData(NoMonitorProcessing, false, GateName.Product);
            data.SetData(ViewerIgnore, false, GateName.Product);

            // The two that are read as four bytes. A managed data object carries raw bytes when
            // what it is given is a stream, and serializes anything else, so these are streams.
            data.SetData(NoHistory, false, No());
            data.SetData(NoCloud, false, No());

            return data;
        }

        /// <summary>Four bytes of zero, which is what those two formats read as "do not".</summary>
        private static MemoryStream No()
        {
            return new MemoryStream(new byte[4]);
        }
    }
}
