namespace MAIS.Core.Models;

/// <summary>Lifecycle states a MAIS module can be in at any point in time.</summary>
public enum ModuleStatus
{
    /// <summary>Status has not yet been determined (initial state).</summary>
    Unknown = 0,

    /// <summary>The module is in the process of starting.</summary>
    Starting = 1,

    /// <summary>The module is running and healthy.</summary>
    Running = 2,

    /// <summary>The module is running but reporting degraded performance or partial failures.</summary>
    Degraded = 3,

    /// <summary>The module has been cleanly stopped.</summary>
    Stopped = 4,

    /// <summary>The module has entered an unrecoverable error state.</summary>
    Faulted = 5,

    /// <summary>The module is in the process of stopping.</summary>
    Stopping = 6
}

/// <summary>
/// Describes how a MAIS module is deployed and hosted within the framework.
/// This drives how the orchestrator manages and communicates with the module.
/// </summary>
public enum ModuleType
{
    /// <summary>Runs directly inside the MAIS.Service process as a hosted background service.</summary>
    InProcess = 0,

    /// <summary>Runs in a Docker/container managed externally; MAIS communicates via REST/gRPC.</summary>
    ContainerisedService = 1,

    /// <summary>An external application or API exposed through an endpoint registered with MAIS.</summary>
    ExternalEndpoint = 2,

    /// <summary>A .NET background worker process managed as a Windows Service child process.</summary>
    BackgroundWorker = 3,

    /// <summary>A consumer of a message queue (RabbitMQ, Azure Service Bus, etc.).</summary>
    MessageQueueConsumer = 4,

    /// <summary>A Python AI worker process managed via process lifecycle APIs.</summary>
    PythonWorker = 5
}
