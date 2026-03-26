using Microsoft.Extensions.Options;
using Peregrine.Api.Infrastructure;
using Peregrine.Api.Infrastructure.Configuration;

namespace Peregrine.Api.Tests.Helpers;

/// <summary>
/// Builds DroneContext instances with test-friendly configuration.
/// </summary>
public static class DroneContextFactory
{
    public static DroneContext Create(Action<DroneConfiguration>? configure = null)
    {
        var config = new DroneConfiguration
        {
            Id = "test-drone",
            Name = "Test Drone",
            HomePosition = new HomePositionConfig
            {
                Latitude = 0.0,
                Longitude = 0.0,
                Altitude = 0.0
            },
            Performance = new PerformanceConfig
            {
                MaxSpeedMps = 15.0,
                MaxAltitudeMeters = 120.0,
                TakeoffSpeedMps = 3.0,
                DefaultHoverAltitudeMeters = 30.0
            },
            Battery = new BatteryConfig
            {
                InitialChargePercent = 100.0,
                EmergencyLandThresholdPercent = 10.0,
                DrainRates = new DrainRatesConfig
                {
                    IdlePerSecond = 0.02,
                    HoveringPerSecond = 0.15,
                    FlyingPerSecond = 0.25,
                    TakeoffLandingPerSecond = 0.20,
                    ChargeRatePerSecond = 0.5
                }
            },
            Simulation = new SimulationConfig
            {
                TickIntervalMs = 500,
                TelemetryIntervalMs = 1000
            }
        };

        configure?.Invoke(config);

        return new DroneContext(Options.Create(config));
    }
}
