using Microsoft.AspNetCore.Authentication;
using GlobalAdmin.Components;
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
    
    // Allow login page, auth endpoints, and static files
    if (path.StartsWith("/login") || 
        path.StartsWith("/api/auth") || 
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
