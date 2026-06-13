# MAIS — Current Architecture Reference

**Multi Agentic Intelligent Suite**
Date: June 2026 | Stack: .NET 10 / C# 14 / Windows

---

## 1. Purpose & Vision

MAIS is a **production-grade, enterprise Windows platform** for orchestrating a modular suite of agentic services under a single unified framework. The organisation is Windows-heavy, so the outer shell is a .NET Windows Service that guarantees enterprise stability. The long-term vision is a platform where AI workers, RAG pipelines, embedding pipelines, external endpoint adapters, background processors, and agentic orchestration frameworks all plug into the same framework with minimal boilerplate.

Predicted scale: **800+ simultaneous client machines** connecting to a shared server.

---

## 2. Solution Structure

```
MAIS.sln
└── src/
    ├── MAIS.Core                        # Shared contracts, models, abstractions
    ├── MAIS.Infrastructure              # Event bus, DI extensions
    ├── MAIS.Server.Service              # Central server (Windows Service + ASP.NET Core)
    ├── MAIS.Client.Service              # Per-workstation client (Windows Service + ASP.NET Core)
    ├── MAIS.Sidebar                     # WPF desktop sidebar (system tray app)
    ├── MAIS.Sidebar.Abstractions        # UI contracts shared by modules and sidebar
    └── MAIS.Modules.CrimsSeverity       # Reference module (self-contained vertical slice)
```

---

## 3. Layered Architecture

### Layer 1 — Service Shell (Server + Client)

Both the server and client are **ASP.NET Core hosted as Windows Services**. Each hosts:
- A REST API
- SignalR hubs
- Background workers (orchestrator, health reporter/monitor)
- Module DI registrations

The sidebar connects to whichever service is running **locally** and shows only the modules that local instance is allowed to run.

### Layer 2 — Modules (Plugins)

Self-contained vertical slices that plug into the framework. A module can be:
- A background polling worker
- A REST microservice adapter
- A containerised service proxy
- A message queue consumer
- A Python AI worker adapter
- A RAG/embedding pipeline adapter
- An external application endpoint

Modules declare their own `ModuleHostType` and the orchestrators filter accordingly.

---

## 4. Deployment Topology

```
┌──────────────────────────────────────────────┐
│          MAIS.Server.Service                 │
│  http://[server]:5000  https://[server]:5001  │
│                                              │
│  ┌─────────────────┐  ┌────────────────────┐ │
│  │ ModuleRegistry  │  │  ClientRegistry    │ │
│  │ (server modules)│  │  (800+ clients)    │ │
│  └─────────────────┘  └────────────────────┘ │
│                                              │
│  ┌─────────────────┐  ┌────────────────────┐ │
│  │ OrchestratorWkr │  │ HealthMonitorWkr   │ │
│  └─────────────────┘  └────────────────────┘ │
│                                              │
│  REST: /api/v1/modules  /api/v1/clients      │
│  SignalR: /hubs/status  /hubs/severity       │
└────────────────┬─────────────────────────────┘
                 │ HTTP (register / policy / status)
    ┌────────────┴──────────────────────────────┐
    │  MAIS.Client.Service  (×800+ machines)    │
    │  http://localhost:5002                    │
    │                                           │
    │  ┌───────────────────┐                   │
    │  │ ClientModuleReg.  │                   │
    │  │ (client modules)  │                   │
    │  └───────────────────┘                   │
    │                                           │
    │  ┌───────────────────┐  ┌──────────────┐ │
    │  │ ClientOrchestrator│  │HealthReporter│ │
    │  └───────────────────┘  └──────────────┘ │
    │                                           │
    │  SignalR: /hubs/severity (local)          │
    └──────────────────┬────────────────────────┘
                       │ connects to local service
              ┌────────┴──────────┐
              │  MAIS.Sidebar     │
              │  (WPF / sys tray) │
              │  ApiBaseUrl:5002  │
              └───────────────────┘
```

---

## 5. Module System

### 5.1 IModule Interface (`MAIS.Core`)

Every capability unit — background worker, external endpoint adapter, AI agent — implements `IModule`:

```csharp
public interface IModule
{
    string Id { get; }            // Stable reverse-domain ID: "mais.crims-severity"
    string DisplayName { get; }
    string Description { get; }
    string Version { get; }
    ModuleType Type { get; }      // ExternalEndpoint | Internal | Agent
    Uri? LaunchUri { get; }       // Optional deep-link URL
    ModuleHostType HostType { get; } // Client | Server | Both

    Task InitialiseAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<ModuleHealth> GetHealthAsync(CancellationToken ct);
}
```

