using System;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Says that an admission record has landed in a database, or that a client's own revocation
    /// has, so that anything drawn from the records can read them again.
    /// </summary>
    /// <remarks>
    /// **The clients page is the reason this exists.** Records are read from the live database on
    /// every connection and never cached, so the gate needs telling nothing: a record that lands
    /// takes effect on the next request with nothing reloaded. A page is not a request. It draws
    /// what it read when it was last asked to, and the only things that asked were a page coming
    /// forward and a revocation, so an operator who paired a client while looking at the clients
    /// page watched the pairing succeed and the list not change. Switching away and back was the
    /// whole of the remedy, which is a thing nobody should have to know.
    ///
    /// **Said rather than polled.** A timer would have to run whenever that page is showing, ask
    /// the database on every tick for a list that changes a few times a year, and still answer
    /// late by up to its own interval. What the writer knows exactly is the moment a record is on
    /// the disk, and that is one line at the end of the one method that puts it there.
    ///
    /// **It carries nothing, deliberately.** A page reads its own database when it hears this, so
    /// naming the database in the event would only let a page decide not to do something it can do
    /// for nothing. What it must not do is read somebody else's database, and it cannot: it has
    /// only its own.
    /// </remarks>
    public interface IAdmissionNotice
    {
        /// <summary>
        /// Raised once a record is written, saved, and read back from the database it went into,
        /// and once a client's revocation of itself is saved and verified.
        /// </summary>
        /// <remarks>
        /// On the user interface thread, because the write it follows is marshaled there and this
        /// is raised inside that same call. A handler may touch controls directly.
        /// </remarks>
        event EventHandler Landed;
    }

    /// <summary>
    /// The one of these the plugin owns, handed to the writer to raise and to every panel to
    /// listen to.
    /// </summary>
    /// <remarks>
    /// The writer is given <see cref="Raise"/> as a plain action rather than this object, because
    /// what a writer needs to know about drawing is that something wants telling. It already takes
    /// `redraw` on the same terms.
    /// </remarks>
    public sealed class AdmissionNotice : IAdmissionNotice
    {
        public event EventHandler Landed;

        /// <summary>Says a record landed, to whoever is listening.</summary>
        public void Raise()
        {
            var listening = Landed;
            if (listening != null)
            {
                listening(this, EventArgs.Empty);
            }
        }
    }
}
