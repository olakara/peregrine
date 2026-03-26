using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class TelemetryEndpointTests : IClassFixture<DroneAppFactory>
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public TelemetryEndpointTests(DroneAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- GET /drone/status ---

    [Fact]
    public async Task GetStatus_Returns200WithDroneStatusFields()
    {
        _factory.ResetDrone();

        var response = await _client.GetAsync("/drone/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DroneStatusDto>();
        body!.DroneId.Should().NotBeNullOrEmpty();
        body.DroneName.Should().NotBeNullOrEmpty();
        body.State.Should().Be("Offline");
        body.BatteryPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetStatus_ReflectsStateChange()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.GetAsync("/drone/status");

        var body = await response.Content.ReadFromJsonAsync<DroneStatusDto>();
        body!.State.Should().Be("Idle");
    }

    [Fact]
    public async Task GetStatus_IncludesWaypointQueueDepth()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.LoadWaypoints([
            new Domain.Waypoint(1.0, 1.0, 10.0),
            new Domain.Waypoint(2.0, 2.0, 20.0)
        ]);

        var response = await _client.GetAsync("/drone/status");

        var body = await response.Content.ReadFromJsonAsync<DroneStatusDto>();
        body!.WaypointQueueDepth.Should().Be(2);
    }

    // --- GET /drone/telemetry (SSE) ---

    [Fact]
    public async Task TelemetryStream_ReturnsTextEventStreamContentType()
    {
        var broadcaster = _factory.Services
            .GetRequiredService<Infrastructure.TelemetryBroadcaster>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = new HttpRequestMessage(HttpMethod.Get, "/drone/telemetry");

        // Start the SSE request — it will block at ResponseHeadersRead until first write
        var responseTask = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Give the endpoint time to register with the broadcaster, then trigger a write
        await Task.Delay(150);
        broadcaster.Publish(_factory.GetDroneContext().GetStatus());

        using var response = await responseTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task TelemetryStream_ReceivesDataEventWhenStatusPublished()
    {
        var broadcaster = _factory.Services
            .GetService(typeof(Infrastructure.TelemetryBroadcaster))
            as Infrastructure.TelemetryBroadcaster;
        broadcaster.Should().NotBeNull();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? receivedLine = null;

        var streamTask = Task.Run(async () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/drone/telemetry");
            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new System.IO.StreamReader(stream);

            while (!cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is not null && line.StartsWith("data: "))
                {
                    receivedLine = line;
                    cts.Cancel();
                    break;
                }
            }
        }, cts.Token);

        await Task.Delay(200); // give the stream time to connect
        var status = _factory.GetDroneContext().GetStatus();
        broadcaster!.Publish(status);

        try { await streamTask; } catch (OperationCanceledException) { }

        receivedLine.Should().NotBeNull("SSE stream should deliver a data event");
        receivedLine!.Should().StartWith("data: ");
        receivedLine.Should().Contain("droneId");
    }
}
