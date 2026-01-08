using GlobalAdmin.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GlobalAdmin.Services;

public class OnPremService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<OnPremService> _logger;

    // Status thresholds
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromHours(2);

    public OnPremService(IMongoClient mongoClient, ILogger<OnPremService> logger)
    {
        _db = mongoClient.GetDatabase("globalAuth");
        _logger = logger;
    }

    private IMongoCollection<BsonDocument> Installs =>
        _db.GetCollection<BsonDocument>("onpremInstalls");

    private IMongoCollection<BsonDocument> RegistrationKeys =>
        _db.GetCollection<BsonDocument>("registrationKeys");

    private IMongoCollection<BsonDocument> PendingCommands =>
        _db.GetCollection<BsonDocument>("onpremCommands");

    #region Registration

    /// <summary>
    /// Generate a new registration key for a customer
    /// </summary>
    public async Task<RegistrationKey> GenerateRegistrationKeyAsync(
        string customerName,
        string contactEmail,
        int maxUsers = 10,
        string tier = "standard",
        int validDays = 30)
    {
        var key = $"REG-{GenerateShortId()}-{DateTime.UtcNow:yyyyMMdd}";

        var doc = new BsonDocument
        {
            { "_id", key },
            { "customerName", customerName },
            { "contactEmail", contactEmail },
            { "maxUsers", maxUsers },
            { "tier", tier },
            { "createdAt", DateTime.UtcNow },
            { "expiresAt", DateTime.UtcNow.AddDays(validDays) },
            { "used", false }
        };

        await RegistrationKeys.InsertOneAsync(doc);

        _logger.LogInformation("Generated registration key {Key} for {Customer}", key, customerName);

        return new RegistrationKey
        {
            Key = key,
            CustomerName = customerName,
            ContactEmail = contactEmail,
            MaxUsers = maxUsers,
            Tier = tier,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(validDays)
        };
    }

    /// <summary>
    /// Validate a registration key
    /// </summary>
    public async Task<(bool IsValid, string? Error, BsonDocument? KeyDoc)> ValidateRegistrationKeyAsync(string key)
    {
        var keyDoc = await RegistrationKeys.Find(
            Builders<BsonDocument>.Filter.Eq("_id", key)).FirstOrDefaultAsync();

        if (keyDoc == null)
            return (false, "Invalid registration key", null);

        if (keyDoc.GetValue("used", false).AsBoolean)
            return (false, "Registration key has already been used", null);

        if (keyDoc.Contains("expiresAt"))
        {
            var expiresAt = keyDoc["expiresAt"].ToUniversalTime();
            if (expiresAt < DateTime.UtcNow)
                return (false, "Registration key has expired", null);
        }

        return (true, null, keyDoc);
    }

    /// <summary>
    /// Register a new on-prem installation
    /// </summary>
    public async Task<OnPremInstall> RegisterInstallAsync(RegisterInstallRequest request)
    {
        var (isValid, error, keyDoc) = await ValidateRegistrationKeyAsync(request.RegistrationKey);

        if (!isValid || keyDoc == null)
            throw new InvalidOperationException(error ?? "Invalid registration key");

        var installId = ObjectId.GenerateNewId().ToString();
        var apiKey = GenerateApiKey();

        // Get license info from registration key
        var maxUsers = keyDoc.GetValue("maxUsers", 10).ToInt32();
        var tier = keyDoc.GetValue("tier", "standard").AsString;

        var doc = new BsonDocument
        {
            { "_id", installId },
            { "customerName", request.CustomerName },
            { "contactEmail", request.ContactEmail },
            { "version", request.Version },
            { "apiKey", apiKey },
            { "status", "healthy" },
            { "registered", DateTime.UtcNow },
            { "lastHeartbeat", DateTime.UtcNow },
            { "configChanged", false },
            { "license", new BsonDocument {
                { "maxUsers", maxUsers },
                { "tier", tier },
                { "enabledFeatures", new BsonArray { "zoneOcr", "automation" } }
            }},
            { "config", new BsonDocument {
                { "ocrEngine", "tess" },
                { "storageType", "minio" },
                { "backupsEnabled", true },
                { "heartbeatIntervalMinutes", 15 }
            }}
        };

        await Installs.InsertOneAsync(doc);

        // Mark registration key as used
        await RegistrationKeys.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", request.RegistrationKey),
            Builders<BsonDocument>.Update
                .Set("used", true)
                .Set("usedAt", DateTime.UtcNow)
                .Set("installId", installId));

        _logger.LogInformation("Registered new on-prem install: {Customer} ({Id})",
            request.CustomerName, installId);

        return MapToInstall(doc);
    }

    #endregion

    #region Authentication

    /// <summary>
    /// Validate an API key for an installation
    /// </summary>
    public async Task<bool> ValidateApiKeyAsync(string installId, string apiKey)
    {
        if (string.IsNullOrEmpty(installId) || string.IsNullOrEmpty(apiKey))
            return false;

        var install = await Installs.Find(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", installId),
                Builders<BsonDocument>.Filter.Eq("apiKey", apiKey)
            )).FirstOrDefaultAsync();

        return install != null;
    }

    #endregion

    #region Heartbeat Processing

    /// <summary>
    /// Process a heartbeat from an on-prem agent
    /// </summary>
    public async Task ProcessHeartbeatAsync(AgentHeartbeat heartbeat)
    {
        var updateDef = Builders<BsonDocument>.Update
            .Set("lastHeartbeat", DateTime.UtcNow)
            .Set("version", heartbeat.Version)
            .Set("status", "healthy");

        // Update metrics if provided
        if (heartbeat.Metrics != null)
        {
            updateDef = updateDef.Set("metrics", new BsonDocument
            {
                { "activeUsers", heartbeat.Metrics.ActiveUsers },
                { "totalUsers", heartbeat.Metrics.TotalUsers },
                { "documentCount", heartbeat.Metrics.DocumentCount },
                { "storageUsedGB", heartbeat.Metrics.StorageUsedGB },
                { "pagesProcessedThisMonth", heartbeat.Metrics.PagesProcessedThisMonth },
                { "ocrPagesThisMonth", heartbeat.Metrics.OcrPagesThisMonth },
                { "collectedAt", heartbeat.Metrics.CollectedAt }
            });
        }

        // Update service status if provided
        if (heartbeat.ServiceStatus != null)
        {
            var statusDoc = new BsonDocument();
            foreach (var kvp in heartbeat.ServiceStatus)
            {
                statusDoc[kvp.Key] = kvp.Value;
            }
            updateDef = updateDef.Set("serviceStatus", statusDoc);
        }

        // Update recent errors if provided
        if (heartbeat.RecentErrors != null && heartbeat.RecentErrors.Any())
        {
            var errorsArray = new BsonArray(
                heartbeat.RecentErrors.Select(e => new BsonDocument
                {
                    { "timestamp", e.Timestamp },
                    { "level", e.Level },
                    { "service", e.Service },
                    { "message", e.Message }
                }));
            updateDef = updateDef.Set("recentErrors", errorsArray);
        }

        await Installs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", heartbeat.InstallId),
            updateDef);

        _logger.LogDebug("Processed heartbeat from {InstallId}", heartbeat.InstallId);
    }

    /// <summary>
    /// Get pending commands and config for an installation
    /// </summary>
    public async Task<HeartbeatResponse> GetHeartbeatResponseAsync(string installId)
    {
        var install = await Installs.Find(
            Builders<BsonDocument>.Filter.Eq("_id", installId)).FirstOrDefaultAsync();

        if (install == null)
            return new HeartbeatResponse();

        var response = new HeartbeatResponse
        {
            ConfigChanged = install.GetValue("configChanged", false).AsBoolean
        };

        // Include config and license if changed
        if (response.ConfigChanged)
        {
            response.Config = MapToConfig(install);
            response.License = MapToLicense(install);

            // Clear the configChanged flag
            await Installs.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", installId),
                Builders<BsonDocument>.Update.Set("configChanged", false));
        }

        // Get pending commands
        var commands = await PendingCommands.Find(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("installId", installId),
                Builders<BsonDocument>.Filter.Eq("acknowledged", false)
            )).ToListAsync();

        if (commands.Any())
        {
            response.PendingCommands = commands.Select(c => new AgentCommand
            {
                Id = c["_id"].AsString,
                Type = c.GetValue("type", "").AsString,
                Parameters = c.Contains("parameters")
                    ? c["parameters"].AsBsonDocument.ToDictionary(
                        e => e.Name,
                        e => e.Value.AsString)
                    : new Dictionary<string, string>(),
                CreatedAt = c.GetValue("createdAt", DateTime.UtcNow).ToUniversalTime()
            }).ToList();
        }

        return response;
    }

    /// <summary>
    /// Acknowledge a command has been received
    /// </summary>
    public async Task AcknowledgeCommandAsync(string commandId)
    {
        await PendingCommands.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", commandId),
            Builders<BsonDocument>.Update
                .Set("acknowledged", true)
                .Set("acknowledgedAt", DateTime.UtcNow));
    }

    #endregion

    #region Install Management

    /// <summary>
    /// Get all on-prem installations
    /// </summary>
    public async Task<List<OnPremInstall>> GetAllInstallsAsync()
    {
        var docs = await Installs.Find(_ => true)
            .SortByDescending(d => d["lastHeartbeat"])
            .ToListAsync();

        // Update statuses based on heartbeat times
        var installs = docs.Select(MapToInstall).ToList();
        foreach (var install in installs)
        {
            install.Status = CalculateStatus(install.LastHeartbeat);
        }

        return installs;
    }

    /// <summary>
    /// Get a specific installation by ID
    /// </summary>
    public async Task<OnPremInstall?> GetInstallAsync(string installId)
    {
        var doc = await Installs.Find(
            Builders<BsonDocument>.Filter.Eq("_id", installId)).FirstOrDefaultAsync();

        if (doc == null) return null;

        var install = MapToInstall(doc);
        install.Status = CalculateStatus(install.LastHeartbeat);
        return install;
    }

    /// <summary>
    /// Update license for an installation
    /// </summary>
    public async Task<bool> UpdateLicenseAsync(string installId, InstallLicense license)
    {
        var update = Builders<BsonDocument>.Update
            .Set("license.maxUsers", license.MaxUsers)
            .Set("license.tier", license.Tier)
            .Set("license.enabledFeatures", new BsonArray(license.EnabledFeatures))
            .Set("configChanged", true);

        if (license.GoogleOcrQuota.HasValue)
        {
            update = update.Set("license.googleOcrQuota", license.GoogleOcrQuota.Value);
        }

        if (license.ExpiresAt.HasValue)
        {
            update = update.Set("license.expiresAt", license.ExpiresAt.Value);
        }

        var result = await Installs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", installId),
            update);

        _logger.LogInformation("Updated license for {InstallId}: MaxUsers={MaxUsers}, Tier={Tier}",
            installId, license.MaxUsers, license.Tier);

        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Update configuration for an installation
    /// </summary>
    public async Task<bool> UpdateConfigAsync(string installId, InstallConfig config)
    {
        var update = Builders<BsonDocument>.Update
            .Set("config.ocrEngine", config.OcrEngine)
            .Set("config.storageType", config.StorageType)
            .Set("config.backupsEnabled", config.BackupsEnabled)
            .Set("config.heartbeatIntervalMinutes", config.HeartbeatIntervalMinutes)
            .Set("configChanged", true);

        if (!string.IsNullOrEmpty(config.BackupEndpoint))
        {
            update = update.Set("config.backupEndpoint", config.BackupEndpoint);
        }

        if (!string.IsNullOrEmpty(config.BackupBucket))
        {
            update = update.Set("config.backupBucket", config.BackupBucket);
        }

        var result = await Installs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", installId),
            update);

        _logger.LogInformation("Updated config for {InstallId}: OCR={OcrEngine}, Storage={StorageType}",
            installId, config.OcrEngine, config.StorageType);

        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Queue a command for an installation
    /// </summary>
    public async Task QueueCommandAsync(string installId, string commandType, Dictionary<string, string>? parameters = null)
    {
        var doc = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "installId", installId },
            { "type", commandType },
            { "parameters", parameters != null ? new BsonDocument(parameters.Select(kvp => new BsonElement(kvp.Key, kvp.Value))) : new BsonDocument() },
            { "createdAt", DateTime.UtcNow },
            { "acknowledged", false }
        };

        await PendingCommands.InsertOneAsync(doc);

        _logger.LogInformation("Queued command {Type} for {InstallId}", commandType, installId);
    }

    /// <summary>
    /// Record backup completion
    /// </summary>
    public async Task RecordBackupAsync(BackupCompleteRequest request)
    {
        var update = Builders<BsonDocument>.Update
            .Set("lastBackup", request.CompletedAt)
            .Set("lastBackupStatus", request.Success ? "success" : "failed")
            .Set("lastBackupSizeBytes", request.BackupSizeBytes);

        if (!string.IsNullOrEmpty(request.Error))
        {
            update = update.Set("lastBackupError", request.Error);
        }

        await Installs.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", request.InstallId),
            update);

        _logger.LogInformation("Recorded backup for {InstallId}: Success={Success}",
            request.InstallId, request.Success);
    }

    #endregion

    #region Summary & Stats

    /// <summary>
    /// Get summary statistics for all on-prem installations
    /// </summary>
    public async Task<OnPremSummary> GetSummaryAsync(string? latestVersion = null)
    {
        var installs = await GetAllInstallsAsync();

        return new OnPremSummary
        {
            TotalInstalls = installs.Count,
            HealthyCount = installs.Count(i => i.Status == "healthy"),
            WarningCount = installs.Count(i => i.Status == "warning"),
            OfflineCount = installs.Count(i => i.Status == "offline"),
            TotalDocuments = installs.Sum(i => i.Metrics?.DocumentCount ?? 0),
            TotalStorageGB = installs.Sum(i => i.Metrics?.StorageUsedGB ?? 0),
            TotalUsers = installs.Sum(i => i.Metrics?.TotalUsers ?? 0),
            InstallsNeedingUpdate = string.IsNullOrEmpty(latestVersion)
                ? 0
                : installs.Count(i => i.Version != latestVersion)
        };
    }

    /// <summary>
    /// Get all unused registration keys
    /// </summary>
    public async Task<List<RegistrationKey>> GetUnusedRegistrationKeysAsync()
    {
        var docs = await RegistrationKeys.Find(
            Builders<BsonDocument>.Filter.Eq("used", false))
            .SortByDescending(d => d["createdAt"])
            .ToListAsync();

        return docs.Select(d => new RegistrationKey
        {
            Key = d["_id"].AsString,
            CustomerName = d.GetValue("customerName", "").AsString,
            ContactEmail = d.GetValue("contactEmail", "").AsString,
            MaxUsers = d.GetValue("maxUsers", 10).ToInt32(),
            Tier = d.GetValue("tier", "standard").AsString,
            CreatedAt = d.GetValue("createdAt", DateTime.UtcNow).ToUniversalTime(),
            ExpiresAt = d.Contains("expiresAt") ? d["expiresAt"].ToUniversalTime() : null
        }).ToList();
    }

    #endregion

    #region Background Tasks

    /// <summary>
    /// Update status for all installations based on heartbeat times
    /// Should be called periodically by a background job
    /// </summary>
    public async Task UpdateAllStatusesAsync()
    {
        var now = DateTime.UtcNow;

        // Mark as warning (no heartbeat in 30 min)
        await Installs.UpdateManyAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Lt("lastHeartbeat", now - WarningThreshold),
                Builders<BsonDocument>.Filter.Gte("lastHeartbeat", now - OfflineThreshold),
                Builders<BsonDocument>.Filter.Ne("status", "warning")),
            Builders<BsonDocument>.Update.Set("status", "warning"));

        // Mark as offline (no heartbeat in 2 hours)
        await Installs.UpdateManyAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Lt("lastHeartbeat", now - OfflineThreshold),
                Builders<BsonDocument>.Filter.Ne("status", "offline")),
            Builders<BsonDocument>.Update.Set("status", "offline"));

        _logger.LogDebug("Updated on-prem installation statuses");
    }

    #endregion

    #region Helpers

    private static string CalculateStatus(DateTime lastHeartbeat)
    {
        var age = DateTime.UtcNow - lastHeartbeat;

        if (age < WarningThreshold)
            return "healthy";
        if (age < OfflineThreshold)
            return "warning";
        return "offline";
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateShortId()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    private static OnPremInstall MapToInstall(BsonDocument doc)
    {
        var install = new OnPremInstall
        {
            Id = doc["_id"].AsString,
            CustomerName = doc.GetValue("customerName", "").AsString,
            ContactEmail = doc.GetValue("contactEmail", "").AsString,
            Version = doc.GetValue("version", "").AsString,
            Status = doc.GetValue("status", "unknown").AsString,
            LastHeartbeat = doc.GetValue("lastHeartbeat", DateTime.MinValue).ToUniversalTime(),
            Registered = doc.GetValue("registered", DateTime.MinValue).ToUniversalTime(),
            ApiKey = doc.GetValue("apiKey", "").AsString,
            ConfigChanged = doc.GetValue("configChanged", false).AsBoolean
        };

        // Map metrics
        if (doc.Contains("metrics") && doc["metrics"].IsBsonDocument)
        {
            var m = doc["metrics"].AsBsonDocument;
            install.Metrics = new InstallMetrics
            {
                ActiveUsers = m.GetValue("activeUsers", 0).ToInt32(),
                TotalUsers = m.GetValue("totalUsers", 0).ToInt32(),
                DocumentCount = m.GetValue("documentCount", 0).ToInt64(),
                StorageUsedGB = m.GetValue("storageUsedGB", 0).ToDouble(),
                PagesProcessedThisMonth = m.GetValue("pagesProcessedThisMonth", 0).ToInt32(),
                OcrPagesThisMonth = m.GetValue("ocrPagesThisMonth", 0).ToInt32(),
                CollectedAt = m.GetValue("collectedAt", DateTime.MinValue).ToUniversalTime()
            };
        }

        // Map license
        install.License = MapToLicense(doc);

        // Map config
        install.Config = MapToConfig(doc);

        // Map service status
        if (doc.Contains("serviceStatus") && doc["serviceStatus"].IsBsonDocument)
        {
            install.ServiceStatus = doc["serviceStatus"].AsBsonDocument
                .ToDictionary(e => e.Name, e => e.Value.AsString);
        }

        // Map recent errors
        if (doc.Contains("recentErrors") && doc["recentErrors"].IsBsonArray)
        {
            install.RecentErrors = doc["recentErrors"].AsBsonArray
                .Select(e => e.AsBsonDocument)
                .Select(e => new AgentLogEntry
                {
                    Timestamp = e.GetValue("timestamp", DateTime.MinValue).ToUniversalTime(),
                    Level = e.GetValue("level", "error").AsString,
                    Service = e.GetValue("service", "").AsString,
                    Message = e.GetValue("message", "").AsString
                }).ToList();
        }

        // Map backup info
        if (doc.Contains("lastBackup"))
        {
            install.LastBackup = doc["lastBackup"].ToUniversalTime();
        }
        if (doc.Contains("lastBackupStatus"))
        {
            install.LastBackupStatus = doc["lastBackupStatus"].AsString;
        }

        return install;
    }

    private static InstallLicense? MapToLicense(BsonDocument doc)
    {
        if (!doc.Contains("license") || !doc["license"].IsBsonDocument)
            return null;

        var l = doc["license"].AsBsonDocument;
        var license = new InstallLicense
        {
            MaxUsers = l.GetValue("maxUsers", 10).ToInt32(),
            Tier = l.GetValue("tier", "standard").AsString
        };

        if (l.Contains("enabledFeatures") && l["enabledFeatures"].IsBsonArray)
        {
            license.EnabledFeatures = l["enabledFeatures"].AsBsonArray
                .Select(f => f.AsString).ToList();
        }

        if (l.Contains("googleOcrQuota"))
        {
            license.GoogleOcrQuota = l["googleOcrQuota"].ToInt32();
        }

        if (l.Contains("expiresAt"))
        {
            license.ExpiresAt = l["expiresAt"].ToUniversalTime();
        }

        return license;
    }

    private static InstallConfig? MapToConfig(BsonDocument doc)
    {
        if (!doc.Contains("config") || !doc["config"].IsBsonDocument)
            return null;

        var c = doc["config"].AsBsonDocument;
        return new InstallConfig
        {
            OcrEngine = c.GetValue("ocrEngine", "tess").AsString,
            StorageType = c.GetValue("storageType", "minio").AsString,
            BackupsEnabled = c.GetValue("backupsEnabled", true).AsBoolean,
            BackupEndpoint = c.Contains("backupEndpoint") ? c["backupEndpoint"].AsString : null,
            BackupBucket = c.Contains("backupBucket") ? c["backupBucket"].AsString : null,
            HeartbeatIntervalMinutes = c.GetValue("heartbeatIntervalMinutes", 15).ToInt32()
        };
    }

    #endregion
}
