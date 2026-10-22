# aeroflow-local

Run the whole AeroFlow platform locally with **.NET Aspire** (ADR-0014 / platform-handbook#55).

Production hosting stays on Azure Container Apps via `workload.yaml`. This repo is the **local inner loop only**. Azure Service Bus spend is deferred (ADR-0013): local emulator only, zero Azure messaging cost for now.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.4xx or later)
- **Docker Desktop** (or another free container runtime) **running** — Service Bus emulator, SQL Server companion, and Keycloak
- About **4 GB free RAM** for the SQL Server image used by the Service Bus emulator (plus headroom for Keycloak and the .NET projects)
- Git

Optional: [Aspire workload / CLI](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) for `aspire run`. Plain `dotnet run` on the AppHost is enough — **no paid tooling**.

## Clone layout (required)

The AppHost uses **ProjectReference** to the real service repos. Clone them as **siblings** of this repo:

```text
parent/
  aeroflow-local/          ← this repository
  svc-gate-allocation/     ← https://github.com/aeroflow-air/svc-gate-allocation
  svc-flight-status/       ← https://github.com/aeroflow-air/svc-flight-status
  svc-baggage-reclaim/     ← https://github.com/aeroflow-air/svc-baggage-reclaim
```

Example:

```bash
mkdir -p ~/aeroflow && cd ~/aeroflow
git clone https://github.com/aeroflow-air/aeroflow-local.git
git clone https://github.com/aeroflow-air/svc-gate-allocation.git
git clone https://github.com/aeroflow-air/svc-flight-status.git
git clone https://github.com/aeroflow-air/svc-baggage-reclaim.git
cd aeroflow-local
```

**Why not submodules?** Aspire needs compile-time project references for typed `AddProject<Projects.*>` and a smooth F5 experience. Sibling clones keep each `svc-*` repo independent (own CI, own releases) while still giving a one-command whole-platform run. Submodules would pin commits and add friction for day-to-day service work; package references would require publishing every local change. Documented siblings are the lightest option that still compiles.

## Run it locally (first time)

All of this is **zero-cost** open-source tooling (no paid Aspire or Azure subscriptions required for the happy path).

### 1. Clone the sibling layout

```bash
mkdir -p ~/aeroflow && cd ~/aeroflow
# either:
gh repo clone aeroflow-air/aeroflow-local
gh repo clone aeroflow-air/svc-gate-allocation
gh repo clone aeroflow-air/svc-flight-status
gh repo clone aeroflow-air/svc-baggage-reclaim
# or:
git clone https://github.com/aeroflow-air/aeroflow-local.git
git clone https://github.com/aeroflow-air/svc-gate-allocation.git
git clone https://github.com/aeroflow-air/svc-flight-status.git
git clone https://github.com/aeroflow-air/svc-baggage-reclaim.git
cd aeroflow-local
```

### 2. Build

```bash
dotnet build AeroFlow.Local.sln
```

### 3. Run the AppHost (HTTP profile)

```bash
cd src/AeroFlow.Local.AppHost
dotnet run --launch-profile http
```

The `http` profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` so Aspire accepts an HTTP dashboard URL without a dev certificate.

**HTTPS option:** trust a local cert once, then use the `https` profile:

```bash
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

### 4. First-run image pulls

On the first start Docker will pull (free public images):

- `mcr.microsoft.com/azure-messaging/servicebus-emulator:1.1.2`
- `mcr.microsoft.com/mssql/server:2022-latest`
- `quay.io/keycloak/keycloak:26.3`

SQL + the emulator can take a few minutes; wait until the dashboard shows resources as **Running**.

### 5. Aspire dashboard

Watch the AppHost console for a line like:

```text
Login to the dashboard at http://localhost:15134/login?t=<token>
```

Open that URL (token is required). You should see **identity**, **messaging** (+ topic/subscription + `messaging-mssql`), **ops-dashboard**, the three real `svc-*` projects, and the five placeholders — all **Running**.

