namespace MAIS.Core.Abstractions;

/// <summary>
/// Lightweight in-process event bus for decoupled communication between MAIS components.
/// Events are typed, async, and delivered to all active subscribers.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all current subscribers of type <typeparamref name="TEvent"/>.
    /// Publication is fire-and-forget with respect to individual handler failures —
    /// a faulting handler is logged but does not prevent other handlers from receiving the event.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/>.
    /// Dispose the returned token to unsubscribe.
    /// </summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : class;
}
