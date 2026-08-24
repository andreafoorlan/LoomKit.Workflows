using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Tests.Fixtures;

// Every test that exercises the manager needs the same start/run/stop scaffolding - this centralizes
// it so a change to teardown (e.g. a timeout) doesn't need to be repeated per test.
internal static class WorkflowManagerTestHarness
{
    public static async Task RunAsync(IWorkflowManager manager, Func<Task> body)
    {
        await manager.StartAsync(CancellationToken.None);

        try
        {
            await body();
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }
}
