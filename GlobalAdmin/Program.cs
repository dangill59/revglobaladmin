using Microsoft.AspNetCore.Authentication;
using GlobalAdmin.Models;
using GlobalAdmin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using MongoDB.Driver;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// MongoDB configuration
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDB")
    ?? "mongodb://rev:rev@localhost:27017/?authSource=admin";
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));

// Register services
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OnPremService>();
builder.Services.AddScoped<StorageCleanupService>();
builder.Services.AddScoped<OpenSearchService>();
builder.Services.AddScoped<EmailService>();

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/api/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "GlobalAdmin.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Events.OnRedirectToLogin = context =>
        {
            // Return 401 for API requests instead of redirect
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Add MVC controllers
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve static files from wwwroot (React build output)
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Map API controllers
app.MapControllers();

// Auth endpoints (minimal API for login/logout)
app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext ctx, AuthService authService) =>
{
    var valid = await authService.ValidateCredentialsAsync(request.Email, request.Password);
    if (!valid)
    {
        return Results.Unauthorized();
    }

    if (!await authService.IsAdminUserAsync(request.Email))
    {
        return Results.Json(new { error = "User is not an admin" }, statusCode: 403);
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Email, request.Email),
        new(ClaimTypes.Name, request.Email),
        new(ClaimTypes.Role, "Admin")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    return Results.Ok(new { email = request.Email });
});

app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value;
    return Results.Ok(new { email });
});

// Setup endpoint - check/create admin users (only works if no admins exist)
app.MapGet("/api/setup/check", async (AuthService authService, IMongoClient mongoClient) =>
{
    var db = mongoClient.GetDatabase("globalAuth");
    var collection = db.GetCollection<MongoDB.Bson.BsonDocument>("revAdminUsers");
    var count = await collection.CountDocumentsAsync(MongoDB.Bson.BsonDocument.Parse("{}"));
    var users = await collection.Find(MongoDB.Bson.BsonDocument.Parse("{}"))
        .Project(MongoDB.Bson.BsonDocument.Parse("{_id: 1}"))
        .ToListAsync();

    return Results.Ok(new {
        adminCount = count,
        emails = users.Select(u => u["_id"].AsString).ToList()
    });
});

app.MapPost("/api/setup/create-admin", async (AdminCreateRequest request, AuthService authService, IMongoClient mongoClient) =>
{
    // Only allow if no admin users exist (first-time setup)
    var db = mongoClient.GetDatabase("globalAuth");
    var collection = db.GetCollection<MongoDB.Bson.BsonDocument>("revAdminUsers");
    var count = await collection.CountDocumentsAsync(MongoDB.Bson.BsonDocument.Parse("{}"));

    if (count > 0)
    {
        return Results.BadRequest(new { error = "Admin users already exist. Use the UI to manage users." });
    }

    var success = await authService.CreateAdminUserAsync(request.Email, request.Password);
    if (success)
    {
        return Results.Ok(new { message = $"Admin user {request.Email} created successfully" });
    }
    return Results.BadRequest(new { error = "Failed to create admin user" });
});

// Add new admin (requires existing admin auth or setup token)
app.MapPost("/api/setup/add-admin", async (AdminCreateRequest request, AuthService authService, HttpContext ctx) =>
{
    // Allow if authenticated as admin OR using setup token
    var setupToken = ctx.Request.Headers["X-Setup-Token"].FirstOrDefault();
    var isAuth = ctx.User.Identity?.IsAuthenticated ?? false;

    // Temp setup token for initial configuration (remove after setup)
    if (setupToken != "scanrev-setup-2024" && !isAuth)
    {
        return Results.Unauthorized();
    }

    var success = await authService.CreateAdminUserAsync(request.Email, request.Password);
    if (success)
    {
        return Results.Ok(new { message = $"Admin user {request.Email} created successfully" });
    }
    return Results.BadRequest(new { error = "User already exists or creation failed" });
});

// Reset admin password
app.MapPost("/api/setup/reset-password", async (AdminResetRequest request, AuthService authService, HttpContext ctx) =>
{
    var setupToken = ctx.Request.Headers["X-Setup-Token"].FirstOrDefault();
    var isAuth = ctx.User.Identity?.IsAuthenticated ?? false;

    if (setupToken != "scanrev-setup-2024" && !isAuth)
    {
        return Results.Unauthorized();
    }

    var success = await authService.UpdatePasswordAsync(request.Email, request.NewPassword);
    if (success)
    {
        return Results.Ok(new { message = $"Password reset for {request.Email}" });
    }
    return Results.BadRequest(new { error = "User not found or update failed" });
});

// On-Prem Agent API endpoints
app.MapPost("/api/agent/register", async (RegisterInstallRequest request, OnPremService onPremService, ILogger<Program> logger) =>
{
    try
    {
        var install = await onPremService.RegisterInstallAsync(request);
        logger.LogInformation("Registered new on-prem install: {Customer}", request.CustomerName);

        return Results.Ok(new RegisterInstallResponse
        {
            InstallId = install.Id,
            ApiKey = install.ApiKey,
            Config = install.Config,
            License = install.License
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to register on-prem install");
        return Results.Problem("Registration failed");
    }
});

app.MapPost("/api/agent/heartbeat", async (AgentHeartbeat heartbeat, OnPremService onPremService, ILogger<Program> logger) =>
{
    try
    {
        // Validate API key
        if (!await onPremService.ValidateApiKeyAsync(heartbeat.InstallId, heartbeat.ApiKey))
        {
            return Results.Unauthorized();
        }

        // Process heartbeat
        await onPremService.ProcessHeartbeatAsync(heartbeat);

        // Get response with any pending config/commands
        var response = await onPremService.GetHeartbeatResponseAsync(heartbeat.InstallId);

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process heartbeat from {InstallId}", heartbeat.InstallId);
        return Results.Problem("Heartbeat processing failed");
    }
});

app.MapGet("/api/agent/config/{installId}", async (string installId, HttpContext ctx, OnPremService onPremService) =>
{
    var apiKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(apiKey) || !await onPremService.ValidateApiKeyAsync(installId, apiKey))
    {
        return Results.Unauthorized();
    }

    var install = await onPremService.GetInstallAsync(installId);
    if (install == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        config = install.Config,
        license = install.License
    });
});

app.MapPost("/api/agent/backup-complete", async (BackupCompleteRequest request, OnPremService onPremService, ILogger<Program> logger) =>
{
    try
    {
        if (!await onPremService.ValidateApiKeyAsync(request.InstallId, request.ApiKey))
        {
            return Results.Unauthorized();
        }

        await onPremService.RecordBackupAsync(request);

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to record backup completion for {InstallId}", request.InstallId);
        return Results.Problem("Failed to record backup");
    }
});

app.MapPost("/api/agent/command/{commandId}/ack", async (string commandId, HttpContext ctx, OnPremService onPremService) =>
{
    var installId = ctx.Request.Headers["X-Install-Id"].FirstOrDefault();
    var apiKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();

    if (string.IsNullOrEmpty(installId) || string.IsNullOrEmpty(apiKey) ||
        !await onPremService.ValidateApiKeyAsync(installId, apiKey))
    {
        return Results.Unauthorized();
    }

    await onPremService.AcknowledgeCommandAsync(commandId);
    return Results.Ok();
});

// SPA fallback - serve index.html for all non-API routes
app.MapFallbackToFile("index.html");

app.Run();

// Request models
public record LoginRequest(string Email, string Password);
public record AdminCreateRequest(string Email, string Password);
public record AdminResetRequest(string Email, string NewPassword);
