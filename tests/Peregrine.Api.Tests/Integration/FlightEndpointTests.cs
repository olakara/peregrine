using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class FlightEndpointTests : IClassFixture<DroneAppFactory>
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public FlightEndpointTests(DroneAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // --- TakeOff ---

    [Fact]
    public async Task TakeOff_FromIdle_Returns200AndTakingOffState()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/takeoff", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("TakingOff");
    }

    [Fact]
    public async Task TakeOff_WithAltitude_Returns200()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsJsonAsync("/drone/takeoff", new { altitude = 50.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TakeOff_FromOffline_Returns409()
    {
        _factory.ResetDrone();

        var response = await _client.PostAsync("/drone/takeoff", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TakeOff_WithLowBattery_Returns409()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(95.0); // leaves ~5%, below 10% emergency threshold

        var response = await _client.PostAsync("/drone/takeoff", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("Battery");
    }

    // --- Land ---

    [Fact]
    public async Task Land_FromHovering_Returns200AndLandingState()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PostAsync("/drone/land", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Landing");
    }

    [Fact]
    public async Task Land_FromFlying_Returns200AndLandingState()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 1.0, 10.0)]);
        drone.Navigate();

        var response = await _client.PostAsync("/drone/land", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Landing");
    }

    [Fact]
    public async Task Land_FromIdle_Returns409()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/land", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- Hover ---

    [Fact]
    public async Task Hover_FromFlying_Returns200AndHoveringState()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 1.0, 10.0)]);
        drone.Navigate();

        var response = await _client.PostAsync("/drone/hover", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Hovering");
    }

    [Fact]
    public async Task Hover_FromHovering_Returns409()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PostAsync("/drone/hover", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- Navigate ---

    [Fact]
    public async Task Navigate_FromHoveringWithWaypoints_Returns200AndFlyingState()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 1.0, 10.0)]);

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Flying");
    }

    [Fact]
    public async Task Navigate_WithEmptyWaypointQueue_Returns409()
    {
        _factory.ResetDrone();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.ClearWaypoints();

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("empty");
    }

    [Fact]
    public async Task Navigate_FromIdle_Returns409()
    {
        _factory.ResetDrone();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
