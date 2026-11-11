using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Internal;
using LoomKit.Workflows.Models;
using LoomKit.Workflows.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Workflows.Defaults;

public class ActivityConsumer : IActivityConsumer
{
    // Closed MethodInfo for InnerActivityHandleAsync<TActivity,TWorkflowContext>, and the dispatch
    // "plan" (which handler/middleware types to close and how to build them, plus the span name),
    // only depend on the activity's runtime type (which determines TWorkflowContext too, see
    // WorkflowContextTypeResolver) and on the consumer options instance, never on a specific activity
    // value - so both are computed once and reused instead of re-running reflection
    // (MakeGenericMethod/MakeGenericType) on every single dispatch.
    private static readonly MethodInfo _innerActivityHandleAsyncDefinition =
        typeof(ActivityConsumer).GetMethod(nameof(InnerActivityHandleAsync), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not locate method {nameof(InnerActivityHandleAsync)}.");

    private static readonly ConcurrentDictionary<Type, MethodInfo> _innerActivityHandleAsyncMethodCache = new();
    private static readonly ConcurrentDictionary<(ActivityConsumerOptions Options, Type ActivityType), ActivityDispatchPlan> _dispatchPlanCache = new();

    public string ActivityConsumerName => _activityConsumerOptions.ActivityConsumerName;
    public string ActivityQueueName => _activityConsumerOptions.ActivityQueueName;
    public ActivityConsumerOptions ActivityConsumerOptions => _activityConsumerOptions;
    public bool IsStarted => _consumerTask is not null && !_consumerTask.IsCompleted;
    public ActivitySchedule? CurrentActivitySchedule => _currentActivitySchedule;

    private readonly ActivityConsumerOptions _activityConsumerOptions;
    private readonly IActivityQueue _activityQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActivityConsumer> _logger;

    private CancellationTokenSource? _linkedCts;
    private Task? _consumerTask;
    private ActivitySchedule? _currentActivitySchedule;

    private readonly object _startStopLock = new();

    public ActivityConsumer(
        ActivityConsumerOptions activityConsumerOptions,
        IActivityQueue activityQueue,
        IServiceProvider serviceProvider,
        ILogger<ActivityConsumer> logger)
    {
        _activityConsumerOptions = activityConsumerOptions;
        _activityQueue = activityQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_startStopLock)
        {
            if (_consumerTask is not null && !_consumerTask.IsCompleted)
            {
                _logger.LogWarning("[{ConsumerName}] StartAsync called but consumer already running", _activityConsumerOptions.ActivityConsumerName);
                return Task.CompletedTask;
            }

            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _consumerTask = Task.Run(async () =>
            {
                while (!_linkedCts.Token.IsCancellationRequested)
                {
                    ActivitySchedule? schedule;

                    try
                    {
                        schedule = await _activityQueue.AwaitForActivityScheduleAsync(
                            _activityConsumerOptions.ActivityConsumerName,
                            _linkedCts.Token);
                    }
                    catch (OperationCanceledException) when (_linkedCts.Token.IsCancellationRequested)
                    {
                        // stop requested while waiting for the next activity: the queue honours the
                        // token by throwing, which is the normal way out of the loop, not an error
                        break;
                    }

                    if (schedule is null)
                        continue;

                    await _activityQueue.NotifyActivityStarted(_activityConsumerOptions.ActivityConsumerName, schedule);

                    _logger.LogTrace("[{ConsumerName}] Handling activity {ActivityScheduleId}", _activityConsumerOptions.ActivityConsumerName, schedule.ActivityScheduleId);

                    try
                    {
                        await HandleActivityScheduleAsync(schedule, _linkedCts.Token);

                        await _activityQueue.NotifyActivityEnded(_activityConsumerOptions.ActivityConsumerName, schedule);

                        _logger.LogTrace("[{ConsumerName}] Handled activity {ActivityScheduleId}", _activityConsumerOptions.ActivityConsumerName, schedule.ActivityScheduleId);
                    }
                    catch (OperationCanceledException) when (_linkedCts.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        // Permanent failure (retries exhausted) delivered as a first-class join
                        // arrival, symmetric to a successful WorkflowJoin, so a join-point handler
                        // never has to infer a branch's disappearance by polling for absence.
                        // CancellationToken.None: best-effort bookkeeping, should still land even
                        // during a shutdown race.
                        if (schedule.ForkId is not null && schedule.JoinId is not null)
                        {
                            await _activityQueue.NotifyForkJoinArrivalAsync(
                                schedule.ForkId, schedule.JoinId, schedule.WorkflowContext,
                                failed: true, exception: exception, CancellationToken.None);
                        }

                        await _activityQueue.NotifyActivityException(_activityConsumerOptions.ActivityConsumerName, schedule, exception);

                        _logger.LogError(exception, "[{ConsumerName}] Exception handling activity {ActivityScheduleId}", _activityConsumerOptions.ActivityConsumerName, schedule.ActivityScheduleId);
                    }
                }
            });

            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_startStopLock)
        {
            if (_linkedCts is null)
                return;

            _linkedCts.Cancel();
        }

        try
        {
            var task = _consumerTask ?? Task.CompletedTask;
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));

