using Cyberpilot;
using Cyberpilot.GitHub;
using Cyberpilot.Persistence;
using Cyberpilot.Web.Hubs;
using Cyberpilot.Web.Services;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.Configure<CyberpilotWebOptions>(builder.Configuration.GetSection("Cyberpilot"));
builder.Services.AddCyberpilotPersistence(builder.Configuration.GetConnectionString("CyberpilotDb")
    ?? "Data Source=cyberpilot.db");
builder.Services.AddCyberpilotServices();
builder.Services.AddSingleton<ICyberpilotRunQueue, CyberpilotRunQueue>();
builder.Services.AddSingleton<IRepositoryConnectionStore, RepositoryConnectionStore>();
builder.Services.AddScoped<IGitHubIssueClientFactory, GitHubIssueClientFactory>();
builder.Services.AddScoped<IGitCommandRunner, GitCommandRunner>();
builder.Services.AddScoped<ILocalRepositoryValidator, LocalRepositoryValidator>();
builder.Services.AddScoped<IRepositoryProfileDetector, RepositoryProfileDetector>();
builder.Services.AddScoped<IPipelineDefinitionAdminStore, PipelineDefinitionAdminStore>();
builder.Services.AddHttpClient("GitHubApi");
builder.Services.AddScoped<IGitHubIssueClient>(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var cyberpilotOptions = serviceProvider.GetRequiredService<IOptions<CyberpilotWebOptions>>().Value;
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        ?? Environment.GetEnvironmentVariable("GH_TOKEN")
        ?? builder.Configuration["GitHub:Token"]
        ?? string.Empty;

    if (string.IsNullOrWhiteSpace(token))
    {
        return new UnconfiguredGitHubIssueClient("Configure GITHUB_TOKEN, GH_TOKEN, or GitHub:Token before using the Cyberpilot dashboard.");
    }

    return new GitHubApiIssueClient(httpClientFactory.CreateClient("GitHubApi"), cyberpilotOptions.Repository, token);
});
builder.Services.AddHostedService<CyberpilotPipelineService>();
builder.Services.AddHostedService<PrPollerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/Home/Error");
app.UseRouting();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CyberpilotDbContext>();
    await EnsureLegacyEnsureCreatedDatabaseHasMigrationHistoryAsync(dbContext);
    await dbContext.Database.MigrateAsync();
}

app.MapStaticAssets();
app.MapControllers();
app.MapHealthChecks("/health/ready");
app.MapHub<PipelineHub>("/pipelineHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static async Task EnsureLegacyEnsureCreatedDatabaseHasMigrationHistoryAsync(CyberpilotDbContext dbContext)
{
    const string providerName = "Microsoft.EntityFrameworkCore.Sqlite";
    const string initialMigrationId = "20260511152548_InitialCyberpilotSchema";
    const string productVersion = "10.0.0";

    if (!string.Equals(dbContext.Database.ProviderName, providerName, StringComparison.Ordinal))
    {
        return;
    }

    var connection = dbContext.Database.GetDbConnection();
    var shouldClose = connection.State != ConnectionState.Open;
    if (shouldClose)
    {
        await dbContext.Database.OpenConnectionAsync();
    }

    try
    {
        var hasPipelineRuns = await TableExistsAsync(connection, "PipelineRuns");
        var hasMigrationHistory = await TableExistsAsync(connection, "__EFMigrationsHistory");
        if (!hasPipelineRuns || hasMigrationHistory)
        {
            return;
        }

        await ExecuteNonQueryAsync(connection,
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """);

        await ExecuteNonQueryAsync(connection,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion);
            """,
            ("$migrationId", initialMigrationId),
            ("$productVersion", productVersion));
    }
    finally
    {
        if (shouldClose)
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}

static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName";
    var parameter = command.CreateParameter();
    parameter.ParameterName = "$tableName";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);

    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result) > 0;
}

static async Task ExecuteNonQueryAsync(System.Data.Common.DbConnection connection, string commandText, params (string Name, object Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    foreach (var (name, value) in parameters)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    await command.ExecuteNonQueryAsync();
}

/// <summary>
/// Marker partial class used to host the application in integration tests.
/// </summary>
public partial class Program
{
}