### 6. Expected endpoints

| Target | Paths | Expected |
|--------|-------|----------|
| Placeholders + ops-dashboard | `/health`, `/alive`, `/ping` | `200` |
| ops-dashboard UI | `/` (Overview), `/board`, `/gates`, `/belts`, `/events` | Live airport dashboard (Blazor Server) |
| `svc-gate-allocation` | `/health`, `/api/gates/ping` | `200` (`/alive` and `/ping` are not mapped yet) |
| `svc-flight-status` | `/health`, `/api/flights/ping` | `200` |
| `svc-baggage-reclaim` | `/health` | `200` (API arrives with platform-handbook#69) |
| Keycloak (`identity`) | `http://localhost:8080` (e.g. `/realms/master`) | Admin / OIDC UI |

Aspire assigns dynamic host ports for the three real services (their sibling `launchSettings` pin `:8080`, which would clash with Keycloak — the AppHost clears that host port). Placeholders use fixed ports in **5101–5107** via their own `launchSettings.json`.

### Troubleshooting

**`ASPIRE_ALLOW_UNSECURED_TRANSPORT` / HTTPS validation error**  
Using `http` without the env var: *"The 'applicationUrl' setting must be an https address…"*. Prefer `--launch-profile http` (already sets the env var), or trust a cert and use `https`.

**Port clashes on `:8080` or `:5000`**  
Keycloak owns **8080**. Real services must not also bind it (handled in AppHost). Placeholders without `launchSettings` used to race on Kestrel’s default **:5000** — each now has a unique profile port (5101–5107).

**Service Bus emulator exits with `SQL DB Unhealthy`**  
The emulator container must reach the SQL companion on the Aspire Docker network. On some **Linux / nested Docker** hosts, `iptables-legacy` can leave `FORWARD DROP` so container↔container traffic fails even when both containers look “Up”. **Linux-only** workaround (not needed on Docker Desktop for Mac/Windows in the usual setup):

```bash
sudo iptables-legacy -I FORWARD 1 -i br-+ -j ACCEPT
sudo iptables-legacy -I FORWARD 2 -o br-+ -j ACCEPT
```

Then restart the AppHost. Also confirm Docker Desktop (or the daemon) is running and you have ~4 GB free RAM for SQL Server.

**Every container shows `Runtime unhealthy` / `dial tcp [::1]:2375 ... actively refused`**  
Aspire is talking to the wrong Docker endpoint, usually a leftover `DOCKER_HOST=tcp://localhost:2375` or a non-default docker context. On Windows (PowerShell):

```powershell
echo $env:DOCKER_HOST                                              # should be empty
[Environment]::SetEnvironmentVariable("DOCKER_HOST", $null, "User")  # clear it permanently
Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue           # clear it in this session
docker context use desktop-linux
docker info                                                         # must succeed before dotnet run
```

Open a new terminal afterwards. On macOS/Linux: `unset DOCKER_HOST`, `docker context use default` (or `desktop-linux` with Docker Desktop).

**`messaging` shows `Running (Unhealthy)` for 2–5 minutes on first start**  
Expected: the Service Bus emulator waits for the SQL Server companion to initialise. Just wait; dependent services start once it turns healthy. If it persists beyond ~5 minutes, check the `messaging-mssql` console log in the dashboard and Docker Desktop's memory allocation (Settings → Resources; give it at least 4 GB).

**`docker` permission denied**  
Add your user to the `docker` group (or use Docker Desktop’s integration), then open a new shell.

**Windows notes:** confirmed working with Docker Desktop (WSL 2 backend). Run commands from PowerShell, make sure `docker info` works first, and don't set `DOCKER_HOST`; the Linux iptables step above is not needed.

## One command (after the first-time setup)

```bash
dotnet run --project src/AeroFlow.Local.AppHost --launch-profile http
```

Or from the AppHost directory: `dotnet run --launch-profile http`. The Aspire dashboard login URL is printed in the console.

## Live flight simulator

`flight-simulator` (`src/AeroFlow.Local.FlightSimulator`) is a worker that makes the local airport busy. Once `messaging` is healthy it publishes flight lifecycle events to the `flight-events` topic as JSON: **Scheduled → (Delayed →) Boarding → Departed → Landed**, with **Cancelled** as a branch off Scheduled or Delayed. It uses the Aspire client integration (`Aspire.Azure.Messaging.ServiceBus`) against the local emulator, so it costs nothing.

- **Contract:** `src/AeroFlow.Local.FlightEvents/FlightEvent.cs`. The message `Subject` and the `eventType` property hold the event type, and `CorrelationId` holds the flight id.
- **Subscribers:** each placeholder has its own subscription (`flight-events/svc-<name>`, created by the AppHost) and logs every event it receives, for example `catering received Boarding for BA2699 LHR->FRA [SIM-00001]`.
- **Real services:** `svc-gate-allocation` and `svc-flight-status` read their own subscriptions (`flight-events/gate-allocation` and `flight-events/flight-status`). Flight status adds each `Scheduled` flight and moves it through Boarding, Departed and Landed (`GET /api/flights/{flightNumber}`). Gate allocation gives each flight departing LHR a gate when it's scheduled, retimes it on a delay and frees it on Departed or Cancelled (`GET /api/gates`). Their logs show lines such as `BA2699 is now Departed after Departed` and `AllocateGate for BA2699 after Scheduled`.
- **Gate events:** each gate change gate allocation makes from a flight event is published to **`gate-events`** as `GateAssigned`, `GateChanged` (a delay retimes the window, or the gate moves) or `GateReleased` (svc-gate-allocation#8). Its logs show `Published GateAssigned for BA2699 at gate 12`.
- **Baggage reclaim:** `svc-baggage-reclaim` reads `flight-events/baggage-reclaim`. When a flight lands at LHR it assigns a belt, simulates the bags coming off and publishes `BeltAssigned`, `BagsOnBelt`, the odd `BagDelayed`, `LastBagOnBelt` and `ReclaimComplete` to the **`baggage-events`** topic, which the ops dashboard will read through `baggage-events/ops-dashboard` (#53). Its logs show `AssignBelt for BA2699 after Landed` and `Published BagsOnBelt for BA2699 on belt 1`. Speed it up or slow it down with `Baggage__*` settings (for example `Baggage__TickSeconds`, `Baggage__BagsPerTick`; see the svc-baggage-reclaim README).
- **Flight numbers** use two-character airline codes (`AF`, `BA`, `U2`, `FR`, `KL`, `LH`, `VS`, `BY`) because both services only accept that shape.
- **Ops dashboard (live):** open the `ops-dashboard` endpoint from the Aspire dashboard. It reads its own `ops-dashboard` subscription on **`flight-events`**, **`gate-events`** and **`baggage-events`** (all created by the AppHost) and keeps an in-memory view of the airport. Duplicate event ids are ignored and unreadable messages are dead-lettered. All three topics have **duplicate detection** on (10-minute window): publishers use deterministic message ids and retry failed sends from an outbox, so a copy that slipped through is dropped by the bus, and the dashboard drops any that remain ([ADR-0017](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0017-reliable-event-publishing.md)). The flight simulator also keeps unsent events and retries them next tick instead of dropping them. On startup it also loads the current flights, gates and belts from `svc-flight-status` (`/api/flights/departures/LHR` and `/arrivals/LHR`), `svc-gate-allocation` (`/api/gates`) and `svc-baggage-reclaim` (`/belts`) through service discovery, so a restart doesn't show an empty airport (platform-handbook#74). Events layer on top and win over the loaded data. A service that is down is retried every 5 s for about two minutes and named in the top bar (`snapshot 19:59:00 · no gates`); the **Refresh** button reloads on demand. Settings: `Snapshot__*` (for example `Snapshot__HomeAirport`).
  - **Board:** LHR departures (gate from `gate-events`) and arrivals (belt from `baggage-events`); `TBA` until assigned.
  - **Gates:** a tile per gate seen in `gate-events`, showing the flight and on/off-block window, or free.
  - **Baggage belts:** each belt's flights moving through waiting, bags on belt, last bag and complete, with delayed bags counted.
  - **Event stream:** the latest 200 events across all three topics.
  - Finished flights drop off after 10 minutes and cleared belts after 2. State starts empty on restart; initial load from the service APIs is #74. The health strip polls `/health` on flight status, gate allocation and baggage reclaim (`HealthStrip__*`).
- **Traces:** in the dashboard, under **Traces**, a `flight-simulator: simulator tick` trace contains the send spans and the receive spans from the placeholders and real services.

### Tune or pause

Settings are in the `Simulator` section of `src/AeroFlow.Local.FlightSimulator/appsettings.json`. That file is reloaded while the simulator runs, so editing it takes effect without a restart. You can also set them as `Simulator__*` environment variables on the resource in `AppHost.cs`.

| Setting | Default | Meaning |
|---------|---------|---------|
| `Enabled` | `true` | `false` pauses publishing (the loop idles and resumes when set back to `true`) |
| `TickSeconds` | `5` | Seconds between ticks; each tick advances every active flight one step |
| `NewFlightProbability` | `0.6` | Chance of scheduling a new flight per tick |
| `MaxActiveFlights` | `8` | Cap on flights in progress |
| `DelayProbability` / `CancelProbability` | `0.15` / `0.05` | Disruption rates |
| `Seed` | `42` | Seeded random generator; the same seed replays the same flights (good for demos) |

Example (AppHost): `.WithEnvironment("Simulator__TickSeconds", "2")` or `.WithEnvironment("Simulator__Enabled", "false")`.

## What runs

| Resource | How | Notes |
|----------|-----|--------|
| **messaging** | Azure Service Bus via `RunAsEmulator()` | Free local emulator + SQL companion container; topic `flight-events` |
| **identity** | Keycloak container (`Aspire.Hosting.Keycloak`) | Free OSS OIDC stand-in; stable host port **8080** |
| **ops-dashboard** | Blazor Server app in this repo, live from `flight-events`, `gate-events` and `baggage-events` ([ADR-0016](https://github.com/aeroflow-air/platform-handbook/pull/81)) | Live airport UI at `/`, plus `/health`, `/alive`, `/ping` |
| **svc-gate-allocation** | Sibling project reference | Real service; assigns and frees gates from `flight-events/gate-allocation` |
| **svc-flight-status** | Sibling project reference | Real service; updates flight state from `flight-events/flight-status` |
| **svc-baggage-reclaim** | Sibling project reference | Real service; belts and bags from `flight-events/baggage-reclaim`, publishes `baggage-events` |
| **svc-turnaround** … **svc-terminal** | Placeholders in `src/placeholders/` | `/health` + `/ping`; log `flight-events` from their own subscription |
| **flight-simulator** | Worker in this repo | Publishes simulated flight lifecycle events to `flight-events` |

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

## Demo scenarios

The ops dashboard has a **Scenario** panel (Overview and Board pages) that asks the flight simulator to change the
simulated airport. Effects arrive as normal `flight-events` lifecycle events, so every service reacts as it would to a
simulated event. The dashboard itself still never publishes events or writes to the services.

- **Delay a flight by N minutes:** pick a flight that hasn't started boarding, set the minutes (1–600) and press
  *Delay flight*. The simulator publishes a `Delayed` event (delays add up). The board shows `Delayed +N`, and
  svc-gate-allocation retimes the gate window (a `GateChanged` "retimed" event in the stream).
- Simulator endpoints (local demo only, no auth): `GET /scenarios/flights`, `POST /scenarios/delay`
  `{ "flightNumber": "BA123", "minutes": 45, "reason": "optional" }`.
