using commonInterfaces.dbDataTypes;
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
        _logger.LogInformation("Created workspace: {Name}", name);

        return workspace;
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

    public async Task<int> GetActiveUserCountAsync(string workspaceName)
    {
        if (string.IsNullOrEmpty(workspaceName)) return 0;

        var workspaceDb = _mongoClient.GetDatabase(workspaceName);
        var usersCollection = workspaceDb.GetCollection<BsonDocument>("users");

        return (int)await usersCollection.CountDocumentsAsync(new BsonDocument());
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
}
