namespace LoomKit.Workflows.Tests.Fixtures;

// InProcessActivityQueue's consumers run on background Tasks with their own polling interval, so
// tests can't observe workflow progress synchronously - this polls a condition instead of sleeping a
// fixed amount, keeping tests both fast (short poll interval) and non-flaky (bounded timeout).
internal static class Wait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(10);
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                return condition();

            await Task.Delay(interval);
        }

        return true;
    }
}
