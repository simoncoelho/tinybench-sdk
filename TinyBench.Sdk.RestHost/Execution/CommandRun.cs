namespace TinyBench.Sdk.RestHost.Execution;

public sealed class CommandRun
{
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly object sync = new();
    private Dictionary<string, object?> outputs = new(StringComparer.OrdinalIgnoreCase);

    public CommandRun(string id, string commandId, DateTimeOffset createdAt)
    {
        Id = id;
        CommandId = commandId;
        CreatedAt = createdAt;
    }

    public string Id { get; }

    public string CommandId { get; }

    public DateTimeOffset CreatedAt { get; }

    public CancellationToken CancellationToken => cancellationTokenSource.Token;

    public void MarkRunning()
    {
        lock (sync)
        {
            Status = CommandRunStatus.Running;
            StartedAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkCompleted(Dictionary<string, object?> outputValues)
    {
        lock (sync)
        {
            Status = CommandRunStatus.Completed;
            CompletedAt = DateTimeOffset.UtcNow;
            outputs = outputValues;
            PercentComplete = 100;
        }
    }

    public void MarkFailed(string error)
    {
        lock (sync)
        {
            Status = CommandRunStatus.Failed;
            CompletedAt = DateTimeOffset.UtcNow;
            Error = error;
        }
    }

    public void MarkCanceled()
    {
        lock (sync)
        {
            Status = CommandRunStatus.Canceled;
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void RequestCancel()
    {
        lock (sync)
        {
            if (Status is CommandRunStatus.Running or CommandRunStatus.Queued)
            {
                Status = CommandRunStatus.CancelRequested;
            }
        }

        cancellationTokenSource.Cancel();
    }

    public CommandRunSnapshot ToSnapshot()
    {
        lock (sync)
        {
            return new CommandRunSnapshot(
                Id,
                CommandId,
                Status.ToString(),
                CreatedAt,
                StartedAt,
                CompletedAt,
                PercentComplete,
                Error,
                new Dictionary<string, object?>(outputs, StringComparer.OrdinalIgnoreCase));
        }
    }

    private CommandRunStatus Status { get; set; } = CommandRunStatus.Queued;

    private DateTimeOffset? StartedAt { get; set; }

    private DateTimeOffset? CompletedAt { get; set; }

    private int PercentComplete { get; set; }

    private string? Error { get; set; }
}

public sealed record CommandRunSnapshot(
    string Id,
    string CommandId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int PercentComplete,
    string? Error,
    IReadOnlyDictionary<string, object?> Outputs);

public enum CommandRunStatus
{
    Queued,
    Running,
    CancelRequested,
    Completed,
    Failed,
    Canceled
}
