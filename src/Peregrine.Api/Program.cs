using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using NetEscapades.Configuration.Yaml;
using Peregrine.Api;
using Peregrine.Api.Infrastructure;
using Peregrine.Api.Infrastructure.Configuration;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

// Bootstrap logger captures fatal errors before the host is fully built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Load drone.yaml as primary configuration source
    var yamlPath = Path.Combine(AppContext.BaseDirectory, "drone.yaml");
    if (!File.Exists(yamlPath))
        yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "drone.yaml");

    builder.Configuration.AddYamlFile(yamlPath, optional: false, reloadOnChange: false);

    // Replace default logging with Serilog. Log levels are driven by the
    // "Serilog" section in appsettings; the output format is controlled by
    // "Logging:UseJsonFormat" (true = compact JSON, false = timestamped text).
    builder.Host.UseSerilog((context, services, loggerConfig) =>
    {
        var useJson = context.Configuration.GetValue<bool>("Logging:UseJsonFormat", true);

        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        if (useJson)
            loggerConfig.WriteTo.Console(new RenderedCompactJsonFormatter());
        else
            loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    });

    // HTTP logging: method, path, status code, and duration per request.
    builder.Services.AddHttpLogging(opts =>
    {
        opts.LoggingFields = HttpLoggingFields.RequestMethod
            | HttpLoggingFields.RequestPath
            | HttpLoggingFields.ResponseStatusCode
            | HttpLoggingFields.Duration;
    });

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

    // Must be registered before routing so every request is captured.
    app.UseHttpLogging();

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
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw; // ensures non-zero exit code for orchestrators and supervisors
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