### 5.2 Module Host Types

| Value | Where it runs |
|-------|--------------|
| `Server` | Only started by `MAIS.Server.Service` |
| `Client` | Only started by `MAIS.Client.Service` |
| `Both` | Started by whichever service loads it |

The server orchestrator filters for `Server | Both`; the client orchestrator filters for `Client | Both`. Both filter from the same DI-registered `IModule` collection.

### 5.3 Three-Point Registration Pattern

Adding a new module requires exactly **3 lines** in the host projects and **3 lines** in the sidebar:

```csharp
// MAIS.Server.Service/Program.cs or MAIS.Client.Service/Program.cs
builder.Services.AddXxxModule(builder.Configuration);   // (1) service-side DI
app.UseXxxModule();                                      // (2) middleware / hub mapping

// MAIS.Sidebar/App.xaml.cs
registry.AddXxxSidebarCard(baseUrl);                    // (3) sidebar card registration
```

The orchestrator auto-discovers all `IModule` instances from DI — no manual registry calls needed.

### 5.4 Module Auto-Discovery Flow (Server)

```
Host starts → OrchestratorWorker.ExecuteAsync()
  → GetServices<IModule>() from DI
  → Filter: HostType == Server || Both
  → Register each in ModuleRegistry
  → InitialiseAsync() on each
  → StartAsync() on each
  → Module status: Running
  → HealthMonitorWorker polls GetHealthAsync() every 30 s
```

### 5.5 Module Auto-Discovery Flow (Client)

```
Host starts → ClientOrchestratorWorker.ExecuteAsync()
  → GetServices<IModule>() from DI
  → Filter: HostType == Client || Both
  → Register each in ClientModuleRegistry
  → ConnectWithRetryAsync() — retries every 30 s until server responds
      → POST /api/v1/clients/register
      → GET  /api/v1/clients/{clientId}/policy
      → Start only modules in policy.EnabledModules[]
  → PeriodicTimer every 300 s → RefreshPolicyAsync()
      → Re-registers if disconnected
      → Re-fetches policy
      → Starts any newly-allowed modules (idempotent — skips Running/Starting)
  → HealthReporterWorker reports to server every 60 s
```

---

## 6. Policy & Role System

Roles are defined **server-side only** in `appsettings.json` under `RolePolicies`. The client declares its role in its own `appsettings.json`; the server resolves the policy.

### 6.1 Server `appsettings.json` (RolePolicies section)

```json
"RolePolicies": {
  "default": {
    "role": "Default",
    "enableSidebar": false,
    "enabledModules": []
  },
  "roles": {
    "support": {
      "role": "Support",
      "enableSidebar": true,
      "enabledModules": ["mais.crims-severity"]
    },
    "trader": {
      "role": "Trader",
      "enableSidebar": false,
      "enabledModules": ["mais.crims-severity"]
    },
    "admin": {
      "role": "Admin",
      "enableSidebar": true,
      "enabledModules": ["mais.crims-severity"]
    }
  }
}
```

### 6.2 Policy Flow

```
Client declares role in Client.UserRole (appsettings.json)
  → Registers with server (POST /api/v1/clients/register)
  → Fetches policy (GET /api/v1/clients/{id}/policy)
  → Server resolves RolePoliciesConfig.GetPolicyForRole(role)
  → Returns ClientProfile { EnableSidebar, EnabledModules[] }
  → Client starts only modules in EnabledModules
  → Refreshed every 300 s (PolicyRefreshIntervalSeconds)
```

### 6.3 ClientProfile

```csharp
public class ClientProfile
{
    public string ClientId { get; set; }
    public string Role { get; set; }
    public bool EnableSidebar { get; set; }
    public List<string> EnabledModules { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
```

---

## 7. REST API Surface

