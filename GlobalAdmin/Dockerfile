# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared libraries from rev repo (needed for MongoDbService, etc.)
COPY rev/commonInterfaces/ ./commonInterfaces/
COPY rev/MongoDbService/ ./MongoDbService/
COPY rev/workspaceResolver/ ./workspaceResolver/
COPY rev/BillingUtilities/ ./BillingUtilities/

# Copy GlobalAdmin project
COPY global-admin/GlobalAdmin/ ./GlobalAdmin/

# Restore and build
WORKDIR /src/GlobalAdmin
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
