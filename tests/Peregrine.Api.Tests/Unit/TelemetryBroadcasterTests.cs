using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Tests.Unit;

public sealed class TelemetryBroadcasterTests
{
    private static DroneStatus MakeStatus(string droneId = "test") =>
        new(droneId, "Test Drone", DroneState.Idle,
            new GpsCoordinate(0, 0, 0), 0, 0, 100.0, false, 0, DateTimeOffset.UtcNow);

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var broadcaster = new TelemetryBroadcaster();
        var act = () => broadcaster.Publish(MakeStatus());
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Publish_SingleSubscriber_ReceivesMessage()
    {
        var broadcaster = new TelemetryBroadcaster();
        var status = MakeStatus("single-sub");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<DroneStatus>();

        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var s in broadcaster.Subscribe(cts.Token))
                {
                    received.Add(s);
                    break; // exit after first message; DisposeAsync cleans up subscriber
                }
            }
            catch (OperationCanceledException) { /* timeout safety net */ }
        });

        await Task.Delay(50); // let subscriber register
        broadcaster.Publish(status);
        await consumer;

        received.Should().ContainSingle()
            .Which.DroneId.Should().Be("single-sub");
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_AllReceiveMessage()
    {
        var broadcaster = new TelemetryBroadcaster();
        var status = MakeStatus("multi-sub");
        const int subscriberCount = 3;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<DroneStatus>[subscriberCount];
        var tasks = new Task[subscriberCount];

        for (var i = 0; i < subscriberCount; i++)
        {
            var idx = i;
            received[idx] = [];
            tasks[idx] = Task.Run(async () =>
            {
                try
                {
                    await foreach (var s in broadcaster.Subscribe(cts.Token))
                    {
                        received[idx].Add(s);
                        break; // exit after first message
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        await Task.Delay(100); // let all subscribers register
        broadcaster.Publish(status);
        await Task.WhenAll(tasks);

        for (var i = 0; i < subscriberCount; i++)
        {
            received[i].Should().ContainSingle($"subscriber {i} should receive the message")
                .Which.DroneId.Should().Be("multi-sub");
        }
    }

    [Fact]
    public async Task Subscribe_CancelledToken_StreamTerminatesWithCancellation()
    {
        // When the cancellation token fires, Subscribe throws OperationCanceledException.
        // This is expected and correct behavior (matching how ASP.NET Core handles SSE disconnects).
        var broadcaster = new TelemetryBroadcaster();
        using var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            await foreach (var _ in broadcaster.Subscribe(cts.Token)) { }
        });

        await Task.Delay(50);
        cts.Cancel();

        var exception = await Record.ExceptionAsync(async () => await task);
        // Acceptable outcomes: clean completion OR OperationCanceledException
        (exception is null || exception is OperationCanceledException).Should().BeTrue(
            "cancelling the token should terminate the stream, possibly with OperationCanceledException");
    }

    [Fact]
    public async Task Publish_FullChannel_DropsOldestWithoutThrowing()
    {
        var broadcaster = new TelemetryBroadcaster();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Subscribe but don't drain — let the channel fill up
        var subscriptionTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in broadcaster.Subscribe(cts.Token))
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(100); // let the subscription register before publishing

        var act = () =>
        {
            for (var i = 0; i < 60; i++)
                broadcaster.Publish(MakeStatus($"overflow-{i}"));
        };

        act.Should().NotThrow();
        cts.Cancel();
        await subscriptionTask;
    }

    [Fact]
    public async Task Publish_AfterSubscriberCancels_DoesNotThrow()
    {
        var broadcaster = new TelemetryBroadcaster();
        using var cts1 = new CancellationTokenSource();

        var subscriptionTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in broadcaster.Subscribe(cts1.Token)) { }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        });

        await Task.Delay(50); // let subscriber register
        cts1.Cancel();
        await subscriptionTask;

        // After the subscriber is gone, further publishes should not throw
        var act = () =>
        {
            for (var i = 0; i < 5; i++)
                broadcaster.Publish(MakeStatus($"post-cancel-{i}"));
        };

        act.Should().NotThrow();
    }
}
