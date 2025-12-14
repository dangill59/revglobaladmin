using MongoDB.Bson;
using MongoDB.Driver;

namespace GlobalAdmin.Services;

public class WorkspaceStats
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public long DatabaseSizeBytes { get; set; }
    public long StorageSizeBytes { get; set; }
    public int DocumentCount { get; set; }
    public int UserCount { get; set; }
    public int LicenseCount { get; set; }
    public int ActiveUsersLast30Days { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class GlobalStats
{
    public int TotalWorkspaces { get; set; }
    public int TotalUsers { get; set; }
    public int TotalDocuments { get; set; }
    public long TotalDatabaseSizeBytes { get; set; }
    public long TotalStorageSizeBytes { get; set; }
    public List<WorkspaceStats> TopWorkspacesBySize { get; set; } = new();
    public List<WorkspaceStats> TopWorkspacesByDocuments { get; set; } = new();
}

public class AnalyticsService
{
    private readonly IMongoClient _mongoClient;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly WorkspaceService _workspaceService;

    public AnalyticsService(
        IMongoClient mongoClient,
        WorkspaceService workspaceService,
        ILogger<AnalyticsService> logger)
    {
        _mongoClient = mongoClient;
        _workspaceService = workspaceService;
        _logger = logger;
    }

    public async Task<WorkspaceStats> GetWorkspaceStatsAsync(BsonDocument workspace)
    {
        var workspaceId = WorkspaceService.GetId(workspace);
        var workspaceName = WorkspaceService.GetString(workspace, "name");

        var stats = new WorkspaceStats
        {
            WorkspaceId = workspaceId,
            WorkspaceName = workspaceName,
            LicenseCount = WorkspaceService.GetInt(workspace, "maxUsers", 5)
        };

        // Check features.revSeats.count or features.userCount.count for license count
        if (workspace.Contains("features") && workspace["features"].IsBsonDocument)
        {
            var features = workspace["features"].AsBsonDocument;
            if (features.Contains("revSeats") && features["revSeats"].IsBsonDocument)
            {
                var revSeats = features["revSeats"].AsBsonDocument;
                if (revSeats.Contains("count"))
                {
                    stats.LicenseCount = revSeats["count"].ToInt32();
                }
            }
        }

        try
        {
            if (string.IsNullOrEmpty(workspaceId)) return stats;

            // Database name is "rev_{workspaceId}" not the workspace name
            var dbName = $"rev_{workspaceId}";
            var db = _mongoClient.GetDatabase(dbName);

            // Get database stats
            var dbStats = await db.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
            stats.DatabaseSizeBytes = dbStats.GetValue("dataSize", 0).ToInt64();
            stats.StorageSizeBytes = dbStats.GetValue("storageSize", 0).ToInt64();

            // Get document count (collection is "pageholders" lowercase)
            var pageHolders = db.GetCollection<BsonDocument>("pageholders");
            stats.DocumentCount = (int)await pageHolders.CountDocumentsAsync(new BsonDocument("_t", "DocumentModel"));

            // Get user count (collection is "workspaceUsers")
            var users = db.GetCollection<BsonDocument>("workspaceUsers");
            stats.UserCount = (int)await users.CountDocumentsAsync(new BsonDocument());

            // Get active users in last 30 days
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var activeFilter = Builders<BsonDocument>.Filter.Gte("lastLogin", thirtyDaysAgo);
            stats.ActiveUsersLast30Days = (int)await users.CountDocumentsAsync(activeFilter);

            // Get last activity (most recent document modified date)
            var lastDoc = await pageHolders
                .Find(new BsonDocument())
                .Sort(Builders<BsonDocument>.Sort.Descending("modified"))
                .Limit(1)
                .FirstOrDefaultAsync();

            if (lastDoc != null && lastDoc.Contains("modified"))
            {
                stats.LastActivity = lastDoc["modified"].ToUniversalTime();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stats for workspace {Name}", workspaceName);
        }

        return stats;
    }

    public async Task<GlobalStats> GetGlobalStatsAsync()
    {
        var workspaces = await _workspaceService.GetAllWorkspacesAsync();
        var stats = new GlobalStats
        {
            TotalWorkspaces = workspaces.Count
        };

        var workspaceStatsList = new List<WorkspaceStats>();

        foreach (var workspace in workspaces)
        {
            var ws = await GetWorkspaceStatsAsync(workspace);
            workspaceStatsList.Add(ws);

            stats.TotalUsers += ws.UserCount;
            stats.TotalDocuments += ws.DocumentCount;
            stats.TotalDatabaseSizeBytes += ws.DatabaseSizeBytes;
            stats.TotalStorageSizeBytes += ws.StorageSizeBytes;
        }

        stats.TopWorkspacesBySize = workspaceStatsList
            .OrderByDescending(w => w.DatabaseSizeBytes)
            .Take(10)
            .ToList();

        stats.TopWorkspacesByDocuments = workspaceStatsList
            .OrderByDescending(w => w.DocumentCount)
            .Take(10)
            .ToList();

        return stats;
    }

    public string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
