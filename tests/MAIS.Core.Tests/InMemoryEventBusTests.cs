using FluentAssertions;
using MAIS.Infrastructure.EventBus;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MAIS.Core.Tests;

public sealed class InMemoryEventBusTests
{
    private static InMemoryEventBus CreateBus() =>
        new(NullLogger<InMemoryEventBus>.Instance);

    private sealed record TestEvent(string Payload);
    private sealed record OtherEvent(int Value);

    // ── Publish / Subscribe ───────────────────────────────────────────────

    [Fact]
    public async Task Publish_WithSubscriber_InvokesHandler()
    {
        var bus = CreateBus();
        string? received = null;

        bus.Subscribe<TestEvent>((evt, _) =>
        {
            received = evt.Payload;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("hello"));

        received.Should().Be("hello");
    }

    [Fact]
    public async Task Publish_NoSubscribers_CompletesWithoutError()
    {
        var bus = CreateBus();
        var act = () => bus.PublishAsync(new TestEvent("ignored"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_AllInvoked()
    {
        var bus = CreateBus();
        var results = new List<string>();

        bus.Subscribe<TestEvent>((e, _) => { results.Add("A:" + e.Payload); return Task.CompletedTask; });
        bus.Subscribe<TestEvent>((e, _) => { results.Add("B:" + e.Payload); return Task.CompletedTask; });

        await bus.PublishAsync(new TestEvent("ping"));

        results.Should().HaveCount(2).And.Contain("A:ping").And.Contain("B:ping");
    }

    [Fact]
    public async Task Publish_WrongEventType_DoesNotInvokeHandler()
    {
        var bus = CreateBus();
        var invoked = false;

        bus.Subscribe<OtherEvent>((_, _) => { invoked = true; return Task.CompletedTask; });

        await bus.PublishAsync(new TestEvent("not-other"));

        invoked.Should().BeFalse();
    }

    // ── Unsubscribe ───────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_Dispose_StopsReceivingEvents()
    {
        var bus = CreateBus();
        var received = new List<string>();

        var subscription = bus.Subscribe<TestEvent>((e, _) =>
        {
            received.Add(e.Payload);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent("before-dispose"));
        subscription.Dispose();
        await bus.PublishAsync(new TestEvent("after-dispose"));

        received.Should().ContainSingle().Which.Should().Be("before-dispose");
    }

    // ── Fault isolation ───────────────────────────────────────────────────

    [Fact]
    public async Task Publish_FaultingHandler_DoesNotPreventOtherHandlers()
    {
        var bus = CreateBus();
        var goodHandlerInvoked = false;

        bus.Subscribe<TestEvent>((_, _) => throw new InvalidOperationException("Deliberate fault"));
        bus.Subscribe<TestEvent>((_, _) => { goodHandlerInvoked = true; return Task.CompletedTask; });

        // Should not throw
        var act = () => bus.PublishAsync(new TestEvent("fault-test"));
        await act.Should().NotThrowAsync();

        goodHandlerInvoked.Should().BeTrue();
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAfterDispose_Throws()
    {
        var bus = CreateBus();
        bus.Dispose();

        var act = () => bus.PublishAsync(new TestEvent("post-dispose"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
