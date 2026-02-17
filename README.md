# Coder Desktop for Linux

Coder Desktop allows you to work on your Coder workspaces as though they are
on your local network, with no port-forwarding required. It provides seamless
access to remote development environments through Coder Connect (VPN-like
connectivity) and file synchronization workflows.

Learn more about Coder Desktop in the
[official documentation](https://coder.com/docs/user-guides/desktop).

This repository contains the Linux-focused C# source code for Coder Desktop,
including the Avalonia tray app, Linux VPN service integration, and Linux
packaging assets.

## Development

Requirements:

- .NET 8 SDK
- Linux desktop environment (for Avalonia tray UI testing)
- `sudo` access (recommended for full VPN service integration in local dev)

Useful command:

```bash
./scripts/run-linux-dev.sh --show --sudo-service
```

## License

The Coder Desktop source is licensed under the GNU Affero General Public
License v3.0 (AGPL-3.0).

Some vendored files in this repo are licensed separately. The license for those
files can be found in the same directory as the files.
