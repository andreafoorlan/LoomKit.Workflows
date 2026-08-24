using System.Diagnostics;
using System.Reflection;

namespace LoomKit.Workflows.Telemetry;

internal static class WorkflowsActivitySource
{
    private static readonly AssemblyName AssemblyName = typeof(WorkflowsActivitySource).Assembly.GetName();

    internal static readonly ActivitySource Source = new(
        AssemblyName.Name ?? "LoomKit.Workflows",
        AssemblyName.Version?.ToString() ?? "1.0.0");
}
