using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class BatteryEndpointTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public BatteryEndpointTests()
    {
        _factory = new DroneAppFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // --- GET /drone/battery ---

    [Fact]
    public async Task GetBattery_ReturnsExpectedFields()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.GetAsync("/drone/battery");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BatteryStatusResponse>();
        body!.BatteryPercent.Should().Be(100.0);
        body.IsCharging.Should().BeFalse();
        body.DroneState.Should().Be("Offline");
    }

    [Fact]
    public async Task GetBattery_ReflectsCurrentBatteryLevel()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.DrainBattery(40.0);

        var response = await _client.GetAsync("/drone/battery");

        var body = await response.Content.ReadFromJsonAsync<BatteryStatusResponse>();
        body!.BatteryPercent.Should().BeApproximately(60.0, 0.01);
    }

    [Fact]
    public async Task GetBattery_IsCharging_WhenCharging()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(50.0);
        drone.StartCharging();

        var response = await _client.GetAsync("/drone/battery");

        var body = await response.Content.ReadFromJsonAsync<BatteryStatusResponse>();
        body!.IsCharging.Should().BeTrue();
        body.DroneState.Should().Be("Charging");
    }

    // --- POST /drone/battery/recharge ---

    [Fact]
    public async Task StartRecharge_FromIdleWithPartialBattery_Returns200AndChargingState()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(50.0);

        var response = await _client.PostAsync("/drone/battery/recharge", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Charging");
        body.Status.IsCharging.Should().BeTrue();
    }

    [Fact]
    public async Task StartRecharge_WhenBatteryFull_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn(); // starts at 100%

        var response = await _client.PostAsync("/drone/battery/recharge", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("fully charged");
    }

    [Fact]
    public async Task StartRecharge_FromOffline_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/battery/recharge", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- POST /drone/battery/recharge/stop ---

    [Fact]
    public async Task StopRecharge_FromCharging_Returns200AndIdleState()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(50.0);
        drone.StartCharging();

        var response = await _client.PostAsync("/drone/battery/recharge/stop", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Idle");
        body.Status.IsCharging.Should().BeFalse();
    }

    [Fact]
    public async Task StopRecharge_WhenNotCharging_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/battery/recharge/stop", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
