namespace LoomKit.Workflows.Models;

// Opaque token used to resume a paused workflow activity.
// Value is a GUID communicated externally (e.g. in an email link URL).
// Action is an internal label used by activity handlers in if/switch to decide the next step.
public record ResumeToken
{
    public string Value { get; init; } = Guid.NewGuid().ToString();
    public required string Action { get; init; }
}
