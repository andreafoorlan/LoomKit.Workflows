using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Events;

public class ActivityScheduledEventArgs : EventArgs
{
    public required string QueueName { get; init; }
    public required ActivitySchedule ActivitySchedule { get; init; }
}
