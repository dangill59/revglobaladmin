using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace GlobalAdmin.Services;

public class RabbitMQService
{
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMQService> _logger;

    public RabbitMQService(IConfiguration config, ILogger<RabbitMQService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> TriggerReindexAsync(string workspaceId)
    {
        var hostname = _config["rabbitMQ:hostname"] ?? "rabbitmq";
        var username = _config["rabbitMQ:user"] ?? "rev";
        var password = _config["rabbitMQ:pass"] ?? "rev";
        var port = int.TryParse(_config["rabbitMQ:port"], out var p) ? p : 5672;

        var vhost = $"rev_{workspaceId}";
        var queueName = "revElasticSearch.ESAdminMessage";

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = hostname,
                Port = port,
                UserName = username,
                Password = password,
                VirtualHost = vhost
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            // Declare the queue (idempotent)
            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Create ESAdminMessage-compatible payload
            var message = new
            {
                cmd = 0, // DocUpdatedType.requeue = 0
                workspaceId = workspaceId,
                executionContext = new
                {
                    parentId = (string?)null,
                    jobId = Guid.NewGuid().ToString()
                }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";

            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("Published reindex message to workspace {WorkspaceId}", workspaceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish reindex message to workspace {WorkspaceId}", workspaceId);
            return false;
        }
    }
}
