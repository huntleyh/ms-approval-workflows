using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Services;
using Microsoft.PowerPlatform.Dataverse.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Strongly-typed configuration
builder.Services.Configure<AppSettings>(builder.Configuration);

// Data service — SQLite when Dataverse URL is not configured, real Dataverse otherwise
var dataverseUrl = builder.Configuration["Dataverse:Url"] ?? string.Empty;
var useDataverse = !string.IsNullOrWhiteSpace(dataverseUrl)
                   && dataverseUrl != "YOUR_DATAVERSE_URL";

if (useDataverse)
{
    builder.Services.AddSingleton<ServiceClient>(sp =>
    {
        var cfg    = sp.GetRequiredService<IConfiguration>();
        var url    = cfg["Dataverse:Url"];
        var client = cfg["Dataverse:ClientId"];
        var secret = cfg["Dataverse:ClientSecret"];
        return new ServiceClient($"AuthType=ClientSecret;Url={url};ClientId={client};ClientSecret={secret}");
    });
    builder.Services.AddScoped<IDataService, DataverseService>();
}
else
{
    var dbPath   = Path.Combine(builder.Environment.ContentRootPath, "Approvals Demo.db");
    var demoMode = bool.TryParse(builder.Configuration["App:DemoMode"], out var dm) && dm;
    builder.Services.AddSingleton<IDataService>(_ => new SqliteDataService(dbPath, demoMode));
}

// Application services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<WorkflowDefinitionService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<ResetService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Seed demo data on first run (or force re-seed when RESET_DB=true env var is set)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
        var resetDb     = string.Equals(builder.Configuration["RESET_DB"], "true", StringComparison.OrdinalIgnoreCase);

        if (resetDb)
        {
            // Wipe and re-seed: delete the SQLite file so SqliteDataService recreates it
            var dbPath = Path.Combine(app.Environment.ContentRootPath, "Approvals Demo.db");
            if (File.Exists(dbPath)) File.Delete(dbPath);
            // Re-resolve the service so it picks up the fresh DB
            dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var logger  = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("RESET_DB=true — wiped Approvals Demo.db and re-seeding.");
        }

        var existing = await dataService.GetActiveWorkflowDefinitionsAsync();
        if (!existing.Any())
        {
            var settings = builder.Configuration;
            await SeedData.InitializeAsync(dataService, settings);
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Seed data skipped — data service may not be configured yet.");
    }
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapControllers();
app.Run();

