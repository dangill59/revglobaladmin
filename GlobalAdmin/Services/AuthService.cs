using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;

namespace GlobalAdmin.Services;

public class AuthService
{
    private readonly IMongoCollection<BsonDocument> _adminUsers;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IMongoClient mongoClient, ILogger<AuthService> logger)
    {
        var db = mongoClient.GetDatabase("globalAuth");
        _adminUsers = db.GetCollection<BsonDocument>("revAdminUsers");
        _logger = logger;
    }

    public async Task<bool> ValidateCredentialsAsync(string email, string password)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            return false;

        var user = await _adminUsers.Find(
            Builders<BsonDocument>.Filter.Eq("_id", email.ToLower())
        ).FirstOrDefaultAsync();

        if (user == null)
        {
            _logger.LogWarning("Login attempt for non-existent admin user: {Email}", email);
            return false;
        }

        var passDigest = user.GetValue("passDigest", "").AsString;
        var inputHash = ComputeMD5Hash(password);

        if (string.Equals(passDigest, inputHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Successful login for admin user: {Email}", email);
            return true;
        }

        _logger.LogWarning("Invalid password for admin user: {Email}", email);
        return false;
    }

    public async Task<bool> IsAdminUserAsync(string email)
    {
        if (string.IsNullOrEmpty(email))
            return false;

        var user = await _adminUsers.Find(
            Builders<BsonDocument>.Filter.Eq("_id", email.ToLower())
        ).FirstOrDefaultAsync();

        return user != null;
    }

    public async Task<bool> CreateAdminUserAsync(string email, string password)
    {
        var existing = await _adminUsers.Find(
            Builders<BsonDocument>.Filter.Eq("_id", email.ToLower())
        ).FirstOrDefaultAsync();

        if (existing != null)
            return false;

        var doc = new BsonDocument
        {
            { "_id", email.ToLower() },
            { "passDigest", ComputeMD5Hash(password) }
        };

        await _adminUsers.InsertOneAsync(doc);
        _logger.LogInformation("Created admin user: {Email}", email);
        return true;
    }

    public async Task<bool> UpdatePasswordAsync(string email, string newPassword)
    {
        var result = await _adminUsers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", email.ToLower()),
            Builders<BsonDocument>.Update.Set("passDigest", ComputeMD5Hash(newPassword))
        );

        return result.ModifiedCount > 0;
    }

    private static string ComputeMD5Hash(string input)
    {
        using var md5 = MD5.Create();
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
