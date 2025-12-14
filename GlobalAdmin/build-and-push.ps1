# Build and push GlobalAdmin Docker image
param(
    [string]$Tag = "latest",
    [string]$Registry = "registry.digitalocean.com/scanrev"
)

$ErrorActionPreference = "Stop"

Write-Host "Building GlobalAdmin Docker image..." -ForegroundColor Cyan

# Build from parent directory to access both repos
Push-Location "$PSScriptRoot/../.."

try {
    # Build the image
    docker build -f global-admin/GlobalAdmin/Dockerfile -t "${Registry}/global-admin:${Tag}" .
    
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed"
    }

    Write-Host "Pushing to registry..." -ForegroundColor Cyan
    docker push "${Registry}/global-admin:${Tag}"
    
    if ($LASTEXITCODE -ne 0) {
        throw "Docker push failed"
    }

    Write-Host "Successfully built and pushed ${Registry}/global-admin:${Tag}" -ForegroundColor Green
}
finally {
    Pop-Location
}
