using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// `getIdentity`: who the caller is, as the database it reached admitted it.
    /// </summary>
    /// <remarks>
    /// **Everything here was settled before the route was chosen.** The connection proved the
    /// certificate, found its admission record and read the database's list of profiles, and this
    /// says what that came to, which is why it declares `x-kpg-verb: none` as the status route
    /// does: it reads nothing a rule is about. It is not written down either, for the reason no
    /// operation declaring `none` is: no rule was asked.
    ///
    /// **The key is the one the operator sees.** It is the admission key the Clients page lists
    /// and revokes and every line of the audit names, so a program and the person running KeePass
    /// can name the same client in the same words.
    ///
    /// **The profiles come in two halves of one list.** `profiles` is what the record holds that
    /// the database still names, which is all the rules are ever asked about for this client, and
    /// `removedProfiles` is the rest, which grants nothing until the operator names it again. Both
    /// are this client's own record, approved for it when it was admitted, at pairing or at a
    /// renewal, so neither tells it anything about anybody else. What a rule grants at a node is
    /// not here: that is a question about a node, and the record says nothing about one.
    /// </remarks>
    public static class IdentityOperation
    {
        public static void Handle(IGateContext context)
        {
            var client = context.Client;
            if (!client.IsPaired || string.IsNullOrEmpty(client.AdmissionKey) || !client.Expires.HasValue)
            {
                // The router refuses a caller with no admission before any handler runs, so this
                // is a defect if it is reached, and it is answered as the caller would be anyway.
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            var json = new JsonWriter();
            json.Text("subject", client.Subject);
            json.Text("admissionKey", client.AdmissionKey);
            json.Text("expires", Moments.Wire(client.Expires.Value));

            json.Array("profiles");
            foreach (var profile in client.Profiles)
            {
                json.Item(profile);
            }

            json.End();

            json.Array("removedProfiles");
            foreach (var profile in client.Removed)
            {
                json.Item(profile);
            }

            json.End();

            context.Respond(200, json.Done());
        }
    }
}
