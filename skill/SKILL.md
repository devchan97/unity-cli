---
name: unity-cli
description: Control Unity Editor from the command line. Execute C# code, manage scenes, build projects, run tests, and automate Unity workflows. Activates when working in Unity projects or when user mentions unity-cli, Unity automation, or Unity editor control.
---

# Unity CLI - Unity Editor Control from Terminal

> Control Unity Editor directly via HTTP. No MCP server, no Node.js, no config files. Single binary + Unity package.

## Installation

### Check if installed
```bash
unity-cli version
```

### Install CLI (if not found)

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/devchan97/unity-cli/master/install.ps1 | iex
```

**Linux/macOS:**
```bash
curl -fsSL https://raw.githubusercontent.com/devchan97/unity-cli/master/install.sh | sh
```

**Go install:**
```bash
go install github.com/devchan97/unity-cli@latest
```

### Install Unity Connector Package

Add to Unity project via Package Manager or directly to `Packages/manifest.json`:
```json
"com.devchan97.unity-cli-connector": "https://github.com/devchan97/unity-cli.git?path=unity-connector"
```

Pin version: append `#v0.2.21` (or latest tag) to the URL.

### Post-Install
1. Open Unity project → Connector starts automatically
2. **Disable throttling**: Edit → Preferences → General → Interaction Mode → **No Throttling**
3. Verify: `unity-cli status`

### Update
```bash
unity-cli update          # Update to latest
unity-cli update --check  # Check only
```

## When This Skill Activates

**Explicit:** User says "/unity-cli", "use unity-cli", or mentions the tool by name.

**Intent detection:** Recognize requests like:
- "Run this in Unity" / "Execute in Unity Editor"
- "Build the Unity project"
- "Check Unity console for errors"
- "Enter play mode" / "Stop play mode"
- "Run Unity tests"
- "Profile Unity performance"
- "Reserialize assets after editing"
- Any task involving Unity Editor automation

**Project detection:** When working directory contains:
- `Assets/` folder + `ProjectSettings/` folder (Unity project)
- `Packages/manifest.json` with Unity packages
- `.unity` or `.prefab` or `.asset` files being edited

## Autonomy Rules

**Run automatically (no confirmation needed):**
- `unity-cli status` - check connection
- `unity-cli list` - discover available tools
- `unity-cli console` - read logs (read-only)
- `unity-cli console --filter error` - check for errors
- `unity-cli profiler status` - check profiler state
- `unity-cli exec "<read-only expression>"` - queries that don't modify state
  - Examples: `Time.time`, `Application.dataPath`, `GameObject.FindObjectsOfType<Camera>().Length`

**Ask before running:**
- `unity-cli editor play/stop/pause` - changes editor state
- `unity-cli editor refresh --compile` - triggers recompilation
- `unity-cli exec "<modifying code>"` - code that creates/destroys/modifies objects
- `unity-cli menu "..."` - executes menu items (side effects unknown)
- `unity-cli reserialize` - modifies asset files on disk
- `unity-cli manage_build` - build operations (long-running, resource intensive)
- `unity-cli manage_tests --action run` - test execution (may enter play mode)
- Any custom tool (unknown side effects)

## Quick Reference

### Editor Control
| Task | Command |
|------|---------|
| Check connection | `unity-cli status` |
| Enter play mode | `unity-cli editor play` |
| Enter play mode (wait) | `unity-cli editor play --wait` |
| Stop play mode | `unity-cli editor stop` |
| Toggle pause | `unity-cli editor pause` |
| Refresh assets | `unity-cli editor refresh` |
| Refresh + recompile | `unity-cli editor refresh --compile` |

### Console Logs
| Task | Command |
|------|---------|
| Read errors/warnings | `unity-cli console` |
| Read all logs | `unity-cli console --filter all --lines 50` |
| Read errors only | `unity-cli console --filter error` |
| With stack traces | `unity-cli console --stacktrace short` |
| Full stack traces | `unity-cli console --stacktrace full` |
| Clear console | `unity-cli console --clear` |

### Execute C# Code
| Task | Command |
|------|---------|
| Simple expression | `unity-cli exec "Time.time"` |
| Query objects | `unity-cli exec "GameObject.FindObjectsOfType<Camera>().Length"` |
| Multi-statement | `unity-cli exec "var go = new GameObject(\"Test\"); return go.name;"` |
| With extra usings | `unity-cli exec "World.All.Count" --usings Unity.Entities` |
| Modify settings | `unity-cli exec "PlayerSettings.bundleVersion = \"1.0\"; return PlayerSettings.bundleVersion;"` |
| Scene query | `unity-cli exec "EditorSceneManager.GetActiveScene().name" --usings UnityEditor.SceneManagement` |

