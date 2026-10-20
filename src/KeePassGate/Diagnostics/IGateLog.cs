using System;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Where the plugin says what it did. This is not the audit log: the audit log records
    /// authorization decisions and lives outside the database, whereas this records the plugin
    /// working or failing to.
    /// </summary>
    /// <remarks>
    /// Every implementation must be total. A logger that throws inside an exception handler would
    /// turn a contained failure into one that reaches KeePass.
    /// </remarks>
    public interface IGateLog
    {
        void Info(string message);

        /// <summary>Something was refused. Expected, and usually somebody else's fault.</summary>
        void Warn(string message);

        /// <summary>Something the plugin does went wrong in a way it recognizes.</summary>
        void Error(string message, Exception error);

        /// <summary>
        /// Something nobody predicted, caught only because letting it escape would end KeePass.
        /// </summary>
        /// <remarks>
        /// Its own level rather than another error, because these are the only lines in the log
        /// that mean "there is a defect here or KeePass has changed under us". They are counted on
        /// the plugin's own window under the Tools menu as well, so that a failure inside a socket
        /// thread is not something only a log reader ever learns about.
        /// </remarks>
        void Unexpected(string message, Exception error);
    }
}
