using System;
using System.Drawing;
using System.Reflection;
using KeePass.UI;
using KeePassGate.Diagnostics;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Pictures of KeePass's own that a window of this plugin's shows, read from the resources
    /// KeePass keeps to itself.
    /// </summary>
    /// <remarks>
    /// **KeePass's rather than drawn or shipped here**, for the reason the tab's icon is KeePass's,
    /// `GateIcon`: the same size, palette and style as every picture in the window around it, and a
    /// KeePass that restyles its pictures restyles these too. Reached by name, since the class is
    /// KeePass's own, and the names are `KeePassContract`'s, which the host tests pin against the
    /// KeePass installed.
    /// </remarks>
    internal static class KeePassPictures
    {
        /// <summary>
        /// The green pair of arrows KeePass draws for Synchronize, at the display's scale, as a
        /// picture of the caller's own; null where KeePass has no such picture.
        /// </summary>
        /// <remarks>
        /// **Scaled as KeePass scales its own pictures**, `DpiUtil.ScaleImage`, at the scale of the
        /// display, which is the factor every inset of the pairing dialog is converted by, and
        /// left as it is at the size it is drawn at, as KeePass leaves it. **A copy**, since what
        /// is answered is let go with the dialog that shows it, and the picture read let go here,
        /// each reading being a new picture, `KeePassContract.RefreshPictureProperty`.
        /// **Nothing thrown for a picture that is not there**: a KeePass that renamed it leaves
        /// Refresh with no picture, which the host tests tell of.
        /// </remarks>
        internal static Image Refresh()
        {
            Bitmap read;
            try
            {
                var resources = typeof(KeePass.Program).Assembly.GetType(KeePassContract.ResourcesTypeName, false);
                var property = resources == null
                    ? null
                    : resources.GetProperty(KeePassContract.RefreshPictureProperty, BindingFlags.Static | BindingFlags.NonPublic);
                read = property == null ? null : property.GetValue(null, null) as Bitmap;
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                return null;
            }

            if (read == null)
            {
                return null;
            }

            using (read)
            {
                try
                {
                    // A copy of the pixels as they are, the clear ones among them, where drawing
                    // the picture into a new picture makes every clear pixel black and clear.
                    var shown = DpiUtil.ScaleImage(read, false);
                    return ReferenceEquals(shown, read) ? read.Clone(new Rectangle(Point.Empty, read.Size), read.PixelFormat) : shown;
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    return null;
                }
            }
        }
    }
}
