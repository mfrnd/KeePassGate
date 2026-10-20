namespace KeePassGate.Pipeline
{
    /// <summary>Whatever answers a request, once the transport has stopped being involved.</summary>
    /// <remarks>
    /// One method, because the transport has exactly one question. What is behind it has been a
    /// switch over two paths and is now a document, and the transport did not have to know either
    /// time, which is the whole reason this is an interface rather than a call.
    /// </remarks>
    public interface IGateHandler
    {
        void Handle(IGateContext context);
    }
}
