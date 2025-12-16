# StripLog

StripLog is a minimal Unity logger focused on **compile-time stripping** of log calls in Release builds via C# `[Conditional]`.

## Key idea

- `Debug` / `Info` / `Warning` (and `ErrorDev`) are defined with `[Conditional]` so calls are removed by the C# compiler.
- `Error` stays always available.

## Defines

By default logs are enabled in Editor and Development builds.

- `FORCE_LOGS` — keeps stripped calls even in Release.
- `LOG_R3_SUPPORT` — enables the optional R3 sample integration (requires R3 in your project).

## Public API (runtime)

- `Log.MinLevel` — runtime filter for Editor/Dev builds.
- `Log.MuteTag(string)` / `Log.UnmuteTag(string)` / `Log.IsTagMuted(string)` — tag filtering (thread-safe).
- `Log.Handler` — pluggable output (`ILogHandler`). Null is replaced with `UnityLogHandler`.

Core methods:

- Stripped: `Log.Debug`, `Log.Info`, `Log.Warning`, `Log.ErrorDev`
- Always: `Log.Error`, `Log.Exception`

## Samples

Import via **Package Manager → StripLog → Samples**.

## Tests

The package contains Editor-only tests under `Tests/Editor`.
Run them via **Window → General → Test Runner → EditMode**.
