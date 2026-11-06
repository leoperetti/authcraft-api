using System.Reflection;
using Infrastructure.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace authcraft_api.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly DatabaseReadinessCheck.IChecker _databaseReadinessCheck;
    private readonly ILogger<HealthController> _logger;
    private readonly string _version;

    public HealthController(
        DatabaseReadinessCheck.IChecker databaseReadinessCheck,
        ILogger<HealthController> logger)
    {
        _databaseReadinessCheck = databaseReadinessCheck;
        _logger = logger;
        _version = GetCommitVersion();
    }

    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new HealthResponse("healthy", _version));
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        if (await _databaseReadinessCheck.IsReadyAsync(cancellationToken))
        {
            return Ok(new HealthResponse("healthy", _version));
        }

        _logger.LogWarning(
            "Database readiness check failed for request {TraceIdentifier}.",
            HttpContext.TraceIdentifier);
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new HealthResponse("unhealthy", _version));
    }

    private static string GetCommitVersion()
    {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "CommitSha");
        if (string.IsNullOrWhiteSpace(metadata?.Value))
        {
            throw new InvalidOperationException("The application build does not include commit version metadata.");
        }

        return metadata.Value;
    }

    private sealed record HealthResponse(string Status, string Version);
}
