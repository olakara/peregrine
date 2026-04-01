using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory that suppresses the FlightSimulatorService background service
/// so integration tests control drone state explicitly rather than via background ticks.
/// </summary>
public sealed class DroneAppFactory : WebApplicationFactory<Program>
{
    private readonly string _tempPlanPath =
        Path.Combine(Path.GetTempPath(), $"test-mission-{Guid.NewGuid()}.plan");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real background service so tests own state transitions
            var descriptor = services.SingleOrDefault(
                d => d.ImplementationType == typeof(FlightSimulatorService));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Replace MissionPlanStore with one using an isolated temp file per factory instance
            var storeDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(MissionPlanStore));
            if (storeDescriptor is not null)
                services.Remove(storeDescriptor);
            services.AddSingleton(new MissionPlanStore(_tempPlanPath));
        });
    }

    /// <summary>Resolves the singleton DroneContext from the test app's DI container.</summary>
    public DroneContext GetDroneContext() =>
        Services.GetRequiredService<DroneContext>();

    /// <summary>Resolves the singleton MissionPlanStore from the test app's DI container.</summary>
    public MissionPlanStore GetMissionPlanStore() =>
        Services.GetRequiredService<MissionPlanStore>();

    /// <summary>
    /// Resets the DroneContext to the Offline state regardless of current state.
    /// Call at the start of each integration test for isolation.
    /// </summary>
    public void ResetDrone()
    {
        var drone = GetDroneContext();
        switch (drone.State)
        {
            case DroneState.Offline:
                break;
            case DroneState.Charging:
                drone.StopCharging();
                drone.PowerOff();
                break;
            case DroneState.Idle:
                drone.PowerOff();
                break;
            case DroneState.TakingOff:
            case DroneState.Hovering:
            case DroneState.Flying:
            case DroneState.Landing:
                drone.ForceLand();
                drone.TransitionToIdle();
                drone.PowerOff();
                break;
        }

        // Clear any mission plan so tests start clean
        drone.ClearMissionPlan();

        // Restore battery to full so tests start with a clean slate
        drone.ChargeBattery(100.0);
    }
}
