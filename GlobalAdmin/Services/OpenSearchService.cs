using OpenSearch.Client;

namespace GlobalAdmin.Services;

public class OpenSearchService
{
    private readonly IConfiguration _config;
    private readonly ILogger<OpenSearchService> _logger;

    public OpenSearchService(IConfiguration config, ILogger<OpenSearchService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private OpenSearchClient? CreateClient()
    {
        var url = _config["OpenSearch:Url"];
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning("OpenSearch URL not configured");
            return null;
        }

        var uri = new Uri(url);

        // Build base URL without credentials
        var baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        var settings = new ConnectionSettings(new Uri(baseUrl))
            .DefaultIndex("default");

        // Extract credentials from URL (format: https://user:pass@host:port)
        // Or use explicit config values if provided
        var username = _config["OpenSearch:Username"];
        var password = _config["OpenSearch:Password"];

        if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':');
            username = userInfo[0];
            password = userInfo.Length > 1 ? userInfo[1] : null;
        }

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            settings = settings.BasicAuthentication(username, password);
            _logger.LogInformation("OpenSearch configured with authentication for {Host}", uri.Host);
        }

        // Disable certificate validation for development (if configured)
        if (_config.GetValue<bool>("OpenSearch:DisableCertificateValidation"))
        {
            settings = settings.ServerCertificateValidationCallback((o, cert, chain, errors) => true);
        }

        return new OpenSearchClient(settings);
    }

    public async Task<bool> DeleteWorkspaceIndexesAsync(string workspaceId)
    {
        var client = CreateClient();
        if (client == null)
        {
            _logger.LogWarning("OpenSearch not configured, skipping index deletion for workspace {WorkspaceId}", workspaceId);
            return false;
        }

        var mainIndex = $"rev_{workspaceId}";
        var ocrIndex = $"rev_{workspaceId}_ocr";

        var success = true;

        // Delete main index
        success &= await DeleteIndexAsync(client, mainIndex);

        // Delete OCR index
        success &= await DeleteIndexAsync(client, ocrIndex);

        return success;
    }

    private async Task<bool> DeleteIndexAsync(OpenSearchClient client, string indexName)
    {
        try
        {
            var existsResponse = await client.Indices.ExistsAsync(indexName);
            if (!existsResponse.Exists)
            {
                _logger.LogInformation("Index {IndexName} does not exist, skipping deletion", indexName);
                return true;
            }

            var response = await client.Indices.DeleteAsync(indexName);
            if (response.IsValid)
            {
                _logger.LogInformation("Successfully deleted index {IndexName}", indexName);
                return true;
            }
            else
            {
                _logger.LogError("Failed to delete index {IndexName}: {Error}", indexName, response.ServerError?.Error?.Reason);
                return false;
            }
        }
        catch (Exception ex)
        {
            // Handle index_not_found_exception gracefully
            if (ex.Message.Contains("index_not_found_exception"))
            {
                _logger.LogInformation("Index {IndexName} not found, considered deleted", indexName);
                return true;
            }

            _logger.LogError(ex, "Error deleting index {IndexName}", indexName);
            return false;
        }
    }
}
