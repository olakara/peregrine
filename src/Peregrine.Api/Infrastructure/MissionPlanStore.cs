using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Peregrine.Api.Infrastructure;

/// <summary>
/// Persists a QGroundControl .plan JSON file to disk so that uploaded missions
/// survive API restarts. The file is written next to drone.yaml in ContentRootPath.
/// </summary>
public sealed class MissionPlanStore
{
    private readonly string _planFilePath;
    private readonly ILogger<MissionPlanStore>? _logger;

    public MissionPlanStore(IWebHostEnvironment env, ILogger<MissionPlanStore> logger)
        : this(Path.Combine(env.ContentRootPath, "mission.plan"), logger) { }

    // Second constructor used by tests to inject a temp-directory path (no logger needed).
    internal MissionPlanStore(string planFilePath, ILogger<MissionPlanStore>? logger = null)
    {
        _planFilePath = planFilePath;
        _logger = logger;
    }

    public void Save(string json)
    {
        try
        {
            File.WriteAllText(_planFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to persist mission plan to {Path}.", _planFilePath);
        }
    }

    public string? Load()
    {
        try
        {
            return File.Exists(_planFilePath) ? File.ReadAllText(_planFilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to read mission plan from {Path}.", _planFilePath);
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_planFilePath))
                File.Delete(_planFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to delete mission plan at {Path}.", _planFilePath);
        }
    }
}
