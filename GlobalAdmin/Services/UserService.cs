using commonInterfaces.dbDataTypes;
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

    public IMongoCollection<User> AllUsers =>
        _globalAuthDb.GetCollection<User>("allusers");

    public IMongoCollection<BsonDocument> AdminUsers =>
        _globalAuthDb.GetCollection<BsonDocument>("revAdminUsers");

    public async Task<List<User>> GetAllUsersAsync(int skip = 0, int take = 100)
    {
        return await AllUsers
            .Find(_ => true)
            .Skip(skip)
            .Limit(take)
            .ToListAsync();
    }

    public async Task<List<User>> SearchUsersAsync(string searchTerm)
    {
        var filter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Regex(u => u.emailaddress, new BsonRegularExpression(searchTerm, "i")),
            Builders<User>.Filter.Regex(u => u.preferredName, new BsonRegularExpression(searchTerm, "i")),
            Builders<User>.Filter.Regex(u => u.UserName, new BsonRegularExpression(searchTerm, "i"))
        );

        return await AllUsers.Find(filter).Limit(100).ToListAsync();
    }

    public async Task<User?> GetUserByIdAsync(string id)
    {
        return await AllUsers.Find(u => u.id == id).FirstOrDefaultAsync();
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await AllUsers.Find(u => u.emailaddress == email).FirstOrDefaultAsync();
    }

    public async Task<bool> DisableUserAsync(string userId)
    {
        var update = Builders<User>.Update
            .Set(u => u.isDisabled, true)
            .Set(u => u.modified, DateTime.UtcNow);

        var result = await AllUsers.UpdateOneAsync(u => u.id == userId, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> EnableUserAsync(string userId)
    {
        var update = Builders<User>.Update
            .Set(u => u.isDisabled, false)
            .Set(u => u.modified, DateTime.UtcNow);

        var result = await AllUsers.UpdateOneAsync(u => u.id == userId, update);
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
}
