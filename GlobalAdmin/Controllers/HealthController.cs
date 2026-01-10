using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Net.Http;

namespace GlobalAdmin.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HealthController : ControllerBase
{
    private readonly IMongoClient _mongoClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IMongoClient mongoClient,
        IConfiguration configuration,
        ILogger<HealthController> logger)
    {
        _mongoClient = mongoClient;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealth()
    {
        var services = new List<ServiceHealth>();

        // Check MongoDB
        services.Add(await CheckMongoDB());

        // Check Redis
        services.Add(await CheckRedis());

        // Check RabbitMQ
        services.Add(await CheckRabbitMQ());

        // Check OpenSearch
        services.Add(await CheckOpenSearch());

        // Check external endpoints
        services.Add(await CheckEndpoint("Main App", _configuration["HealthCheck:MainAppUrl"] ?? "https://sod.sonopaper.com"));
        services.Add(await CheckEndpoint("Admin Panel", _configuration["HealthCheck:AdminUrl"] ?? "https://admin.test.sonopaper.com/login"));

        var overallStatus = services.All(s => s.Status == "healthy") ? "healthy" :
                           services.Any(s => s.Status == "unhealthy") ? "unhealthy" : "degraded";

        return Ok(new
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow,
            services
        });
    }

    private async Task<ServiceHealth> CheckMongoDB()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _mongoClient.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            sw.Stop();
            return new ServiceHealth("Database", "MongoDB", "healthy", $"{sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MongoDB health check failed");
            return new ServiceHealth("Database", "MongoDB", "unhealthy", ex.Message);
        }
    }

    private async Task<ServiceHealth> CheckRedis()
    {
        try
        {
            // redis:Configuration from configmap (just hostname like "redis")
            var redisHost = _configuration["redis:Configuration"] ?? "redis";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var redis = await ConnectionMultiplexer.ConnectAsync(redisHost + ",connectTimeout=5000,abortConnect=false");
            var db = redis.GetDatabase();
            await db.PingAsync();
            sw.Stop();
            return new ServiceHealth("Cache", "Redis", "healthy", $"{sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return new ServiceHealth("Cache", "Redis", "unhealthy", ex.Message);
        }
    }

    private async Task<ServiceHealth> CheckRabbitMQ()
    {
        try
        {
            var host = _configuration["rabbitMQ:hostname"] ?? "rabbitmq";
            var adminPort = _configuration["rabbitMQ:adminPort"] ?? "15672";
            var user = _configuration["rabbitMQ:user"] ?? "revRabbit";
            var pass = _configuration["rabbitMQ:pass"] ?? "";

            // If no password configured, just check if port is reachable
            if (string.IsNullOrEmpty(pass))
            {
                using var tcpClient = new System.Net.Sockets.TcpClient();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await tcpClient.ConnectAsync(host, int.Parse(adminPort));
                sw.Stop();
                return new ServiceHealth("Message Queue", "RabbitMQ", "healthy", $"{sw.ElapsedMilliseconds}ms (TCP)");
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{user}:{pass}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.GetAsync($"http://{host}:{adminPort}/api/overview");
            sw2.Stop();

            if (response.IsSuccessStatusCode)
                return new ServiceHealth("Message Queue", "RabbitMQ", "healthy", $"{sw2.ElapsedMilliseconds}ms");
            else
                return new ServiceHealth("Message Queue", "RabbitMQ", "degraded", $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ health check failed");
            return new ServiceHealth("Message Queue", "RabbitMQ", "unhealthy", ex.Message);
        }
    }

    private async Task<ServiceHealth> CheckOpenSearch()
    {
        try
        {
            // OpenSearch URL comes from config (may include auth in URL for managed services)
            var openSearchUrl = _configuration["OpenSearch:Url"];

            if (string.IsNullOrEmpty(openSearchUrl))
            {
                // Fallback to legacy config
                var host = _configuration["elasticsearch:host"] ?? "opensearch";
                var port = _configuration["elasticsearch:port"] ?? "9200";
                openSearchUrl = $"http://{host}:{port}";
            }

            // Parse URL to extract credentials if present (e.g., https://user:pass@host:port)
            var uri = new Uri(openSearchUrl);
            string? authHeader = null;
            var requestUrl = openSearchUrl;

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                // Extract credentials and build auth header
                authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(uri.UserInfo));
                // Rebuild URL without credentials
                requestUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.PathAndQuery}";
            }

            // Create handler that can skip cert validation for managed services
            var handler = new HttpClientHandler();
            if (_configuration.GetValue<bool>("OpenSearch:DisableCertificateValidation"))
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            if (authHeader != null)
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var baseUrl = requestUrl.TrimEnd('/');
            var response = await client.GetAsync($"{baseUrl}/_cluster/health");
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var clusterStatus = content.Contains("\"status\":\"green\"") ? "healthy" :
                                   content.Contains("\"status\":\"yellow\"") ? "degraded" : "unhealthy";
                return new ServiceHealth("Search", "OpenSearch", clusterStatus, $"{sw.ElapsedMilliseconds}ms");
            }
            return new ServiceHealth("Search", "OpenSearch", "unhealthy", $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSearch health check failed");
            return new ServiceHealth("Search", "OpenSearch", "unhealthy", ex.Message);
        }
    }

    private async Task<ServiceHealth> CheckEndpoint(string name, string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.GetAsync(url);
            sw.Stop();

            if (response.IsSuccessStatusCode)
                return new ServiceHealth("Website", name, "healthy", $"{sw.ElapsedMilliseconds}ms");
            else
                return new ServiceHealth("Website", name, "degraded", $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Endpoint health check failed for {Url}", url);
            return new ServiceHealth("Website", name, "unhealthy", "Unreachable");
        }
    }
}

public record ServiceHealth(string Category, string Name, string Status, string Details);
