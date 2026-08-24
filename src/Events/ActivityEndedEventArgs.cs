using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Events;

public class ActivityEndedEventArgs : EventArgs
{
    public required string QueueName { get; init; }
    public required string ConsumerName { get; init; }
    public required ActivitySchedule ActivitySchedule { get; init; }
}
