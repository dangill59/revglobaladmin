namespace GlobalAdmin.Models;

/// <summary>
/// Represents an on-premises installation of Rev
/// </summary>
public class OnPremInstall
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    // Connection status
    public DateTime LastHeartbeat { get; set; }
    public string Status { get; set; } = "unknown";  // healthy, warning, offline
    public string Version { get; set; } = string.Empty;

    // Metrics (updated on each heartbeat)
    public InstallMetrics? Metrics { get; set; }

    // License info (controlled by GlobalAdmin)
    public InstallLicense? License { get; set; }

    // Config (pushed from GlobalAdmin to agent)
    public InstallConfig? Config { get; set; }

    // Service status from last heartbeat
    public Dictionary<string, string>? ServiceStatus { get; set; }

    // Recent errors from the installation
    public List<AgentLogEntry>? RecentErrors { get; set; }

    // Backup tracking
    public DateTime? LastBackup { get; set; }
    public string? LastBackupStatus { get; set; }

    // Internal
    public string ApiKey { get; set; } = string.Empty;
    public DateTime Registered { get; set; }
    public bool ConfigChanged { get; set; }
}

/// <summary>
/// Metrics collected from an on-prem installation
/// </summary>
public class InstallMetrics
{
    public int ActiveUsers { get; set; }
    public int TotalUsers { get; set; }
    public long DocumentCount { get; set; }
    public double StorageUsedGB { get; set; }
    public int PagesProcessedThisMonth { get; set; }
    public int OcrPagesThisMonth { get; set; }
    public DateTime CollectedAt { get; set; }
}

/// <summary>
/// License configuration for an on-prem installation
/// </summary>
public class InstallLicense
{
    public int MaxUsers { get; set; } = 10;
    public string Tier { get; set; } = "standard";  // standard, professional, enterprise
    public List<string> EnabledFeatures { get; set; } = new() { "zoneOcr", "automation" };
    public int? GoogleOcrQuota { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Configuration pushed to an on-prem installation
/// </summary>
public class InstallConfig
{
    public string OcrEngine { get; set; } = "tess";  // tess, google, hybrid
    public string StorageType { get; set; } = "minio";  // minio, file
    public bool BackupsEnabled { get; set; } = true;
    public string? BackupEndpoint { get; set; }
    public string? BackupBucket { get; set; }
    public int HeartbeatIntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Log entry from an on-prem agent
/// </summary>
public class AgentLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "error";  // error, warning, info
    public string Service { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Command to be executed by an on-prem agent
/// </summary>
public class AgentCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;  // backup_now, restart_service, update_config
    public Dictionary<string, string> Parameters { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Acknowledged { get; set; }
}

#region API Request/Response Models

/// <summary>
/// Request to register a new on-prem installation
/// </summary>
public class RegisterInstallRequest
{
    public string RegistrationKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Response after successful registration
/// </summary>
public class RegisterInstallResponse
{
    public string InstallId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public InstallConfig? Config { get; set; }
    public InstallLicense? License { get; set; }
}

/// <summary>
/// Heartbeat payload from on-prem agent
/// </summary>
public class AgentHeartbeat
{
    public string InstallId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public InstallMetrics? Metrics { get; set; }
    public List<AgentLogEntry>? RecentErrors { get; set; }
    public Dictionary<string, string>? ServiceStatus { get; set; }
}

/// <summary>
/// Response to agent heartbeat
/// </summary>
public class HeartbeatResponse
{
    public bool ConfigChanged { get; set; }
    public InstallConfig? Config { get; set; }
    public InstallLicense? License { get; set; }
    public List<AgentCommand>? PendingCommands { get; set; }
}

/// <summary>
/// Request to report backup completion
/// </summary>
public class BackupCompleteRequest
{
    public string InstallId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long BackupSizeBytes { get; set; }
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// Registration key for new installations
/// </summary>
public class RegistrationKey
{
    public string Key { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public int MaxUsers { get; set; } = 10;
    public string Tier { get; set; } = "standard";
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool Used { get; set; }
    public string? InstallId { get; set; }
}

#endregion

#region Summary/Dashboard Models

/// <summary>
/// Summary statistics for all on-prem installations
/// </summary>
public class OnPremSummary
{
    public int TotalInstalls { get; set; }
    public int HealthyCount { get; set; }
    public int WarningCount { get; set; }
    public int OfflineCount { get; set; }
    public long TotalDocuments { get; set; }
    public double TotalStorageGB { get; set; }
    public int TotalUsers { get; set; }
    public int InstallsNeedingUpdate { get; set; }
}

#endregion
