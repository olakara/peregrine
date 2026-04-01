# Peregrine — Research Findings

> **Source**: Comparative analysis of implemented code vs. documentation/request files.
> **Date**: 2026-04-01

---

## Executive Summary

The project documentation is materially behind the implementation in a few high-impact areas. The biggest mismatch is API surface: the README and request collection describe 16 endpoints, but the code exposes **19**, including a full mission-plan API (`GET/POST/DELETE /drone/mission`) that is not documented in the README endpoint tables or request files.

There are also runtime-behavior inconsistencies: README claims production JSON logs and development text logs, but current config does the opposite; README says permissive CORS is for local development, but code enables `AllowAll` unconditionally (including production in Docker Compose).

Several comments in request files are stale or internally inconsistent (battery charge-rate math, home-position coordinates, duplicate "max speed" sample).

Finally, there are implementation blind spots not called out in docs: waypoint altitudes are not validated against configured max altitude, and mission-plan persistence failures are silently downgraded to warnings while upload still returns success semantics.

---

## Architecture / System Overview — Documented vs Implemented

### Documented API surface vs implemented API surface

- README API tables and request docs frame the API around **16 endpoints** (power/flight/waypoints/telemetry/battery), with no mission endpoints listed.
  - `README.md:78–134`, `README.md:160–172`
- Implementation maps **19 endpoints**, including:
  - `POST /drone/mission`
  - `GET /drone/mission`
  - `DELETE /drone/mission`
  - Sources: `Features/Mission/UploadMissionPlanEndpoint.cs:11–63`, `GetMissionPlanEndpoint.cs:9–23`, `DeleteMissionPlanEndpoint.cs:9–21`
- Tests validate the mission endpoints heavily, confirming they are intentional and first-class, not dead code.
  - `tests/Peregrine.Api.Tests/Integration/MissionEndpointTests.cs:96–600`

**Blind spot impact:** Consumers relying on README/request files will miss mission upload/query/delete capabilities and lifecycle expectations.

### State-machine documentation drift

- README describes `navigate` as `Hovering → Flying` only and diagrams only that path. (`README.md:78–134`)
- Actual `Navigate()` logic also supports `Idle → TakingOff` **when a mission plan has Takeoff**, then auto-navigates after hover.
  - `Infrastructure/DroneContext.cs:111–140`, `DroneContext.cs:250–267`
  - `Infrastructure/FlightSimulatorService.cs:105–111`
- The endpoint summary in code reflects the new behavior, but the README table does not.
  - `Features/Flight/NavigateEndpoint.cs:17`

**Blind spot impact:** Integrators may implement incorrect control flow and treat valid mission-driven navigate requests as unsupported.

---

## Inconsistencies — README / Comments vs Code

### 1. Endpoint count and coverage mismatch

| Source | Count |
|--------|-------|
| README claim | 16 endpoints |
| Code reality | 19 endpoints |
| Request files | 5 files (power/flight/waypoints/telemetry/battery); no mission file |

- `README.md:160–172`

### 2. Test-count mismatch

| Source | Count |
|--------|-------|
| README claim | "219 tests (111 unit + 51 integration)" |
| Actual `dotnet test` run | **266 total, 0 failed** |

- README's own split is internally inconsistent: `111 + 51 ≠ 219`.
- `README.md:15`

### 3. Logging-mode mismatch

- README says production uses compact JSON; development uses timestamped text.
- **Current configs are inverted:**
  - `appsettings.json` (production baseline): `UseJsonFormat: false` → **text**
  - `appsettings.Development.json`: `UseJsonFormat: true` → **JSON**
- Docker Compose sets `ASPNETCORE_ENVIRONMENT=Production`, so container runtime follows production settings (text, not JSON).
  - `docker-compose.yml:10–13`
  - `src/Peregrine.Api/Program.cs:31–45`, `Program.cs:66–74`


### 4. Stale request-file comments / data

| File | Issue |
|------|-------|
| `request/battery.http:5–8` | Comment says charge rate is `0.5%/s` (≈ 3m20s full charge); `config/drone.yaml` actual rate is `0.10%/s` (≈ 16m40s from 0%) |
| `request/waypoints.http:6–8` | Comment says home is `24.4149, 54.4561`; `drone.yaml` is `24.414516, 54.456488` |
| `request/flight.http:57–63` | Comment says "maximum — 15 m/s" but payload value is `10.0`, duplicating the non-max sample |

---

## Implementation Blind Spots (not documented)

### A. Max altitude is not enforced on loaded waypoints

- Comments/docs imply a 120 m max altitude constraint.
- `LoadWaypointsEndpoint` and `DroneContext.LoadWaypoints` accept altitude values **without range checks**.
  - `Features/Waypoints/LoadWaypointsEndpoint.cs:10–21`
  - `Infrastructure/DroneContext.cs:167–184`