### Menu Items
| Task | Command |
|------|---------|
| Save project | `unity-cli menu "File/Save Project"` |
| Refresh assets | `unity-cli menu "Assets/Refresh"` |
| Open console | `unity-cli menu "Window/General/Console"` |

### Asset Reserialize
| Task | Command |
|------|---------|
| Entire project | `unity-cli reserialize` |
| Single file | `unity-cli reserialize Assets/Prefabs/Player.prefab` |
| Multiple files | `unity-cli reserialize Assets/Scenes/Main.unity Assets/Scenes/Lobby.unity` |

### Profiler
| Task | Command |
|------|---------|
| View hierarchy | `unity-cli profiler hierarchy` |
| With depth | `unity-cli profiler hierarchy --depth 3` |
| Focus on system | `unity-cli profiler hierarchy --root SimulationSystem --depth 3` |
| Average frames | `unity-cli profiler hierarchy --frames 30 --min 0.5` |
| Filter + sort | `unity-cli profiler hierarchy --min 0.5 --sort self --max 10` |
| Enable recording | `unity-cli profiler enable` |
| Disable recording | `unity-cli profiler disable` |
| Check status | `unity-cli profiler status` |

### Camera
| Task | Command |
|------|---------|
| Take screenshot | `unity-cli manage_camera --action screenshot` |
| Screenshot to path | `unity-cli manage_camera --action screenshot --path /tmp/shot.png` |
| Camera info | `unity-cli manage_camera --action info` |
| List all cameras | `unity-cli manage_camera --action list` |
| Set camera position | `unity-cli manage_camera --action set_position --x 0 --y 5 --z -10` |
| Set camera rotation | `unity-cli manage_camera --action set_rotation --x 30 --y 0 --z 0` |

### Asset Patch
| Task | Command |
|------|---------|
| Apply text replacement | `unity-cli asset_patch --action patch --path Assets/Scenes/Main.unity --params '{"replacements":[{"old":"value: 1","new":"value: 2"}]}'` |
| Read raw asset YAML | `unity-cli asset_patch --action read --path Assets/Prefabs/Player.prefab` |
| Read with offset | `unity-cli asset_patch --action read --path Assets/Scenes/Main.unity --offset 50 --lines 30` |

### Version
| Task | Command |
|------|---------|
| CLI version | `unity-cli version` |
| Full version JSON | `unity-cli version --json` |

### Batch Commands
| Task | Command |
|------|---------|
| Run multiple commands | `unity-cli batch --commands '[{"command":"status"},{"command":"console"}]'` |
| Up to 20 commands | Commands execute in parallel on the Unity side |

### Tool Discovery
| Task | Command |
|------|---------|
| List all tools | `unity-cli list` |
| Call custom tool | `unity-cli my_tool --params '{"key": "value"}'` |

## Global Options

| Flag | Description | Default |
|------|-------------|---------|
| `--port <N>` | Override Unity instance port | auto |
| `--project <path>` | Select instance by project path | latest |
| `--timeout <ms>` | HTTP request timeout | 120000 |
| `--debug` | Log HTTP requests/responses to stderr | off |
| `--format <type>` | Output format: `json` (default), `table`, `csv` | json |

```bash
unity-cli --port 8091 editor play          # Specific port
unity-cli --project MyGame editor stop     # By project name
```

## exec: The Power Command

`exec` is the most important command. It compiles and runs arbitrary C# inside Unity Editor at runtime with full access to UnityEngine, UnityEditor, and all loaded assemblies.

**Default usings:** System, System.Collections.Generic, System.Linq, System.Reflection, UnityEngine, UnityEditor

**Expression vs Statement:**
- Single expression → auto-returns result: `unity-cli exec "Time.time"`
- Multiple statements → needs explicit `return`: `unity-cli exec "var x = 1 + 2; return x;"`

**Why this matters for AI agents:** Instead of needing 35+ predefined MCP tools, `exec` gives you infinite flexibility with a single command. Need to inspect a component? `exec`. Need to modify a prefab? `exec`. Need to query ECS entities? `exec`.

**Compilation caching:** `exec` automatically caches compiled assemblies by SHA256 hash of the code (LRU cache, max 50 entries). Repeated calls with the same code skip compilation entirely, making them ~5x faster. The cache is transparent — no user action needed.

### Common exec Patterns

**Scene inspection:**
```bash
unity-cli exec "EditorSceneManager.GetActiveScene().name" --usings UnityEditor.SceneManagement
unity-cli exec "EditorSceneManager.GetActiveScene().rootCount" --usings UnityEditor.SceneManagement
```

