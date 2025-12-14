using MongoDB.Bson;
using MongoDB.Driver;

namespace GlobalAdmin.Services;

public class UserService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _globalAuthDb;
    private readonly ILogger<UserService> _logger;

    public UserService(IMongoClient mongoClient, ILogger<UserService> logger)
    {
        _mongoClient = mongoClient;
        _globalAuthDb = mongoClient.GetDatabase("globalAuth");
        _logger = logger;
    }

    public IMongoCollection<BsonDocument> AllUsers =>
        _globalAuthDb.GetCollection<BsonDocument>("allusers");

    public IMongoCollection<BsonDocument> AdminUsers =>
        _globalAuthDb.GetCollection<BsonDocument>("revAdminUsers");

    public async Task<List<BsonDocument>> GetAllUsersAsync(int skip = 0, int take = 100)
    {
        return await AllUsers
            .Find(_ => true)
            .Skip(skip)
            .Limit(take)
            .ToListAsync();
    }

    public async Task<List<BsonDocument>> SearchUsersAsync(string searchTerm)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Regex("emailaddress", new BsonRegularExpression(searchTerm, "i")),
            Builders<BsonDocument>.Filter.Regex("preferredName", new BsonRegularExpression(searchTerm, "i")),
            Builders<BsonDocument>.Filter.Regex("UserName", new BsonRegularExpression(searchTerm, "i"))
        );

        return await AllUsers.Find(filter).Limit(100).ToListAsync();
    }

    public async Task<bool> DisableUserAsync(string userId)
    {
        var update = Builders<BsonDocument>.Update
            .Set("isDisabled", true)
            .Set("modified", DateTime.UtcNow);

        var result = await AllUsers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(userId)),
            update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EnableUserAsync(string userId)
    {
        var update = Builders<BsonDocument>.Update
            .Set("isDisabled", false)
            .Set("modified", DateTime.UtcNow);

        var result = await AllUsers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(userId)),
            update);
        return result.ModifiedCount > 0;
    }

    public async Task<long> GetTotalUserCountAsync()
    {
        return await AllUsers.CountDocumentsAsync(_ => true);
    }

    // Admin users management
    public async Task<List<string>> GetAdminUsersAsync()
    {
        var admins = await AdminUsers.Find(_ => true).ToListAsync();
        return admins.Select(a => a.GetValue("email", "").AsString).ToList();
    }

    public async Task<bool> AddAdminUserAsync(string email)
    {
        var existing = await AdminUsers.Find(
            Builders<BsonDocument>.Filter.Eq("email", email)).FirstOrDefaultAsync();

        if (existing != null) return false;

        await AdminUsers.InsertOneAsync(new BsonDocument
        {
            { "email", email },
            { "created", DateTime.UtcNow }
        });

        _logger.LogInformation("Added admin user: {Email}", email);
        return true;
    }

    public async Task<bool> RemoveAdminUserAsync(string email)
    {
        var result = await AdminUsers.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("email", email));

        if (result.DeletedCount > 0)
        {
            _logger.LogInformation("Removed admin user: {Email}", email);
        }

        return result.DeletedCount > 0;
    }

    public async Task<bool> IsAdminAsync(string email)
    {
        var admin = await AdminUsers.Find(
            Builders<BsonDocument>.Filter.Eq("email", email)).FirstOrDefaultAsync();

        return admin != null;
    }

    // Helper methods
    public static string GetId(BsonDocument doc)
    {
        return doc["_id"].AsObjectId.ToString();
    }

    public static string GetString(BsonDocument doc, string field, string defaultValue = "")
    {
        return doc.Contains(field) && !doc[field].IsBsonNull ? doc[field].AsString : defaultValue;
    }

    public static bool GetBool(BsonDocument doc, string field, bool defaultValue = false)
    {
        return doc.Contains(field) && !doc[field].IsBsonNull ? doc[field].AsBoolean : defaultValue;
    }

    public static DateTime? GetDateTime(BsonDocument doc, string field)
    {
        if (!doc.Contains(field) || doc[field].IsBsonNull) return null;
        return doc[field].ToUniversalTime();
    }
}
