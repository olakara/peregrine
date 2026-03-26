using System.Text.Json.Serialization;
using NetEscapades.Configuration.Yaml;
using Peregrine.Api;
using Peregrine.Api.Infrastructure;
using Peregrine.Api.Infrastructure.Configuration;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Load drone.yaml as primary configuration source
var yamlPath = Path.Combine(AppContext.BaseDirectory, "drone.yaml");
if (!File.Exists(yamlPath))
    yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "drone.yaml");

builder.Configuration.AddYamlFile(yamlPath, optional: false, reloadOnChange: false);

// Bind YAML configuration to strongly-typed POCO
builder.Services.Configure<DroneConfiguration>(
    builder.Configuration.GetSection("drone"));

// Core simulation services (all singletons — one drone, shared state)
builder.Services.AddSingleton<DroneContext>();
builder.Services.AddSingleton<TelemetryBroadcaster>();
builder.Services.AddHostedService<FlightSimulatorService>();

builder.Services.AddOpenApi();

// Serialize enums as strings for a readable API
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference(opts =>
{
    opts.Title = "Peregrine Drone Simulator API";
    opts.Theme = ScalarTheme.Mars;
});

// Auto-register all IEndpoint slices via reflection
var endpointTypes = typeof(IEndpoint).Assembly
    .GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract && typeof(IEndpoint).IsAssignableFrom(t));

foreach (var type in endpointTypes)
{
    var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
    endpoint.MapEndpoints(app);
}

app.Run();
