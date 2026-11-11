using LoomKit.Workflows.Abstracts;

namespace LoomKit.Workflows.Tests.Fixtures;

// Extends the library's WorkflowContext base (instead of implementing IWorkflowContext directly) so
// this test suite also exercises that convenience type end-to-end.
public sealed record TestWorkflowContext : WorkflowContext
{
    // Mutated in place (not via `with`) so every activity sharing this context reference appends to
    // the same list - lets a test observe the full execution order across multiple dispatches.
    public List<string> Trace { get; init; } = [];

    // Timestamp of every attempt made by FlakyActivity. A thread-safe queue (not a List) because the
    // consumer's background loop appends while the test thread polls Count.
    public System.Collections.Concurrent.ConcurrentQueue<DateTime> Attempts { get; init; } = new();
}
