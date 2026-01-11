using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GlobalAdmin.Services;
using MongoDB.Bson;

namespace GlobalAdmin.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly WorkspaceService _workspaceService;
    private readonly AnalyticsService _analyticsService;
    private readonly RabbitMQService _rabbitMQService;
    private readonly ILogger<WorkspacesController> _logger;

    public WorkspacesController(
        WorkspaceService workspaceService,
        AnalyticsService analyticsService,
        RabbitMQService rabbitMQService,
        ILogger<WorkspacesController> logger)
    {
        _workspaceService = workspaceService;
        _analyticsService = analyticsService;
        _rabbitMQService = rabbitMQService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var workspaces = await _workspaceService.GetAllWorkspacesAsync();
        var result = new List<object>();

        foreach (var ws in workspaces)
        {
            var stats = await _analyticsService.GetWorkspaceStatsAsync(ws);
            result.Add(new
            {
                id = WorkspaceService.GetId(ws),
                name = WorkspaceService.GetString(ws, "name"),
                ownerUser = WorkspaceService.GetString(ws, "ownerUser"),
                maxUsers = WorkspaceService.GetInt(ws, "maxUsers", 5),
                isDisabled = WorkspaceService.GetBool(ws, "isDisabled"),
                created = ws.Contains("created") && ws["created"].IsBsonDateTime
                    ? ws["created"].ToUniversalTime() : (DateTime?)null,
                userCount = stats.UserCount,
                documentCount = stats.DocumentCount,
                databaseSizeBytes = stats.DatabaseSizeBytes
            });
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var workspace = await _workspaceService.GetWorkspaceByIdAsync(id);
        if (workspace == null)
            return NotFound();

        var stats = await _analyticsService.GetWorkspaceStatsAsync(workspace);

        // Extract features
        var features = workspace.Contains("features") && workspace["features"].IsBsonDocument
            ? workspace["features"].AsBsonDocument : new BsonDocument();

        // Extract soft delete config
        var softDeleteConfig = workspace.Contains("softDeleteConfig") && workspace["softDeleteConfig"].IsBsonDocument
            ? workspace["softDeleteConfig"].AsBsonDocument : new BsonDocument();

        // Extract audit config
        var auditConfig = workspace.Contains("auditConfig") && workspace["auditConfig"].IsBsonDocument
            ? workspace["auditConfig"].AsBsonDocument : new BsonDocument();

        return Ok(new
        {
            id = WorkspaceService.GetId(workspace),
            name = WorkspaceService.GetString(workspace, "name"),
            ownerUser = WorkspaceService.GetString(workspace, "ownerUser"),
            maxUsers = WorkspaceService.GetInt(workspace, "maxUsers", 5),
            isDisabled = WorkspaceService.GetBool(workspace, "isDisabled"),
            created = workspace.Contains("created") && workspace["created"].IsBsonDateTime
                ? workspace["created"].ToUniversalTime() : (DateTime?)null,
            stats = new
            {
                userCount = stats.UserCount,
                documentCount = stats.DocumentCount,
                databaseSizeBytes = stats.DatabaseSizeBytes,
                storageSizeBytes = stats.StorageSizeBytes,
                licenseCount = stats.LicenseCount
            },
            settings = new
            {
                // Soft Delete - enabled via features.softDelete, config is separate
                softDeleteEnabled = features.Contains("softDelete"),
                softDeleteRetentionDays = softDeleteConfig.Contains("retentionDays") ? softDeleteConfig["retentionDays"].ToInt32() : 30,
                softDeleteAutoDelete = softDeleteConfig.Contains("autoDeleteEnabled") && softDeleteConfig["autoDeleteEnabled"].AsBoolean,
                softDeleteRequireReason = softDeleteConfig.Contains("requireReasonOnDelete") && softDeleteConfig["requireReasonOnDelete"].AsBoolean,
                softDeleteNotifyOnPurge = softDeleteConfig.Contains("notifyOnPurge") && softDeleteConfig["notifyOnPurge"].AsBoolean,

                // Features
                featureFullTextOcr = features.Contains("fullTextOCR"),
                featureBarcode = features.Contains("barcode"),
                featureScripts = features.Contains("scripts"),
                featureTwoFactor = features.Contains("twofactorAuth"),

                // OCR
                ocrEngine = features.Contains("fullTextOCR") && features["fullTextOCR"].IsBsonDocument
                    && features["fullTextOCR"].AsBsonDocument.Contains("config")
                    && features["fullTextOCR"]["config"].IsBsonDocument
                    ? WorkspaceService.GetString(features["fullTextOCR"]["config"].AsBsonDocument, "ocrEngine", "tess") : "tess",
                googleOcrQuota = workspace.Contains("quotas") && workspace["quotas"].IsBsonDocument
                    && workspace["quotas"].AsBsonDocument.Contains("googleOCR")
                    && workspace["quotas"]["googleOCR"].IsBsonDocument
                    ? WorkspaceService.GetInt(workspace["quotas"]["googleOCR"].AsBsonDocument, "limit", 0) : 0,

                // Session & Activity
                inactivityTimeout = WorkspaceService.GetInt(workspace, "inactivityTimeoutMin", 15),
                activityRetentionHours = WorkspaceService.GetInt(workspace, "activityRetentionHours", 24),

                // Processing - value stored in bytes, display in MB (default 50MB)
                maxImmediateSizeMB = WorkspaceService.GetLong(workspace, "maxImmediatePageProcessingSize", 0) > 0
                    ? (int)(WorkspaceService.GetLong(workspace, "maxImmediatePageProcessingSize", 0) / (1024 * 1024))
                    : 50,
                suspendProcessing = WorkspaceService.GetBool(workspace, "suspendBackGroundImageProcessing"),

                // Custom Branding
                customBrandingEnabled = features.Contains("customBranding"),
                brandingLogoUrl = workspace.Contains("branding") && workspace["branding"].IsBsonDocument
                    ? WorkspaceService.GetString(workspace["branding"].AsBsonDocument, "logoUrl", "") : "",
                brandingPrimaryColor = workspace.Contains("branding") && workspace["branding"].IsBsonDocument
                    ? WorkspaceService.GetString(workspace["branding"].AsBsonDocument, "primaryColor", "#0d6efd") : "#0d6efd",

                // Audit Logs - check auditConfig.enabled (the field the app actually uses)
                auditLogsEnabled = auditConfig.Contains("enabled") && auditConfig["enabled"].AsBoolean
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest request)
    {
        try
        {
            var workspace = await _workspaceService.CreateWorkspaceAsync(
                request.Name,
                request.OwnerEmail,
                request.MaxUsers);

            return Ok(new {
                id = WorkspaceService.GetId(workspace),
                name = request.Name
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/settings")]
    public async Task<IActionResult> UpdateSettings(string id, [FromBody] WorkspaceSettings settings)
    {
        try
        {
            await _workspaceService.UpdateWorkspaceSettingsAsync(id, settings);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/license")]
    public async Task<IActionResult> UpdateLicense(string id, [FromBody] UpdateLicenseRequest request)
    {
        try
        {
            await _workspaceService.UpdateLicenseCountAsync(id, request.MaxUsers);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/disable")]
    public async Task<IActionResult> Disable(string id)
    {
        await _workspaceService.DisableWorkspaceAsync(id);
        return Ok();
    }

    [HttpPost("{id}/enable")]
    public async Task<IActionResult> Enable(string id)
    {
        await _workspaceService.EnableWorkspaceAsync(id);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _workspaceService.DeleteWorkspaceAsync(id);
        if (result.Success)
            return Ok(result);
        return BadRequest(result);
    }

    [HttpGet("{id}/users")]
    public async Task<IActionResult> GetUsers(string id)
    {
        var users = await _workspaceService.GetWorkspaceUsersAsync(id);
        return Ok(users.Select(u => new
        {
            id = WorkspaceService.GetId(u),
            // Handle both old (userId) and new (userName/emailAddress) field names
            userName = WorkspaceService.GetString(u, "userName", WorkspaceService.GetString(u, "userId")),
            emailAddress = WorkspaceService.GetString(u, "emailAddress", WorkspaceService.GetString(u, "userId")),
            // Handle both old (fullName) and new (preferredName) field names
            preferredName = WorkspaceService.GetString(u, "preferredName", WorkspaceService.GetString(u, "fullName")),
            isAdmin = WorkspaceService.GetBool(u, "isAdmin")
        }));
    }

    [HttpPost("{id}/fix-processing-size")]
    public async Task<IActionResult> FixProcessingSize(string id)
    {
        try
        {
            // Reset maxImmediatePageProcessingSize to 0 (use default 50MB)
            await _workspaceService.ResetMaxImmediatePageProcessingSizeAsync(id);
            return Ok(new { message = "Reset maxImmediatePageProcessingSize to default (50MB)" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/reindex")]
    public async Task<IActionResult> TriggerReindex(string id)
    {
        try
        {
            var success = await _rabbitMQService.TriggerReindexAsync(id);
            if (success)
            {
                _logger.LogInformation("Triggered reindex for workspace {WorkspaceId}", id);
                return Ok(new { message = "Reindex triggered successfully. Documents will be indexed in the background." });
            }
            return BadRequest(new { error = "Failed to trigger reindex. Check RabbitMQ connection." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger reindex for workspace {WorkspaceId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record CreateWorkspaceRequest(string Name, string OwnerEmail, int MaxUsers = 5);
public record UpdateLicenseRequest(int MaxUsers);