### Server (`http://[server]:5000/api/v1`)

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/modules` | List all server modules |
| `GET` | `/modules/{id}` | Get module details |
| `POST` | `/modules/{id}/start` | Start a module |
| `POST` | `/modules/{id}/stop` | Stop a module |
| `GET` | `/modules/{id}/health` | Get module health snapshot |
| `POST` | `/clients/register` | Client registration |
| `GET` | `/clients` | List all connected clients |
| `GET` | `/clients/{id}` | Get client details |
| `GET` | `/clients/{id}/policy` | Fetch role-based policy for client |
| `POST` | `/clients/{id}/status` | Client reports module health |
| `GET` | `/health` | ASP.NET health check |

### Client (`http://localhost:5002/api/v1`)

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/modules` | List modules running on this client |

---

## 8. SignalR Hubs

| Hub | URL | Hosted on | Purpose |
|-----|-----|-----------|---------|
| `StatusHub` | `/hubs/status` | Server only | Module status change broadcast |
| `CrimsSeverityHub` | `/hubs/severity` | **Both** server and client | Real-time severity data stream to sidebar |

**Important:** Both the server and client host their own `/hubs/severity`. The sidebar always connects to the **local** service's hub (same host, same port as `ApiBaseUrl`). The hub is not proxied between client and server.

---

## 9. Sidebar (MAIS.Sidebar — WPF)

- Runs as a **WPF system tray application** on the workstation.
- Connects to the **local service** (default: `http://localhost:5002`).
- Reads `ServiceConnectionOptions.ApiBaseUrl` from its own `appsettings.json`.
- On startup, queries `/api/v1/modules` for the list of running modules.
- For each module, looks up a registered `ModuleCardViewModel` factory in `ModuleCardRegistry`.
- WPF's implicit `DataTemplate` renders the matching card.
- Each module card can embed a WebView2 panel for rich HTML/JS UI.

### Sidebar Configuration (`ServiceConnectionOptions`)

```json
"ServiceConnection": {
  "ApiBaseUrl": "http://localhost:5002",
  "SignalRUrl": "https://localhost:5001",
  "Timeout": 10
}
```

`ApiBaseUrl` determines which service the sidebar connects to. Pointing it to port `5000`/`5001` connects to the server directly (for server-side deployments). Pointing it to `5002` connects to the local client service.

---

## 10. Reference Module — CrimsSeverity

The only module currently implemented. It is the template for all future modules.

### What it does

- Polls an external log aggregator (CRIMS) at `http://boswidad01:8080/api/dashboard/filterseverity` every 10 seconds.
- Receives severity counts: `CRITICAL`, `HIGH`, `WARNING`, `INFO`.
- Detects spikes in critical entries (configurable threshold and window).
- Streams data in real-time to the sidebar via SignalR.
- Sidebar displays a compact bar chart with reactive background (glows red during spikes).
- Clicking the panel opens `http://boswidad01:8080` in the browser.

### Module ID

`mais.crims-severity`

### HostType

`Both` — can run on server or client. Configured per-instance via `appsettings.json`:
- Server: `"HostType": "Both"`
- Client: `"HostType": "Client"`

### Data Flow

```
CrimsSeverityWorker (polls every 10 s)
  → HTTP GET boswidad01:8080/api/dashboard/filterseverity
  → Spike detection (SpikeWindowCycles=3, SpikeCriticalDeltaThreshold=50)
  → ISeverityReporter.ReportSeverityDataAsync()
    → SignalRSeverityReporter (used on BOTH server and client)
    → IHubContext<CrimsSeverityHub>.Clients.All.SeverityDataUpdated(update)
  ↓
CrimsSeverityHub at /hubs/severity
  ↓
Sidebar WebView2 (connected via SignalR JS client)
  → severity-panel.html updates bar chart + spike background
```

### Spike Detection

```
SpikeWindowCycles:            3 polling cycles
SpikeCriticalDeltaThreshold:  +50 critical entries within the window triggers a spike
CooldownSeconds:              120 s before spike state clears
```

### Sidebar Card

- Embedded HTML/JS panel (`severity-panel.html`) served from assembly as embedded resource.
- Loaded in WebView2 inside a WPF `UserControl`.
- Hub URL injected via `AddScriptToExecuteOnDocumentCreatedAsync` before navigation (ensures the variable is set before the page script runs).
- Bar chart built with Chart.js.
- Real-time SignalR JS client (`signalr.min.js`) — both libs served from embedded assembly.

### Module Configuration (`appsettings.json`)

```json
"Modules": {
  "CrimsSeverity": {
    "HostType": "Client",
    "DataEndpointUrl": "http://boswidad01:8080/api/dashboard/filterseverity",
    "SourceApplicationUrl": "http://boswidad01:8080",
    "PollingIntervalSeconds": 10,
    "SpikeWindowCycles": 3,
    "SpikeCriticalDeltaThreshold": 50,
    "CooldownSeconds": 120,
    "RequestTimeoutSeconds": 5
  }
}
```

### Files in the Module

