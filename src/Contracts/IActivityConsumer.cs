using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Contracts;

public interface IActivityConsumer
{
    string ActivityConsumerName { get; }
    string ActivityQueueName { get; }
    ActivityConsumerOptions ActivityConsumerOptions { get; }
    bool IsStarted { get; }
    ActivitySchedule? CurrentActivitySchedule { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
