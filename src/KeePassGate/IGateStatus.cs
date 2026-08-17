namespace KeePassGate
{
    // Deliberately in the root namespace and not in `Ui`. It is implemented by the transport and
    // read by the tab, and putting it under `Ui` made `GateListener` take a `using` on the user
    // interface to describe itself. The root is visible from every namespace in this assembly
    // without one.
    public interface IGateStatus
    {
        bool IsListening { get; }

        int Port { get; }
    }
}
