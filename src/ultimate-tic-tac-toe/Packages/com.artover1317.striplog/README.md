# StripLog

Minimal Unity logger focused on **compile-time stripping** of log calls in Release builds via C# `[Conditional]`.

## Why StripLog?

Most logging solutions (Serilog, NLog, ZLogger) focus on **runtime filtering** — the calls remain in your build and are checked at runtime.

StripLog focuses on **compile-time stripping** — `Debug`/`Info`/`Warning` calls are removed from Release builds by the C# compiler. That means no runtime checks and no argument evaluation for those calls.

Notes:
- This applies to **stripped calls** (`Debug`/`Info`/`Warning`, and `ErrorDev` if you use it). `Error` is intended to stay always available.
- If you define `FORCE_LOGS`, stripped calls are kept even in Release.

## Quick Start

```csharp
using StripLog;

// Simple logging with tags
Log.Info("UI", "Button clicked");
Log.Warning("Network", "High latency detected");
Log.Error("Core", "Critical failure"); // Always logged, even in Release

// For heavy operations, use lazy evaluation
Log.Debug("State", () => $"Data: {JsonUtility.ToJson(bigObject)}");
```

## API overview

Configuration:

- `Log.MinLevel` — runtime filter (Editor/Dev builds).
- `Log.MuteTag("Tag")` / `Log.UnmuteTag("Tag")` / `Log.IsTagMuted("Tag")` — tag filtering (thread-safe).
- `Log.Handler` — replace output destination (`ILogHandler`). If set to null, falls back to `UnityLogHandler`.

Calls:

- Stripped: `Log.Debug`, `Log.Info`, `Log.Warning`, `Log.ErrorDev`
- Always available: `Log.Error`, `Log.Exception`

## Custom output (ILogHandler)

```csharp
Log.Handler = new UnityLogHandler();
Log.MinLevel = LogLevel.Info;

Log.MuteTag("Spam");
Log.Info("Core", "Hello");
```

## Defines Behavior

| Build Type | Debug/Info/Warning/ErrorDev | Error |
|------------|-------------------|-------|
| Editor | ✅ Logged | ✅ Logged |
| Development Build | ✅ Logged | ✅ Logged |
| Release Build | ❌ **Stripped** | ✅ Logged |
| Release + `FORCE_LOGS` | ✅ Logged | ✅ Logged |

## What StripLog Does NOT Solve

- ❌ Structured logging — use Serilog/ZLogger
- ❌ Remote/network logging — use Sherlog
- ❌ In-game console/viewer — use UnityIngameDebugConsole
- ❌ Analytics integration

## Installation

### Local (this repo)

Package is already at `Packages/com.artover1317.striplog/`.

### Git URL

```
https://github.com/ArtOver1317/striplog.git?path=Packages/com.artover1317.striplog
```

## Samples

Import via Package Manager → StripLog → Samples:

- **Basic Tags** — organizing log tags as constants
- **Colored Output** — custom handler with Editor colors
- **ScriptableObject Settings** — configure via Inspector
- **R3 Integration** — observable log stream

## License

MIT

## Running tests

This package contains Editor-only tests.

- Unity: **Window → General → Test Runner → EditMode**
- Assembly: `StripLog.Tests.Editor`
