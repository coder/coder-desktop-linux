You are an experienced, pragmatic software engineering AI agent. Do not over-engineer a solution when a simple one is possible. Keep edits minimal. If you want an exception to ANY rule, you MUST stop and get permission first.

# Project Overview

This repository contains the Linux-focused Coder Desktop implementation in C#/.NET. The app provides tray-based access to Coder workspaces through a local VPN-like tunnel (`Coder Connect`) and includes Linux packaging assets for distribution.

Primary goals:
- Provide a Linux desktop/tray UX for connecting to Coder workspaces.
- Coordinate with a local/root VPN service over RPC.
- Ship installable Linux artifacts (`.deb`, `.rpm`, `.tar.gz`, and AUR source bundle).

Technology choices:
- **Language/runtime:** C# on .NET 8 (`net8.0` across projects)
- **UI:** Avalonia 11 (`App.Avalonia`)
- **Architecture:** DI with `Microsoft.Extensions.*`, shared ViewModel/service layer in `App.Shared`
- **RPC/protocol:** Protobuf messages (`Vpn.Proto/vpn.proto`) + custom stream RPC (`Vpn/Speaker.cs`)
- **Logging:** Serilog + Microsoft.Extensions.Logging
- **Testing:** NUnit (`Tests.*` projects)
- **Packaging:** Bash scripts under `Packaging.Linux/`

# Reference

## Important files

- `Coder.Desktop.sln` — solution entrypoint for all projects
- `App.Avalonia/App.axaml.cs` — application composition root (DI/service registration, startup flow)
- `App.Avalonia/Program.cs` — UI process entrypoint + bootstrap logging
- `App.Shared/Services/RpcController.cs` — app-side RPC lifecycle/state orchestration
- `Vpn/Speaker.cs` — generic request/reply protocol transport over streams
- `Vpn.Proto/vpn.proto` — protocol contract used by app/service/tunnel
- `Vpn.Service/Program.cs` — VPN service host, config sources, platform transport wiring
- `scripts/run-linux-dev.sh` — local Linux dev launcher (service + app)
- `.github/workflows/ci.yaml` — CI source of truth for baseline build/test checks
- `.editorconfig` — formatting/style baseline (including line endings)

## Important directories

- `App.Avalonia/` — Avalonia UI, views, controls, Linux desktop host wiring
- `App.Shared/` — cross-UI application logic (ViewModels/Models/Services abstractions)
- `App.Linux/` — Linux-only service implementations (startup, credential backend, notifications)
- `Vpn.Service/` — long-running manager service (systemd-compatible on Linux)
- `Vpn/`, `Vpn.Linux/`, `Vpn.Proto/` — RPC core, Linux transports, protobuf schema
- `CoderSdk/`, `MutagenSdk/` — API clients and vendored proto-related integration code
- `Tests.CoderSdk/`, `Tests.Vpn*/` — NUnit unit/integration-style test projects
- `Packaging.Linux/` — package build scripts, unit files, desktop metadata
- `scripts/` — developer automation scripts

## Project architecture (high level)

1. `App.Avalonia` boots and builds DI container in `App.axaml.cs`.
2. `RpcController` in `App.Shared` connects to the local service via `IRpcClientTransport` (`UnixSocketClientTransport` on Linux).
3. RPC is executed via `Speaker<TSend,TReceive>` request/reply framing and protobuf payloads.
4. `Vpn.Service` hosts service logic (`Manager`, `TunnelSupervisor`, etc.) and exposes RPC over Unix socket.
5. Packaging scripts stage/publish both app and service binaries into Linux package formats.

# Essential commands

Run all commands from repository root.

## Build

```bash
dotnet restore Coder.Desktop.sln
dotnet build Coder.Desktop.sln -c Release --no-restore
```

## Format

```bash
dotnet format Coder.Desktop.sln
```

## Lint

No dedicated linter script exists. Use formatting/analyzer verification:

```bash
dotnet format Coder.Desktop.sln --verify-no-changes
```

## Test

Match CI coverage:

