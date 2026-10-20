using System;
using KeePassGate.Gate;
using KeePassGate.Pipeline;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// What the transport knows about a pairing connection that the router cannot work out for
    /// itself: which invitation it arrived on, which certificate it was answered with, and where it
    /// came from.
    /// </summary>
    /// <remarks>
    /// It travels in <see cref="IGateContext.Items"/> rather than on the context itself, which is
    /// what that dictionary is for: a stage of the pipeline leaving something for a later one. The
    /// seam stays the shape `DESIGN.md` §103 describes, and a route that knows nothing about
    /// pairing cannot accidentally read a token's worth of state.
    ///
    /// The token itself is not here. The handler asks the invitation for it when it needs it, so
    /// the secret is never sitting in a per request dictionary that a later stage might log.
    /// </remarks>
    public sealed class PairingConnection
    {
        public const string Key = "kpg.pairing";

        private readonly Invitation _invitation;
        private readonly IGateTarget _target;
        private readonly byte[] _binding;
        private readonly byte[] _nonce;
        private readonly string _peer;

        public PairingConnection(
            Invitation invitation, IGateTarget target, byte[] binding, byte[] nonce, string peer)
        {
            if (invitation == null)
            {
                throw new ArgumentNullException(nameof(invitation));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (nonce == null)
            {
                throw new ArgumentNullException(nameof(nonce));
            }

            _invitation = invitation;
            _target = target;
            _binding = (byte[])binding.Clone();
            _nonce = (byte[])nonce.Clone();
            _peer = peer ?? string.Empty;
        }

        public Invitation Invitation
        {
            get { return _invitation; }
        }

        /// <summary>
        /// The database the invitation named, which is the only one it can ever pair with. Carried
        /// here rather than looked up again, so that a database closing mid pairing fails the
        /// pairing instead of quietly pairing with something else.
        /// </summary>
        public IGateTarget Target
        {
            get { return _target; }
        }

        /// <summary>RFC 5929 `tls-server-end-point` over the certificate this connection was served with.</summary>
        public byte[] Binding
        {
            get { return (byte[])_binding.Clone(); }
        }

        /// <summary>The nonce out of the name this connection arrived on.</summary>
        public byte[] Nonce
        {
            get { return (byte[])_nonce.Clone(); }
        }

        /// <summary>Where it came from, for the approval dialog and the log. Never trusted for anything.</summary>
        public string Peer
        {
            get { return _peer; }
        }

        public void PutOn(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Items[Key] = this;
        }

        /// <summary>Null on every connection that named a database rather than an invitation.</summary>
        public static PairingConnection Of(IGateContext context)
        {
            if (context == null)
            {
                return null;
            }

            object found;
            return context.Items.TryGetValue(Key, out found) ? found as PairingConnection : null;
        }
    }
}
