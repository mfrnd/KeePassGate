using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Spec;
using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// `getGroupAccess` and `getEntryAccess`: what the caller may do at one group or entry, profile
    /// by profile, in the notation the KPG tab shows a rule in.
    /// </summary>
    /// <remarks>
    /// **The router has already answered, which is what declaring `any` buys.** It found the node,
    /// asked the rules about every verb and switch for each profile the caller holds, wrote the
    /// decision down, and refused with the `404` an absent node gets unless some profile is granted
    /// some verb there. What is left is saying what it found, which it left on the request from the
    /// same walk as the decision, so the answer and the line in the audit cannot disagree.
    ///
    /// **One mask per profile, because a switch belongs to its rule.** One profile's `R` and
    /// another's `P` do not add up to reading a protected value, and a single mask made of both
    /// would claim that they do. A profile granted nothing at the node is eight dots rather than
    /// left out, so the members are exactly the profiles `getIdentity` reports as held.
    /// </remarks>
    public static class AccessOperation
    {
        public static void Handle(IGateContext context)
        {
            object found;
            var grants = context.Items.TryGetValue(SpecRouter.GrantsItem, out found)
                ? found as IReadOnlyList<KeyValuePair<string, AclGrant>>
                : null;

            if (grants == null)
            {
                // Only an operation the router decided as `any` arrives with them, so arriving
                // without is a defect, and it is answered as every absence is.
                context.Respond(404, HttpResponse.ErrorBody(SpecRouter.UnreachableError));
                return;
            }

            var json = new JsonWriter();
            json.Object("grants");

            foreach (var grant in grants)
            {
                json.Text(grant.Key, VerbMask.Of(grant.Value));
            }

            json.End();
            context.Respond(200, json.Done());
        }
    }
}
