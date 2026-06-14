namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class QueueEntry
{
    public string         QueueId      { get; init; } = Guid.NewGuid().ToString();
    public UpdateRequest  Request      { get; init; } = null!;
    public UpdateApproval Approval     { get; init; } = null!;
    public QueueEntryStatus Status     { get; set; }  = QueueEntryStatus.Waiting;
    public DateTimeOffset  EnqueuedAt  { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? StartedAt   { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string?         FailureReason { get; set; }
}

public enum QueueEntryStatus { Waiting, Scheduled, InProgress, Completed, Failed }

public sealed class CrimsProcessStatus
{
    public string QueueId     { get; init; } = "";
    public bool   IsRunning   { get; init; }
    public string MachineName { get; init; } = "";
}

public sealed class UpdateOutcome
{
    public string              QueueId      { get; init; } = "";
    public bool                Success      { get; init; }
    public IReadOnlyList<string> UpdatedFiles { get; init; } = [];
    public string?             FailureReason { get; init; }
    public DateTimeOffset      CompletedAt  { get; init; } = DateTimeOffset.UtcNow;
}