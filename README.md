# TraefikHighAvailabilitySyncer

A tool to synchronize Traefik configuration across multiple instances in a high availability setup.

> [!WARNING]  
> This tool is very experimental and should not be used in production environments. I don't even know if it works. Use at your own risk.

## Features
- Synchronizes Traefik configuration across multiple instances.
- Checks Traefik health after configuration changes before syncing to other instances.