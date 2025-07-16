namespace TraefikHighAvailabilitySyncer;

public class TraefikPrimarySyncer(ILogger<TraefikPrimarySyncer> logger, IConfiguration configuration, TraefikDocker dockerClient) : IHostedService 
{
    private FileSystemWatcher? _watcher;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("TraefikPrimarySyncer started");
        
        // Initialize the file system watcher to monitor changes in the Traefik configuration directory.
        _watcher = new FileSystemWatcher(configuration.GetValue<string>("TraefikConfigDirectory") ?? throw new InvalidOperationException("TraefikConfigDirectory is not configured"));
        
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        
        _watcher.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping TraefikPrimarySyncer");
        if (_watcher == null)
        {
            logger.LogWarning("FileSystemWatcher is not initialized. Cannot stop.");
            return Task.CompletedTask;
        }
        
        _watcher.Dispose();
        logger.LogInformation("TraefikPrimarySyncer stopped");
        return Task.CompletedTask;
    }
    
    private async void OnChanged(object source, FileSystemEventArgs e)
    {
        if (e.Name == null) return;
        if (!e.Name.EndsWith(".yaml") && !e.Name.EndsWith(".yml"))
        {
            return; // Only process YAML files
        }
        
        logger.LogInformation("Configuration file changed: {FileName}", e.Name);
        
        // Restart the Traefik container to apply the new configuration and wait for it to become healthy.
        var healthyAfterRestart = await ApplyConfigurationAndCheckHealthAsync();
        
        if (!healthyAfterRestart)
        {
            logger.LogCritical("Traefik container did not become healthy after configuration change. Not updating secondary instances.");
            return; // Exit if Traefik is not healthy
        }
        
        var secondaryInstances = configuration.GetSection("SecondaryInstances").Get<string[]>() 
                             ?? throw new InvalidOperationException("SecondaryInstances is not configured");
        
        using var httpClient = new HttpClient();
        
        // Tell each secondary instance to update its configuration
        foreach (var instance in secondaryInstances)
        {
            try
            {
                logger.LogInformation("Sending configuration update request to {Instance}", instance);
                httpClient.BaseAddress = new Uri(instance);
                
                var response = httpClient.PostAsync("/update-config", null).Result;
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Configuration update request sent to {Instance}", instance);
                }
                else
                {
                    logger.LogError("Failed to send configuration update request to {Instance}: {ReasonPhrase}", instance, response.ReasonPhrase);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending configuration update request to {Instance}", instance);
            }
        }
    }
    
    private async Task<bool> ApplyConfigurationAndCheckHealthAsync()
    {
        var containerId = await dockerClient.GetTraefikContainerIdAsync();
        if (string.IsNullOrEmpty(containerId))
        {
            throw new InvalidOperationException("Traefik container not found");
        }
        
        await dockerClient.RestartTraefikContainerAsync(containerId);
        
        // Wait for the Traefik container to become healthy
        var waitTime = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < waitTime)
        {
            if (await dockerClient.IsTraefikContainerHealthyAsync(containerId))
            {
                logger.LogInformation("Traefik container is healthy after restart.");
                return true; // Traefik is healthy
            }
            logger.LogInformation("Waiting for Traefik container to become healthy... Trying again in 5 seconds.");
            await Task.Delay(5000); // Wait for 5 seconds before checking again
        }
        
        logger.LogCritical("Traefik container did not become healthy within the expected time frame.");
        return false;
    }
}