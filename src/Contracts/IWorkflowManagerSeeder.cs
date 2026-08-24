namespace LoomKit.Workflows.Contracts;

public interface IWorkflowManagerSeeder
{
    Task SeedActivities(IWorkflowManager workflowManager);
}
