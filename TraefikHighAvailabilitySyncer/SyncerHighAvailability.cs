namespace TraefikHighAvailabilitySyncer;

public class SyncerHighAvailability(IConfiguration configuration, ILogger<SyncerHighAvailability> logger) : IHostedService
{
    private readonly bool _isPrimary = configuration.GetValue<bool>("IsPrimary");
    private readonly string _primaryHost = configuration.GetValue<string>("PrimaryHost")
        ?? throw new InvalidOperationException("PrimaryHost is not configured");
    
    private readonly Arp _arp = new(configuration, logger);
    private readonly int _arpInterval = configuration.GetValue<int>("ArpIntervalSeconds", 10);
    
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Loop to check the health of the primary instance
        logger.LogInformation("SyncerHighAvailability started. IsPrimary: {IsPrimary}", _isPrimary);
        if (_isPrimary)
        {
            logger.LogInformation("This instance is configured as primary. No further action required.");
            return; // If this instance is primary, no need to do anything else
        }
        
        while (true)
        {
            try
            {
                var primaryIsHealthy = await PrimaryIsHealthy();
                if (primaryIsHealthy)
                {
                    logger.LogDebug("Primary instance is healthy. Not sending out gratious arp requests.");
                    
                    // Stop sending out gratious arp requests
                }
                else
                {
                    logger.LogInformation("Primary instance is not healthy. Sending out ARP requests to take over the virtual IP.");
                    
                    // Take over the virtual IP, send out gratious arp requests
                    _arp.SendArp();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while checking the primary instance health.");
            }
            
            logger.LogDebug("Waiting for {ArpInterval} seconds before checking again.", _arpInterval);
            await Task.Delay(TimeSpan.FromSeconds(_arpInterval), cancellationToken); // Wait before checking again
        }
        
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SyncerHighAvailability stopping.");
    }
    
    private async Task<bool> PrimaryIsHealthy()
    {
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(_primaryHost);
        
        try
        {
            var response = await httpClient.GetAsync("/health");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Primary instance is not healthy. Status code: {StatusCode}", response.StatusCode);
                return false; // Primary is not healthy
            }
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Failed to ping primary instance");
            return false; // Primary is not reachable
        }
        logger.LogInformation("Primary instance is healthy.");
        return true; // Primary is healthy
    }
}