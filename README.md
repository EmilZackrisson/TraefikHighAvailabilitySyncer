# TraefikHighAvailabilitySyncer

A tool to synchronize Traefik configuration across multiple instances in a high availability setup.

> [!WARNING]  
> This tool is very experimental and should not be used in critical environments.

## Features
- Synchronizes Traefik configuration across multiple instances.
- Checks Traefik health after configuration changes before syncing to other instances.
- Allows keepalived to check health of the Traefik container for high availability.
