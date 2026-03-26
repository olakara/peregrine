namespace Peregrine.Api.Infrastructure.Configuration;

public sealed class DroneConfiguration
{
    public string Id { get; set; } = "peregrine-1";
    public string Name { get; set; } = "Peregrine";
    public HomePositionConfig HomePosition { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public BatteryConfig Battery { get; set; } = new();
    public SimulationConfig Simulation { get; set; } = new();
}

public sealed class HomePositionConfig
{
    public double Latitude { get; set; } = 37.7749;
    public double Longitude { get; set; } = -122.4194;
    public double Altitude { get; set; } = 0.0;
}

public sealed class PerformanceConfig
{
    public double MaxSpeedMps { get; set; } = 15.0;
    public double MaxAltitudeMeters { get; set; } = 120.0;
    public double TakeoffSpeedMps { get; set; } = 3.0;
    public double DefaultHoverAltitudeMeters { get; set; } = 30.0;
}

public sealed class BatteryConfig
{
    public double InitialChargePercent { get; set; } = 100.0;
    public DrainRatesConfig DrainRates { get; set; } = new();
    public double EmergencyLandThresholdPercent { get; set; } = 10.0;
}

public sealed class DrainRatesConfig
{
    public double IdlePerSecond { get; set; } = 0.02;
    public double HoveringPerSecond { get; set; } = 0.15;
    public double FlyingPerSecond { get; set; } = 0.25;
    public double TakeoffLandingPerSecond { get; set; } = 0.20;
    public double ChargeRatePerSecond { get; set; } = 0.5;
}

public sealed class SimulationConfig
{
    public int TickIntervalMs { get; set; } = 500;
    public int TelemetryIntervalMs { get; set; } = 1000;
}
