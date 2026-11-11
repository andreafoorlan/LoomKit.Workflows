using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Workflows.Tests.Fixtures;

// Fails on every attempt and records when it was attempted, so a test can measure the gap between
// two attempts (the retry delay).
public sealed record FlakyActivity : Activity<TestWorkflowContext>;

public sealed class FlakyActivityHandler : IActivityHandler<FlakyActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        FlakyActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Attempts.Enqueue(DateTime.UtcNow);

        throw new InvalidOperationException("flaky");
    }
}

// Counts how many times a scope-owned service was disposed asynchronously.
public sealed class DisposalLog
{
    private int _asyncDisposals;

    public int AsyncDisposals => Volatile.Read(ref _asyncDisposals);

    public void RecordAsyncDisposal() => Interlocked.Increment(ref _asyncDisposals);
}

// Implements ONLY IAsyncDisposable (no IDisposable), like a pooled DbContext lease or an async
// client. The DI container can only dispose it through DisposeAsync on the scope: a synchronous
// scope.Dispose() throws InvalidOperationException for such a service.
public sealed class AsyncOnlyDisposable(DisposalLog log) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        log.RecordAsyncDisposal();

        return ValueTask.CompletedTask;
    }
}

public sealed record ScopedDependencyActivity : Activity<TestWorkflowContext>;

public sealed class ScopedDependencyActivityHandler(AsyncOnlyDisposable dependency)
    : IActivityHandler<ScopedDependencyActivity, TestWorkflowContext>
{
    // held only so the scope creates (and therefore owns) the service
    private readonly AsyncOnlyDisposable _dependency = dependency;

    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        ScopedDependencyActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("scoped");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowEnd<TestWorkflowContext> { Context = context });
    }
}

// Collects every log entry so a test can assert on what was (not) logged.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(
        System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> entries,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue((logLevel, $"{category}: {formatter(state, exception)}"));
    }
}
