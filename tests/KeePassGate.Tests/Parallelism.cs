using NUnit.Framework;

// **Fixtures run side by side, and a fixture that touches the process says so.** One test after
// another left every core but one idle through the longest step of the gate, so fixtures run in
// parallel and the tests inside each one still run in order. What makes that safe is a mark rather
// than a hope: a fixture or test that reads or writes anything the whole process shares, such as
// an environment variable, `Trace.Listeners`, the time zone, a shown window and the focus with it,
// or the account's key directories, carries `[NonParallelizable]` and runs with nothing else.
[assembly: Parallelizable(ParallelScope.Fixtures)]
