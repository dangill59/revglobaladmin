using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GlobalAdmin.Services;
using GlobalAdmin.Models;

namespace GlobalAdmin.Controllers;

[ApiController]
[Route("api/installs")]
[Authorize]
public class OnPremController : ControllerBase
{
    private readonly OnPremService _onPremService;
    private readonly ILogger<OnPremController> _logger;

    public OnPremController(OnPremService onPremService, ILogger<OnPremController> logger)
    {
        _onPremService = onPremService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllInstalls()
    {
        var installs = await _onPremService.GetAllInstallsAsync();
        return Ok(installs.Select(i => new
        {
            i.Id,
            i.CustomerName,
            i.ContactEmail,
            i.Version,
            i.Status,
            registeredAt = i.Registered,
            i.LastHeartbeat,
            metrics = i.Metrics != null ? new
            {
                i.Metrics.ActiveUsers,
                totalDocuments = i.Metrics.DocumentCount,
                storageUsedBytes = (long)(i.Metrics.StorageUsedGB * 1024 * 1024 * 1024)
            } : null,
            license = i.License != null ? new
            {
                i.License.MaxUsers,
                i.License.Tier
            } : null
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetInstall(string id)
    {
        var install = await _onPremService.GetInstallAsync(id);
        if (install == null)
            return NotFound();

        return Ok(install);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var installs = await _onPremService.GetAllInstallsAsync();
        var summary = new
        {
            total = installs.Count,
            healthy = installs.Count(i => i.Status == "healthy"),
            warning = installs.Count(i => i.Status == "warning"),
            offline = installs.Count(i => i.Status == "offline"),
            totalDocuments = installs.Sum(i => i.Metrics?.DocumentCount ?? 0),
            totalStorage = (long)installs.Sum(i => (i.Metrics?.StorageUsedGB ?? 0) * 1024 * 1024 * 1024)
        };
        return Ok(summary);
    }
}

[ApiController]
[Route("api/registrationkeys")]
[Authorize]
public class RegistrationKeysController : ControllerBase
{
    private readonly OnPremService _onPremService;
    private readonly ILogger<RegistrationKeysController> _logger;

    public RegistrationKeysController(OnPremService onPremService, ILogger<RegistrationKeysController> logger)
    {
        _onPremService = onPremService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetKeys()
    {
        var keys = await _onPremService.GetUnusedRegistrationKeysAsync();
        return Ok(keys);
    }

    [HttpPost]
    public async Task<IActionResult> GenerateKey([FromBody] GenerateKeyRequest request)
    {
        try
        {
            var key = await _onPremService.GenerateRegistrationKeyAsync(
                request.CustomerName,
                request.ContactEmail,
                request.MaxUsers,
                request.Tier,
                request.ValidityDays);

            return Ok(key);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> RevokeKey(string key)
    {
        // TODO: Implement key revocation in OnPremService
        return Ok();
    }
}

public record GenerateKeyRequest(
    string CustomerName,
    string ContactEmail,
    int MaxUsers = 5,
    string Tier = "standard",
    int ValidityDays = 30);
