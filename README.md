# Peregrine 🦅

A drone flight simulator API built with ASP.NET Core 10. Peregrine exposes a REST API to control a simulated drone, manage waypoints, monitor battery, and stream live telemetry via Server-Sent Events.

## Features

- **State machine** — strict drone state transitions (Offline → Idle → TakingOff → Hovering → Flying → Landing)
- **GPS navigation** — Haversine-based navigation between ordered waypoints
- **Battery simulation** — state-aware drain rates with automatic emergency landing at low battery
- **Live telemetry** — real-time status streaming via Server-Sent Events (SSE)
- **YAML configuration** — all drone performance and simulation parameters are externally configurable
- **OpenAPI docs** — interactive API documentation via Scalar UI
- **Structured logging** — Serilog with compact JSON (production) or timestamped text (development) output, plus per-request HTTP logging
- **CORS** — unconditional `AllowAll` policy (any origin, any method, any header) applied across all environments including production. Required for the browser-based Drone Mapper and SSE Monitor tools. Restrict origins before exposing this API to untrusted networks.
- **Test suite** — 266 tests (183 unit + 83 integration) covering all endpoints, state transitions, battery logic, and GPS math

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker & Docker Compose (optional, for containerized run)

### Run locally

```bash
dotnet run --project src/Peregrine.Api
```

The API will be available at `http://localhost:5180`.  
OpenAPI docs: `http://localhost:5180/scalar/v1`

### Run with Docker

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

### Run the tests

```bash
dotnet test tests/Peregrine.Api.Tests/Peregrine.Api.Tests.csproj
```

## Configuration

Drone behaviour is controlled by `config/drone.yaml`:

```yaml
drone:
  id: "550e8400-e29b-41d4-a716-446655440000"
  name: "Peregrine"
  homePosition:
    latitude: 24.414516   # Abu Dhabi, UAE
    longitude: 54.456488
    altitude: 0.0
  performance:
    maxSpeedMps: 15.0
    maxAltitudeMeters: 120.0
    takeoffSpeedMps: 3.0
    defaultHoverAltitudeMeters: 30.0
  battery:
    initialChargePercent: 100.0
    drainRates:
      idlePerSecond: 0.00001
      hoveringPerSecond: 0.001
      flyingPerSecond: 0.005
      takeoffLandingPerSecond: 0.007
      chargeRatePerSecond: 0.10
    emergencyLandThresholdPercent: 10.0
  simulation:
    tickIntervalMs: 500
    telemetryIntervalMs: 1000
```

## API Reference

All endpoints are under the `/drone` base path.

### Power

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/drone/power/on` | Power on the drone (`Offline → Idle`) |
| `POST` | `/drone/power/off` | Power off the drone (`Idle/Charging → Offline`) |

### Flight

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/drone/takeoff` | Take off to a specified altitude (`Idle → TakingOff → Hovering`) |
| `POST` | `/drone/land` | Initiate landing (`Hovering/Flying → Landing → Idle`) |
| `POST` | `/drone/hover` | Pause navigation and hover in place (`Flying → Hovering`) |
| `POST` | `/drone/navigate` | Start navigating loaded waypoints (`Hovering → Flying`, or `Idle → TakingOff` when mission plan includes a Takeoff command) |
| `POST` | `/drone/return-home` | Navigate to home position and auto-land (`Hovering/Flying → Flying → Landing → Idle`) |
| `PUT` | `/drone/speed` | Set cruise speed in m/s (any powered-on state; resets on power-off) |
| `PUT` | `/drone/altitude` | Adjust target altitude while hovering or flying |

### Waypoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/drone/waypoints` | Load an ordered list of GPS waypoints |
| `DELETE` | `/drone/waypoints` | Clear the waypoint queue |

### Telemetry

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/drone/status` | Get the current drone status snapshot |
| `GET` | `/drone/telemetry` | SSE stream of live drone status updates |

### Battery

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/drone/battery` | Get current battery level and charging status |
| `POST` | `/drone/battery/recharge` | Start recharging (`Idle → Charging`). Auto-stops when full. |
| `POST` | `/drone/battery/recharge/stop` | Stop recharging (`Charging → Idle`) |

