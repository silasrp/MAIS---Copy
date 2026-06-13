# MAIS Architecture & Technology Overview

## Executive Summary

**MAIS** (Multi Agentic Intelligent Suite) is a modular, plugin-based Windows desktop application built on .NET 10. It features a backend service (ASP.NET Core) that orchestrates self-contained modules, with a WPF sidebar UI that discovers and displays module cards in real-time.

---

## Technology Stack

### Core Platform
- **.NET 10** - Target framework for all projects
- **C# 14.0** - Latest language features (file-scoped namespaces, required properties, etc.)
- **Windows-specific** - WPF for UI, Windows Services for hosting

### Backend & Services
- **ASP.NET Core** - REST API, SignalR hubs, Swagger/OpenAPI
- **SignalR** - Real-time bidirectional communication between service and sidebar
- **Serilog** - Structured logging with file/console sinks
- **Windows Service Host** - Service runs as NT service with graceful shutdown

### Frontend (Desktop)
- **WPF (Windows Presentation Foundation)** - Desktop UI framework
- **MVVM Community Toolkit** - ViewModel base classes and patterns
- **System Tray Integration** - Sidebar lives in system tray

### Data & Communication
- **JSON** (Newtonsoft.Json) - Serialization format
- **HTTP/REST** - Service-to-sidebar communication
- **SignalR Hubs** - Real-time status updates, module data streaming

### Module-Specific (CrimsSeverity Example)
- **Microsoft.Web.WebView2** - Embedded Chromium for HTML panels
- **Embedded Resources** - Static HTML/JS served from assembly

---

## Project Structure

```
MAIS/
├── src/
│   ├── MAIS.Core/                          # Core abstractions & models
│   │   ├── Abstractions/
│   │   │   ├── IModule                     # Module contract
│   │   │   ├── IModuleRegistry             # Registry contract
│   │   │   └── IEventBus                   # Event system
│   │   ├── Events/                         # Domain events
│   │   └── Models/                         # Shared domain models
│   │
│   ├── MAIS.Infrastructure/                # Infrastructure services
│   │   ├── EventBus/                       # In-memory event bus
│   │   └── Extensions/                     # DI setup helpers
│   │
│   ├── MAIS.Service/                       # Backend service
│   │   ├── Program.cs                      # Entry point, DI setup
│   │   ├── Registry/
│   │   │   └── ModuleRegistry              # Module lifecycle manager
│   │   ├── Workers/
│   │   │   ├── OrchestratorWorker          # Module startup/shutdown
│   │   │   └── HealthMonitorWorker         # Module health polling
│   │   ├── Api/
│   │   │   ├── Controllers/
│   │   │   │   └── ModulesController       # REST API for modules
│   │   │   └── Hubs/
│   │   │       └── StatusHub               # SignalR for status updates
│   │   └── Configuration/
│   │       └── MaisOptions                 # Configuration schema
│   │
│   ├── MAIS.Sidebar/                       # WPF desktop application
│   │   ├── App.xaml.cs                     # Entry point, module registration
│   │   ├── Views/
│   │   │   ├── SidebarWindow.xaml          # Main sidebar container
│   │   │   └── ModuleCard.xaml             # Card template
│   │   ├── ViewModels/
│   │   │   ├── SidebarViewModel            # Main VM (module list)
│   │   │   └── ModuleCardViewModel         # Base card VM
│   │   ├── Services/
│   │   │   ├── MaisServiceClient           # HTTP client to backend
│   │   │   └── SystemTrayService           # Tray icon/context menu
│   │   └── Infrastructure/
│   │       └── Win32/                      # Windows API interop
│   │
│   ├── MAIS.Sidebar.Abstractions/          # Sidebar contracts for modules
│   │   ├── ModuleCardRegistry              # Registry for module UI cards
│   │   ├── ModuleCardViewModelBase          # Base class for module VMs
│   │   └── IModuleControlClient            # Commands from UI to service
│   │
│   └── MAIS.Modules.CrimsSeverity/         # Example self-contained module
│       ├── Extensions/
│       │   ├── ServiceExtensions.cs        # Service-side registration
│       │   └── SidebarExtensions.cs        # Sidebar-side registration
│       ├── CrimsSeverityHub.cs             # SignalR hub for this module
│       ├── CrimsSeverityModule.cs          # Module descriptor
│       ├── CrimsSeverityWorker.cs          # Background polling worker
│       ├── CrimsSeverityOptions.cs         # Configuration (appsettings.json)
│       ├── Sidebar/
│       │   ├── CrimsSeverityCard.xaml      # Module UI card
│       │   ├── CrimsSeverityCard.xaml.cs   # Code-behind
│       │   ├── CrimsSeverityCardViewModel.cs # Card ViewModel
│       │   └── CrimsSeverityResources.xaml # DataTemplate registration
│       └── wwwroot/                        # Embedded static assets
│           ├── severity-panel.html         # Module HTML panel
│           └── libs/                       # Chart.js, SignalR.js, etc.
│
└── tests/
    ├── MAIS.Core.Tests/
    └── MAIS.Service.Tests/
```

