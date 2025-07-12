using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace TraefikHighAvailabilitySyncer;

public class TraefikDocker(string dockerUri, ILogger<TraefikDocker> logger)
{
    private readonly DockerClient _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    
    public async Task<string?> GetTraefikContainerIdAsync()
    {
        try
        {
            var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters() { All = true });
        
            // Find the Traefik container by image name
            var traefikContainer = containers.FirstOrDefault(c => c.Image.StartsWith("traefik"));
            if (traefikContainer == null)
            {
                throw new InvalidOperationException("Traefik container not found");
            }
            return traefikContainer.ID;
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Failed to get traefik container id");
            return null;
        }
    }
    
    public async Task<bool> IsTraefikContainerHealthyAsync(string containerId)
    {
        var container = await _client.Containers.InspectContainerAsync(containerId);
        if (container == null)
        {
            throw new InvalidOperationException($"Container with ID {containerId} not found");
        }
        return container.State.Health?.Status == "healthy";
    }
    
    public async Task RestartTraefikContainerAsync(string containerId)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty", nameof(containerId));
        }
        await _client.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters());
    }
    
    public async Task WaitForTraefikContainerToBeHealthyAsync(string containerId, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            if (await IsTraefikContainerHealthyAsync(containerId))
            {
                return; // Traefik is healthy
            }
            await Task.Delay(1000); // Wait for 1 second before checking again
        }
        throw new TimeoutException($"Traefik container {containerId} did not become healthy within the timeout period.");
    }
}