            if (completed != task)
                _logger.LogWarning("[{ConsumerName}] Consumer did not stop within timeout", _activityConsumerOptions.ActivityConsumerName);
            else
                await task;
        }
        catch (OperationCanceledException ex)
        {
            // the consumer loop treats its own cancellation as a normal exit, so this is the caller's
            // token cancelling the wait: shutdown was cut short, same as the timeout warning above
            _logger.LogWarning(ex, "[{ConsumerName}] Stop was canceled before the consumer finished", _activityConsumerOptions.ActivityConsumerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConsumerName}] Error during StopAsync", _activityConsumerOptions.ActivityConsumerName);
        }
        finally
        {
            _linkedCts?.Dispose();
            _linkedCts = null;
            _consumerTask = null;
        }
    }

    private async Task HandleActivityScheduleAsync(ActivitySchedule schedule, CancellationToken cancellationToken)
    {
        var activity = schedule.Activity ?? throw new InvalidOperationException("Activity is null.");

        _currentActivitySchedule = schedule;
        schedule.ActivityStatus.ConsumerName = _activityConsumerOptions.ActivityConsumerName;
        schedule.ActivityStatus.StartedAt = DateTime.UtcNow;

        var activityType = activity.GetType();

        var closedMethodInfo = _innerActivityHandleAsyncMethodCache.GetOrAdd(activityType, static type =>
        {
            var workflowContextType = WorkflowContextTypeResolver.ResolveWorkflowContextType(type);
            return _innerActivityHandleAsyncDefinition.MakeGenericMethod(type, workflowContextType);
        });

        IServiceProvider serviceProvider = _serviceProvider;
        AsyncServiceScope? scope = null;

        if (_activityConsumerOptions.UseScopedServiceProvider)
        {
            // async scope: a synchronous Dispose() throws for scoped services that only implement
            // IAsyncDisposable (e.g. a pooled DbContext), and would skip their async cleanup
            scope = _serviceProvider.CreateAsyncScope();
            serviceProvider = scope.Value.ServiceProvider;
        }

        try
        {
            await (Task)closedMethodInfo.Invoke(this, [activity, schedule, serviceProvider, cancellationToken])!;
        }
        finally
        {
            schedule.ActivityStatus.EndedAt = DateTime.UtcNow;
            _currentActivitySchedule = null;

            if (scope.HasValue)
                await scope.Value.DisposeAsync();
        }
    }

    // If the activity extends Activity<TCtx>, inject PreviousActivityId automatically via record `with`.
    // C# resolves `with` to the concrete runtime type, so no information is lost.
    // Activities that implement IActivity directly (without extending Activity<TCtx>) are returned as-is.
    private static IActivity WithPreviousActivityId(IActivity activity, string previousActivityId)
        => activity is Activity<IWorkflowContext> baseRecord
            ? baseRecord with { PreviousActivityId = previousActivityId }
            : activity;

    private async Task InnerActivityHandleAsync<TActivity, TWorkflowContext>(
        TActivity activity,
        ActivitySchedule schedule,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
        where TActivity : IActivity<TWorkflowContext>
        where TWorkflowContext : IWorkflowContext
    {
        var activityType = typeof(TActivity);

        var plan = _dispatchPlanCache.GetOrAdd((_activityConsumerOptions, activityType), static key =>
        {
            var (options, type) = key;
            var workflowContextType = WorkflowContextTypeResolver.ResolveWorkflowContextType(type);
            var handlerType = typeof(IActivityHandler<,>).MakeGenericType(type, workflowContextType);

            // built in reverse so the first-registered middleware ends up outermost (executes first)
            var middlewareFactories = options.ActivityMiddlewares
                .Reverse()
                .Select(middlewareType => ActivatorUtilities.CreateFactory(middlewareType.MakeGenericType(type, workflowContextType), [handlerType]))
                .ToArray();

            return new ActivityDispatchPlan(handlerType, middlewareFactories, $"workflows.activity {type.Name}");
        });

        var handler = (IActivityHandler<TActivity, TWorkflowContext>)serviceProvider.GetRequiredService(plan.HandlerType);

        var currentHandler = handler;
        foreach (var middlewareFactory in plan.MiddlewareFactories)
        {
            currentHandler = (IActivityHandler<TActivity, TWorkflowContext>)middlewareFactory(serviceProvider, [currentHandler]);
        }

        // Extract typed context
        var context = (TWorkflowContext)schedule.WorkflowContext;

        // Start span, restoring parent trace context across the async workflow boundary if available
        System.Diagnostics.Activity? workflowActivity;
        if (!string.IsNullOrEmpty(schedule.TraceParent)
            && ActivityContext.TryParse(schedule.TraceParent, null, isRemote: true, out var parentContext))
        {
            workflowActivity = WorkflowsActivitySource.Source.StartActivity(plan.ActivityName, ActivityKind.Internal, parentContext);
        }
        else
        {
            workflowActivity = WorkflowsActivitySource.Source.StartActivity(plan.ActivityName, ActivityKind.Internal);
        }

        workflowActivity?.SetTag("workflow.id", schedule.WorkflowId);
        workflowActivity?.SetTag("workflow.name", schedule.WorkflowName);
        workflowActivity?.SetTag("activity.type", activityType.FullName);
        workflowActivity?.SetTag("activity.schedule_id", schedule.ActivityScheduleId);
        workflowActivity?.SetTag("activity.retries_left", schedule.RetriesLeft);
        workflowActivity?.SetTag("activity.queue_name", _activityConsumerOptions.ActivityQueueName);
        workflowActivity?.SetTag("activity.consumer_name", _activityConsumerOptions.ActivityConsumerName);

        using (workflowActivity)
        {
            try
            {
                // Invoke
                var step = await currentHandler.HandleAsync(activity, context, schedule, cancellationToken);

                // Process result
                switch (step)
                {
                    case WorkflowEnd<TWorkflowContext>:
                        workflowActivity?.SetTag("activity.outcome", "end");
                        _logger.LogInformation(
                            "[{ConsumerName}] Workflow {WorkflowId} completed",
                            _activityConsumerOptions.ActivityConsumerName, schedule.WorkflowId);
                        break;

                    case WorkflowFail<TWorkflowContext> fail:
                        // Deliberate, not an execution error: outcome tag only, span status is NOT
                        // set to Error (that's reserved for genuinely unhandled exceptions below) so
                        // an expected business failure doesn't page on-call the same way a bug would.
                        workflowActivity?.SetTag("activity.outcome", "fail");
                        if (fail.Exception is not null)
                        {
                            workflowActivity?.AddEvent(new ActivityEvent("workflow.failed", tags: new ActivityTagsCollection
                            {
                                ["exception.type"] = fail.Exception.GetType().FullName!,
                                ["exception.message"] = fail.Exception.Message
                            }));
                        }

                        _logger.LogWarning(
                            "[{ConsumerName}] Workflow {WorkflowId} failed{ExceptionMessage}",
                            _activityConsumerOptions.ActivityConsumerName, schedule.WorkflowId,
                            fail.Exception is not null ? $": {fail.Exception.Message}" : "");

                        // Same delivery channel as a permanently-failed branch (see the outer catch
                        // in StartAsync): the join point doesn't distinguish "exhausted retries" from
                        // "handler deliberately failed", both arrive as JoinArrival.Failed = true.
                        if (schedule.ForkId is not null && schedule.JoinId is not null)
                        {
                            await _activityQueue.NotifyForkJoinArrivalAsync(
                                schedule.ForkId, schedule.JoinId, fail.Context,
                                failed: true, exception: fail.Exception, cancellationToken);
                        }
                        break;

                    case WorkflowJoin<TWorkflowContext> join:
                        workflowActivity?.SetTag("activity.outcome", "join");

                        if (schedule.ForkId is not null && schedule.JoinId is not null)
                        {
                            await _activityQueue.NotifyForkJoinArrivalAsync(
                                schedule.ForkId, schedule.JoinId, join.Context,
                                failed: false, exception: null, cancellationToken);

                            _logger.LogInformation(
                                "[{ConsumerName}] Activity {ActivityType} joined fork {ForkId} as {JoinId}",
                                _activityConsumerOptions.ActivityConsumerName, activityType.Name, schedule.ForkId, schedule.JoinId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[{ConsumerName}] Activity {ActivityType} returned WorkflowJoin but its schedule has no ForkId/JoinId - ignoring",
                                _activityConsumerOptions.ActivityConsumerName, activityType.Name);
                        }
                        break;

                    case IWorkflowForkStep fork:
                        workflowActivity?.SetTag("activity.outcome", "fork");
                        var forkContext = fork.NextContext ?? schedule.WorkflowContext;
                        var branches = fork.Branches.ToList();

                        _logger.LogInformation(
                            "[{ConsumerName}] Workflow {WorkflowId} forking into {BranchCount} branches (fork {ForkId})",
                            _activityConsumerOptions.ActivityConsumerName, schedule.WorkflowId, branches.Count, fork.ForkId);

                        foreach (var branch in branches)
                        {
                            var branchSchedule = new ActivitySchedule
                            {
                                WorkflowId = schedule.WorkflowId,
                                WorkflowName = schedule.WorkflowName,
                                Activity = WithPreviousActivityId(branch.Activity, activity.ActivityId),
                                WorkflowContext = forkContext,
                                NextAt = DateTime.UtcNow,
                                ForkId = fork.ForkId,
                                JoinId = branch.JoinId,
                                RetriesLeft = _activityQueue.ActivityQueueOptions.MaxActivityRetries,
                                ActivityStatus = new ActivityStatus(),
                                TraceParent = System.Diagnostics.Activity.Current?.Id
                            };

                            await _activityQueue.EnqueueActivityScheduleAsync(branchSchedule, cancellationToken);
                        }

                        if (fork.JoinPointActivity is not null)
                        {
                            var joinPointSchedule = new ActivitySchedule
                            {
                                WorkflowId = schedule.WorkflowId,
                                WorkflowName = schedule.WorkflowName,
                                Activity = WithPreviousActivityId(fork.JoinPointActivity, activity.ActivityId),
                                WorkflowContext = forkContext,
                                NextAt = DateTime.UtcNow,
                                IsPaused = true,
                                ForkId = fork.ForkId,
                                RetriesLeft = _activityQueue.ActivityQueueOptions.MaxActivityRetries,
                                ActivityStatus = new ActivityStatus(),
                                TraceParent = System.Diagnostics.Activity.Current?.Id
                            };

                            await _activityQueue.EnqueueActivityScheduleAsync(joinPointSchedule, cancellationToken);
                        }
                        break;

                    case IWorkflowJoinWaitStep wait:
                        workflowActivity?.SetTag("activity.outcome", "join_wait");

                        var joinWaitSchedule = new ActivitySchedule
                        {
                            WorkflowId = schedule.WorkflowId,
                            WorkflowName = schedule.WorkflowName,
                            Activity = WithPreviousActivityId(wait.NextActivity, activity.ActivityId),
                            WorkflowContext = wait.NextContext ?? schedule.WorkflowContext,
                            NextAt = DateTime.UtcNow,
                            IsPaused = true,
                            ForkId = schedule.ForkId,
                            RetriesLeft = _activityQueue.ActivityQueueOptions.MaxActivityRetries,
                            ActivityStatus = new ActivityStatus(),
                            TraceParent = System.Diagnostics.Activity.Current?.Id
                        };

                        await _activityQueue.EnqueueActivityScheduleAsync(joinWaitSchedule, cancellationToken);
                        break;

                    case IWorkflowNextStep nextStep:
                        workflowActivity?.SetTag("activity.outcome", nextStep.IsPaused ? "pause" : "continue");
                        if (nextStep.IsPaused && nextStep.ResumeTokens is not null)
                            workflowActivity?.SetTag("activity.resume_tokens",
                                string.Join(",", nextStep.ResumeTokens.Select(t => t.Action)));

                        var nextSchedule = new ActivitySchedule
                        {
                            WorkflowId = schedule.WorkflowId,
                            WorkflowName = schedule.WorkflowName,
                            Activity = WithPreviousActivityId(nextStep.NextActivity, activity.ActivityId),
                            WorkflowContext = nextStep.NextContext ?? schedule.WorkflowContext,
                            NextAt = nextStep.NextAt ?? DateTime.UtcNow,
                            IsPaused = nextStep.IsPaused,
                            ResumeTokens = nextStep.ResumeTokens,
                            // A multi-hop branch (continue/pause several times before eventually
                            // returning WorkflowJoin or forking again) keeps its fork membership until
                            // it either resolves it (WorkflowJoin, see above) or replaces it by forking
                            // again (fresh ForkId per WorkflowFork, see the fork case above).
                            ForkId = schedule.ForkId,
                            JoinId = schedule.JoinId,
                            RetriesLeft = _activityQueue.ActivityQueueOptions.MaxActivityRetries,
                            ActivityStatus = new ActivityStatus(),
                            TraceParent = System.Diagnostics.Activity.Current?.Id
                        };

                        await _activityQueue.EnqueueActivityScheduleAsync(nextSchedule, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                workflowActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                workflowActivity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName!,
                    ["exception.message"] = ex.Message,
                    ["exception.stacktrace"] = ex.StackTrace ?? ""
                }));
                throw;
            }
        }
    }

    private sealed record ActivityDispatchPlan(Type HandlerType, ObjectFactory[] MiddlewareFactories, string ActivityName);
}
