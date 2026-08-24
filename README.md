# LoomKit.Workflows

A workflow orchestration library for .NET built around the same job-queue model as [LoomKit.Jobs](https://github.com/andreafoorlan/LoomKit.Jobs): workflows are sequences of **activities**, placed on named **queues**, picked up by **consumers**, and processed through an extensible **middleware pipeline**. Unlike a plain job, an activity's handler decides what happens next — continue, pause and wait for an external signal, fork into parallel branches (optionally re-joining them), or end — so a whole multi-step process is orchestrated as a chain of independently-scheduled, independently-retryable steps instead of one long-running call. This is the multi-step-orchestration sibling of [LoomKit.Requests](https://github.com/andreafoorlan/LoomKit.Requests), [LoomKit.Notifications](https://github.com/andreafoorlan/LoomKit.Notifications), and LoomKit.Jobs: same dependency-injection and middleware shape, adapted to a process with branching and state instead of a single request/response or a single deferred unit of work.

> **Status:** early stage. The public API may still change between versions — pin a commit/tag if you depend on it.

## Features

- Named queues and consumers, wired together and started/stopped as a single `IHostedService`
- Handlers resolved from your DI container (`Microsoft.Extensions.DependencyInjection`)
- An optional middleware pipeline per consumer, configured at startup — built-in retry middleware included
- Branching workflows: `WorkflowFork` spawns independent parallel branches sharing the same `WorkflowId`
- Fork **re-join**: a fork can optionally also enqueue a join-point activity that waits for its branches (`WorkflowJoin`) to report in, deciding for itself when it has enough — including a branch's permanent failure, delivered as a first-class arrival so the join point never has to infer a branch's disappearance by polling
- Resume-token-based pausing: `WorkflowPause` suspends an activity until an external caller triggers a matching token (e.g. from a webhook or an email link)
- Bounded delayed continuation (`NextAt` on `WorkflowContinue`/`WorkflowPause`) for polling-with-timeout patterns
- Optional `WorkflowName` label (non-unique, unlike `WorkflowId`) for dedup/observability queries across concurrent instances of "the same kind" of workflow
- Built-in tracing via `System.Diagnostics.ActivitySource` (OpenTelemetry-compatible), including full trace-context propagation across the async/queue boundary
- Lifecycle events (`ActivityScheduled`/`ActivityStarted`/`ActivityEnded`/`ActivityException`) on the manager
- `CancellationToken` propagated end-to-end, from the consumer through every middleware down to the handler
- Extensible: bring your own `IActivityQueue`, `IActivityConsumer`, or `IWorkflowManager` implementation if the defaults don't fit

## Requirements

- .NET 10 or later
- Runtime dependencies: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Installation

### Via NuGet (recommended)

```bash
dotnet add package LoomKit.Workflows
```

Available on [nuget.org](https://www.nuget.org/packages/LoomKit.Workflows) once the first tagged release (`v1.0.0`) has been published — a package version is published automatically for every `vX.Y.Z` tag pushed to this repo.

If you'd rather build against the source directly instead (e.g. to track `main`, or to debug/modify the library alongside your app), two options:

### As a git submodule

```bash
git submodule add https://github.com/andreafoorlan/LoomKit.Workflows.git external/LoomKit.Workflows
cd external/LoomKit.Workflows
git checkout v1.0.0
cd ../..
git add external/LoomKit.Workflows
git commit -m "Add LoomKit.Workflows submodule pinned to v1.0.0"
```

Then reference the project from your solution/project:

```xml
<ProjectReference Include="..\external\LoomKit.Workflows\src\LoomKit.Workflows.csproj" />
```

When cloning a repository that already has this submodule:

```bash
git clone --recurse-submodules <your-repo-url>
# or, on an existing clone:
git submodule update --init --recursive
```

To move to a newer release later:

```bash
cd external/LoomKit.Workflows
git fetch --tags
git checkout v1.1.0
cd ../..
git add external/LoomKit.Workflows
git commit -m "Bump LoomKit.Workflows submodule to v1.1.0"
```

(`git submodule add -b <tag>` doesn't pin reliably since submodules track branches, not tags — `checkout` inside the submodule plus committing the resulting gitlink in the parent repo is what actually pins the commit.)

### Plain project reference

If you're vendoring the source directly instead of using a submodule:

```xml
<ProjectReference Include="..\path\to\LoomKit.Workflows\src\LoomKit.Workflows.csproj" />
```

## Core concepts

| Type | Purpose |
|---|---|
| `IWorkflowContext` | Immutable-ish state shared across all activities of one workflow instance. Carries `WorkflowId`, `CreatedAt`, `UpdatedAt`, plus whatever domain state you add. `WorkflowContext` is an optional abstract base record that pre-fills those three with sensible defaults, so a concrete context only declares its own fields. |
| `IActivity` / `IActivity<TCtx>` | Marker for one step, tied to a specific `IWorkflowContext` type. `Activity<TCtx>` is the abstract base record to extend — `ActivityId` is auto-generated and `PreviousActivityId` is injected automatically by the consumer. |
| `IActivityHandler<TActivity, TCtx>` | Implement one per activity type — executes the step and returns an `IWorkflowStep` describing what happens next. |
| `IActivityQueue` | Stores and delivers `ActivitySchedule` instances, including paused ones. `InProcessActivityQueue` is the built-in in-memory implementation. |
| `IActivityConsumer` | Background worker that dequeues activities from a queue and dispatches them to handlers via a middleware pipeline. |
| `IWorkflowManager` | The entry point your application code calls to start/inspect workflows — also the `IHostedService` that owns every queue and consumer. |
| `ActivityMiddleware<TActivity, TCtx>` | Optional cross-cutting behavior wrapped around a handler (logging, retry, ...). |
| `IWorkflowManagerSeeder` | Hook called once at manager startup to pre-start workflows. |

### `IWorkflowStep` — what happens next

An `IActivityHandler` returns one of these:

| Return type | Effect |
|---|---|
| `WorkflowContinue<TNext, TCtx>` | Enqueue `Next` and continue the workflow — immediately by default, or at a future `NextAt` for bounded polling/timeout patterns. |
| `WorkflowPause<TNext, TCtx>` | Enqueue `Next` in **paused** state; resume only when `TriggerResumeTokenAsync` is called with a matching `ResumeToken.Value`. Supports multiple tokens per pause point, each with its own `Action` label. |
| `WorkflowFork<TCtx>` | Enqueue every branch in `Branches` to run independently, sharing the same `WorkflowId`. Optionally also enqueues `JoinPointActivity`, paused, to collect `WorkflowJoin` arrivals from those branches — see [Fork & re-join](#fork--re-join). |
| `WorkflowJoin<TCtx>` | A branch's terminal signal when its fork has a join point: contributes `Context` to the fork's join point instead of just vanishing. |
| `WorkflowJoinWait<TNext, TCtx>` | Returned by a join-point activity's own handler when it decides it doesn't have enough arrivals yet — re-enqueues `Next` (typically itself, with updated internal bookkeeping) paused, waiting for more branches to report in. |
| `WorkflowEnd<TCtx>` | The workflow (or this branch) is complete; nothing further is enqueued. |
| `WorkflowFail<TCtx>` | A deliberate, final failure outcome, with an optional `Exception` — distinct from an unhandled exception (see [Explicit failure](#explicit-failure-workflowfail)). |

## Quick start

### 1. Define the workflow context

```csharp
public sealed record OrderContext : WorkflowContext
{
    public required string OrderId { get; init; }
    public string? PaymentId { get; init; }
}
```

(`WorkflowContext` pre-fills `WorkflowId`/`CreatedAt`/`UpdatedAt` — implement `IWorkflowContext` directly instead if you need full control over those.)

### 2. Define activities and handlers

```csharp
public sealed record ProcessPaymentActivity(decimal Amount) : Activity<OrderContext>;
public sealed record SendConfirmationActivity(string Email) : Activity<OrderContext>;

public sealed class ProcessPaymentHandler : IActivityHandler<ProcessPaymentActivity, OrderContext>
{
    private readonly IPaymentService _payments;

    public ProcessPaymentHandler(IPaymentService payments) => _payments = payments;

    public async Task<IWorkflowStep<IActivity<OrderContext>, OrderContext>> HandleAsync(
        ProcessPaymentActivity activity, OrderContext context, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        var paymentId = await _payments.ChargeAsync(context.OrderId, activity.Amount, cancellationToken);

        return new WorkflowContinue<SendConfirmationActivity, OrderContext>
        {
            Next = new SendConfirmationActivity("customer@example.com"),
            Context = context with { PaymentId = paymentId, UpdatedAt = DateTime.UtcNow }
        };
    }
}
```

### 3. Register handlers, queues, and consumers in DI

Handlers are plain DI services. Register them one by one:

```csharp
services.AddScoped<IActivityHandler<ProcessPaymentActivity, OrderContext>, ProcessPaymentHandler>();
services.AddScoped<IActivityHandler<SendConfirmationActivity, OrderContext>, SendConfirmationHandler>();
```

...or scan one or more assemblies for every closed `IActivityHandler<,>` implementation and register them all at once — no extra package required, this is built in:

```csharp
services.AddActivityHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(Program).Assembly);
```

Calling it more than once, or passing overlapping assemblies, won't produce duplicate registrations. It only picks up **closed, concrete** handler classes — an open-generic handler isn't discovered and must still be registered by hand.

Then wire up the manager itself:

```csharp
services.AddDefaultWorkflowManager(builder => builder
    .UseQueue<InProcessActivityQueue>("orders", q =>
    {
        q.MaxActivityRetries = 3;
        q.ActivityAwaitCheckInterval = 500;   // ms between queue polls
        q.ActivityRetryInterval = 5000;
    })
    .UseConsumer<ActivityConsumer>("orders-consumer", "orders", c =>
    {
        c.UseScopedServiceProvider = true;                 // one DI scope per activity
        c.UseActivityMiddleware(typeof(ActivityRetryMiddleware<,>));
    })
    .UseWorkflowManagerSeeder<OrderWorkflowSeeder>());
```

`AddDefaultWorkflowManager` registers `IWorkflowManager` as a singleton and as an `IHostedService` (not configurable — the host only manages hosted services as singletons), backed by the default `DefaultWorkflowManager`, `InProcessActivityQueue`, and `ActivityConsumer`.

### 4. Start a workflow

```csharp
public sealed class OrderService(IWorkflowManager workflowManager)
{
    public Task PlaceOrderAsync(string orderId, decimal amount, CancellationToken cancellationToken = default)
        => workflowManager.StartWorkflowAsync(
            queueName: "orders",
            firstActivity: new ProcessPaymentActivity(amount),
            context: new OrderContext { OrderId = orderId },
            workflowName: "order-processing",
            cancellationToken: cancellationToken);
}
```

### 5. Pause and resume with a resume token

A pause point isn't limited to one token — supply as many as the activity has possible outcomes, each with its own `Action` label:

```csharp
// in a handler, to pause and wait for an external signal - here, two possible outcomes:
var approveToken = new ResumeToken { Action = "approve" };
var cancelToken = new ResumeToken { Action = "cancel" };
// each token's Value (a GUID) gets communicated externally, e.g. in two different email links
return new WorkflowPause<WaitForApprovalActivity, OrderContext>
{
    Next = new WaitForApprovalActivity(),
    ResumeTokens = [approveToken, cancelToken],
    Context = context
};

// later, from a webhook/API endpoint - the manager searches every queue automatically for whichever
// token value matches, regardless of which of the two it turns out to be:
await workflowManager.TriggerResumeTokenAsync(tokenValue: resumeTokenValue);
```

The resumed activity reads which token actually fired from `TriggeredResumeToken.Action` to decide what happens next:

```csharp
public sealed class WaitForApprovalHandler : IActivityHandler<WaitForApprovalActivity, OrderContext>
{
    public Task<IWorkflowStep<IActivity<OrderContext>, OrderContext>> HandleAsync(
        WaitForApprovalActivity activity, OrderContext context, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        IWorkflowStep<IActivity<OrderContext>, OrderContext> result = schedule.TriggeredResumeToken?.Action switch
        {
            "approve" => new WorkflowContinue<ShipOrderActivity, OrderContext> { Next = new ShipOrderActivity(), Context = context },
            "cancel" => new WorkflowFail<OrderContext> { Context = context, Exception = new InvalidOperationException("Order cancelled by approver.") },
            _ => new WorkflowEnd<OrderContext> { Context = context }
        };

        return Task.FromResult(result);
    }
}
```

## Fork & re-join

`WorkflowFork<TCtx>` spawns independent branches sharing the same `WorkflowId`. With `JoinPointActivity` left `null`, it behaves exactly like a plain fork always has: every branch runs to its own `WorkflowEnd`, nobody waits for anybody.

Setting `JoinPointActivity` also enqueues it, paused, as the fork's join point. Branches then return `WorkflowJoin` instead of `WorkflowEnd` to contribute to it:

```csharp
public sealed record CollectShipmentResultsActivity : Activity<OrderContext>
{
    public required IReadOnlyList<string> ExpectedJoinIds { get; init; }
    public IReadOnlyDictionary<string, bool> Received { get; init; } = new Dictionary<string, bool>();
}

public sealed class ForkOrderHandler : IActivityHandler<ForkOrderActivity, OrderContext>
{
    public Task<IWorkflowStep<IActivity<OrderContext>, OrderContext>> HandleAsync(
        ForkOrderActivity activity, OrderContext context, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        var joinIds = new[] { "payment", "inventory" }; // assigned by the fork, not the branches

        var fork = new WorkflowFork<OrderContext>
        {
            Branches =
            [
                ForkBranch<OrderContext>.Of(new ChargePaymentActivity(), joinId: "payment"),
                ForkBranch<OrderContext>.Of(new ReserveInventoryActivity(), joinId: "inventory")
            ],
            JoinPointActivity = new CollectShipmentResultsActivity { ExpectedJoinIds = joinIds },
            Context = context
        };

        return Task.FromResult<IWorkflowStep<IActivity<OrderContext>, OrderContext>>(fork);
    }
}

// a branch, once done, joins instead of ending:
public sealed class ChargePaymentHandler : IActivityHandler<ChargePaymentActivity, OrderContext>
{
    public async Task<IWorkflowStep<IActivity<OrderContext>, OrderContext>> HandleAsync(
        ChargePaymentActivity activity, OrderContext context, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        await ChargeAsync(context.OrderId, cancellationToken);
        return new WorkflowJoin<OrderContext> { Context = context };
    }
}

// the join point: dispatched again every time new arrivals land, decides for itself whether to
// keep waiting or proceed - the library has no built-in notion of "required" vs "optional" arrivals,
// that decision is entirely yours
public sealed class CollectShipmentResultsHandler : IActivityHandler<CollectShipmentResultsActivity, OrderContext>
{
    public Task<IWorkflowStep<IActivity<OrderContext>, OrderContext>> HandleAsync(
        CollectShipmentResultsActivity activity, OrderContext context, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        var received = new Dictionary<string, bool>(activity.Received);
        foreach (var arrival in schedule.TriggeredJoinArrivals ?? [])
            received[arrival.JoinId] = !arrival.Failed; // failures land here too, see below

        if (received.Count < activity.ExpectedJoinIds.Count)
        {
            return Task.FromResult<IWorkflowStep<IActivity<OrderContext>, OrderContext>>(
                new WorkflowJoinWait<CollectShipmentResultsActivity, OrderContext>
                {
                    Next = activity with { Received = received },
                    Context = context
                });
        }

        return Task.FromResult<IWorkflowStep<IActivity<OrderContext>, OrderContext>>(
            new WorkflowEnd<OrderContext> { Context = context });
    }
}
```

**Permanent branch failures are delivered too**, through the same channel as a deliberate `WorkflowFail` (see below): if a branch exhausts its retries and the exception propagates, the consumer notifies the join point exactly as it would for `WorkflowFail` (`JoinArrival.Failed = true`, `Exception` set) — so a join-point handler never has to infer a branch's disappearance by polling for absence; it can just check `arrival.Failed` on whatever it receives.

**Known limitation, not yet addressed:** a `WorkflowJoin`/failure arrival for a `ForkId` whose join point has already decided to proceed (and moved on) has nowhere left to be delivered — it's silently dropped (with a warning log) rather than causing an error. There's no cleanup/closure signal yet for "this fork's join is done, stop delivering to it."

⚠️ **Nested forks are not fully supported yet.** A branch can itself return another `WorkflowFork` — that inner fork (and its own join point, if any) runs and resolves independently. But it does **not** compose back into the outer fork's join point: the moment a branch forks again, its own `ForkId`/`JoinId` membership in the outer fork is abandoned, so that slot in the outer join point's arrivals will never be filled. There's currently no supported way for an inner fork+join to feed a result back to an ancestor's join point.

## Explicit failure (`WorkflowFail`)

`WorkflowFail<TCtx>` is a deliberate, final failure outcome, carrying an optional `Exception` — distinct from letting an exception propagate:

```csharp
return new WorkflowFail<OrderContext>
{
    Context = context,
    Exception = new PaymentDeclinedException(context.OrderId)
};
```

**It bypasses `ActivityRetryMiddleware` entirely.** Middleware only intercepts *exceptions* thrown by the next handler in the chain — it never inspects a handler's return value — so a `WorkflowFail` returned normally is never retried, regardless of `RetriesLeft`. Use it for a final business decision that retrying wouldn't change; throw instead for a transient error you *do* want retried. Retries exhausted after a genuine exception still take the exception path (`ActivityException` event, logged as an error) rather than becoming a `WorkflowFail` — the two stay deliberately separate, since one signals a bug/operational fault and the other a successful execution that concluded negatively.

On a fork branch (`ForkId`/`JoinId` set), `WorkflowFail` is delivered to the join point the same way a permanently-failed branch is (see above) — a join-point handler doesn't need to know or care whether a `Failed = true` arrival came from an exhausted retry or an explicit decision.

The OTel span for a `WorkflowFail` is tagged `activity.outcome = "fail"` and (if `Exception` is set) records it as a span event — but its status is **not** set to `Error`, unlike a genuinely unhandled exception, so a business failure doesn't trigger the same alerting a bug would.

## Delayed continuation

`WorkflowContinue`/`WorkflowPause` accept an optional `NextAt`. Left `null` (the default), the next activity is enqueued immediately, same as always. Set it to implement bounded polling/timeout patterns:

```csharp
return new WorkflowContinue<CheckPaymentStatusActivity, OrderContext>
{
    Next = new CheckPaymentStatusActivity(attempt: attempt + 1),
    NextAt = DateTime.UtcNow.AddSeconds(30), // recheck in 30s
    Context = context
};
```

## `WorkflowName`

`StartWorkflowAsync`/`StartWorkflowAtAsync` accept an optional `workflowName`, carried forward on every `ActivitySchedule` for that workflow instance (continue/pause/fork/retry — the same treatment as `WorkflowId`). Unlike `WorkflowId`, it's **not unique by design**: several concurrent instances of "the same kind" of workflow (e.g. every run of a nightly report) can share the same name, which is exactly what makes it useful for dedup/observability queries:

```csharp
var alreadyRunning = (await workflowManager.ListQueuedActivitySchedulesAsync(q => q))
    .SelectMany(kv => kv.Value)
    .Any(s => s.WorkflowName == "nightly-report");
```

It's also emitted as the `workflow.name` tracing tag alongside `workflow.id` (see [Observability](#observability-opentelemetry)) — useful since a raw `WorkflowId` GUID isn't legible on its own in a trace view.

## Extensibility: custom queues, consumers, and managers

Implement `IActivityQueue` and pass the type to `UseQueue<T>`. The constructor receives the queue's `ActivityQueueOptions` and any other services registered in DI, injected automatically via `ActivatorUtilities`:

```csharp
public sealed class RedisActivityQueue : IActivityQueue
{
    public RedisActivityQueue(IWorkflowManager manager, ActivityQueueOptions options, IConnectionMultiplexer redis) { /* ... */ }
    // implement the rest of IActivityQueue
}

services.AddDefaultWorkflowManager(builder => builder.UseQueue<RedisActivityQueue>("orders", _ => { }));
```

`IActivityConsumer` works the same way via `UseConsumer<T>`.

If you need different behavior at the manager level itself, derive from `WorkflowManager<TOptions>` and register it with the generic overload — like `IJobScheduler` in LoomKit.Jobs, there's no `WithLifetime`: an `IWorkflowManager` is always registered as a singleton `IHostedService`, since that's the only lifetime the host manages hosted services with.

```csharp
services.AddWorkflowManager<MyWorkflowManager, MyWorkflowManagerOptionsBuilder, MyWorkflowManagerOptions>(options => { });
```

## Lifecycle events

```csharp
workflowManager.ActivityScheduled += (_, e) => Console.WriteLine($"Scheduled {e.ActivitySchedule.Activity.ActivityId}");
workflowManager.ActivityStarted   += (_, e) => Console.WriteLine($"Started   on {e.ConsumerName}");
workflowManager.ActivityEnded     += (_, e) => Console.WriteLine($"Ended     in workflow {e.ActivitySchedule.WorkflowId}");
workflowManager.ActivityException += (_, e) => Console.WriteLine($"Error:    {e.Exception.Message}");
```

## Observability (OpenTelemetry)

`LoomKit.Workflows` ships built-in tracing via `System.Diagnostics.ActivitySource` (named after the assembly, `LoomKit.Workflows`) — no OTel package required in the library itself.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("LoomKit.Workflows"));
```

Every activity dispatch starts a `workflows.activity {ActivityType}` span, tagged with `workflow.id`, `workflow.name`, `activity.type`, `activity.schedule_id`, `activity.retries_left`, `activity.queue_name`, `activity.consumer_name`, and `activity.outcome` (`end`, `fail`, `continue`, `pause`, `fork`, `join`, or `join_wait`). When paused, `activity.resume_tokens` lists the pending `Action`s. `ActivityRetryMiddleware<,>` adds `activity.retried`/`activity.retries_exhausted` events. On an unhandled exception the span records it via `Activity.AddException` (standard OpenTelemetry semantic conventions) and sets its status to `Error`.

⚠️ If your tracing backend doesn't have the same access controls as your application logs, avoid throwing exceptions from handlers whose `Message` carries secrets or personal data — they will flow into your trace exporter as-is.

**Trace context propagation across async boundaries:** the W3C `traceparent` of the span active at `StartWorkflowAsync`/enqueue time is stored on `ActivitySchedule.TraceParent` and restored as the parent context when the next activity's span starts, so the whole chain — including fork branches and retries (which use the *same* `TraceParent` as the failed attempt, appearing as siblings rather than children) — links together in your trace view.

Every `ActivitySchedule` also carries `ActivityStatus.EnqueuedAt`, stamped by the queue at enqueue time and never touched afterwards (unlike `NextAt`, which retries overwrite) — use it to tell "queued/paused for a long time" apart from `StartedAt`, which only covers time spent actually executing.

## Activity tracking

Every `IActivity` carries `ActivityId` (unique per instance) and `PreviousActivityId` (the activity that produced it, `null` for the first activity of a workflow). Both are populated automatically when extending `Activity<TCtx>` — including across fork branches and re-paused join points — letting you reconstruct the full execution chain without any handler code.

## License

[MIT](LICENSE)
