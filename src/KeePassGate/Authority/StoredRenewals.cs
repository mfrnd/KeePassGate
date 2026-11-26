using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>Where the answer about a client's renewals comes from, `STORE.md` §618.</summary>
    internal enum RenewalCause
    {
        /// <summary>No version stands and none is lost: the operator is asked.</summary>
        None,

        /// <summary>The strictest standing version, every text in form.</summary>
        Stands,

        /// <summary>A version is lost: refused forever, whatever stands.</summary>
        Lost,

        /// <summary>A standing text is out of form: refused forever, whatever else stands.</summary>
        Unreadable,
    }

    /// <summary>
    /// What a reading of the store holds of the standing answers about a single client, `STORE.md`
    /// §618: the standing versions and the lost versions of `renew:` and the client key, and the
    /// answer they give together.
    /// </summary>
    /// <remarks>
    /// **Read with no clock.** The answer is a refusal forever, a refusal until a moment, or none;
    /// whether a refusal until a moment refuses is asked of <see cref="Refuses"/> with the moment
    /// of asking, where a renewal is answered and where the clients page draws a row, and nowhere
    /// else.
    /// </remarks>
    internal sealed class RenewalStanding
    {
        private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();

        private RenewalStanding(IReadOnlyList<string> standing, IReadOnlyList<string> lost, RenewalCause cause, RenewalRefusal answer)
        {
            Standing = standing;
            Lost = lost;
            Cause = cause;
            Answer = answer;
        }

        /// <summary>The identifiers of the standing versions, ascending, out of form among them.</summary>
        public IReadOnlyList<string> Standing { get; private set; }

        /// <summary>The identifiers of the lost versions, ascending.</summary>
        public IReadOnlyList<string> Lost { get; private set; }

        public RenewalCause Cause { get; private set; }

        /// <summary>
        /// The strictest answer: a refusal forever, also for a lost version and a text out of
        /// form; a refusal until the latest moment a standing version names; or null, where the
        /// operator is asked.
        /// </summary>
        public RenewalRefusal Answer { get; private set; }

        /// <summary>Whether a renewal asked for at this moment is refused without asking.</summary>
        public bool Refuses(DateTimeOffset now)
        {
            return Answer != null && Answer.Refuses(now);
        }

        /// <summary>
        /// What these standing versions and these lost versions give together: a lost version
        /// refuses forever, then a standing text out of form, then the strictest standing text,
        /// `refuse:forever` above the later moment above the earlier above `ask`.
        /// </summary>
        internal static RenewalStanding Of(IReadOnlyList<StoreNaming> standing, IReadOnlyList<string> lost)
        {
            var ids = standing.Select(version => version.Id).ToList();
            if (lost.Count > 0)
            {
                return new RenewalStanding(ids, lost, RenewalCause.Lost, RenewalRefusal.Forever());
            }

            if (standing.Count == 0)
            {
                return new RenewalStanding(ids, lost, RenewalCause.None, null);
            }

            var forever = false;
            string latest = null;
            DateTimeOffset until = DateTimeOffset.MinValue;
            foreach (var version in standing)
            {
                string doc = null;
                if (version.Members != null)
                {
                    version.Members.TryGetValue(StoreKinds.Doc, out doc);
                }

                DateTimeOffset moment;
                switch (RenewalText.Read(doc, out moment))
                {
                    case RenewalTextKind.OutOfForm:
                        return new RenewalStanding(ids, lost, RenewalCause.Unreadable, RenewalRefusal.Forever());
                    case RenewalTextKind.Forever:
                        forever = true;
                        break;
                    case RenewalTextKind.Until:
                        // Two moments are compared as the texts the operators chose, the later
                        // the stricter: no clock and no time of a write is read.
                        if (latest == null || string.CompareOrdinal(doc, latest) > 0)
                        {
                            latest = doc;
                            until = moment;
                        }

                        break;
                }
            }

            var answer = forever ? RenewalRefusal.Forever() : latest != null ? RenewalRefusal.Until(until) : null;
            return new RenewalStanding(ids, lost, RenewalCause.Stands, answer);
        }

        /// <summary>No answer about the client: what a client key out of form is given.</summary>
        internal static RenewalStanding None()
        {
            return new RenewalStanding(NoIds, NoIds, RenewalCause.None, null);
        }
    }

    /// <summary>
    /// The standing answers to renewals a reading of the store holds, `STORE.md` §618, worked out
    /// from the standing and the lost versions of the object `renew:` and each client key.
    /// </summary>
    /// <remarks>
    /// **An object out of form is the object of nothing.** A version of type `renew` whose object
    /// holds anything but 64 lowercase hexadecimal characters is a record, restated and counted by
    /// the room, and no answer is read from it.
    ///
    /// **Total.** Nothing a store holds makes this throw: a member a version lacks is a text out of
    /// form, which refuses its client.
    /// </remarks>
    internal static class StoredRenewals
    {
        /// <summary>What the reading holds about a client's renewals; no answer where it holds nothing or the key is out of form.</summary>
        public static RenewalStanding Of(StoreInForce inForce, string client)
        {
            string key;
            var objectText = RenewalText.ObjectOf(client);
            if (inForce == null || !RenewalText.ReadObject(objectText, out key))
            {
                return RenewalStanding.None();
            }

            return RenewalStanding.Of(inForce.VersionsOf(objectText), inForce.LostOf(objectText));
        }
    }
}
