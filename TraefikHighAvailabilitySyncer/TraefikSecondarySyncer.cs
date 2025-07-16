namespace TraefikHighAvailabilitySyncer;

public class TraefikSecondarySyncer
{
    public async Task<IResult> UpdateConfig(TraefikDocker dockerClient, IConfiguration configuration, ILogger<TraefikSecondarySyncer> logger)
    {
        // Get endpoint to primary instance
        var primaryHost = configuration.GetValue<string>("PrimaryHost") 
                              ?? throw new InvalidOperationException("PrimaryHost is not configured");
        
        // Check the health of primary instance
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(primaryHost);
        var healthResponse = httpClient.GetAsync("/health").Result;
        if (!healthResponse.IsSuccessStatusCode)
        {
            logger.LogError("/update-config: Primary instance is not healthy. Not updating configuration.");
            return Results.InternalServerError("Primary instance is not healthy. Not updating configuration.");
        }
        
        // Get the dynamic configuration from the primary instance
        var dynamicConfigResponse = httpClient.GetAsync("/config/dynamic").Result;
        if (!dynamicConfigResponse.IsSuccessStatusCode)
        {
            logger.LogError("/update-config: Failed to retrieve dynamic configuration from primary instance.");
            return Results.InternalServerError("Failed to retrieve dynamic configuration from primary instance.");
        }
        
        var dynamicConfigContent = dynamicConfigResponse.Content.ReadAsStringAsync().Result;
        var configDirectory = configuration.GetValue<string>("TraefikConfigDirectory")
                              ?? throw new InvalidOperationException("TraefikConfigDirectory is not configured");
        var dynamicConfigFilePath = Path.Combine(configDirectory, "dynamic.yml");
        await File.WriteAllTextAsync(dynamicConfigFilePath, dynamicConfigContent);
        
        // Optionally, you can also update the static configuration if needed
        var staticConfigResponse = httpClient.GetAsync("/config/static").Result;
        if (!staticConfigResponse.IsSuccessStatusCode)
        {
            logger.LogError("/update-config: Failed to retrieve static configuration from primary instance.");
            return Results.InternalServerError("Failed to retrieve static configuration from primary instance.");
        }
        
        var staticConfigContent = staticConfigResponse.Content.ReadAsStringAsync().Result;
        var staticConfigFilePath = Path.Combine(configDirectory, "traefik.yml");
        await File.WriteAllTextAsync(staticConfigFilePath, staticConfigContent);
        
        // Restart the Traefik container to apply the new configuration
        var containerId = await dockerClient.GetTraefikContainerIdAsync();
        if (string.IsNullOrEmpty(containerId))
        {
            logger.LogError("/update-config: Traefik container not found.");
            return Results.NotFound("Traefik container not found. Please ensure Traefik is running.");
        }
        logger.LogInformation("/update-config: Restarting Traefik container with ID {ContainerId} to apply new configuration.", containerId);
        await dockerClient.RestartTraefikContainerAsync(containerId);
        
        // Wait for the Traefik container to become healthy
        var dockerHealthyWaitTime = TimeSpan.FromSeconds(configuration.GetValue("TraefikConfigWaitTime", 60));
        try
        {
            logger.LogInformation("Waiting for Traefik container to become healthy after restart.");
            var startTime = DateTime.UtcNow;
            dockerClient.WaitForTraefikContainerToBeHealthyAsync(containerId, dockerHealthyWaitTime).Wait();
            var elapsedTime = DateTime.UtcNow - startTime;
            logger.LogInformation("Traefik container became healthy after {ElapsedTime} seconds.", elapsedTime.TotalSeconds);
        }
        catch (TimeoutException e)
        {
            logger.LogError(e, "/update-config: Failed to restart traefik container. Timeout.");
            return Results.Problem("Traefik container did not become healthy after configuration update.", statusCode: 503);
        }
        logger.LogInformation("/update-config: Traefik container restarted and is healthy.");
        return Results.Ok();
    }
    
    
}