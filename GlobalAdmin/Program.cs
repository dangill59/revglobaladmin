using GlobalAdmin.Components;
using GlobalAdmin.Services;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// MongoDB configuration
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDB")
    ?? "mongodb://rev:rev@localhost:27017/?authSource=admin";
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));

// Register services
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<UserService>();

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
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
