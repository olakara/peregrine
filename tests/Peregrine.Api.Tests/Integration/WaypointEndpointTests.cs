using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class WaypointEndpointTests : IClassFixture<DroneAppFactory>
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public WaypointEndpointTests(DroneAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- POST /drone/waypoints ---

    [Fact]
    public async Task LoadWaypoints_WithValidList_Returns200AndQueueDepth()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsJsonAsync("/drone/waypoints", new[]
        {
            new { latitude = 1.0, longitude = 1.0, altitude = 10.0 },
            new { latitude = 2.0, longitude = 2.0, altitude = 20.0 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.WaypointQueueDepth.Should().Be(2);
        body.Message.Should().Contain("2 waypoint");
    }

    [Fact]
    public async Task LoadWaypoints_WithCustomSpeed_Returns200()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsJsonAsync("/drone/waypoints", new[]
        {
            new { latitude = 1.0, longitude = 1.0, altitude = 10.0, speedMps = 5.0 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoadWaypoints_WithEmptyBody_Returns400()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsJsonAsync("/drone/waypoints", Array.Empty<object>());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoadWaypoints_WhenOffline_Returns409()
    {
        _factory.ResetDrone();

        var response = await _client.PostAsJsonAsync("/drone/waypoints", new[]
        {
            new { latitude = 1.0, longitude = 1.0, altitude = 10.0 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LoadWaypoints_ReplacesExistingQueue()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        await _client.PostAsJsonAsync("/drone/waypoints", new[]
        {
            new { latitude = 1.0, longitude = 1.0, altitude = 10.0 },
            new { latitude = 2.0, longitude = 2.0, altitude = 20.0 }
        });

        var response = await _client.PostAsJsonAsync("/drone/waypoints", new[]
        {
            new { latitude = 5.0, longitude = 5.0, altitude = 50.0 }
        });

        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.WaypointQueueDepth.Should().Be(1);
    }

    // --- DELETE /drone/waypoints ---

    [Fact]
    public async Task ClearWaypoints_FromIdle_Returns200AndEmptyQueue()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.LoadWaypoints([new Waypoint(1, 1, 10)]);

        var response = await _client.DeleteAsync("/drone/waypoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.WaypointQueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task ClearWaypoints_WhileFlying_Returns409()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1, 1, 10)]);
        drone.Navigate();

        var response = await _client.DeleteAsync("/drone/waypoints");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
