using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Gate
{
    /// <summary>Why a connection was or was not matched to a database.</summary>
    public enum GateOutcome
    {
        /// <summary>The ClientHello carried no server name at all.</summary>
        NoName,

        /// <summary>A name, and no open database that could have produced it.</summary>
        NoMatch,

        /// <summary>
        /// More than one database answered to it. Two copies of the same file are open, and they
        /// are genuinely indistinguishable, so neither is served.
        /// </summary>
        Ambiguous,

        /// <summary>Exactly one database, which is the only case that goes any further.</summary>
        Resolved
    }

    public sealed class GateResolution : IDisposable
    {
        private readonly GateOutcome _outcome;
        private readonly IGateTarget _target;
        private readonly Invitation _invitation;

        private GateResolution(GateOutcome outcome, IGateTarget target, Invitation invitation)
        {
            _outcome = outcome;
            _target = target;
            _invitation = invitation;
        }

        /// <summary>Gives back the target this resolution is holding.</summary>
        /// <remarks>
        /// A resolved target arrives with a reference taken for the connection that asked, so the
        /// connection says when it has finished. Nothing else here is owned.
        /// </remarks>
        public void Dispose()
        {
            if (_target != null)
            {
                _target.Dispose();
            }
        }

        public GateOutcome Outcome
        {
            get { return _outcome; }
        }

        /// <summary>Null unless exactly one database matched.</summary>
        public IGateTarget Target
        {
            get { return _target; }
        }

        public bool IsResolved
        {
            get { return _outcome == GateOutcome.Resolved; }
        }

        /// <summary>
        /// The invitation this connection came in on, or null when it named a database directly.
        /// Only a pairing connection has one, and only a pairing connection may reach `/pair/*`.
        /// </summary>
        public Invitation Invitation
        {
            get { return _invitation; }
        }

        public bool IsPairing
        {
            get { return _invitation != null; }
        }

        public static GateResolution NoName()
        {
            return new GateResolution(GateOutcome.NoName, null, null);
        }

        public static GateResolution NoMatch()
        {
            return new GateResolution(GateOutcome.NoMatch, null, null);
        }

        public static GateResolution Ambiguous()
        {
            return new GateResolution(GateOutcome.Ambiguous, null, null);
        }

        public static GateResolution Resolved(IGateTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return new GateResolution(GateOutcome.Resolved, target, null);
        }

        /// <summary>A connection that named an invitation rather than a database.</summary>
        public static GateResolution Invited(IGateTarget target, Invitation invitation)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (invitation == null)
            {
                throw new ArgumentNullException(nameof(invitation));
            }

            return new GateResolution(GateOutcome.Resolved, target, invitation);
        }
    }

    /// <summary>
    /// Turns the name in a ClientHello into the one database it belongs to, before any TLS work.
    /// </summary>
    /// <remarks>
    /// Two properties matter more than the code. **Every candidate is tried**, with no early exit,
    /// so the work does not depend on which database matched or on how far down the list it was.
    /// And **nothing is answered**: a name that resolves to nothing drops the connection with no
    /// response, so an expired name is indistinguishable from one that never existed, and an
    /// attacker who cannot compute a tag never reaches the handshake, a certificate, or a dialog.
    ///
    /// **Pairing tokens are resolved here too**, from plugin memory, and an expired one leaves the
    /// set before anything is compared. That is what makes an invitation that has run out
    /// indistinguishable from one that never existed, and it is also why an invitation lost to a
    /// restart looks exactly like an attack: the connection is dropped either way, and nothing on
    /// the wire says which it was.
    /// </remarks>
    public sealed class GateResolver
    {
        private readonly IGateTargets _targets;
        private readonly IInvitations _invitations;
        private readonly IGateLog _log;

        public GateResolver(IGateTargets targets, IGateLog log)
            : this(targets, null, log)
        {
        }

        public GateResolver(IGateTargets targets, IInvitations invitations, IGateLog log)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            _targets = targets;
            _invitations = invitations;
            _log = log ?? NullGateLog.Instance;
        }

        public GateResolution Resolve(string serverName)
        {
            if (string.IsNullOrEmpty(serverName))
            {
                return GateResolution.NoName();
            }

            IReadOnlyList<IGateTarget> candidates;
            try
            {
                candidates = _targets.Current();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the open databases could not be listed", error);
                return GateResolution.NoMatch();
            }

            if (candidates == null || candidates.Count == 0)
            {
                return GateResolution.NoMatch();
            }

            // The one being handed out, and nothing else. Declared out here so the release below
            // can tell it from the rest, and set only on the path that actually returns it: a name
            // that matched and is still refused has to give its reference back like any other.
            IGateTarget kept = null;

            try
            {
                IGateTarget matched = null;
                Invitation invited = null;
                var matches = 0;

                foreach (var candidate in candidates)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    var key = candidate.GateKey;
                    bool hit;
                    try
                    {
                        hit = SniName.Matches(serverName, key);
                    }
                    catch (Exception error) when (Expected.OfCryptography(error))
                    {
                        // A gate key of the wrong length is a database that cannot be addressed at
                        // all, rather than one that did not match this name.
                        hit = false;
                    }
                    finally
                    {
                        if (key != null)
                        {
                            Array.Clear(key, 0, key.Length);
                        }
                    }

                    if (!hit)
                    {
                        continue;
                    }

                    matches++;
                    if (matched == null)
                    {
                        matched = candidate;
                    }
                }

                foreach (var invitation in Invited(serverName))
                {
                    // Every candidate holding the invited database counts, exactly as every candidate
                    // whose gate key answers does above. A file and a copy of it open together share
                    // an authority, so both hold it, and stopping at the first would pair against
                    // whichever copy happens to be listed first rather than refusing the two.
                    var held = false;
                    foreach (var candidate in candidates)
                    {
                        if (candidate == null ||
                            !string.Equals(candidate.DatabaseId, invitation.DatabaseId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        held = true;
                        matches++;
                        if (matched == null)
                        {
                            matched = candidate;
                            invited = invitation;
                        }
                    }

                    if (!held)
                    {
                        // The invitation outlived the database being open. There is nothing to serve
                        // and nothing to say: it is dropped like any other name that resolves nowhere.
                        _log.Info("invitation " + invitation.Id + " named a database that is not open");
                    }
                }

                if (matches == 0)
                {
                    return GateResolution.NoMatch();
                }

                if (matches > 1)
                {
                    // `kept` stays null, so the match found above goes back with everything else.
                    // An ambiguous name is served by nobody, and a reference nobody hands out is a
                    // database whose host key and decrypted contents are never released.
                    _log.Warn("refusing to serve: " + matches +
                              " databases or invitations answer to the same name, so they cannot be told apart");
                    return GateResolution.Ambiguous();
                }

                kept = matched;
                return invited == null
                    ? GateResolution.Resolved(kept)
                    : GateResolution.Invited(kept, invited);
            }
            finally
            {
                // Every candidate arrived holding a reference taken for this caller. The one being
                // returned keeps its own, released when the resolution is disposed; the rest go back
                // here, including a match on an ambiguous name, which is refused rather than served.
                // codeql[cs/missed-using-statement] all but the one handed back are disposed, which no `using` can do
                foreach (var candidate in candidates.Where(
                    candidate => candidate != null && !ReferenceEquals(candidate, kept)))
                {
                    candidate.Dispose();
                }
            }
        }

        private IReadOnlyList<Invitation> Invited(string serverName)
        {
            if (_invitations == null)
            {
                return Array.Empty<Invitation>();
            }

            try
            {
                return _invitations.Matching(serverName) ?? Array.Empty<Invitation>();
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                _log.Error("the outstanding invitations could not be tried", error);
                return Array.Empty<Invitation>();
            }
        }
    }
}
