# Project Architecture & Philosophy

This document outlines the architecture and build layout of AI Usage Tracker.

## Core Philosophy: Clean Architecture

The project follows the principles of **Clean Architecture** to maintain a separation of concerns, improve testability, and ensure platform independence where possible.

## Platform baseline: .NET 10

The solution baseline is **.NET 10**. `global.json` selects SDK 10.0.300 with `latestFeature` roll-forward.

- Application projects target `net10.0`; Slim and Windows UI tests target `net10.0-windows10.0.17763.0`.
- Dependency upgrades must remain compatible with those targets.
- Test and tooling packages must not silently force the project toward a newer runtime model than the application itself.
- In particular, web-test host packages (for example `Microsoft.AspNetCore.Mvc.Testing`) should stay aligned with the application runtime unless there is an explicit decision to modernize the web test harness separately.

This rule exists to maximize compatibility and reduce accidental breakage from version skew between the runtime, hosting stack, and test infrastructure.

### 1. Separation of Intent from Implementation
We separate **What** the application does (Core) from **How** it does it (Infrastructure).
- Interfaces are defined in **Core**.
- Concrete implementations (Database, API Clients, Windows Services) are in **Infrastructure**.

### 2. Dependency Rule
Dependencies always point inwards.
- `UI`, `Monitor`, and `CLI` depend on `Infrastructure` and `Core`.
- `Infrastructure` depends on `Core`.
- `Core` has **zero** dependencies on other project layers.

---

## Project Structure

### [AIUsageTracker.Core](../AIUsageTracker.Core/)
The **Domain Layer**. It contains:
- **Models**: Plain Data Objects (DTOs) used across the entire solution (e.g., `ProviderUsage`, `AgentInfo`).
- **Interfaces**: Definitions for services (e.g., `INotificationService`, `IUsageDatabase`).
- **Shared Logic**: Utility classes that are platform-agnostic.
- **Philosophy**: This project should remain "pure" .NET without any Windows-specific or third-party library dependencies (except for basic logging or JSON).

### [AIUsageTracker.Infrastructure](../AIUsageTracker.Infrastructure/)
The **Implementation Layer**. It contains:
- **Providers**: The actual logic for connecting to AI Service APIs (OpenAI, Anthropic, etc.).
- **Data Access**: SQLite implementation using Dapper for usage history.
- **Services**: Concrete implementations of Core interfaces (e.g., `WindowsNotificationService`).
- **Configuration**: Logic for loading/saving `auth.json` and `providers.json`.
- **Philosophy**: Keep provider and persistence details out of the presentation layer. The project targets `net10.0` and guards platform-specific operations.

### [AIUsageTracker.Monitor](../AIUsageTracker.Monitor/)
The **Background Engine**.
- It runs as a low-privilege background process.
- It is responsible for periodic data collection from all AI providers.
- It exposes a **Local REST API** (typically on port 5000) that the UI and CLI consume.
- **Philosophy**: Centrally manage data collection to prevent multiple applications from hitting API rate limits simultaneously.

### [AIUsageTracker.UI.Slim](../AIUsageTracker.UI.Slim/)
The **Presentation Layer**.
- A WPF tray dashboard with provider configuration, layout preferences, and quick quota checks.
- Communicates with the **Monitor** via the local API.

### [AIUsageTracker.CLI](../AIUsageTracker.CLI/)
The **Management Tool**.
- Provides a command-line interface `act` for users who prefer the terminal.
- Allows for scripting and headless status checks.

### [AIUsageTracker.Web](../AIUsageTracker.Web/)
The **Alternative Dashboard**.
- A web dashboard at `http://localhost:5100` for inspecting usage, history, and reliability.

## Build outputs

`Directory.Build.props` enables the SDK's artifacts layout. Generated files stay under the ignored repository-root `artifacts/` directory, separated by project and configuration/runtime pivot:

```text
artifacts/
  bin/<project>/<pivot>/       compiled applications and tests
  obj/<project>/              intermediate/generated files
  publish/                    packaging staging
  test-results/               test and coverage output
dist/                         completed installers and ZIPs
```

Use the scripts' shared output resolver rather than hardcoding target-framework folders or searching recursively for a binary. The Monitor launcher uses the running app's build pivot; portable packages also support the adjacent `Monitor` component directory. Installed applications keep their existing flat layout.

Each executable has a distinct role: Slim, Monitor, Web, or CLI. Test and Seeder outputs are development artifacts. Centralizing these outputs does not combine the applications or discard build caches after every compilation.

Cleanup must target generated output only. Runtime settings and usage databases, source, fixtures, and committed screenshot baselines are not build artifacts. See [build and cleanup](build-and-cleanup.md) for commands.

---

## Why This Matters

1. **Robustness**: If the UI crashes, the Monitor continues to track data.
2. **Performance**: The UI stays responsive because heavy network/DB work is offloaded to the Monitor.
3. **Flexibility**: CLI, Slim, and Web share the same Monitor.
4. **Maintainability**: Adding a new AI provider only requires a new class in `Infrastructure`, without touching the UI logic.

