using System.Collections.Concurrent;

namespace TraefikHighAvailabilitySyncer;

public class TraefikPrimarySyncer(ILogger<TraefikPrimarySyncer> logger, IConfiguration configuration, TraefikDocker dockerClient) : IHostedService 
{
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private readonly ConcurrentDictionary<string, DateTime> _fileTimestamps = new();
    private bool firstRun = true;


    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("TraefikPrimarySyncer started (polling mode)");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = Task.Run(() => PollDirectoryAsync(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping TraefikPrimarySyncer");
        _cts?.Cancel();
        _cts?.Dispose();
        return _pollingTask ?? Task.CompletedTask;
    }

    private async Task PollDirectoryAsync(CancellationToken token)
    {
        var directory = configuration.GetValue<string>("TraefikConfigDirectory") ?? throw new InvalidOperationException("TraefikConfigDirectory is not configured");
        while (!token.IsCancellationRequested)
        {
            var files = Directory.GetFiles(directory, "*.yml").Concat(Directory.GetFiles(directory, "*.yaml"));
            var changedFiles = new List<string>();

            foreach (var file in files)
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (!_fileTimestamps.TryGetValue(file, out var prevWrite) || lastWrite > prevWrite)
                {
                    if (firstRun)
                    {
                        continue;
                    }
                    _fileTimestamps[file] = lastWrite;
                    changedFiles.Add(file);
                }
            }

            firstRun = false; // After the first run, we start checking for changes

            if (changedFiles.Count > 0)
            {
                logger.LogInformation("Configuration files changed: {Files}", string.Join(", ", changedFiles.Select(Path.GetFileName)));
                await OnConfigFilesChangedAsync(changedFiles);
            }

            await Task.Delay(1000, token); // Poll every second
        }
    }
    
    private async Task OnConfigFilesChangedAsync(List<string> files)
    {
        foreach (var file in files)
        {
            logger.LogDebug("FileSystemWatcher triggered for file: {FileName}", file);
            logger.LogInformation("Configuration file changed: {FileName}", file);
        }

        // Restart the Traefik container to apply the new configuration and wait for it to become healthy.
        var healthyAfterRestart = await ApplyConfigurationAndCheckHealthAsync();

        if (!healthyAfterRestart)
        {
            logger.LogCritical("Traefik container did not become healthy after configuration change. Not updating secondary instances.");
            return; // Exit if Traefik is not healthy
        }

        var secondaryInstances = configuration.GetValue<string>("SecondaryInstances")?.Split(",").Select(s => s.Trim()).ToList()
                             ?? throw new InvalidOperationException("SecondaryInstances is not configured");

        if (secondaryInstances.Count == 0)
        {
            logger.LogWarning("No secondary instances configured. Skipping configuration update.");
            return; // No secondary instances to notify
        }

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
        
        logger.LogInformation("Traefik restarting container with ID {ContainerId} to apply new configuration.", containerId);
        await dockerClient.RestartTraefikContainerAsync(containerId);
        
        logger.LogInformation("Traefik container restarted. Waiting for it to become healthy...");
        
        // Wait for the Traefik container to become healthy
        var waitTime = TimeSpan.FromSeconds(configuration.GetValue("TraefikConfigWaitTime", 60));
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < waitTime)
        {
            if (await dockerClient.IsTraefikContainerHealthyAsync(containerId))
            {
                logger.LogInformation("Traefik container is healthy after restart.");
                return true; // Traefik is healthy
            }
            logger.LogInformation("Waiting for Traefik container to become healthy... Trying again in 5 seconds.");
            await Task.Delay(1000); // Wait for 5 seconds before checking again
        }
        
        logger.LogCritical("Traefik container did not become healthy within the expected time frame.");
        return false;
    }
}