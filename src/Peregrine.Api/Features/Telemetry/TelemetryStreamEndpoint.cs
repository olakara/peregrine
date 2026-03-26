using System.Text.Json;
using System.Text.Json.Serialization;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Telemetry;

public sealed class TelemetryStreamEndpoint : IEndpoint
{
    private static readonly JsonSerializerOptions SseSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/drone/telemetry", async (
            TelemetryBroadcaster broadcaster,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            await foreach (var status in broadcaster.Subscribe(cancellationToken))
            {
                var json = JsonSerializer.Serialize(status, SseSerializerOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        })
        .WithName("TelemetryStream")
        .WithSummary("Server-Sent Events stream of drone status updates")
        .WithTags("Telemetry")
        .Produces(200, contentType: "text/event-stream");
    }
}