### Mission

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/drone/mission` | Upload a QGroundControl .plan mission file (JSON body) |
| `GET` | `/drone/mission` | Get the currently loaded mission plan summary |
| `DELETE` | `/drone/mission` | Clear the currently loaded mission plan |

## Drone State Machine

```
Offline ──power on──► Idle ──takeoff──► TakingOff ──► Hovering ──navigate──► Flying
   ▲                   │ ▲               ▲                │                      │
   │               recharge│   navigate  │          land/emergency          hover/land
   │                   ▼ │  (mission)   │                │                      │
   └──power off──── Charging            └────────────────┴──────── Landing ◄────┘
                                                                        │
                                                                        ▼
                                                                       Idle
```

> `navigate` from `Idle` requires a mission plan with a Takeoff command loaded via `POST /drone/mission`.

Battery below the emergency threshold triggers an automatic landing from any airborne state.

## Tools

Two standalone HTML tools are included in the `tools/` directory. Open them directly in a browser — no build step required.

### SSE Event Monitor (`tools/sse-monitor/index.html`)

A minimal debug page for inspecting raw SSE streams. Enter any SSE endpoint URL, click **Connect**, and watch incoming events appear in a scrollable log. Useful for verifying that `GET /drone/telemetry` is streaming correctly.

### Drone Mapper (`tools/drone-mapper/index.html`)

A live map interface built with Leaflet that subscribes to `GET /drone/telemetry` and plots the drone's position in real time. Features include:

- Animated drone marker that rotates to the current heading
- Flight trail polyline (last 300 GPS fixes)
- Status panel showing state badge, battery bar, speed, altitude, and waypoint queue depth
- Auto-follow mode that re-centres the map on every telemetry update
- Dark-themed UI with colour-coded drone state badges

Default telemetry endpoints:
- When running via `dotnet run`: `http://localhost:5180/drone/telemetry`
- When running via Docker (port 8080 exposed): `http://localhost:8080/drone/telemetry`

## HTTP Request Files

Feature-scoped `.http` files for all 19 API endpoints are in the `request/` folder (VS Code REST Client format):

| File | Endpoints covered |
|------|-------------------|
| `_variables.http` | Shared base URL variable |
| `power.http` | `POST /drone/power/on`<br>`POST /drone/power/off` |
| `flight.http` | `POST /drone/takeoff`<br>`POST /drone/land`<br>`POST /drone/hover`<br>`POST /drone/navigate`<br>`POST /drone/return-home`<br>`PUT /drone/speed`<br>`PUT /drone/altitude` |
| `waypoints.http` | `POST /drone/waypoints`<br>`DELETE /drone/waypoints` |
| `telemetry.http` | `GET /drone/status`<br>`GET /drone/telemetry` |
| `battery.http` | `GET /drone/battery`<br>`POST /drone/battery/recharge`<br>`POST /drone/battery/recharge/stop` |
| `mission.http` | `POST /drone/mission`<br>`GET /drone/mission`<br>`DELETE /drone/mission` |

## Project Structure

```
Peregrine/
├── config/
│   └── drone.yaml              # Drone simulation configuration
├── request/                    # VS Code REST Client .http files
├── src/
│   └── Peregrine.Api/          # ASP.NET Core API project
│       ├── Domain/             # Domain models (DroneState, DroneStatus, etc.)
│       ├── Features/           # Vertical slices (Battery, Flight, Mission, Power, Telemetry, Waypoints)
│       ├── Infrastructure/     # DroneContext, FlightSimulatorService, GeoMath, TelemetryBroadcaster
│       └── Program.cs          # Application entry point
├── tests/
│   └── Peregrine.Api.Tests/    # xUnit unit + integration tests (266 tests)
├── tools/
│   ├── drone-mapper/           # Live map visualisation (Leaflet, plain HTML)
│   └── sse-monitor/            # SSE debug viewer (plain HTML)
├── docker-compose.yml
└── Peregrine.slnx
```

## Tech Stack

| | |
|---|---|
| Framework | ASP.NET Core 10 |
| Language | C# 13 |
| Configuration | YAML (`NetEscapades.Configuration.Yaml`) |
| API Docs | OpenAPI + Scalar UI |
| Real-time | Server-Sent Events (SSE) |
| Logging | Serilog (structured JSON / text) |
| Containerisation | Docker (multi-stage, Alpine runtime) |
| Testing | xUnit · WebApplicationFactory · FluentAssertions |
| Map (tool) | Leaflet 1.9 |
