# Peregrine 🦅

A drone flight simulator API built with ASP.NET Core 10. Peregrine exposes a REST API to control a simulated drone, manage waypoints, monitor battery, and stream live telemetry via Server-Sent Events.

## Features

- **State machine** — strict drone state transitions (Offline → Idle → TakingOff → Hovering → Flying → Landing)
- **GPS navigation** — Haversine-based navigation between ordered waypoints
- **Battery simulation** — state-aware drain rates with automatic emergency landing at low battery
- **Live telemetry** — real-time status streaming via Server-Sent Events (SSE)
- **YAML configuration** — all drone performance and simulation parameters are externally configurable
- **OpenAPI docs** — interactive API documentation via Scalar UI

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

## Configuration

Drone behaviour is controlled by `config/drone.yaml`:

```yaml
drone:
  id: "peregrine-1"
  name: "Peregrine"
  homePosition:
    latitude: 24.46667 
    longitude: 54.36667
    altitude: 0.0
  performance:
    maxSpeedMps: 15.0
    maxAltitudeMeters: 120.0
    takeoffSpeedMps: 3.0
    defaultHoverAltitudeMeters: 30.0
  battery:
    initialChargePercent: 100.0
    drainRates:
      idlePerSecond: 0.02
      hoveringPerSecond: 0.15
      flyingPerSecond: 0.25
      takeoffLandingPerSecond: 0.20
      chargeRatePerSecond: 0.5
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

## Project Structure

```
Peregrine/
├── config/
│   └── drone.yaml          # Drone simulation configuration
├── src/
│   └── Peregrine.Api/      # ASP.NET Core API project
│       ├── Endpoints/      # API endpoint handlers
│       ├── Models/         # Domain models and DTOs
│       ├── Services/       # Background simulator and telemetry broadcaster
│       └── Program.cs      # Application entry point
├── docker-compose.yml
└── Peregrine.sln
```

## Tech Stack

| | |
|---|---|
| Framework | ASP.NET Core 10 |
| Language | C# 13 |
| Configuration | YAML (`NetEscapades.Configuration.Yaml`) |
| API Docs | OpenAPI + Scalar UI |
| Real-time | Server-Sent Events (SSE) |
| Containerisation | Docker (multi-stage build) |
