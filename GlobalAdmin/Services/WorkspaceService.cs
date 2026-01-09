using MongoDB.Bson;
using MongoDB.Driver;

namespace GlobalAdmin.Services;

public class WorkspaceService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _globalAuthDb;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly StorageCleanupService _storageService;
    private readonly OpenSearchService _openSearchService;

    public WorkspaceService(
        IMongoClient mongoClient,
        ILogger<WorkspaceService> logger,
        StorageCleanupService storageService,
        OpenSearchService openSearchService)
    {
        _mongoClient = mongoClient;
        _globalAuthDb = mongoClient.GetDatabase("globalAuth");
        _logger = logger;
        _storageService = storageService;
        _openSearchService = openSearchService;
    }

    public IMongoCollection<BsonDocument> Workspaces =>
        _globalAuthDb.GetCollection<BsonDocument>("workspaces");

    public async Task<List<BsonDocument>> GetAllWorkspacesAsync()
    {
        return await Workspaces.Find(_ => true).ToListAsync();
    }

    public async Task<BsonDocument?> GetWorkspaceByIdAsync(string id)
    {
        return await Workspaces.Find(Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id))).FirstOrDefaultAsync();
    }

    public async Task<BsonDocument?> GetWorkspaceByNameAsync(string name)
    {
        return await Workspaces.Find(Builders<BsonDocument>.Filter.Eq("name", name)).FirstOrDefaultAsync();
    }

    public async Task<BsonDocument> CreateWorkspaceAsync(string name, string ownerEmail, int maxUsers = 5)
    {
        // 1. Check if workspace already exists
        var existing = await GetWorkspaceByNameAsync(name);
        if (existing != null)
        {
            throw new Exception($"Workspace '{name}' already exists");
        }

        // 2. Create workspace document in globalAuth.workspaces
        var workspace = new BsonDocument
        {
            { "name", name },
            { "ownerUser", ownerEmail },
            { "maxUsers", maxUsers },
            { "isDisabled", false },
            { "created", DateTime.UtcNow },
            { "modified", DateTime.UtcNow }
        };

        await Workspaces.InsertOneAsync(workspace);
        var workspaceId = workspace["_id"].AsObjectId.ToString();
        _logger.LogInformation("Created workspace document: {Name} with ID {Id}", name, workspaceId);

        // 3. Create/ensure owner user exists in globalAuth.allusers
        var allUsersCollection = _globalAuthDb.GetCollection<BsonDocument>("allusers");
        var ownerUser = await allUsersCollection.Find(
            Builders<BsonDocument>.Filter.Eq("emailaddress", ownerEmail)).FirstOrDefaultAsync();

        if (ownerUser == null)
        {
            ownerUser = new BsonDocument
            {
                { "UserName", ownerEmail },
                { "emailaddress", ownerEmail },
                { "preferredName", ownerEmail.Split('@')[0] },
                { "pwdDigest", "" },
                { "isDisabled", false },
                { "created", DateTime.UtcNow },
                { "modified", DateTime.UtcNow }
            };
            await allUsersCollection.InsertOneAsync(ownerUser);
            _logger.LogInformation("Created owner user: {Email}", ownerEmail);
        }

        // 4. Provision the workspace database with required collections
        await ProvisionWorkspaceDatabaseAsync(workspaceId, ownerEmail);

        return workspace;
    }

    private async Task ProvisionWorkspaceDatabaseAsync(string workspaceId, string ownerEmail)
    {
        // Database name is "rev_{workspaceId}"
        var dbName = $"rev_{workspaceId}";
        var db = _mongoClient.GetDatabase(dbName);

        // Create collections (using actual ScanRev collection names)
        var collections = new[] { "workspaceUsers", "pageholders", "projects", "automations", "savedSearches" };

        foreach (var collName in collections)
        {
            try
            {
                await db.CreateCollectionAsync(collName);
            }
            catch (MongoCommandException)
            {
                // Collection already exists, ignore
            }
        }

        // Add owner to workspace users collection
        var usersCollection = db.GetCollection<BsonDocument>("workspaceUsers");
        var workspaceUser = new BsonDocument
        {
            { "userName", ownerEmail },
            { "emailAddress", ownerEmail },
            { "preferredName", ownerEmail.Split('@')[0] },
            { "isAdmin", true },
            { "created", DateTime.UtcNow },
            { "modified", DateTime.UtcNow }
        };
        await usersCollection.InsertOneAsync(workspaceUser);

        // Create a default project
        var projectsCollection = db.GetCollection<BsonDocument>("projects");
        var defaultProject = new BsonDocument
        {
            { "name", "Default Project" },
            { "description", "Default project created with workspace" },
            { "created", DateTime.UtcNow },
            { "modified", DateTime.UtcNow }
        };
        await projectsCollection.InsertOneAsync(defaultProject);

        _logger.LogInformation("Provisioned workspace database: {DbName}", dbName);
    }

    public async Task<bool> UpdateLicenseCountAsync(string workspaceId, int newLicenseCount)
    {
        var update = Builders<BsonDocument>.Update
            .Set("maxUsers", newLicenseCount)
            .Set("modified", DateTime.UtcNow);

        var result = await Workspaces.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)),
            update);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> DisableWorkspaceAsync(string workspaceId)
    {
        var update = Builders<BsonDocument>.Update
            .Set("isDisabled", true)
            .Set("modified", DateTime.UtcNow);

        var result = await Workspaces.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)),
            update);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> EnableWorkspaceAsync(string workspaceId)
    {
        var update = Builders<BsonDocument>.Update
            .Set("isDisabled", false)
            .Set("modified", DateTime.UtcNow);

        var result = await Workspaces.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)),
            update);

        return result.ModifiedCount > 0;
    }

    public async Task<WorkspaceDeletionResult> DeleteWorkspaceAsync(string workspaceId)
    {
        var result = new WorkspaceDeletionResult { WorkspaceId = workspaceId };

        try
        {
            // 1. Delete OpenSearch indexes
            _logger.LogInformation("Deleting OpenSearch indexes for workspace {WorkspaceId}", workspaceId);
            result.OpenSearchDeleted = await _openSearchService.DeleteWorkspaceIndexesAsync(workspaceId);

            // 2. Delete S3 storage
            _logger.LogInformation("Deleting S3 storage for workspace {WorkspaceId}", workspaceId);
            result.StorageObjectsDeleted = await _storageService.DeleteWorkspaceStorageAsync(workspaceId);

            // 3. Drop the workspace MongoDB database
            var dbName = $"rev_{workspaceId}";
            _logger.LogInformation("Dropping MongoDB database {DbName}", dbName);
            await _mongoClient.DropDatabaseAsync(dbName);
            result.DatabaseDropped = true;

            // 4. Delete workspace document from globalAuth.workspaces
            _logger.LogInformation("Deleting workspace document from globalAuth");
            var deleteResult = await Workspaces.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)));
            result.WorkspaceDocDeleted = deleteResult.DeletedCount > 0;

            result.Success = true;
            _logger.LogInformation("Successfully deleted workspace {WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Error deleting workspace {WorkspaceId}", workspaceId);
        }

        return result;
    }

    public async Task<int> GetActiveUserCountAsync(string workspaceId)
    {
        if (string.IsNullOrEmpty(workspaceId)) return 0;

        // Database name is "rev_{workspaceId}"
        var dbName = $"rev_{workspaceId}";
        var workspaceDb = _mongoClient.GetDatabase(dbName);
        var usersCollection = workspaceDb.GetCollection<BsonDocument>("workspaceUsers");

        return (int)await usersCollection.CountDocumentsAsync(new BsonDocument());
    }

    public async Task<List<BsonDocument>> GetWorkspaceUsersAsync(string workspaceId)
    {
        if (string.IsNullOrEmpty(workspaceId)) return new List<BsonDocument>();

        // Database name is "rev_{workspaceId}"
        var dbName = $"rev_{workspaceId}";
        var workspaceDb = _mongoClient.GetDatabase(dbName);
        var usersCollection = workspaceDb.GetCollection<BsonDocument>("workspaceUsers");

        return await usersCollection.Find(_ => true).ToListAsync();
    }

    // Helper methods for reading BsonDocument fields
    public static string GetString(BsonDocument doc, string field, string defaultValue = "")
    {
        return doc.Contains(field) && !doc[field].IsBsonNull ? doc[field].AsString : defaultValue;
    }

    public static int GetInt(BsonDocument doc, string field, int defaultValue = 0)
    {
        if (!doc.Contains(field) || doc[field].IsBsonNull) return defaultValue;
        return doc[field].IsInt32 ? doc[field].AsInt32 : (int)doc[field].AsInt64;
    }

    public static bool GetBool(BsonDocument doc, string field, bool defaultValue = false)
    {
        return doc.Contains(field) && !doc[field].IsBsonNull ? doc[field].AsBoolean : defaultValue;
    }

    public static string GetId(BsonDocument doc)
    {
        return doc["_id"].AsObjectId.ToString();
    }

    public async Task<bool> UpdateFeaturesAsync(string workspaceId, WorkspaceFeatures features)
    {
        var update = Builders<BsonDocument>.Update
            .Set("features.fullTextOCR.config.ocrEngine", features.OcrEngine)
            .Set("features.barcode.count", features.BarcodeEnabled ? 1 : 0)
            .Set("features.scripts.count", features.ScriptsEnabled ? 1 : 0)
            .Set("quotas.googleOCR.limit", features.GoogleOcrLimit)
            .Set("suspendBackGroundImageProcessing", features.SuspendProcessing)
            .Set("maxImmediatePageProcessingSize", features.MaxImmediateSize)
            .Set("inactivityTimeoutMin", features.InactivityTimeout)
            .Set("modified", DateTime.UtcNow);

        var result = await Workspaces.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)),
            update);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateWorkspaceSettingsAsync(string workspaceId, WorkspaceSettings settings)
    {
        var softDeleteConfig = new BsonDocument
        {
            { "enabled", settings.SoftDeleteEnabled },
            { "retentionDays", settings.SoftDeleteRetentionDays },
            { "autoDeleteEnabled", settings.SoftDeleteAutoDelete },
            { "requireReasonOnDelete", settings.SoftDeleteRequireReason },
            { "notifyOnPurge", settings.SoftDeleteNotifyOnPurge }
        };

        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId));

        // Build update with sets
        var updateBuilder = Builders<BsonDocument>.Update
            .Set("softDeleteConfig", softDeleteConfig)
            .Set("quotas.googleOCR.limit", settings.GoogleOcrQuota)
            .Set("inactivityTimeoutMin", settings.InactivityTimeout)
            .Set("activityRetentionHours", settings.ActivityRetentionHours)
            .Set("maxImmediatePageProcessingSize", settings.MaxImmediateSize)
            .Set("suspendBackGroundImageProcessing", settings.SuspendProcessing)
            .Set("modified", DateTime.UtcNow);

        // Features need to be SET when enabled or UNSET when disabled
        // Because the app checks ContainsKey(), not the count value
        var updates = new List<UpdateDefinition<BsonDocument>> { updateBuilder };

        // Full-Text OCR - special handling: always set config, toggle count
        if (settings.FeatureFullTextOcr)
        {
            updates.Add(Builders<BsonDocument>.Update
                .Set("features.fullTextOCR.count", 1)
                .Set("features.fullTextOCR.config.ocrEngine", settings.OcrEngine));
        }
        else
        {
            updates.Add(Builders<BsonDocument>.Update.Unset("features.fullTextOCR"));
        }

        // Barcode
        if (settings.FeatureBarcode)
            updates.Add(Builders<BsonDocument>.Update.Set("features.barcode.count", 1));
        else
            updates.Add(Builders<BsonDocument>.Update.Unset("features.barcode"));

        // Scripts
        if (settings.FeatureScripts)
            updates.Add(Builders<BsonDocument>.Update.Set("features.scripts.count", 1));
        else
            updates.Add(Builders<BsonDocument>.Update.Unset("features.scripts"));

        // Two-Factor Auth - MUST unset key entirely to disable
        if (settings.FeatureTwoFactor)
            updates.Add(Builders<BsonDocument>.Update.Set("features.twofactorAuth.count", 1));
        else
            updates.Add(Builders<BsonDocument>.Update.Unset("features.twofactorAuth"));

        var combinedUpdate = Builders<BsonDocument>.Update.Combine(updates);
        var result = await Workspaces.UpdateOneAsync(filter, combinedUpdate);

        _logger.LogInformation("Updated workspace settings for {WorkspaceId}: SoftDelete={SoftDelete}, TwoFactor={TwoFactor}, FullTextOCR={FullText}",
            workspaceId, settings.SoftDeleteEnabled, settings.FeatureTwoFactor, settings.FeatureFullTextOcr);

        return result.ModifiedCount > 0;
    }
}

