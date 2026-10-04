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
    /// does: it reads nothing a rule is about. It is not written down either, as the status route
    /// is not: no rule was asked, and unlike a pairing, a renewal or a client's revocation of
    /// itself, which declare `none` too and are written as admissions, it changes no admission.
    ///
    /// **The keys are the ones the operator sees**, so a program and the person running KeePass can
    /// name the same client in the same words. `clientKey` is the key the operator knows the client
    /// by, which is the operator's ruling of 2026-10-16: the admission key of the certificate it
    /// paired with, kept through every renewal, which every count and naming of clients on the KPG
    /// tab goes by and each row of the Clients page names. `admissionKey` is the certificate on
    /// this connection, which the Clients page revokes, and which changes with each renewal. Both
    /// stay, because each answers a question the other cannot, and every line of the audit names
    /// both under these same two names.
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
            if (!client.IsPaired
                || string.IsNullOrEmpty(client.AdmissionKey)
                || string.IsNullOrEmpty(client.ClientKey)
                || !client.Expires.HasValue)
            {
                // The router refuses a caller with no admission before any handler runs, so this
                // is a defect if it is reached, and it is answered as the caller would be anyway.
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            var json = new JsonWriter();
            json.Text("subject", client.Subject);
            json.Text("clientKey", client.ClientKey);
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
