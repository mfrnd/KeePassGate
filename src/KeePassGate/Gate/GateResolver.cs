using System;
using System.Collections.Generic;
using KeePassGate.Diagnostics;

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

        private GateResolution(GateOutcome outcome, IGateTarget target)
        {
            _outcome = outcome;
            _target = target;
        }

        /// <summary>
        /// Gives back the target this resolution is holding.
        /// </summary>
        /// <remarks>
        /// A resolved target arrives with a reference taken on the caller's behalf, so a
        /// connection that has finished with it says so. Nothing else is owned here.
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

        public static GateResolution NoName()
        {
            return new GateResolution(GateOutcome.NoName, null);
        }

        public static GateResolution NoMatch()
        {
            return new GateResolution(GateOutcome.NoMatch, null);
        }

        public static GateResolution Ambiguous()
        {
            return new GateResolution(GateOutcome.Ambiguous, null);
        }

        public static GateResolution Resolved(IGateTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return new GateResolution(GateOutcome.Resolved, target);
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
    /// Pairing tokens are resolved here too, once there are any. They live in plugin memory and are
    /// tried after the databases. TODO(step 8): that is step 8.
    /// </remarks>
    public sealed class GateResolver
    {
        private readonly IGateTargets _targets;
        private readonly IGateLog _log;

        public GateResolver(IGateTargets targets, IGateLog log)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            _targets = targets;
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

                IGateTarget matched = null;
                IGateTarget kept = null;
                var matches = 0;

                try
                {
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

                if (matches == 0)
                {
                    return GateResolution.NoMatch();
                }

                if (matches > 1)
                {
                    _log.Warn("refusing to serve: " + matches +
                              " open databases answer to the same name, so they cannot be told apart");
                    return GateResolution.Ambiguous();
                }

                kept = matched;
                return GateResolution.Resolved(matched);
            }
            finally
            {
                // Every candidate arrived holding a reference. The one being returned keeps its
                // own, released when the resolution is disposed; the rest go back here, including
                // the match on an ambiguous name, which is refused rather than served.
                foreach (var candidate in candidates)
                {
                    if (candidate != null && !ReferenceEquals(candidate, kept))
                    {
                        candidate.Dispose();
                    }
                }
            }
        }
    }
}
