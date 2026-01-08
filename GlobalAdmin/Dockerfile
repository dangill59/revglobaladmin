# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Create directory structure matching csproj relative paths
# GlobalAdmin.csproj uses paths like ../../rev/commonInterfaces/
# So we need:
#   /src/rev/commonInterfaces/
#   /src/rev/MongoDbService/
#   /src/rev/workspaceResolver/
#   /src/rev/BillingUtilities/
#   /src/global-admin/GlobalAdmin/

# Copy shared libraries from rev repo
COPY rev/commonInterfaces/ ./rev/commonInterfaces/
COPY rev/MongoDbService/ ./rev/MongoDbService/
COPY rev/workspaceResolver/ ./rev/workspaceResolver/
COPY rev/BillingUtilities/ ./rev/BillingUtilities/

# Copy GlobalAdmin project
COPY global-admin/GlobalAdmin/ ./global-admin/GlobalAdmin/

# Restore and build
WORKDIR /src/global-admin/GlobalAdmin
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user
RUN useradd -m -u 1000 appuser

COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appuser /app

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "GlobalAdmin.dll"]
