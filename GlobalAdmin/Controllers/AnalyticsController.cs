using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GlobalAdmin.Services;

namespace GlobalAdmin.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(AnalyticsService analyticsService, ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    [HttpGet("global")]
    public async Task<IActionResult> GetGlobalStats()
    {
        var stats = await _analyticsService.GetGlobalStatsAsync();
        return Ok(new
        {
            totalWorkspaces = stats.TotalWorkspaces,
            totalUsers = stats.TotalUsers,
            totalDocuments = stats.TotalDocuments,
            totalDatabaseSizeBytes = stats.TotalDatabaseSizeBytes,
            totalStorageSizeBytes = stats.TotalStorageSizeBytes,
            topWorkspacesBySize = stats.TopWorkspacesBySize.Select(w => new
            {
                workspaceId = w.WorkspaceId,
                workspaceName = w.WorkspaceName,
                databaseSizeBytes = w.DatabaseSizeBytes,
                documentCount = w.DocumentCount,
                userCount = w.UserCount
            }),
            topWorkspacesByDocuments = stats.TopWorkspacesByDocuments.Select(w => new
            {
                workspaceId = w.WorkspaceId,
                workspaceName = w.WorkspaceName,
                databaseSizeBytes = w.DatabaseSizeBytes,
                documentCount = w.DocumentCount,
                userCount = w.UserCount
            })
        });
    }
}
