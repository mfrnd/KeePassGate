using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using KeePassGate.Pipeline;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The document a request driven without a transport was answered with, whichever way its
    /// handler handed the answer over.
    /// </summary>
    /// <remarks>
    /// A handler answers with a document handed over whole, which `GateContext.Json` holds, or
    /// with content that is written as it is sent, which `GateContext.Content` holds and nothing
    /// has run yet. A test that drives the router has no transport to run that content, so this
    /// does what the transport does with it: runs it, once, and takes what it wrote as the
    /// answer. A read of an entry or of a field reads its values there and nowhere else, so a
    /// test helper asks this of every context it hands back, whether or not the test goes on to
    /// read the document.
    ///
    /// **A HEAD is answered without its content**, as `HttpResponse.Write` answers a HEAD, so
    /// content handed over for a HEAD is never run here either and the document is empty.
    /// </remarks>
    public static class GateAnswers
    {
        private static readonly ConditionalWeakTable<GateContext, string> Written =
            new ConditionalWeakTable<GateContext, string>();

        /// <summary>
        /// The answer's document: what was handed over whole, or what the answer's content
        /// writes, run the first time this is asked and kept for every asking after it.
        /// </summary>
        public static string Document(this GateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return context.Content == null ? context.Json : Written.GetValue(context, Render);
        }

        private static string Render(GateContext context)
        {
            if (string.Equals(context.Method, "HEAD", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            {
                context.Content(text);
                return text.ToString();
            }
        }
    }
}
