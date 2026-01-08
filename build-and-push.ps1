$ErrorActionPreference = "Stop"

$TAG = "0.0.1"
$REGISTRY = "registry.digitalocean.com/scanrev"
$IMAGE = "global-admin"

Write-Host "Building Docker image..." -ForegroundColor Green

# Build from parent directory to access both repos
Push-Location "C:\myprojects\Rev"

docker build -t "${REGISTRY}/${IMAGE}:${TAG}" -t "${REGISTRY}/${IMAGE}:latest" -f global-admin/GlobalAdmin/Dockerfile .

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location

Write-Host "Pushing to registry..." -ForegroundColor Green
docker push "${REGISTRY}/${IMAGE}:${TAG}"
docker push "${REGISTRY}/${IMAGE}:latest"

Write-Host "Done! Image: ${REGISTRY}/${IMAGE}:${TAG}" -ForegroundColor Green
Write-Host ""
Write-Host "To deploy to staging (admin.test.sonopaper.com):" -ForegroundColor Yellow
Write-Host "  kubectl apply -f GlobalAdmin/k8s/deployment-staging.yaml -n dallas"
Write-Host ""
Write-Host "To deploy to production (admin.scanrev.com):" -ForegroundColor Yellow
Write-Host "  kubectl apply -f GlobalAdmin/k8s/deployment.yaml -n <namespace>"