---

## Architecture Overview

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                     Windows Service Host                        │
│  (MAIS.Service - ASP.NET Core running on localhost:5100)        │
│                                                                 │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ Orchestrator                                           │    │
│  │ • Starts/stops modules on service startup/shutdown    │    │
│  │ • Auto-discovers IModule instances from DI container  │    │
│  │ • Registers each module with ModuleRegistry           │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ Module Registry                                        │    │
│  │ • Maintains live module instances                      │    │
│  │ • Tracks module status (Running, Faulted, Stopped)    │    │
│  │ • Broadcasts ModuleStatusChanged events               │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ Modules (Plugins)                                      │    │
│  │ • CrimsSeverity: Polls CRIMS API, streams severity    │    │
│  │ • [Future modules registered via same pattern]        │    │
│  │ • Each has: Hub, Worker, Descriptor, Configuration    │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ REST API (http://localhost:5100)                       │    │
│  │ • GET  /api/modules - List all modules                │    │
│  │ • POST /api/modules/:id/start                         │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                 │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ SignalR Hubs                                           │    │
│  │ • /hubs/status - Service ↔ Sidebar (module list)      │    │
│  │ • /hubs/severity - CrimsSeverity → Sidebar (live data)│    │
│  │ • [Future module hubs registered dynamically]         │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
         ▲                                              ▲
         │ HTTP / WebSocket (SignalR)                  │
         │                                             │
┌────────┴─────────────────────────────────────────────┴──────────┐
│              WPF Desktop Application                             │
│         (MAIS.Sidebar - Windows Forms/WPF)                       │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Sidebar Window                                          │   │
│  │ • Queries service for available modules at startup      │   │
│  │ • Displays module cards in a scrollable panel           │   │
│  │ • Live-updates status via SignalR push notifications   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Module Card Registry (ModuleCardRegistry)              │   │
│  │ • Registered at App startup in App.xaml.cs             │   │
│  │ • Maps module ID → ViewModel factory + XAML template   │   │
│  │ • CrimsSeverity card auto-registered via extension     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Module Cards (WPF UserControls)                         │   │
│  │ • CrimsSeverityCard: Shows WebView2 panel + status      │   │
│  │ • Each card has ViewModel bound to service module       │   │
│  │ • LiveData via SignalR → real-time updates             │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ System Tray Integration                                 │   │
│  │ • Hide/Show window                                      │   │
│  │ • Right-click context menu                              │   │
│  │ • Minimize to tray on close                             │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## Modularity Design

### Module Contract (MAIS.Core)

```
public interface IModule
{
    string Id { get; }                          // Unique identifier
    string DisplayName { get; }                 // User-friendly name
    string Description { get; }                 // What it does
    string Version { get; }                     // Semantic version
    ModuleType Type { get; }                    // ExternalEndpoint, etc.
    Uri? LaunchUri { get; }                     // Optional launch URL
    
    Task InitialiseAsync(CancellationToken ct);  // One-time setup
    Task StartAsync(CancellationToken ct);       // Start operations
    Task StopAsync(CancellationToken ct);        // Graceful shutdown
    Task<ModuleHealth> GetHealthAsync(CancellationToken ct);
}

public enum ModuleType
{
    ExternalEndpoint,    // Polls external API
    Internal,            // Internal service
    Agent                // AI agent module
}

public enum ModuleStatus
{
    Unknown,
    Starting,
    Running,
    Degraded,
    Faulted,
    Stopping,
    Stopped
}
```

### Self-Contained Module Pattern (CrimsSeverity Example)

Each module is a complete vertical slice with **three integration points**:

#### 1. **Service Registration** (`ServiceExtensions.cs`)
```
// Called from MAIS.Service Program.cs
builder.Services.AddCrimsSeverityModule(builder.Configuration);

// What it does:
// - Reads configuration from appsettings.json
// - Creates IModule instance
// - Registers background worker (IHostedService)
// - Returns to DI container
```

#### 2. **Module Lifecycle** (Automatic via Orchestrator)
```
Service Start → OrchestratorWorker → 
  Discovers all IModule instances from DI →
    Registers each with ModuleRegistry →
      Calls InitialiseAsync() on each module →
        Calls StartAsync() on each module →
          Background workers begin operations
```

#### 3. **Sidebar UI Registration** (`SidebarExtensions.cs`)
```
// Called from MAIS.Sidebar App.xaml.cs at startup
registry.AddCrimsSeveritySidebarCard();

// What it does:
// - Registers ViewModel factory
// - Registers XAML DataTemplate
// - Sidebar auto-renders card when module list updated
```

### Module Components

#### **Module Descriptor** (`CrimsSeverityModule.cs`)
- Implements `IModule` interface
- Provides metadata (ID, name, version, type)
- Delegates lifecycle to background worker

#### **Background Worker** (`CrimsSeverityWorker.cs`)
- Implements `IHostedService`
- Injected via DI (has access to `HttpClient`, `IHubContext`, config, etc.)
- Runs async operations (e.g., polling external API every N seconds)
- Broadcasts data via SignalR to sidebar

#### **SignalR Hub** (`CrimsSeverityHub.cs`)
- Server-side hub at `/hubs/severity`
- Receives data from worker, broadcasts to connected clients
- Defines client contract (`ICrimsSeverityHubClient`)

#### **Configuration** (`CrimsSeverityOptions.cs`, `appsettings.json`)
```
{
  "Modules": {
    "CrimsSeverity": {
      "DataEndpointUrl": "http://...",
      "PollingIntervalSeconds": 10,
      "SpikeWindowCycles": 3,
      "CooldownSeconds": 120
    }
  }
}
```

#### **Sidebar Card** (`CrimsSeverityCard.xaml`)
- WPF UserControl displayed in sidebar
- Binds to `CrimsSeverityCardViewModel`
- Shows WebView2 (embedded Chromium) for HTML panel
- Receives real-time updates via SignalR connection

---

## Module Registration Flow

### Service Side (Backend)

```
Program.cs (MAIS.Service)
    ↓
builder.Services.AddCrimsSeverityModule(config)
    ├─ services.Configure<CrimsSeverityOptions>(config section)
    ├─ services.AddSingleton<IModule>(new CrimsSeverityModule(opts))
    └─ services.AddHostedService<CrimsSeverityWorker>()
    
    ↓ (continues in Program.cs)
    
builder.Services.AddSingleton<IModuleRegistry, ModuleRegistry>()
builder.Services.AddHostedService<OrchestratorWorker>()

    ↓ (Application starts)
    
OrchestratorWorker.ExecuteAsync()
    ├─ Waits for host to fully start (SignalR hubs registered, etc.)
    ├─ Discovers all IModule instances: serviceProvider.GetServices<IModule>()
    ├─ Foreach module:
    │   └─ registry.Register(module)  ← Adds to live registry
    └─ Starts all modules
        ├─ module.InitialiseAsync()
        ├─ module.StartAsync()
        └─ CrimsSeverityWorker begins polling
```

**Key Innovation:** Module auto-discovery from DI container means adding a new module is just:
1. One extension method call in `Program.cs`
2. No manual registry manipulation

### Sidebar Side (Frontend)

```
App.xaml.cs (MAIS.Sidebar)
    ↓
OnStartup()
    ├─ ConfigureServices() → builds DI container
    ├─ RegisterModuleCards(services)
    │   ├─ var registry = services.GetRequiredService<ModuleCardRegistry>()
    │   ├─ registry.AddCrimsSeveritySidebarCard()
    │   │   └─ Registers factory: (descriptor, client) → ViewModel
    │   │   └─ Registers URI: pack://application:,,,/MAIS.Modules.CrimsSeverity;component/Sidebar/CrimsSeverityResources.xaml
    │   └─ Loads all registered XAML templates into App.Resources
    └─ SidebarWindow loads and displays

    ↓ (SidebarViewModel connects to service)
    
SidebarViewModel
    ├─ Queries: await client.GetModulesAsync()
    ├─ Receives: List<ModuleDescriptor>
    └─ For each descriptor:
        ├─ Looks up ViewModel factory in registry
        ├─ Creates ViewModel instance
        └─ WPF's implicit DataTemplate renders CrimsSeverityCard
        
    ↓ (Real-time updates)
    
SignalR connection to /hubs/status
    ├─ Listens for ModuleStatusChanged events
    ├─ Updates UI in real-time (status indicators, etc.)
    └─ Each module hub (/hubs/severity) updates module-specific data
```

---

## Key Design Patterns

### 1. **Dependency Injection (DI)**
- Service Locator pattern for module discovery
- Extensions methods for configuration registration
- Lazy initialization via `IOptions<T>`

### 2. **Observer Pattern (Events)**
- `IEventBus` for inter-module communication
- `ModuleStatusChanged` events broadcast to sidebar

### 3. **Registry Pattern**
- `ModuleRegistry` - Service-side module lifecycle
- `ModuleCardRegistry` - Sidebar-side card templates

### 4. **Factory Pattern**
- Module card factories registered per module
- Late binding of UI to business logic

### 5. **Template Method**
- `IModule` defines lifecycle (Init, Start, Stop)
- Each module implements specific behavior

### 6. **Strategy Pattern**
- `ModuleType` enum (ExternalEndpoint, Internal, Agent)
- Future: different startup/health strategies per type

---

## Configuration (appsettings.json)

```
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "C:\\ProgramData\\MAIS\\Logs\\mais-service-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      },
      {
        "Name": "Console"
      }
    ]
  },
  "MAIS": {
    "LaunchSidebarOnStart": true,
    "SidebarExecutablePath": "C:\\Program Files\\MAIS\\MAIS.Sidebar.exe",
    "ModuleStartTimeoutSeconds": 30,
    "ModuleStopTimeoutSeconds": 10,
    "HealthCheckIntervalSeconds": 60
  },
  "Modules": {
    "CrimsSeverity": {
      "DataEndpointUrl": "http://boswidad01:8080/api/dashboard/filterseverity",
      "SourceApplicationUrl": "http://boswidad01:8080",
      "PollingIntervalSeconds": 10,
      "SpikeWindowCycles": 3,
      "SpikeCriticalDeltaThreshold": 50,
      "CooldownSeconds": 120,
      "RequestTimeoutSeconds": 5
    }
  }
}
```

---

## Adding a New Module (Checklist)

To add a new module (e.g., `MAIS.Modules.LogAnalyzer`):

### 1. **Create Module Project**
```
MAIS.Modules.LogAnalyzer/
├── LogAnalyzerModule.cs          # IModule implementation
├── LogAnalyzerOptions.cs         # Configuration class
├── LogAnalyzerWorker.cs          # IHostedService (background ops)
├── LogAnalyzerHub.cs             # SignalR hub for real-time data
├── Extensions/
│   ├── ServiceExtensions.cs      # AddLogAnalyzerModule()
│   └── SidebarExtensions.cs      # AddLogAnalyzerSidebarCard()
└── Sidebar/
    ├── LogAnalyzerCard.xaml      # UI card
    ├── LogAnalyzerCard.xaml.cs   # Code-behind
    ├── LogAnalyzerCardViewModel.cs
    └── LogAnalyzerResources.xaml  # DataTemplate
```

### 2. **Register with Service**
```
// Program.cs
builder.Services.AddLogAnalyzerModule(builder.Configuration);

// Add config to appsettings.json
{
  "Modules": {
    "LogAnalyzer": { ... }
  }
}
```

### 3. **Register with Sidebar**
```
// App.xaml.cs
registry.AddLogAnalyzerSidebarCard();
```

### 4. **Done!**
- Service automatically discovers and starts it
- Sidebar automatically displays card
- SignalR hubs available at `/hubs/loganalyzer`

---

## Communication Patterns

### Service → Sidebar (Real-time)
```
CrimsSeverityWorker (polls CRIMS API every 10s)
    ↓
Broadcasts via SignalR: SeverityDataUpdated(data)
    ↓
SidebarWindow subscribes to /hubs/severity
    ↓
CrimsSeverityCard receives data, updates UI
```

### Sidebar → Service (On-demand)
```
User clicks "Restart" button in card
    ↓
MaisServiceClient.RequestStartAsync(moduleId)
    ↓
HTTP POST /api/modules/:id/start
    ↓
ModulesController → OrchestratorWorker
    ↓
module.InitialiseAsync() → module.StartAsync()
```

### Module Status Updates
```
OrchestratorWorker calls module.StartAsync()
    ↓
registry.UpdateStatus(moduleId, ModuleStatus.Running)
    ↓
ModuleRegistry raises ModuleStatusChanged event
    ↓
IEventBus publishes ModuleStatusChangedEvent
    ↓
StatusHub broadcasts to all connected sidebar instances
    ↓
SidebarViewModel updates module descriptor
    ↓
UI reflects new status (green indicator, etc.)
```

---

## Thread Safety & Concurrency

- **ModuleRegistry**: Thread-safe via `Lock` on mutations, `ConcurrentDictionary` for lock-free reads
- **Modules**: Each runs in own `BackgroundService` (separate thread)
- **SignalR**: Built-in concurrency control for hub clients
- **Event Bus**: In-memory, thread-safe subscription/publish

---

## Error Handling & Resilience

- **Module startup timeout**: 30 seconds (configurable)
- **Faulted module detection**: Health monitor polls every 60 seconds
- **Graceful degradation**: Module failure doesn't crash service
- **Status events**: Sidebar notified of module faults in real-time
- **Logging**: Structured Serilog logs to file + console

---

## Future Extensibility Points

1. **New Module Types**: Add to `ModuleType` enum, implement in Orchestrator
2. **Persistence**: Swap in-memory event bus for message queue (RabbitMQ, etc.)
3. **UI Customization**: Module cards can override default XAML template
4. **Authentication**: Add security layer to service API
5. **Clustering**: Scale service across multiple instances with shared registry
6. **Dashboards**: Aggregate module data into central analytics view
7. **Agent Modules**: Integrate AI agents for autonomous operations

---

## Summary

MAIS is a **plugin architecture on .NET 10** where:

- **Backend** orchestrates self-contained module plugins
- **Modules** are vertical slices with config, service logic, UI, and real-time communication
- **Sidebar** is a thin WPF client that discovers modules and displays them as cards
- **Discovery** happens automatically via DI container (minimal boilerplate)
- **Communication** is real-time via SignalR and event bus
- **Extensibility** is baked in: add module in 3 steps, register once, done

This is production-grade, enterprise architecture suitable for teams building extensible Windows desktop applications on modern .NET.
