using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The plugin's own window in every state that changes its shape.
    /// </summary>
    /// <remarks>
    /// The one window here that is about the plugin rather than about a database, and the only one
    /// an operator opens when nothing is unlocked. It sizes itself to what it has to say, so what
    /// varies is how many bands it grows: a gate that is not listening says one more thing than one
    /// that is, incidents are a row that is usually not there, and what OK last did is a line that
    /// starts empty.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class GateStatusGallery
    {
        [Test]
        public void Screenshot()
        {
            // Serving, nothing wrong, nobody has pressed anything: the state the window is in
            // almost every time it is opened.
            Shoot("status-01-listening", Gate(true, 51100), null);

            // Not serving, and nothing on record about why: the state a gate is in when something
            // stopped it that never reported itself.
            Shoot("status-02-not-listening", Gate(false, 0), null);

            // Not serving, with the reason. This is the picture that has to carry the most: what
            // went wrong, that only a restart changes it, and that every setting on the window
            // still does what it says.
            Shoot("status-03-refused-the-port", Refused("127.0.0.1:51100 could not be taken: "
                + "Only one usage of each socket address is normally permitted"), null);

            // Something went wrong somewhere else in the plugin and was counted. The row is not
            // there the rest of the time, so this is the only picture that shows it.
            Shoot("status-04-incidents", Gate(true, 51100), Incidents(3));

            // What OK says once it has been pressed and the port was taken.
            Shoot("status-05-after-ok", Gate(true, 51100), null, window => window.Keep());

            // And once it has been pressed with a port nothing will accept. The line under the
            // table is the same one, and it has to carry a refusal as well as a result.
            Shoot("status-06-a-port-not-remembered", Gate(true, 51100), null, window => window.Keep(), "no");
        }

        private static void Shoot(string name, IGateStatus gate, GateIncidents incidents)
        {
            Shoot(name, gate, incidents, null, null);
        }

        private static void Shoot(
            string name, IGateStatus gate, GateIncidents incidents, System.Action<GateStatusForm> arrange)
        {
            Shoot(name, gate, incidents, arrange, null);
        }

        private static void Shoot(
            string name,
            IGateStatus gate,
            GateIncidents incidents,
            System.Action<GateStatusForm> arrange,
            string refusal)
        {
            using (var window = new GateStatusForm(gate, incidents, port => refusal, 51100))
            {
                if (arrange != null)
                {
                    arrange(window);
                }

                Gallery.Shot(window, name);
            }
        }

        private static IGateStatus Gate(bool listening, int port)
        {
            return new FixedGate(listening, port);
        }

        private static IGateStatus Refused(string why)
        {
            return new FixedGate(false, 0) { Refusal = why };
        }

        private static GateIncidents Incidents(int count)
        {
            var incidents = new GateIncidents(null);

            for (var i = 0; i < count; i++)
            {
                incidents.Record("something the plugin did not expect", new System.InvalidOperationException("no"));
            }

            return incidents;
        }

        /// <summary>A gate that says what a picture needs it to say, and listens to nothing.</summary>
        private sealed class FixedGate : IGateStatus
        {
            public FixedGate(bool listening, int port)
            {
                IsListening = listening;
                Port = port;
            }

            public bool IsListening { get; private set; }

            public int Port { get; private set; }

            public string Refusal { get; set; }
        }
    }
}
