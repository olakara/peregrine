using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class FlightEndpointTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public FlightEndpointTests()
    {
        _factory = new DroneAppFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // --- TakeOff ---

    [Fact]
    public async Task TakeOff_FromIdle_Returns200AndTakingOffState()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/takeoff", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("TakingOff");
    }

    [Fact]
    public async Task TakeOff_WithAltitude_Returns200()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsJsonAsync("/drone/takeoff", new { altitude = 50.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TakeOff_FromOffline_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/takeoff", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TakeOff_WithLowBattery_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/land", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- Hover ---

    [Fact]
    public async Task Hover_FromFlying_Returns200AndHoveringState()
    {
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
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
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- SetSpeed ---

    [Fact]
    public async Task SetSpeed_FromHovering_Returns200AndUpdatesStatus()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PutAsJsonAsync("/drone/speed", new { speedMps = 8.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Message.Should().Contain("8.0 m/s");
    }

    [Fact]
    public async Task SetSpeed_FromIdle_Returns200()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PutAsJsonAsync("/drone/speed", new { speedMps = 5.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetSpeed_FromOffline_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState(); // leaves drone Offline

        var response = await _client.PutAsJsonAsync("/drone/speed", new { speedMps = 5.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SetSpeed_AboveMaxSpeed_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PutAsJsonAsync("/drone/speed", new { speedMps = 999.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("exceeds maximum");
    }

    [Fact]
    public async Task SetSpeed_Zero_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PutAsJsonAsync("/drone/speed", new { speedMps = 0.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- ReturnHome ---

    [Fact]
    public async Task ReturnHome_FromHovering_Returns200AndFlyingState()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PostAsync("/drone/return-home", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Flying");
        body.Message.Should().Contain("home");
    }

    [Fact]
    public async Task ReturnHome_FromFlying_Returns200AndFlyingState()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 1.0, 10.0)]);
        drone.Navigate();

        var response = await _client.PostAsync("/drone/return-home", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Flying");
    }

    [Fact]
    public async Task ReturnHome_FromIdle_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/return-home", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReturnHome_FromOffline_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState(); // leaves drone Offline

        var response = await _client.PostAsync("/drone/return-home", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- AdjustAltitude ---

    [Fact]
    public async Task AdjustAltitude_FromHovering_Returns200AndUpdatesMessage()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PutAsJsonAsync("/drone/altitude", new { altitudeMeters = 50.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Message.Should().Contain("50.0 m");
        body.Status.Should().NotBeNull();
    }

    [Fact]
    public async Task AdjustAltitude_FromFlying_Returns200()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 1.0, 10.0)]);
        drone.Navigate();

        var response = await _client.PutAsJsonAsync("/drone/altitude", new { altitudeMeters = 80.0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdjustAltitude_FromIdle_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PutAsJsonAsync("/drone/altitude", new { altitudeMeters = 50.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("Hovering or Flying");
    }

    [Fact]
    public async Task AdjustAltitude_AboveMaxAltitude_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PutAsJsonAsync("/drone/altitude", new { altitudeMeters = 9999.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("maximum");
    }

    [Fact]
    public async Task AdjustAltitude_ZeroAltitude_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PutAsJsonAsync("/drone/altitude", new { altitudeMeters = 0.0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("greater than 0");
    }
}