- Flight tick moves altitude directly toward waypoint altitude, allowing values above the configured max when a waypoint altitude exceeds it.
  - `Infrastructure/FlightSimulatorService.cs:200–207`

**Risk:** Users can queue unrealistic/unsafe altitudes despite the configured max, creating policy/behavior drift.

### B. Mission persistence failures are invisible to API clients

- Mission upload returns success **after** `store.Save(rawJson)` without checking the persistence result.
  - `Features/Mission/UploadMissionPlanEndpoint.cs:47–58`
- `MissionPlanStore.Save()` catches IO/auth exceptions, logs a warning, and **does not propagate** the failure.
  - `Infrastructure/MissionPlanStore.cs:25–35`

**Risk:** API can return `200 OK` for a mission upload even when restart persistence failed, causing latent data-loss surprises on next boot.

### C. Mission-plan functionality is undiscoverable

- The mission parser and endpoint suite are substantial (QGroundControl support, geofence/rally parsing, mission status object), but this capability is absent from request docs and README endpoint tables.
  - `Features/Mission/MissionModels.cs:11–280`
  - `Features/Mission/UploadMissionPlanEndpoint.cs`

**Risk:** A high-value feature remains effectively undiscoverable for most users.

---

## Prioritized Fix List

> **Status**: Items 1–3 addressed in the first commit. Items 4–8 addressed in the second commit.

| # | Fix | Impact | Status |
|---|-----|--------|--------|
| 1 | Update README API reference and state machine to include mission endpoints and `Idle → TakingOff` navigate path | High | ✅ Fixed |
| 2 | Add `request/mission.http` with valid/invalid upload, get, clear examples | High | ✅ Fixed |
| 3 | Correct README test counts from actual CI/local truth (currently 266 passing) | Medium | ✅ Fixed |
| 4 | Resolve logging documentation/config drift (flip config or update README truthfully) | Medium | ✅ Fixed (config was already correct — `appsettings.json` has `UseJsonFormat: true` / production=JSON, dev=text; matches README) |
| 5 | Reconcile CORS statement with production behavior (restrict policy in prod, or document the current risk explicitly) | Medium | ✅ Fixed |
| 6 | Enforce altitude limits for waypoint ingestion, or explicitly document that waypoint altitude bypasses the max | Medium | ✅ Fixed |
| 7 | Surface persistence failure in mission upload response, or add an explicit "best-effort persistence" contract to docs | Medium | ✅ Fixed |
| 8 | Fix stale request comments (`battery.http`, `waypoints.http`, `flight.http`) | Low | ✅ Fixed |

---

## Confidence Assessment

**High confidence** on endpoint mismatch, logging/CORS mismatch, test-count mismatch, and request-comment drift — each is directly supported by source files and runtime test output.

**Medium confidence** on intended product behavior for max-altitude enforcement and persistence semantics — the code behavior is certain, but whether this is an intended policy or a bug is inferred from comments/documentation phrasing.

---

## Source References

| Ref | Location |
|-----|----------|
| README API tables | `README.md:78–134`, `README.md:160–172` |
| README features/stats | `README.md:13–15` |
| Mission endpoints | `Features/Mission/UploadMissionPlanEndpoint.cs:11–63`, `GetMissionPlanEndpoint.cs:9–23`, `DeleteMissionPlanEndpoint.cs:9–21` |
| Mission tests | `tests/Peregrine.Api.Tests/Integration/MissionEndpointTests.cs:96–600` |
| Navigate state logic | `Infrastructure/DroneContext.cs:111–140`, `DroneContext.cs:250–267` |
| Flight simulator tick | `Infrastructure/FlightSimulatorService.cs:105–111`, `:200–207` |
| appsettings (prod) | `src/Peregrine.Api/appsettings.json:1–16` |
| appsettings (dev) | `src/Peregrine.Api/appsettings.Development.json:1–17` |
| Docker Compose env | `docker-compose.yml:10–13` |
| Program logging/CORS | `src/Peregrine.Api/Program.cs:31–45`, `:66–74`, `:87` |
| drone.yaml config | `config/drone.yaml:4–24` |
| Battery request comments | `request/battery.http:5–8` |
| Waypoints request comments | `request/waypoints.http:6–8` |
| Flight request comments | `request/flight.http:57–63` |
| LoadWaypoints endpoint | `Features/Waypoints/LoadWaypointsEndpoint.cs:10–21` |
| DroneContext waypoints | `Infrastructure/DroneContext.cs:167–184` |
| MissionPlanStore | `Infrastructure/MissionPlanStore.cs:25–35` |
| MissionModels | `Features/Mission/MissionModels.cs:11–280` |
