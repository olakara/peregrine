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
- **CORS** — permissive `AllowAll` policy enabled for local development (restrict origins before deploying to production)
- **Test suite** — 161 tests (97 unit + 64 integration) covering all endpoints, state transitions, battery logic, and GPS math

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
| `POST` | `/drone/navigate` | Start navigating loaded waypoints (`Hovering → Flying`) |

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

## Drone State Machine

```
Offline ──power on──► Idle ──takeoff──► TakingOff ──► Hovering ──navigate──► Flying
                       ▲                                   │                     │
                       │                              land/emergency          hover/land
                       │                                   │                     │
                       └──────────────── Landing ◄─────────┴─────────────────────┘
```

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

Default endpoint: `http://localhost:5180/drone/telemetry`

## HTTP Request Files

Feature-scoped `.http` files for all 13 API endpoints are in the `request/` folder (VS Code REST Client format):

| File | Endpoints covered |
|------|-------------------|
| `_variables.http` | Shared base URL variable |
| `power.http` | `POST /drone/power/on\|off` |
| `flight.http` | `POST /drone/takeoff\|land\|hover\|navigate` |
| `waypoints.http` | `POST\|DELETE /drone/waypoints` |
| `telemetry.http` | `GET /drone/status` and `/drone/telemetry` |
| `battery.http` | `GET\|POST /drone/battery` and recharge endpoints |

## Project Structure

```
Peregrine/
├── config/
│   └── drone.yaml              # Drone simulation configuration
├── request/                    # VS Code REST Client .http files
├── src/
│   └── Peregrine.Api/          # ASP.NET Core API project
│       ├── Domain/             # Domain models (DroneState, DroneStatus, etc.)
│       ├── Features/           # Vertical slices (Battery, Flight, Power, Telemetry, Waypoints)
│       ├── Infrastructure/     # DroneContext, FlightSimulatorService, GeoMath, TelemetryBroadcaster
│       └── Program.cs          # Application entry point
├── tests/
│   └── Peregrine.Api.Tests/    # xUnit unit + integration tests (161 tests)
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