public class WorkspaceFeatures
{
    public string OcrEngine { get; set; } = "tess";
    public bool BarcodeEnabled { get; set; }
    public bool ScriptsEnabled { get; set; }
    public int GoogleOcrLimit { get; set; }
    public bool SuspendProcessing { get; set; }
    public int MaxImmediateSize { get; set; }
    public int InactivityTimeout { get; set; } = 15;
}

public class WorkspaceSettings
{
    // Soft Delete
    public bool SoftDeleteEnabled { get; set; }
    public int SoftDeleteRetentionDays { get; set; } = 30;
    public bool SoftDeleteAutoDelete { get; set; } = true;
    public bool SoftDeleteRequireReason { get; set; }
    public bool SoftDeleteNotifyOnPurge { get; set; }

    // Features
    public bool FeatureFullTextOcr { get; set; }
    public bool FeatureBarcode { get; set; }
    public bool FeatureScripts { get; set; }
    public bool FeatureTwoFactor { get; set; }

    // OCR
    public string OcrEngine { get; set; } = "tess";
    public int GoogleOcrQuota { get; set; }

    // Session & Activity
    public int InactivityTimeout { get; set; } = 15;
    public int ActivityRetentionHours { get; set; } = 24;

    // Processing
    public int MaxImmediateSize { get; set; } = 10;
    public bool SuspendProcessing { get; set; }
}

public class WorkspaceDeletionResult
{
    public string WorkspaceId { get; set; } = "";
    public bool Success { get; set; }
    public bool OpenSearchDeleted { get; set; }
    public int StorageObjectsDeleted { get; set; }
    public bool DatabaseDropped { get; set; }
    public bool WorkspaceDocDeleted { get; set; }
    public string? Error { get; set; }
}
