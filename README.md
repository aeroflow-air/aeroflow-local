# aeroflow-local

Run the whole AeroFlow platform locally with **.NET Aspire** (ADR-0014 / platform-handbook#55).

Production hosting stays on Azure Container Apps via `workload.yaml`. This repo is the **local inner loop only**. Azure Service Bus spend is deferred (ADR-0013): local emulator only, zero Azure messaging cost for now.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.4xx or later)
- A container runtime (Docker Desktop, Podman, or equivalent) — required for the Service Bus emulator and Keycloak
- Git

Optional: [Aspire workload / CLI](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) for `aspire run`. Plain `dotnet run` on the AppHost is enough.

## Clone layout (required)

The AppHost uses **ProjectReference** to the real service repos. Clone them as **siblings** of this repo:

```text
parent/
  aeroflow-local/          ← this repository
  svc-gate-allocation/     ← https://github.com/aeroflow-air/svc-gate-allocation
  svc-flight-status/       ← https://github.com/aeroflow-air/svc-flight-status
```

Example:

```bash
mkdir -p ~/aeroflow && cd ~/aeroflow
git clone https://github.com/aeroflow-air/aeroflow-local.git
git clone https://github.com/aeroflow-air/svc-gate-allocation.git
git clone https://github.com/aeroflow-air/svc-flight-status.git
cd aeroflow-local
```

**Why not submodules?** Aspire needs compile-time project references for typed `AddProject<Projects.*>` and a smooth F5 experience. Sibling clones keep each `svc-*` repo independent (own CI, own releases) while still giving a one-command whole-platform run. Submodules would pin commits and add friction for day-to-day service work; package references would require publishing every local change. Documented siblings are the lightest option that still compiles.

## One command

```bash
dotnet run --project src/AeroFlow.Local.AppHost
```

Or from the AppHost directory:

```bash
dotnet run
```

The Aspire dashboard opens automatically (see `launchSettings.json`). Every resource should show as healthy once containers and projects are up.

## What runs

| Resource | How | Notes |
|----------|-----|--------|
| **messaging** | Azure Service Bus via `RunAsEmulator()` | Free local emulator + SQL companion container; topic `flight-events` |
| **identity** | Keycloak container (`Aspire.Hosting.Keycloak`) | Free OSS OIDC stand-in; stable host port **8080** |
| **ops-dashboard** | Placeholder minimal API in this repo | `/health`, `/alive`, `/ping` |
| **svc-gate-allocation** | Sibling project reference | Real service |
| **svc-flight-status** | Sibling project reference | Real service |
| **svc-baggage-reclaim** … **svc-terminal** | Placeholders in `src/placeholders/` | `/health` + `/ping` until real repos exist |

Service discovery and connection strings are injected by the AppHost — no hand-set local secrets for the happy path.

### Why Keycloak for identity?

- **Free container** (no Azure / Entra cost in the inner loop)
- Real **OIDC** so JWT validation matches the eventual Entra path (#51)
- First-class **Aspire hosting integration**
- Apache-2.0 licensed

Exact Entra product choice remains with #51; this is the local plug-in only.

## Add a new service

1. Prefer a real `svc-*` repo cloned as a sibling when it exists.
2. Add a `ProjectReference` from `AeroFlow.Local.AppHost.csproj` (sibling path or `src/placeholders/...`).
3. In `AppHost.cs`, `AddProject<Projects.Your_Project>("resource-name")`, then `.WithReference(serviceBus)` / `.WithReference(keycloak)` as needed.
4. For placeholders still in this repo, reference `AeroFlow.Local.ServiceDefaults` and call `AddServiceDefaults()` + `MapDefaultEndpoints()`.
5. Restore and run the AppHost again.

## Build check

```bash
dotnet build AeroFlow.Local.sln
```

Requires the sibling clone layout above.

## Related

- Planning ADR: [ADR-0014](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0014-aspire-local-development.md)
- Event bus ADR: [ADR-0013](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0013-central-event-bus.md)
- Implementation issue: [platform-handbook#55](https://github.com/aeroflow-air/platform-handbook/issues/55)
- Planning issue: [platform-handbook#54](https://github.com/aeroflow-air/platform-handbook/issues/54)

## License

MIT — see [LICENSE](LICENSE).
