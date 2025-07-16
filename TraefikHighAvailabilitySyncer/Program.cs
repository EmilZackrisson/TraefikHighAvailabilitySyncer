using Scalar.AspNetCore;
using TraefikHighAvailabilitySyncer;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Setup logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Configuration.AddEnvironmentVariables();

// If configured as primary, the service will be used to sync the configuration of Traefik in a high availability setup.
if (builder.Configuration.GetValue<bool>("IsPrimary"))
{
    // Register the Traefik primary syncer as a hosted service.
    builder.Services.AddHostedService<TraefikPrimarySyncer>();
}

// Register the Docker client for Traefik operations.
builder.Services.AddSingleton(p =>
{
    var loggerFactory = p.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<TraefikDocker>();
    var dockerUri = p.GetRequiredService<IConfiguration>().GetValue<string>("DockerUri")
                    ?? throw new InvalidOperationException("DockerUri is not configured");
    
    return new TraefikDocker(dockerUri, logger);
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableOpenApi"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.Logger.LogInformation("Scalar API reference is available at /scalar/v1");
}

// Redirect root to /scalar/v1
app.MapGet("/", () => Results.Redirect("/scalar/v1"))
    .WithName("RootRedirect");

app.MapGet("/config/dynamic", (IConfiguration configuration) =>
    {
        // Get the file path to the dynamic configuration file
        var configDirectory = configuration.GetValue<string>("TraefikConfigDirectory")
            ?? throw new InvalidOperationException("TraefikConfigDirectory is not configured");
        var dynamicConfigFilePath = Path.Combine(configDirectory, "dynamic.yml");
        
        if (!File.Exists(dynamicConfigFilePath))
        {
            app.Logger.LogDebug("Did not find dynamic configuration file at {DynamicConfigFilePath}", dynamicConfigFilePath);
            return Results.NotFound("Dynamic configuration file not found.");
        }
        
        // Load the dynamic configuration from the file
        var dynamicConfigContent = File.ReadAllText(dynamicConfigFilePath);
        
        // Return the dynamic configuration content
        return Results.Text(dynamicConfigContent, "application/x-yaml");
    })
    .WithName("GetDynamicConfig");

app.MapGet("/config/static", (IConfiguration configuration) =>
    {
        // Get the file path to the dynamic configuration file
        var configDirectory = configuration.GetValue<string>("TraefikConfigDirectory")
                              ?? throw new InvalidOperationException("TraefikConfigDirectory is not configured");
        var staticConfigFilePath = Path.Combine(configDirectory, "traefik.yml");
        if (!File.Exists(staticConfigFilePath))
        {
            app.Logger.LogDebug("Did not find static configuration file at {StaticConfigFilePath}", staticConfigFilePath);
            return Results.NotFound("Static configuration file not found.");
        }
        
        // Load the dynamic configuration from the file
        var staticConfigContent = File.ReadAllText(staticConfigFilePath);
        
        // Return the dynamic configuration content
        return Results.Text(staticConfigContent, "application/x-yaml");
    })
    .WithName("GetStaticConfig");

app.MapGet("/health", async (TraefikDocker dockerClient) =>
    {
        var containerId = await dockerClient.GetTraefikContainerIdAsync();
        
        if (string.IsNullOrEmpty(containerId))
        {
            app.Logger.LogError("/health: Traefik container not found.");
            return Results.NotFound("Traefik container not found. Please ensure Traefik is running.");
        }
        
        var healthy = await dockerClient.IsTraefikContainerHealthyAsync(containerId);

        return healthy ? Results.Ok("Traefik is healthy.") :
            Results.Problem("Traefik is not healthy.", statusCode: 503);
    })
    .WithName("HealthCheck");

app.MapPost("/update-config", async (IConfiguration configuration, TraefikDocker dockerClient, ILogger<TraefikSecondarySyncer> logger) =>
    {
        // Only run if configured as secondary
        if (configuration.GetValue<bool>("IsPrimary"))
        {
            app.Logger.LogWarning("/update-config: This endpoint is only available on secondary instances.");
            return Results.BadRequest("This endpoint is only available on secondary instances.");
        }
        
        app.Logger.LogInformation("/update-config: Received request to update configuration on secondary instance.");
        var secondarySyncer = new TraefikSecondarySyncer();
        var result = await secondarySyncer.UpdateConfig(dockerClient, configuration, logger);
        
        return result;
    })
    .WithName("UpdateConfig");

await app.RunAsync();