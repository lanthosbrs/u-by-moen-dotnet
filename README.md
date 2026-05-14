# U by Moen — .NET API Bridge

A .NET 10 ASP.NET Core Web API that wraps the [U by Moen](https://www.moen.com/ubymoen) IoT cloud API, exposing a simple local REST interface that Home Assistant (or any other home-automation platform) can call via `rest_command` and `rest` integrations.

## Architecture

```
┌──────────────────────────────────────┐
│  Home Assistant                      │
│  (rest_command / rest sensor)        │
└─────────────────┬────────────────────┘
                  │ HTTP
┌─────────────────▼────────────────────┐
│  UByMoen.Api  (ASP.NET Core)         │
│  ┌───────────────────────────────┐   │
│  │  DevicesController            │   │
│  │  PusherBackgroundService      │   │
│  │  DeviceStateService (cache)   │   │
│  └───────────┬───────────────────┘   │
└──────────────┼───────────────────────┘
               │ HTTP + WebSocket
┌──────────────▼───────────────────────┐
│  Moen IoT Cloud  (moen-iot.com)      │
│  REST API + Pusher WebSocket         │
└──────────────────────────────────────┘
```

### Projects

| Project | Purpose |
|---|---|
| `UByMoen.Core` | Models, constants, exceptions, `IMoenApiClient`, `IMoenPusherClient`, and their implementations |
| `UByMoen.Api` | ASP.NET Core host — REST endpoints, background WebSocket service, DI wiring |

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A U by Moen account with at least one connected shower system

### Configuration

Credentials are read from `appsettings.json` (or environment variables / user-secrets for production).

**`appsettings.json`** (never commit real credentials):
```json
{
  "Moen": {
    "Email": "you@example.com",
    "Password": "your-password"
  }
}
```

For local development, use [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):
```bash
cd src/UByMoen.Api
dotnet user-secrets set "Moen:Email" "you@example.com"
dotnet user-secrets set "Moen:Password" "your-password"
```

For Docker / production, use environment variables:
```bash
MOEN__EMAIL=you@example.com
MOEN__PASSWORD=your-password
```

### Run

```bash
cd src/UByMoen.Api
dotnet run
```

The API listens on `http://localhost:5000` by default.

---

## API Reference

All endpoints return / accept JSON. Successful commands return `204 No Content`.

### Devices

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/devices` | List all devices with current state |
| `GET` | `/api/devices/{serial}` | Get a single device |
| `POST` | `/api/devices/{serial}/on` | Turn shower on (auto-resumes if paused-by-preset) |
| `POST` | `/api/devices/{serial}/off` | Turn shower off |
| `POST` | `/api/devices/{serial}/resume` | Resume water after a preset paused it |
| `POST` | `/api/devices/{serial}/temperature` | Set target temperature |
| `POST` | `/api/devices/{serial}/presets/{position}` | Activate a preset |
| `POST` | `/api/devices/{serial}/outlets/{position}/on` | Turn an outlet on |
| `POST` | `/api/devices/{serial}/outlets/{position}/off` | Turn an outlet off |

#### Set temperature body
```json
{ "temperature": 104.0 }
```

### Device state fields

```json
{
  "serialNumber": "XXXXXXXX",
  "displayName": "Master Bath",
  "mode": "ready",
  "currentTemperature": 102.5,
  "targetTemperature": 104.0,
  "maxTemperature": 115.0,
  "activePreset": 1,
  "firmwareVersion": "2.3.1",
  "batteryLevel": null,
  "timerEnabled": false,
  "timerRemaining": null,
  "outlets": [
    { "position": 1, "active": true, "iconIndex": 0, "name": null }
  ],
  "presets": [
    { "position": 1, "title": "Morning", "targetTemperature": 104.0 }
  ]
}
```

### Mode values

| Value | Meaning |
|-------|---------|
| `off` | Shower is off |
| `adjusting` | Shower is on, heating/cooling to target |
| `ready` | Shower is on and at target temperature |
| `pause` | Shower temporarily paused |
| `paused-by-preset` | Preset's "ready pauses water" feature activated |

---

## Home Assistant Integration

Add to your HA `configuration.yaml`:

```yaml
# Expose current shower state as sensors
rest:
  - resource: http://192.168.1.x:5000/api/devices/YOURSERIAL
    scan_interval: 30
    sensor:
      - name: "Shower Mode"
        value_template: "{{ value_json.mode }}"
      - name: "Shower Current Temp"
        value_template: "{{ value_json.currentTemperature }}"
        unit_of_measurement: "°F"
      - name: "Shower Target Temp"
        value_template: "{{ value_json.targetTemperature }}"
        unit_of_measurement: "°F"

# Commands
rest_command:
  shower_on:
    url: "http://192.168.1.x:5000/api/devices/YOURSERIAL/on"
    method: POST
  shower_off:
    url: "http://192.168.1.x:5000/api/devices/YOURSERIAL/off"
    method: POST
  shower_set_temp:
    url: "http://192.168.1.x:5000/api/devices/YOURSERIAL/temperature"
    method: POST
    content_type: "application/json"
    payload: '{"temperature": {{ temperature }}}'
  shower_preset_1:
    url: "http://192.168.1.x:5000/api/devices/YOURSERIAL/presets/1"
    method: POST
```

---

## Real-time Updates

On startup the service:
1. Authenticates with the Moen cloud API
2. Connects to the Pusher WebSocket for real-time device events
3. Subscribes to a private channel per device (`private-{channelId}`)
4. Falls back to polling every 30 seconds if the WebSocket is unavailable

State changes (temperature, mode, outlets) pushed by the Moen app or physical controls are reflected immediately in subsequent `GET` calls.

---

## Building & Publishing

```bash
# Build
dotnet build

# Publish self-contained for Linux (e.g. for a Raspberry Pi / Docker)
dotnet publish src/UByMoen.Api -c Release -r linux-x64 --self-contained

# Run via Docker
docker build -t u-by-moen-api .
docker run -e MOEN__EMAIL=you@example.com -e MOEN__PASSWORD=secret -p 5000:8080 u-by-moen-api
```

---

## License

MIT
