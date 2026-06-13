using System.Collections.Concurrent;
using MAIS.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MAIS.Infrastructure.EventBus;

/// <summary>
/// Thread-safe, in-process event bus. Subscriptions are kept in a concurrent dictionary
/// keyed by event type. A faulting handler is logged and isolated — it does not prevent
/// other handlers from receiving the event.
/// </summary>
public sealed class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly ILogger<InMemoryEventBus> _logger;
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly Lock _lock = new();
    private bool _disposed;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(InMemoryEventBus));
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent);
        _logger.LogDebug("Publishing event {EventType}", eventType.Name);

        List<Delegate> snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers) || handlers.Count == 0)
            {
                _logger.LogDebug("No subscribers for {EventType}", eventType.Name);
                return;
            }
            snapshot = new List<Delegate>(handlers);
        }

        var tasks = snapshot
            .OfType<Func<TEvent, CancellationToken, Task>>()
            .Select(handler => InvokeHandlerSafeAsync(handler, @event, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : class
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(InMemoryEventBus));
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        lock (_lock)
        {
            _handlers.GetOrAdd(eventType, _ => []).Add(handler);
        }

        _logger.LogDebug("Subscriber registered for {EventType}", eventType.Name);
        return new Subscription(() => Unsubscribe(eventType, handler));
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    private async Task InvokeHandlerSafeAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        TEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler(@event, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception in event handler for {EventType}. Handler: {Handler}",
                typeof(TEvent).Name,
                handler.Method.Name);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _handlers.Clear();
    }

    // -------------------------------------------------------------------------

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            unsubscribe();
        }
    }
}
