using Microsoft.AspNetCore.Authentication;
using GlobalAdmin.Components;
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
    });

builder.Services.AddAuthorization();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Auth endpoints
app.MapGet("/api/auth/login", async (string email, string? returnUrl, HttpContext ctx, AuthService authService) =>
{
    // Verify user is admin (already validated password in the form)
    if (!await authService.IsAdminUserAsync(email))
    {
        return Results.Redirect("/login?error=invalid");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Email, email),
        new(ClaimTypes.Name, email),
        new(ClaimTypes.Role, "Admin")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    return Results.Redirect(returnUrl ?? "/");
});

app.MapGet("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Redirect unauthenticated users to login (except for login page and static files)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";
    
    // Allow login page, auth endpoints, agent API, and static files
    if (path.StartsWith("/login") ||
        path.StartsWith("/api/auth") ||
        path.StartsWith("/api/agent") ||
        path.StartsWith("/_") ||
        path.Contains("."))
    {
        await next();
        return;
    }

    // Check if authenticated
    if (!context.User.Identity?.IsAuthenticated ?? true)
    {
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(context.Request.Path)}");
        return;
    }

    await next();
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
