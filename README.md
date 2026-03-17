# unity-cli

[English](README.md) | [Korean](README.ko.md)

> Control Unity Editor from the command line. Built for AI agents, works with anything.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Fork notice:** This is a fork of [devchan97/unity-cli](https://github.com/devchan97/unity-cli) with additional features — tool discovery caching, exec compilation caching, exec/package timeouts, test result capture via ICallbacks proxy, batch command endpoint, `--debug` flag, and 6 new built-in tools (scene, assets, build, packages, tests, gameobject management).

**No server to run. No config to write. No process to manage. Just type a command.**

## Why this fork

The original [unity-cli](https://github.com/youngwoocho02/unity-cli) is a brilliant tool — a single binary that talks directly to Unity via HTTP. No MCP server, no config, no ceremony.

This fork was created using **Claude Code Agent Teams** to systematically improve the CLI's performance and expand its built-in toolset:

- **Phase 1**: Added 6 new tool modules (scene, assets, build, packages, tests, gameobject) — expanding from 7 to 13 built-in commands
- **Phase 2**: Performance optimizations — tool discovery caching, execution timeouts, test result capture via dynamic ICallbacks proxy, and batch command support
- **Phase 3**: Deep optimizations — exec compilation caching (SHA256 + LRU, ~5x faster repeated calls), batch parallel enqueue, `--debug` flag for HTTP request/response logging, Windows MAX_PATH fix via response files

The entire process — code generation, cross-review, and integration — was orchestrated by parallel AI agent teams, demonstrating how AI-driven development can enhance open-source tools at scale.

## Install

### Linux / macOS

```bash
curl -fsSL https://raw.githubusercontent.com/devchan97/unity-cli/master/install.sh | sh
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/devchan97/unity-cli/master/install.ps1 | iex
```

### Other options

```bash
# Go install (any platform with Go)
go install github.com/devchan97/unity-cli@latest

# Manual download (pick your platform)
# Linux amd64 / Linux arm64 / macOS amd64 / macOS arm64 / Windows amd64
curl -fsSL https://github.com/devchan97/unity-cli/releases/latest/download/unity-cli-linux-amd64 -o unity-cli
chmod +x unity-cli && sudo mv unity-cli /usr/local/bin/
```

Supported platforms: Linux (amd64, arm64), macOS (Intel, Apple Silicon), Windows (amd64).

### Update

```bash
# Update to the latest version
unity-cli update

# Check for updates without installing
unity-cli update --check
```

## Unity Setup

Add the Unity Connector package via **Package Manager → Add package from git URL**:

```
https://github.com/devchan97/unity-cli.git?path=unity-connector
```

Or add directly to `Packages/manifest.json`:
```json
"com.devchan97.unity-cli-connector": "https://github.com/devchan97/unity-cli.git?path=unity-connector"
```

To pin a specific version, append a tag to the URL (e.g. `#v0.2.21`).

Once added, the Connector starts automatically when Unity opens. No configuration needed.

### Recommended: Disable Editor Throttling

By default, Unity throttles editor updates when the window is unfocused. This means CLI commands may not execute until you click back into Unity.

To fix this, go to **Edit → Preferences → General → Interaction Mode** and set it to **No Throttling**.

This ensures CLI commands are processed immediately, even when Unity is in the background.

## Quick Start

```bash
# Check Unity connection
unity-cli status

# Enter play mode and wait
unity-cli editor play --wait

# Run C# code inside Unity
unity-cli exec "Application.dataPath"

# Read console logs
unity-cli console --filter all
```

## How It Works

```
Terminal                              Unity Editor
────────                              ────────────
$ unity-cli editor play --wait
    │
    ├─ reads ~/.unity-cli/instances.json
    │  → finds Unity on port 8090
    │
    ├─ POST http://127.0.0.1:8090/command
    │  { "command": "manage_editor",
    │    "params": { "action": "play",
    │                "wait_for_completion": true }}
    │                                      │
    │                                  HttpServer receives
    │                                      │
    │                                  CommandRouter dispatches
    │                                      │
    │                                  ManageEditor.HandleCommand()
    │                                  → EditorApplication.isPlaying = true
    │                                  → waits for PlayModeStateChange
    │                                      │
    ├─ receives JSON response  ←───────────┘
    │  { "success": true,
    │    "message": "Entered play mode (confirmed)." }
    │
    └─ prints: Entered play mode (confirmed).
```

The Unity Connector:
1. Opens an HTTP server on `localhost:8090` when the Editor starts
2. Registers itself in `~/.unity-cli/instances.json` so the CLI knows where to connect
3. Writes a heartbeat to `~/.unity-cli/status/{port}.json` every 0.5s with the current state
4. Discovers all `[UnityCliTool]` classes via reflection (cached after first scan per domain reload)
5. Routes incoming commands to the matching handler on the main thread
6. Survives domain reloads (script recompilation)

Before compiling or reloading, the Connector records the state (`compiling`, `reloading`) to the status file. When the main thread freezes, the timestamp stops updating. The CLI detects this and waits for a fresh timestamp before sending commands.

## Built-in Commands

### Editor Control

```bash
# Enter play mode
unity-cli editor play

# Enter play mode and wait until fully loaded
unity-cli editor play --wait

# Stop play mode
unity-cli editor stop

# Toggle pause (only works during play mode)
unity-cli editor pause

# Refresh assets
unity-cli editor refresh

# Refresh and recompile scripts (waits for compilation to finish)
unity-cli editor refresh --compile
```

### Console Logs

```bash
# Read error and warning logs (default)
unity-cli console

# Read last 20 log entries of all types
unity-cli console --lines 20 --filter all

# Read only errors
unity-cli console --filter error

# Include stack traces (short: filtered, full: raw)
unity-cli console --stacktrace short

# Clear console
unity-cli console --clear
```

### Execute C# Code

Run arbitrary C# code inside the Unity Editor at runtime. This is the most powerful command — it gives you full access to UnityEngine, UnityEditor, ECS, and every loaded assembly. No need to write a custom tool for one-off queries or mutations.

Single expressions auto-return their result. Multi-statement code needs an explicit `return`.

```bash
# Simple expressions
unity-cli exec "Time.time"
unity-cli exec "Application.dataPath"
unity-cli exec "EditorSceneManager.GetActiveScene().name" --usings UnityEditor.SceneManagement

# Query game objects
unity-cli exec "GameObject.FindObjectsOfType<Camera>().Length"
unity-cli exec "Selection.activeGameObject?.name ?? \"nothing selected\""

# Multi-statement (explicit return)
unity-cli exec "var go = new GameObject(\"Marker\"); go.tag = \"EditorOnly\"; return go.name;"

# ECS world inspection with extra usings
unity-cli exec "World.All.Count" --usings Unity.Entities
unity-cli exec "var sb = new System.Text.StringBuilder(); foreach(var w in World.All) sb.AppendLine(w.Name); return sb.ToString();" --usings Unity.Entities

# Modify project settings at runtime
unity-cli exec "PlayerSettings.bundleVersion = \"1.2.3\"; return PlayerSettings.bundleVersion;"
```

Because `exec` compiles and runs real C#, it can do anything a custom tool can — inspect ECS entities, modify assets, call internal APIs, run editor utilities. For AI agents, this means **zero-friction access to Unity's entire runtime** without writing a single line of tool code.

### Menu Items

```bash
# Execute any Unity menu item by path
unity-cli menu "File/Save Project"
unity-cli menu "Assets/Refresh"
unity-cli menu "Window/General/Console"
```

Note: `File/Quit` is blocked for safety.

### Asset Reserialize

AI agents (and humans) can edit Unity asset files — `.prefab`, `.unity`, `.asset`, `.mat` — as plain text YAML. But Unity's YAML serializer is strict: a missing field, wrong indent, or stale `fileID` will corrupt the asset silently.

`reserialize` fixes this. After a text edit, it tells Unity to load the asset into memory and write it back out through its own serializer. The result is a clean, valid YAML file — as if you had edited it through the Inspector.

```bash
# Reserialize the entire project (no arguments)
unity-cli reserialize

# After editing a prefab's transform values in a text editor
unity-cli reserialize Assets/Prefabs/Player.prefab

# After batch-editing multiple scenes
unity-cli reserialize Assets/Scenes/Main.unity Assets/Scenes/Lobby.unity

# After modifying material properties
unity-cli reserialize Assets/Materials/Character.mat
```

This is what makes text-based asset editing safe. Without it, a single misplaced YAML field can break a prefab with no visible error until runtime. With it, **AI agents can confidently modify any Unity asset through plain text** — add components to prefabs, adjust scene hierarchies, change material properties — and know the result will load correctly.

### Profiler

```bash
# Read profiler hierarchy (last frame, top-level)
unity-cli profiler hierarchy

# Recursive drill-down
unity-cli profiler hierarchy --depth 3

# Set root by name (substring match) — focus on a specific system
unity-cli profiler hierarchy --root SimulationSystem --depth 3

# Drill into a specific item by ID
unity-cli profiler hierarchy --parent 4 --depth 2

# Average over last 30 frames
unity-cli profiler hierarchy --frames 30 --min 0.5

# Average over a specific frame range
unity-cli profiler hierarchy --from 100 --to 200

# Filter and sort
unity-cli profiler hierarchy --min 0.5 --sort self --max 10

# Enable/disable profiler recording
unity-cli profiler enable
unity-cli profiler disable

# Show profiler state
unity-cli profiler status

# Clear captured frames
unity-cli profiler clear
```

### List Tools

```bash
# Show all available tools (built-in + project custom) with parameter schemas
unity-cli list
```

### Custom Tools

```bash
# Call a custom tool directly by name
unity-cli my_custom_tool

# Call with parameters
unity-cli my_custom_tool --params '{"key": "value"}'
```

### Status

```bash
# Show Unity Editor state
unity-cli status
# Output: Unity (port 8090): ready
#   Project: /path/to/project
#   Version: 6000.1.0f1
#   PID:     12345
```

The CLI also checks Unity's state automatically before sending any command. If Unity is busy (compiling, reloading), it waits for Unity to become responsive.

## Global Options

| Flag | Description | Default |
|------|-------------|---------|
| `--port <N>` | Override Unity instance port (skip auto-discovery) | auto |
| `--project <path>` | Select Unity instance by project path | latest |
| `--timeout <ms>` | HTTP request timeout | 120000 |
| `--debug` | Log HTTP requests and responses to stderr | off |

```bash
# Connect to a specific Unity instance
unity-cli --port 8091 editor play

# Select by project path when multiple Unity instances are open
unity-cli --project MyGame editor stop
```

Use `--help` on any command for detailed usage:

```bash
unity-cli editor --help
unity-cli exec --help
unity-cli profiler --help
```

## Writing Custom Tools

Create a static class with `[UnityCliTool]` attribute in any Editor assembly. The Connector discovers it automatically on domain reload.

```csharp
using UnityCliConnector;
using Newtonsoft.Json.Linq;

[UnityCliTool(Description = "Spawn an enemy at a position")]
public static class SpawnEnemy
{
    // Command name auto-derived: "spawn_enemy"
    // Call with: unity-cli spawn_enemy --params '{"x":1,"y":0,"z":5}'

    public class Parameters
    {
        [ToolParameter("X world position", Required = true)]
        public float X { get; set; }

        [ToolParameter("Y world position", Required = true)]
        public float Y { get; set; }

        [ToolParameter("Z world position", Required = true)]
        public float Z { get; set; }

        [ToolParameter("Prefab name in Resources folder")]
        public string Prefab { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        float x = parameters["x"]?.Value<float>() ?? 0;
        float y = parameters["y"]?.Value<float>() ?? 0;
        float z = parameters["z"]?.Value<float>() ?? 0;
        string prefabName = parameters["prefab"]?.Value<string>() ?? "Enemy";

        var prefab = Resources.Load<GameObject>(prefabName);
        var instance = Object.Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity);

        return new SuccessResponse("Enemy spawned", new
        {
            name = instance.name,
            position = new { x, y, z }
        });
    }
}
```

The `Parameters` class is optional but recommended. When present, `unity-cli list` exposes parameter names, types, descriptions, and required flags — so AI assistants can discover how to call your tool without reading the source code.

### Rules

- Class must be `static`
- Must have `public static object HandleCommand(JObject parameters)` or `async Task<object>` variant
- Return `SuccessResponse(message, data)` or `ErrorResponse(message)`
- Add a `Parameters` nested class with `[ToolParameter]` attributes for discoverability
- Class name is auto-converted to snake_case for the command name
- Override with `[UnityCliTool(Name = "my_name")]` if needed
- Runs on Unity main thread, so all Unity APIs are safe to call
- Discovered automatically on Editor start and after every script recompilation
- Duplicate tool names are detected and logged as errors — only the first discovered handler is used

## Multiple Unity Instances

When multiple Unity Editors are open, each registers on a different port (8090, 8091, ...):

```bash
# See all running instances
cat ~/.unity-cli/instances.json

# Select by project path
unity-cli --project MyGame editor play

# Select by port
unity-cli --port 8091 editor play

# Default: uses the most recently registered instance
unity-cli editor play
```

## Compared to MCP

| | MCP | unity-cli |
|---|-----|-----------|
| **Install** | Python + uv + FastMCP + config JSON | Single binary |
| **Dependencies** | Python runtime, WebSocket relay | None |
| **Protocol** | JSON-RPC 2.0 over stdio + WebSocket | Direct HTTP POST |
| **Setup** | Generate MCP config, restart AI tool | Add Unity package, done |
| **Reconnection** | Complex reconnect logic for domain reloads | Stateless per request |
| **Compatibility** | MCP-compatible clients only | Anything with a shell |
| **Custom tools** | Same `[Attribute]` + `HandleCommand` pattern | Same |

## Changelog

### v0.3.0 (2026-03-18) — Phase 3

**Added:**
- `exec` compilation caching (SHA256 + LRU, max 50 entries, ~5x faster repeated calls)
- `--debug` global flag for HTTP request/response logging to stderr
- Batch parallel enqueue optimization (N ticks → 1 tick)

**Fixed:**
- `exec` MAX_PATH error on Windows (switched to response file for assembly references)
- Missing `.meta` files for Phase 1 custom tools (6 files)

### v0.2.0 (2026-03-17) — Phase 2

**Added:**
- ToolDiscovery caching (scan once per domain reload)
- `exec` timeout (30s default, 300s max)
- ManagePackages timeout
- ManageTests ICallbacks dynamic proxy for test result capture
- `/batch` endpoint (up to 20 commands per request)

### v0.1.0 (2026-03-17) — Phase 1

**Added:**
- ManageScene, ManageAssets, ManageBuild
- ManagePackages, ManageTests, ManageGameObject
- Claude Code skill auto-install (`install-skill.ps1` / `install-skill.sh`)

## Contributing

### Local Build

```bash
# Prerequisites: Go 1.24+
git clone https://github.com/devchan97/unity-cli.git
cd unity-cli
go build -o unity-cli .

# Run
./unity-cli status
```

### Unity Connector

The C# side lives in `unity-connector/`. To develop:

1. Open a Unity project
2. In `Packages/manifest.json`, point to your local clone:
   ```json
   "com.devchan97.unity-cli-connector": "file:///path/to/unity-cli/unity-connector"
   ```
3. Edit C# code → Unity auto-recompiles → changes take effect immediately

### Writing a Custom Tool

1. Create a static class with `[UnityCliTool]` in any Editor assembly
2. Implement `public static object HandleCommand(JObject parameters)`
3. Return `SuccessResponse` or `ErrorResponse`
4. Class name auto-converts to snake_case command name
5. Run `unity-cli list` to verify discovery

See the [Writing Custom Tools](#writing-custom-tools) section for a full example.

### Project Structure

```
unity-cli/
├── main.go              # CLI entry point
├── cmd/                 # Go CLI command definitions
├── internal/            # Go internal packages
├── unity-connector/     # C# Unity package
│   └── Editor/          # Connector, tools, HTTP server
├── skill/               # Claude Code skill definition
├── install.ps1          # Windows installer
├── install.sh           # Linux/macOS installer
└── .github/workflows/   # CI/CD (tag push → auto release)
```

## Credits

Originally created by **DevBookOfArray** ([youngwoocho02](https://github.com/youngwoocho02))

Forked and extended by **devchan97** ([devchan97](https://github.com/devchan97))

## License

MIT
