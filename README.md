# GlobalAdmin Portal

A Blazor Server application for managing ScanRev workspaces, licenses, and configurations across all tenants.

## Features

- **Workspace Management**: View, create, edit, and disable workspaces
- **License Management**: Adjust user license counts per workspace
- **Feature Configuration**: Configure OCR engine, barcode detection, automation scripts
- **User Management**: View workspace users, manage admin access to GlobalAdmin
- **Analytics**: View document counts, database sizes, active users

## Authentication

GlobalAdmin uses the `globalAuth.revAdminUsers` collection for authentication. Users must have an entry in this collection to access the portal.

**Default credentials (development):** `developer@scanrev.com` / `admin`

### Adding Admin Users

Admin users are stored in MongoDB with MD5 password hashes:

```javascript
// Connect to MongoDB
db.revAdminUsers.insertOne({
  _id: "admin@example.com",
  passDigest: "21232F297A57A5A743894A0E4A801FC3"  // MD5 of "admin"
})
```

Or use the Users page in GlobalAdmin to add admin users (after logging in).

## Local Development

### Prerequisites

- .NET 8 SDK
- Docker (for MongoDB)
- Access to the `rev` repository (shared libraries)

### Setup

1. Start local MongoDB:
   ```bash
   cd ../rev/development
   docker-compose up -d
   ```

2. Run the application:
   ```bash
   cd GlobalAdmin
   dotnet run
   ```

3. Open http://localhost:5229/login

### Configuration

Local settings in `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://rev:rev@localhost:27017/?authSource=admin"
  }
}
```

## Production Deployment

### Prerequisites

- Docker
- Access to DigitalOcean Container Registry
- kubectl configured for your cluster

### Build and Push Docker Image

```powershell
# Login to DigitalOcean registry
doctl registry login

# Build and push (from GlobalAdmin directory)
.\build-and-push.ps1 -Tag "1.0.0"
```

### Deploy to Kubernetes

The deployment reads the MongoDB connection string from the existing `rev-config` ConfigMap used by other ScanRev services.

```bash
# Deploy to any namespace with rev-config ConfigMap
kubectl apply -f k8s/deployment.yaml -n dallas

# Verify deployment
kubectl get pods -n dallas -l app=global-admin
kubectl logs -n dallas -l app=global-admin
```

### Kubernetes Resources

The `k8s/deployment.yaml` creates:

| Resource | Name | Description |
|----------|------|-------------|
| Deployment | global-admin | Single replica, 128-256Mi memory |
| Service | global-admin | ClusterIP on port 80 |
| Ingress | global-admin | TLS at admin.scanrev.com |

### Environment Variables

| Variable | Description | Source |
|----------|-------------|--------|
| `ConnectionStrings__MongoDB` | MongoDB connection string | ConfigMap `rev-config` |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | Set to `Production` |
| `ASPNETCORE_URLS` | Listen URL | `http://+:8080` |

## Architecture

```
GlobalAdmin
├── Components/
│   ├── Layout/           # NavMenu, MainLayout
│   └── Pages/
│       ├── Home.razor           # Dashboard
│       ├── Login.razor          # Authentication
│       ├── Workspaces.razor     # Workspace list + create/edit
│       ├── WorkspaceDetails.razor  # Stats, features, users
│       └── Users.razor          # Admin user management
├── Services/
│   ├── AuthService.cs      # Login/logout, password validation
│   ├── WorkspaceService.cs # CRUD, provisioning, features
│   ├── AnalyticsService.cs # Stats, document counts
│   └── UserService.cs      # Admin user management
└── k8s/
    └── deployment.yaml     # K8s manifests
```

## Database Collections

GlobalAdmin accesses these MongoDB collections:

| Database | Collection | Purpose |
|----------|------------|---------|
| globalAuth | workspaces | Workspace configuration |
| globalAuth | allusers | User accounts |
| globalAuth | revAdminUsers | GlobalAdmin access |
| rev_{workspaceId} | workspaceUsers | Workspace members |
| rev_{workspaceId} | pageholders | Documents |

**Note:** Workspace databases use the format `rev_{workspaceId}` (e.g., `rev_67c0697b3d9674ae8671d447`), not the workspace name.

## Workspace Features

The following features can be configured per workspace:

| Feature | Field | Options |
|---------|-------|---------|
| OCR Engine | `features.fullTextOCR.config.ocrEngine` | `tess` (Tesseract), `google` (Google Vision) |
| Barcode Detection | `features.barcode.count` | 0 (disabled), 1 (enabled) |
| Automation Scripts | `features.scripts.count` | 0 (disabled), 1 (enabled) |
| Google OCR Quota | `quotas.googleOCR.limit` | Number of pages |
| Background Processing | `suspendBackGroundImageProcessing` | true/false |
| Immediate Processing Size | `maxImmediatePageProcessingSize` | Number of pages |
| Session Timeout | `inactivityTimeoutMin` | Minutes |

## Troubleshooting

### Cannot connect to MongoDB

- **Local**: Ensure Docker containers are running (`docker ps`)
- **Production**: Verify the `rev-config` ConfigMap has correct connection string

### Login fails

- Check that user exists in `revAdminUsers` collection
- Verify password hash is correct MD5 (uppercase hex)

### Workspace shows 0 documents

- Verify the workspace database exists: `rev_{workspaceId}`
- Check collection name is `pageholders` (lowercase)

## Security Notes

- All pages require authentication except `/login`
- Cookies are HttpOnly and SameSite=Strict
- Session expires after 8 hours of inactivity
- Passwords are stored as MD5 hashes (legacy compatibility)

## License

Proprietary - ScanRev
