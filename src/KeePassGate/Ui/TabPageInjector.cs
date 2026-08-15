using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Puts one tab page on somebody else's tabbed dialog, and takes it off again.
    /// </summary>
    /// <remarks>
    /// Two properties matter more than the mechanics. It is idempotent, because a dialog can be
    /// announced more than once and a second KPG tab would be a defect a user sees. And it is
    /// total: every failure is logged and reported, never thrown, because this runs on the UI
    /// thread while a KeePass dialog is being constructed, and an exception there takes the
    /// password manager with it.
    /// </remarks>
    public sealed class TabPageInjector : IFormDecorator
    {
        private readonly IGatePanelFactory _panels;
        private readonly IGateLog _log;
        private readonly string _tabControlName;
        private readonly object _stateLock = new object();
        private readonly List<Attachment> _attachments = new List<Attachment>();

        private readonly string _pageText;
        private readonly string _pageName;
        private readonly Func<Image> _icon;

        public TabPageInjector(IGatePanelFactory panels, IGateLog log)
            : this(panels, log, KeePassContract.DatabaseSettingsTabControlName)
        {
        }

        public TabPageInjector(IGatePanelFactory panels, IGateLog log, string tabControlName)
            : this(panels, log, tabControlName, GateTab.Text, GateTab.PageControlName)
        {
        }

        /// <param name="pageText">
        /// The caption on the tab strip, which is `GateName.OnATab` for both of the pages this
        /// plugin builds. A parameter rather than a constant so that the two pages are built by one
        /// class without it deciding what either is called; what tells them apart is
        /// <paramref name="pageName"/>, which is what everything here finds them by.
        /// </param>
        public TabPageInjector(
            IGatePanelFactory panels, IGateLog log, string tabControlName, string pageText, string pageName)
            : this(panels, log, tabControlName, pageText, pageName, null)
        {
        }

        /// <param name="icon">
        /// Asked for a fresh picture to put on the tab, or null for none. A function rather than an
        /// image because the icons belong to KeePass's main window, which exists only inside a
        /// running KeePass, and every test of this runs without one.
        /// </param>
        public TabPageInjector(
            IGatePanelFactory panels,
            IGateLog log,
            string tabControlName,
            string pageText,
            string pageName,
            Func<Image> icon)
        {
            if (panels == null)
            {
                throw new ArgumentNullException(nameof(panels));
            }

            _panels = panels;
            _log = log ?? NullGateLog.Instance;
            _tabControlName = tabControlName;
            _pageText = pageText;
            _pageName = pageName;
            _icon = icon;
        }

        /// <summary>How many forms currently carry the tab. For tests and for shutdown assertions.</summary>
        public int AttachedCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _attachments.Count;
                }
            }
        }

        public bool IsAttached(Form form)
        {
            lock (_stateLock)
            {
                return Find(form) != null;
            }
        }

        public bool Attach(Form form)
        {
            if (form == null)
            {
                _log.Warn("attach refused: no form");
                return false;
            }

            try
            {
                lock (_stateLock)
                {
                    if (Find(form) != null)
                    {
                        return true;
                    }
                }

                var tabControl = FindTabControl(form);
                if (tabControl == null)
                {
                    _log.Warn("attach refused: no tab control on " + Describe(form));
                    return false;
                }

                var page = BuildPage(form);
                try
                {
                    tabControl.TabPages.Add(page);
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    page.Dispose();
                    throw;
                }

                var attachment = new Attachment(form, tabControl, page);
                lock (_stateLock)
                {
                    // Another thread could have attached while the page was being built. Losing
                    // that race must not leave two tabs behind.
                    if (Find(form) != null)
                    {
                        RemovePage(tabControl, page);
                        return true;
                    }

                    _attachments.Add(attachment);
                }

                // Decorated only once the attachment is recorded. Putting a picture on the tab is
                // the one part of this that calls into GDI+, and a failure there used to escape
                // with the page already on the control and nothing tracking it: no detach could
                // take it off again, and the next announcement of the same form added a second.
                Decorate(attachment);
                attachment.Subscribe(OnFormClosed);
                _log.Info("attached the " + _pageText + " tab to " + Describe(form));
                return true;
            }
            catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfReflection(error))
            {
                _log.Error("attach failed on " + Describe(form), error);
                return false;
            }
        }

        public void Detach(Form form)
        {
            if (form == null)
            {
                return;
            }

            Attachment attachment;
            lock (_stateLock)
            {
                attachment = Find(form);
                if (attachment == null)
                {
                    return;
                }

                _attachments.Remove(attachment);
            }

            Release(attachment);
        }

        public void DetachAll()
        {
            List<Attachment> attachments;
            lock (_stateLock)
            {
                attachments = new List<Attachment>(_attachments);
                _attachments.Clear();
            }

            foreach (var attachment in attachments)
            {
                Release(attachment);
            }
        }

        private static string Describe(Form form)
        {
            if (form == null)
            {
                return "(no form)";
            }

            try
            {
                return form.GetType().FullName;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                return "(unnameable form)";
            }
        }

        private static void RemovePage(TabControl tabControl, TabPage page)
        {
            try
            {
                if (tabControl != null && tabControl.TabPages.Contains(page))
                {
                    tabControl.TabPages.Remove(page);
                }
            }
            finally
            {
                page.Dispose();
            }
        }

        private Attachment Find(Form form)
        {
            foreach (var attachment in _attachments)
            {
                if (ReferenceEquals(attachment.Form, form))
                {
                    return attachment;
                }
            }

            return null;
        }

        private TabControl FindTabControl(Form form)
        {
            var named = ControlTree.FindByName<TabControl>(form, _tabControlName);
            if (named != null)
            {
                return named;
            }

            var first = ControlTree.FindFirst<TabControl>(form);
            if (first != null)
            {
                _log.Warn("tab control '" + _tabControlName + "' not found on " + Describe(form) +
                          ", falling back to '" + first.Name + "'");
            }

            return first;
        }

        private TabPage BuildPage(Form form)
        {
            // Left to right whatever the dialog is, because what the pages draw is this plugin's
            // own untranslated English and the names it keeps. A KeePass translation reading
            // right to left sets the dialog to it before the page exists, and inherited, Unicode's
            // bidirectional algorithm moves a leading run of digits, or a leading or trailing '-'
            // or '_', to the other end of a name: `2-ops` and `ops-2` draw alike, `-ops` and
            // `ops-` trade places, and whatever is chosen by what it reads is another name. The
            // tab strip is the dialog's and stays mirrored.
            var page = new TabPage(_pageText)
            {
                Name = _pageName,
                RightToLeft = RightToLeft.No,
                UseVisualStyleBackColor = true
            };

            try
            {
                var panel = _panels.CreatePanel(form);
                if (panel == null)
                {
                    throw new InvalidOperationException("The panel factory returned nothing.");
                }

                try
                {
                    // Spaced as the dialog around it is. KeePass scaled that dialog's own pages by
                    // its font as it built them, before this page existed, so its pass never
                    // reaches this one: `Spacing.ScaleOf` is that same factor, applied here once.
                    var scale = Spacing.ScaleOf(form);
                    if (scale != Spacing.AsDesigned)
                    {
                        panel.Scale(scale);
                    }

                    panel.Dock = DockStyle.Fill;
                    page.Controls.Add(panel);
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    // Let go of here, because disposing the page below reaches only what the page
                    // holds, and a panel it refused is not in it. Left alone, a panel that took
                    // hold of anything outside itself as it was built, as the settings tab takes
                    // the plugin's notice of every pairing, stays reachable through that for the
                    // rest of the process and goes on answering it on a page nobody will see.
                    if (!page.Controls.Contains(panel))
                    {
                        panel.Dispose();
                    }

                    throw;
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                page.Dispose();
                throw;
            }

            return page;
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            // Defense in depth: the host is expected to announce the closing window, and this
            // catches the case where it does not, so a closed dialog never keeps the tab alive.
            Detach(sender as Form);
        }

        /// <summary>
        /// Puts the plugin's icon on the page it just added.
        /// </summary>
        /// <remarks>
        /// **A tab strip shows icons only if the whole control has an image list**, and none of the
        /// three KeePass dialogs this lands on brings one. So one is made here and given to the
        /// control, which makes every tab on that dialog a little taller: there is no way in
        /// WinForms to put a picture on one tab and leave the others as they were. KeePass's own
        /// pages keep an image index of -1 and so stay wordless, which is the intended look rather
        /// than an accident of ours.
        ///
        /// Taken away again on detach, along with the list, so a dialog this plugin decorated is
        /// left exactly as it was found. A control that already has a list keeps it and is given
        /// no picture: an entry added to somebody else's list would outlive the detach, and
        /// taking it out again would renumber whatever that plugin had already put there.
        /// </remarks>
        private void Decorate(Attachment attachment)
        {
            if (_icon == null)
            {
                return;
            }

            try
            {
                if (attachment.TabControl.ImageList != null)
                {
                    return;
                }

                // Disposed here, because `ImageList.Images.Add` copies the picture into the
                // native list and leaves the original ours: dropping the reference instead left
                // one GDI+ bitmap and its handle for the garbage collector to release, every
                // time the user opened the dialog, inside the password manager's own process.
                using (var picture = _icon())
                {
                    if (picture == null)
                    {
                        return;
                    }

                    var ours = new ImageList
                    {
                        ColorDepth = ColorDepth.Depth32Bit,
                        ImageSize = new Size(picture.Width, picture.Height)
                    };

                    // Recorded before anything that can fail, so that detaching disposes it
                    // whatever happens next. Handing it to the control is what can throw: the
                    // setter keeps the list and then makes its native handle, and recorded after
                    // that, a throw there left the list on KeePass's control with nothing that
                    // would ever take it off or let it go. `StripIcon` takes it off the control
                    // only when the control still holds it.
                    attachment.Icons = ours;
                    ours.Images.Add(picture);
                    attachment.TabControl.ImageList = ours;
                    attachment.Page.ImageIndex = 0;
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A tab without a picture on it is a tab. Said and not raised, because this runs
                // while a KeePass dialog is being built.
                _log.Warn("the " + _pageText + " tab could not be given an icon: " + error.Message);
            }
        }

        private void StripIcon(Attachment attachment)
        {
            var ours = attachment.Icons;
            if (ours == null)
            {
                return;
            }

            attachment.Icons = null;
            try
            {
                // Only if the control is still holding ours. Something else may have put its
                // own list there since the attach, and taking that away would be this plugin
                // doing to another plugin exactly what the rule above says it must not do.
                if (ReferenceEquals(attachment.TabControl.ImageList, ours))
                {
                    attachment.TabControl.ImageList = null;
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("could not give the image list back on " + Describe(attachment.Form), error);
            }

            ours.Dispose();
        }

        private void Release(Attachment attachment)
        {
            try
            {
                attachment.Unsubscribe(OnFormClosed);
                RemovePage(attachment.TabControl, attachment.Page);
                _log.Info("detached the " + _pageText + " tab from " + Describe(attachment.Form));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("detach failed on " + Describe(attachment.Form), error);
            }
            finally
            {
                // In a `finally`, because the caller has already taken this attachment out of
                // the list: a throw from `RemovePage` would otherwise leave our image list on
                // KeePass's control with nothing left that could ever take it off.
                StripIcon(attachment);
            }
        }

        private sealed class Attachment
        {
            private readonly Form _form;
            private readonly TabControl _tabControl;
            private readonly TabPage _page;
            private bool _subscribed;

            public Attachment(Form form, TabControl tabControl, TabPage page)
            {
                _form = form;
                _tabControl = tabControl;
                _page = page;
            }

            public Form Form
            {
                get { return _form; }
            }

            public TabControl TabControl
            {
                get { return _tabControl; }
            }

            public TabPage Page
            {
                get { return _page; }
            }

            /// <summary>The image list this injector gave the control, or null if it had one.</summary>
            public ImageList Icons { get; set; }

            public void Subscribe(FormClosedEventHandler handler)
            {
                if (_subscribed)
                {
                    return;
                }

                _form.FormClosed += handler;
                _subscribed = true;
            }

            public void Unsubscribe(FormClosedEventHandler handler)
            {
                if (!_subscribed)
                {
                    return;
                }

                _form.FormClosed -= handler;
                _subscribed = false;
            }
        }
    }
}
