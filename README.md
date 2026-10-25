# WeldingSystem \u2014 Reference Architecture (Bosch Rexroth PRI 7000-style)

A working, buildable reference solution demonstrating the full industrial telemetry pipeline:
**Hardware (Protobuf) \u2192 WCF Duplex Service (ADO.NET/SQLite) \u2192 WPF Client (Prism Events)**

This is intentionally sized to run on a single dev machine (Windows + Visual Studio 2022) and
demonstrate every architectural seam explicitly, with comments at each seam explaining *why*,
not just *what*.

---

## Solution Structure

```
WeldingSystem.sln
\u251c\u2500 WeldingSystem.Contracts/     Proto schema + WCF duplex interfaces (shared by all projects)
\u251c\u2500 WeldingSystem.Hardware/      Console worker simulating the welding controller
\u251c\u2500 WeldingSystem.Service/       Self-hosted WCF duplex service + raw ADO.NET SQLite DAL
\u2514\u2500 WeldingSystem.Client/        WPF app, Prism IEventAggregator, duplex callback listener
```

## Architecture Flow (what actually happens end to end)

1. **WeldingSystem.Hardware** generates a `WeldingEvent` Protobuf message every ~2 seconds,
   serializes it (`ToByteArray()`), and calls the WCF duplex channel's one-way
   `PushTelemetry(WeldingEventDto)` operation over `NetTcpBinding`.
2. **WeldingSystem.Service** receives the call, writes it to SQLite via raw
   `SQLiteConnection`/`SQLiteCommand` (no EF Core, wrapped in a transaction across
   `TelemetryLogs` + `WeldingJobs` + `DeviceStatus`), then fans the same event out to every
   subscribed WPF client's callback channel (`IWeldingCallback.OnTelemetryReceived`), and
   raises `OnMachineAlarm` if the error code is non-zero.
3. **WeldingSystem.Client**'s `WeldingCallbackHandler` implements `IWeldingCallback` and, on
   receiving either callback, publishes a Prism event (`TelemetryReceivedEvent` /
   `MachineAlarmEvent`) via `IEventAggregator`.
4. Two independent ViewModels (`DashboardViewModel`, `AlarmsViewModel`) each subscribe to
   those events with `ThreadOption.UIThread` and update their own bound properties \u2014
   neither ViewModel knows the other exists, and neither knows WCF exists.

## Why these specific technology choices (for interview defense)

| Layer | Choice | Why |
|---|---|---|
| Hardware\u2192Service encoding | Protobuf | Compact, versioned, language-agnostic wire schema \u2014 decoupled from the WCF transport itself |
| Transport | WCF `NetTcpBinding` + `NetNamedPipeBinding` | TCP for remote/hardware clients, named pipe for same-box WPF client (lower overhead) |
| Duplex pattern | `CallbackContract` + `InstanceContext` | Lets the service PUSH alarms to clients instead of clients polling |
| Data access | Raw ADO.NET, no EF Core | Matches the original PRI 7000 architecture; demonstrates explicit transaction control |
| Client pub/sub | Prism `IEventAggregator` | Decouples the WCF callback handler from every ViewModel; many subscribers, zero direct references |

## Prerequisites

- Visual Studio 2022 (Windows) with **.NET desktop development** and **WCF/WF** workloads installed
- .NET Framework 4.8 Developer Pack
- NuGet restore will pull: `Google.Protobuf`, `Grpc.Tools`, `System.ServiceModel.*`, `System.Data.SQLite.Core`, `Prism.Core`, `Prism.Wpf`

> **Note on target framework:** this solution targets **net48** because `System.ServiceModel`
> (WCF) server-side hosting (`ServiceHost`) is Windows/.NET Framework-only. If you want to run
> on .NET 8, replace `WeldingSystem.Service`'s WCF hosting with **CoreWCF** (`CoreWCF.NetTcp`,
> `CoreWCF.Primitives`) \u2014 the `IWeldingService`/`IWeldingCallback` contracts are compatible with
> both, since CoreWCF implements the same `System.ServiceModel` attribute model.

## How to Run the Full Pipeline

1. **Open `WeldingSystem.sln`** in Visual Studio, let NuGet restore complete.
2. **Build the solution** (Build \u2192 Build Solution). This also runs the Protobuf code generator
   for `WeldingTelemetry.proto` in `WeldingSystem.Contracts`, producing `WeldingEvent.cs`.
3. **Start `WeldingSystem.Service` first** (right-click the project \u2192 Debug \u2192 Start new instance).
   You should see:
   ```
   WeldingService is running.
   Endpoints:
     net.tcp://localhost:9000/WeldingService
     net.pipe://localhost/WeldingServicePipe
   ```
   This also creates `welding.db` (SQLite) next to the service's executable on first run.
4. **Start `WeldingSystem.Client` second.** The WPF window opens, connects via the duplex
   channel, and calls `Subscribe("LINE3-WELDER-01")`.
5. **Start `WeldingSystem.Hardware` third.** It immediately begins generating and pushing
   simulated weld events every 2 seconds.
6. Watch the WPF **Dashboard** tile update live with Current/Voltage/State, and the **Alarm Log**
   populate whenever a simulated fault (error code 4071, ~10% of cycles) occurs \u2014 both update
   independently from the same Prism event.

To run all three as separate processes at once from Visual Studio: right-click the Solution \u2192
**Set Startup Projects** \u2192 "Multiple startup projects" \u2192 set all three (`Service`, `Client`,
`Hardware`) to **Start**, with `Service` ordered first, `Hardware` last.

## Inspecting the Data

The SQLite file is at `WeldingSystem.Service/bin/Debug/net48/welding.db`. Open it with any
SQLite browser (e.g., DB Browser for SQLite) while the service is running to see live inserts
into `TelemetryLogs`, and the upserted current state in `WeldingJobs` / `DeviceStatus`.

## What to Extend for a Real Production Version (intentionally left as an exercise)

- `WeldingService.Unsubscribe` doesn't actually remove the callback (flagged in code comments)
  \u2014 swap `ConcurrentBag<IWeldingCallback>` for a `ConcurrentDictionary<IWeldingCallback, byte>`
  per machine for O(1) removal, and prune dead channels on `CommunicationException`.
- Add a Circuit Breaker + local store-and-forward buffer in `HardwareSimulatorWorker` for when
  the service is unreachable (see your IoT prep material, Days 6/17) instead of only retrying.
- Add `[ServiceBehavior(IncludeExceptionDetailInFaults = ...)]` and typed `FaultContract`s
  instead of letting raw exceptions cross the WCF boundary.
- Swap manual composition root (`App.xaml.cs`) for `PrismApplication` + a DI container
  (`Prism.DryIoc`) once the client grows beyond two ViewModels.