```bash
dotnet test Tests.CoderSdk/Tests.CoderSdk.csproj --no-restore -v q
dotnet test Tests.Vpn.Proto/Tests.Vpn.Proto.csproj --no-restore -v q
dotnet test Tests.Vpn/Tests.Vpn.csproj --no-restore -v q
dotnet test Tests.Vpn.Service/Tests.Vpn.Service.csproj --no-restore -v q
```

## Clean

```bash
dotnet clean Coder.Desktop.sln
```

## Development server / local run

```bash
./scripts/run-linux-dev.sh --show --sudo-service
```

Useful variants:

```bash
./scripts/run-linux-dev.sh --no-build -- --minimized
./scripts/run-linux-dev.sh --help
```

## Other important scripts

```bash
# Multi-format Linux packages (deb/rpm/tar)
VERSION=1.2.3 ./Packaging.Linux/build-release-packages.sh amd64

# AUR source bundle
VERSION=1.2.3 ./Packaging.Linux/build-aur-source.sh

# Deb-only helper wrapper
VERSION=1.2.3 ./Packaging.Linux/build-deb.sh amd64

# Refresh vendored Mutagen proto tree
pwsh ./MutagenSdk/Update-Proto.ps1 -mutagenTag <tag>
```

# Patterns

- **Keep App.Shared UI-framework-agnostic.**
  - Do: expose abstractions (`IWindowService`, `IDispatcher`) and neutral types.
  - Don’t: instantiate Avalonia-specific UI types in `App.Shared`.

- **UI thread handoff in ViewModels is explicit.**
  - Do: check `_dispatcher.CheckAccess()` and repost with `_dispatcher.Post(...)` when required.
  - Don’t: mutate UI-bound observable state from background threads.

- **Avoid redundant property writes in ViewModels.**
  - Do: compare old/new before assigning to reduce flashing and unnecessary notifications.
  - Don’t: blindly reassign observable properties during model refreshes.

- **RPC request/reply discipline.**
  - Do: use `SendRequestAwaitReply` for command flows needing responses and preserve message type checks.
  - Don’t: ignore unexpected message types or skip lifecycle transitions on failures.

- **Testing style.**
  - NUnit is standard; async tests commonly include `[CancelAfter(30_000)]` to avoid hangs.

# Anti-patterns

- **Do not wire both tray icon `Command` and click event behavior** in ways that double-trigger UI toggles (`App.Avalonia/App.axaml.cs` explicitly avoids this).
- **Do not assume unvalidated URLs are safe for UI hyperlink controls**; invalid values can crash UI paths (see `TrayWindowViewModel` comments).
- **Do not manually edit vendored/generated proto imports in `MutagenSdk/Proto/`** when they originate from Mutagen; regenerate via `Update-Proto.ps1`.
- **Do not bypass serialization/lock patterns in RPC state management** (`RpcController` uses explicit operation/state locks for consistency).

# Code style

Source of truth: `.editorconfig`

Key rules currently in effect:
- Default indentation: 4 spaces
- JSON/YAML indentation: 2 spaces
- `.proto` indentation: tabs (`indent_size = 1`)
- Default line endings: `CRLF`
- UTF-8, trim trailing whitespace, final newline
- C# style prefers `var` in common scenarios and nullable reference types are enabled

When editing files, match surrounding style and existing naming/structure patterns.

# Commit and Pull Request Guidelines

## Before committing

- Run at minimum the CI-equivalent build/test flow for impacted areas.
- If you touched shared/protocol/service code, run all test commands listed above.
- If you touched packaging, run the relevant packaging script(s) and capture outputs/artifact names.

## Commit messages

Current history is minimal and imperative (e.g., `add linux release packaging workflow`).

Preferred format for new commits:
- `type: concise imperative summary`
- Examples: `fix: handle rpc reconnect timeout`, `build: adjust linux package dependencies`

If no clear type applies, use a concise imperative summary rather than vague text.

## Pull request description requirements

No PR template is present in this repository, so include:
- **What changed** (bullet list)
- **Why** (user/problem impact)
- **How it was validated** (exact commands + results)
- **Risk/rollback notes** for service, protocol, or packaging changes
- **Screenshots/video** for tray/UI behavior changes
