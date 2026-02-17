# Linux Packaging

## Debian package (`.deb`)

```bash
# Build .deb for amd64
./build-deb.sh amd64

# Build .deb for arm64
VERSION=1.0.0 ./build-deb.sh arm64
```

The output package is written to the repository root as:

- `coder-desktop_<version>_amd64.deb`
- `coder-desktop_<version>_arm64.deb`

## Arch / Manjaro package (`PKGBUILD`)

A sample `PKGBUILD` and `coder-desktop.install` are provided for Arch-based
systems.

```bash
cd Packaging.Linux
makepkg -si
```

## Local development run helper

From repo root, use:

```bash
./scripts/run-linux-dev.sh
```

This script:

- builds `Vpn.Service` + `App.Avalonia` (unless `--no-build`)
- starts the service on a user-writable socket
- overrides `Manager:TunnelBinaryPath` to:
  - `${XDG_CACHE_HOME:-$HOME/.cache}/coder-desktop-dev/coder-vpn` (default)
  - `/tmp/coder-desktop-dev-root/coder-vpn` when `--sudo-service` is used
- sets `Manager:TunnelBinaryAllowVersionMismatch=true` for Linux dev runs because
  Windows PE ProductVersion validation does not apply to Linux tunnel binaries
- can run the service via `--sudo-service` (recommended when testing real tunnel setup,
  since creating/configuring TUN interfaces typically requires root privileges)
- starts the Avalonia app in visible mode (`--show` by default)
- writes logs under `${XDG_RUNTIME_DIR:-/tmp}/coder-desktop-dev/logs/`

Useful variants:

```bash
./scripts/run-linux-dev.sh --no-build
./scripts/run-linux-dev.sh -- --minimized
./scripts/run-linux-dev.sh --sudo-service
```

The app also writes bootstrap logs to:

- `~/.local/state/coder-desktop/app-avalonia.log`

## Package contents

- `/usr/lib/coder-desktop/app/` — Avalonia desktop app binaries
- `/usr/lib/coder-desktop/service/` — VPN service binaries
- `/usr/bin/coder-desktop` — Desktop app launcher wrapper
- `/usr/bin/coder-vpn-service` — Symlink to VPN service binary
- `/etc/systemd/system/coder-desktop.service` — systemd unit
- `/etc/coder-desktop/config.json` — Default configuration
- `/usr/share/applications/coder-desktop.desktop` — Desktop entry
- `/usr/share/pixmaps/coder-desktop.ico` — App icon

## Runtime dependencies

### Debian/Ubuntu package names

- `dotnet-runtime-8.0`
- `libnotify-bin`
- `libsecret-tools`
- `freerdp2-x11` or `remmina` (optional)

### Arch/Manjaro package names

- `dotnet-runtime-8.0`
- `libnotify`
- `libsecret`
- `freerdp` or `remmina` (optional)
