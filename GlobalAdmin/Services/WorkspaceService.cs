using MongoDB.Bson;
using MongoDB.Driver;

namespace GlobalAdmin.Services;

public class WorkspaceService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _globalAuthDb;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(IMongoClient mongoClient, ILogger<WorkspaceService> logger)
    {
        _mongoClient = mongoClient;
        _globalAuthDb = mongoClient.GetDatabase("globalAuth");
        _logger = logger;
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

        var update = Builders<BsonDocument>.Update
            // Soft Delete
            .Set("softDeleteConfig", softDeleteConfig)
            // Features
            .Set("features.fullTextOCR.count", settings.FeatureFullTextOcr ? 1 : 0)
            .Set("features.fullTextOCR.config.ocrEngine", settings.OcrEngine)
            .Set("features.barcode.count", settings.FeatureBarcode ? 1 : 0)
            .Set("features.scripts.count", settings.FeatureScripts ? 1 : 0)
            .Set("features.twofactorAuth.count", settings.FeatureTwoFactor ? 1 : 0)
            // Quotas
            .Set("quotas.googleOCR.limit", settings.GoogleOcrQuota)
            // Session & Activity
            .Set("inactivityTimeoutMin", settings.InactivityTimeout)
            .Set("activityRetentionHours", settings.ActivityRetentionHours)
            // Processing
            .Set("maxImmediatePageProcessingSize", settings.MaxImmediateSize)
            .Set("suspendBackGroundImageProcessing", settings.SuspendProcessing)
            .Set("modified", DateTime.UtcNow);

        var result = await Workspaces.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(workspaceId)),
            update);

        _logger.LogInformation("Updated workspace settings for {WorkspaceId}: SoftDelete={SoftDelete}, FullTextOCR={FullText}",
            workspaceId, settings.SoftDeleteEnabled, settings.FeatureFullTextOcr);

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