**GameObject queries:**
```bash
unity-cli exec "GameObject.FindObjectsOfType<Transform>().Length"
unity-cli exec "Selection.activeGameObject?.name ?? \"nothing\""
unity-cli exec "var go = GameObject.Find(\"Player\"); return go?.GetComponents<Component>().Select(c => c.GetType().Name).ToArray();"
```

**Project settings:**
```bash
unity-cli exec "PlayerSettings.bundleVersion"
unity-cli exec "PlayerSettings.productName"
unity-cli exec "EditorBuildSettings.scenes.Select(s => s.path).ToArray()"
```

**Asset operations:**
```bash
unity-cli exec "AssetDatabase.FindAssets(\"t:Prefab\").Length"
unity-cli exec "AssetDatabase.FindAssets(\"t:Scene\").Select(g => AssetDatabase.GUIDToAssetPath(g)).ToArray()"
```

## Writing Custom Tools

Create C# classes with `[UnityCliTool]` in any Editor assembly. Auto-discovered on domain reload.

```csharp
using UnityCliConnector;
using Newtonsoft.Json.Linq;

[UnityCliTool(Description = "My custom tool")]
public static class MyTool
{
    public class Parameters
    {
        [ToolParameter("Description", Required = true)]
        public string Param1 { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters);
        var val = p.GetRequired("param1");
        if (!val.IsSuccess) return new ErrorResponse(val.ErrorMessage);
        // Unity API calls here
        return new SuccessResponse("Done", new { result = "..." });
    }
}
```

**Rules:**
- Class must be `static`
- Must have `public static object HandleCommand(JObject parameters)`
- Return `SuccessResponse(message, data)` or `ErrorResponse(message)`
- Class name → snake_case = command name (e.g., `SpawnEnemy` → `spawn_enemy`)
- Override with `[UnityCliTool(Name = "my_name")]`
- Runs on Unity main thread (all Unity APIs safe)

## Agent Teams Integration

### Unity Project Development Pattern
```
Team Lead
├── scene-architect: Design + build scenes via unity-cli
├── systems-dev: Game systems + unity-cli exec for runtime verification
├── qa-agent: unity-cli console + test for quality assurance
└── build-agent: unity-cli build for automated builds
```

### Key Agent Workflows

**Verify code changes:**
```bash
unity-cli editor refresh --compile    # Recompile after code edits
unity-cli console --filter error      # Check for compilation errors
unity-cli editor play --wait          # Enter play mode
unity-cli console --filter all        # Check runtime logs
unity-cli editor stop                 # Exit play mode
```

**Iterate on gameplay:**
```bash
unity-cli editor play --wait
unity-cli exec "GameObject.Find(\"Player\").transform.position"  # Check state
unity-cli exec "Time.timeScale = 2f; return Time.timeScale;"     # Speed up
unity-cli editor stop
```

**Profile performance:**
```bash
unity-cli profiler enable
unity-cli editor play --wait
# ... let it run ...
unity-cli profiler hierarchy --frames 30 --min 0.5 --sort self
unity-cli editor stop
unity-cli profiler disable
```

## Architecture

```
Terminal / AI Agent              Unity Editor
────────────────────             ────────────
$ unity-cli <command>
    │
    ├─ reads ~/.unity-cli/instances.json
    │  → finds Unity on port 8090
    │
    ├─ checks ~/.unity-cli/status/{port}.json
    │  → waits if compiling/reloading
    │
    ├─ POST http://127.0.0.1:8090/command
    │  {"command": "...", "params": {...}}
    │                                     │
    │                              HttpServer receives
    │                              CommandRouter dispatches
    │                              Tool handler executes
    │                              (on main thread)
    │                                     │
    ├─ receives JSON response  ←──────────┘
    │  {"success": true, "message": "...", "data": {...}}
    │
    └─ prints result
```

**Key design:** Stateless HTTP. No WebSocket, no server process, no config. Unity package starts automatically. CLI discovers instances automatically.

## Error Handling

| Error | Cause | Action |
|-------|-------|--------|
| "no instances found" | Unity not running or Connector not installed | Open Unity project with Connector package |
| "not responding" | Unity frozen or throttled | Check Unity, disable editor throttling |
| "compilation error" in exec | Invalid C# code | Fix syntax, check usings |
| Timeout | Long operation | Increase `--timeout`, or use `--wait` for async ops |
| Connection refused | Wrong port or Unity restarting | Wait and retry, or check `--port` |

## Troubleshooting

```bash
unity-cli status                    # Check connection
unity-cli list                      # See available tools
unity-cli --help                    # All commands
unity-cli editor --help             # Editor subcommand help
unity-cli exec --help               # Exec help
unity-cli profiler --help           # Profiler help
```

**Multiple Unity instances:** Use `--port` or `--project` to target specific instance.
```bash
cat ~/.unity-cli/instances.json     # See all registered instances
```
