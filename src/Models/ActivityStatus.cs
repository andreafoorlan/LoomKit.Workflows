namespace LoomKit.Workflows.Models;

public class ActivityStatus
{
    public string? QueueName { get; set; }
    public string? ConsumerName { get; set; }

    // Stamped by the queue at EnqueueActivityScheduleAsync time, never touched afterwards (unlike
    // NextAt, which retries/re-pauses overwrite). Lets you compute "how long has this specific
    // schedule been sitting queued/paused", distinct from StartedAt below which only covers time
    // spent actually being consumed.
    public DateTime? EnqueuedAt { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