```
MAIS.Modules.CrimsSeverity/
├── CrimsSeverityModule.cs          # IModule implementation (metadata + lifecycle delegation)
├── CrimsSeverityWorker.cs          # IHostedService — polling loop, spike detection
├── CrimsSeverityHub.cs             # SignalR hub + DTOs (SeverityEntry, SeverityDataUpdate)
├── CrimsSeverityOptions.cs         # Strongly-typed config
├── Reporters/
│   ├── ISeverityReporter.cs
│   ├── SignalRSeverityReporter.cs  # Used on BOTH server and client
│   └── HttpSeverityReporter.cs    # Unused / stub (kept for possible future use)
├── Extensions/
│   ├── ServiceExtensions.cs        # AddCrimsSeverityModule() + UseCrimsSeverityModule()
│   └── SidebarExtensions.cs        # AddCrimsSeveritySidebarCard()
├── Sidebar/
│   ├── CrimsSeverityCard.xaml      # WPF UserControl
│   ├── CrimsSeverityCard.xaml.cs   # Code-behind (WebView2 init + hub URL injection)
│   ├── CrimsSeverityCardViewModel.cs
│   └── CrimsSeverityResources.xaml # DataTemplate registration
└── wwwroot/
    ├── severity-panel.html
    └── libs/
        ├── chart.min.js
        └── signalr.min.js
```

---

## 11. Infrastructure

### Event Bus (`MAIS.Infrastructure`)

`InMemoryEventBus` — in-process publish/subscribe used for decoupled communication between the module registry and other components (e.g., SignalR status hub). Not persisted; not shared across processes.

### Logging

Serilog throughout. Both services and the sidebar write structured logs:
- Console sink (development)
- Rolling file sink (production)
  - Server: `logs/MAIS.Server-{date}.txt`
  - Client: `logs/MAIS.Client-{date}.txt`
  - Sidebar: `%ProgramData%/MAIS/Logs/mais-sidebar-{date}.log`

### Thread Safety

- `ModuleRegistry`: `ConcurrentDictionary` for lock-free reads; `Lock` on mutations.
- `ClientRegistry`: same pattern.
- SignalR: built-in concurrency.
- Workers: each runs in its own `BackgroundService` managed by the .NET host.

---

## 12. Network Ports

| Service | Port | Protocol |
|---------|------|----------|
| Server HTTP | 5000 | HTTP |
| Server HTTPS | 5001 | HTTPS |
| Client | 5002 | HTTP |
| Sidebar connects to | 5002 (default) | HTTP |

---

## 13. Known Deferred Items

| Item | Status | Notes |
|------|--------|-------|
| Authentication / API keys | Deliberately deferred | No auth on any endpoint currently. Planned for a future phase. |
| Policy push (server → client) | Not implemented | Policy is currently pull-only (client polls every 300 s). A SignalR push from server to client when roles change would improve responsiveness. |
| Module stop on policy revocation | Not implemented | `StartAllowedModulesAsync` is idempotent but does not stop modules removed from policy during a refresh. |
| Persistent client registry | Not implemented | `ClientRegistry` is in-memory. Server restart loses all client registrations (clients re-register on next policy refresh cycle). |
| Module hot-reload | Not implemented | Adding a new module requires a service restart. |

---

## 14. Adding a New Module (Checklist)

```
MAIS.Modules.YourModule/
├── YourModule.cs                   # implements IModule
├── YourModuleOptions.cs            # config class
├── YourModuleWorker.cs             # IHostedService
├── YourModuleHub.cs                # SignalR hub (if real-time data needed)
├── Extensions/
│   ├── ServiceExtensions.cs        # AddYourModule() + UseYourModule()
│   └── SidebarExtensions.cs        # AddYourModuleSidebarCard()
└── Sidebar/
    ├── YourModuleCard.xaml
    ├── YourModuleCard.xaml.cs
    ├── YourModuleCardViewModel.cs
    └── YourModuleResources.xaml
```

**Service registration (Program.cs — server and/or client):**
```csharp
builder.Services.AddYourModule(builder.Configuration);
// ...
app.UseYourModule();
```

**Sidebar registration (App.xaml.cs):**
```csharp
registry.AddYourModuleSidebarCard(baseUrl);
```

**appsettings.json:**
```json
"Modules": {
  "YourModule": {
    "HostType": "Client",   // Client | Server | Both
    ...
  }
}
```

**Role policies (server appsettings.json):**
```json
"roles": {
  "support": {
    "enabledModules": ["mais.crims-severity", "mais.your-module"]
  }
}
```

That is all that is required. The orchestrator discovers and starts the module automatically.
