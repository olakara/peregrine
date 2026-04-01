using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class PowerEndpointTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public PowerEndpointTests()
    {
        _factory = new DroneAppFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task PowerOn_FromOffline_Returns200AndIdleState()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/power/on", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Idle");
    }

    [Fact]
    public async Task PowerOn_WhenAlreadyIdle_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        await _client.PostAsync("/drone/power/on", null);

        var response = await _client.PostAsync("/drone/power/on", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PowerOff_FromIdle_Returns200AndOfflineState()
    {
        _factory.ResetDroneAndAssertCleanState();
        await _client.PostAsync("/drone/power/on", null);

        var response = await _client.PostAsync("/drone/power/off", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Offline");
    }

    [Fact]
    public async Task PowerOff_WhenOffline_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/power/off", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PowerOff_WhileCharging_Returns200()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(50.0);
        drone.StartCharging();

        var response = await _client.PostAsync("/drone/power/off", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        body!.Status!.State.Should().Be("Offline");
    }
}
