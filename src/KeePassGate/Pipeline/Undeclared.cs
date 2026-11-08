using KeePassGate.Spec;
using KeePassGate.Transport;

namespace KeePassGate.Pipeline
{
    /// <summary>What a caller is told about a path it may not have.</summary>
    /// <remarks>
    /// One place, because the whole property is that every path answers a given caller the same
    /// way. Three copies of this rule were three chances for one of them to be changed alone, and
    /// that is how the router and the two handlers came to disagree about it.
    ///
    /// An admitted client gets the `404` an undeclared route gets. Anybody else gets the `401`
    /// every route gets before a certificate is proved, so a caller the database does not admit
    /// cannot tell a route that exists from one that does not.
    ///
    /// **The `404` is the router's own words for a node it may not reach**, read from
    /// <see cref="SpecRouter.UnreachableError"/> rather than spelled again here, since an admitted
    /// client is meant to be unable to tell an undeclared path from a refused node, and two
    /// spellings of the answer are two chances for one of them to be changed alone.
    /// </remarks>
    public static class Undeclared
    {
        public static void Refuse(IGateContext context)
        {
            if (!context.Client.IsPaired)
            {
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            context.Respond(404, HttpResponse.ErrorBody(SpecRouter.UnreachableError));
        }
    }
}
