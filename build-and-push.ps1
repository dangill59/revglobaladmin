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
Write-Host "To deploy to staging:" -ForegroundColor Yellow
Write-Host "  kubectl --kubeconfig=`"C:\Users\dan\Downloads\staging-kubeconfig.yaml`" apply -f k8s/deployment.yml -n dallas"
