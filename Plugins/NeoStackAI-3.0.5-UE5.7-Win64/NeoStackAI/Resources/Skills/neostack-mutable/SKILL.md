---
name: neostack-mutable
description: Create, compile, instance and bake Mutable Customizable Objects (character customization) through `execute_script`. Use when a task mentions Mutable, Customizable Object, CustomizableObjectInstance, character customization parameters, or baking a customized character to skeletal mesh / material / texture assets.
---

# Mutable through `execute_script`

Start with `help("Mutable")` (do not wrap it in `log()`), then `mutable_support()`.
Every mutable verb reports `[OK]` / `[FAIL]` / `[WARN]` lines; a `nil` or `false`
return always comes with a `[FAIL]` line naming the reason.

Requirements: the `Mutable` engine plugin enabled, UE 5.7 or newer. When the
globals are missing (`type(mutable_compile) ~= "function"`) the integration is
a stub on this project.

## Mental model

1. A **Customizable Object** (`UCustomizableObject`) is the asset with the graph.
   It must be **compiled** before anything else works: uncompiled objects have
   no parameters, and instance writes are refused.
2. An **instance** (`UCustomizableObjectInstance`) holds parameter values.
   Instances from `create_instance` are transient and rooted by the binding;
   keep the `id` and call `mutable_release(id)` when done.
3. An **update** generates the skeletal meshes for the current parameters.
4. A **bake** saves those meshes, materials and textures as real assets on disk.
   Bakes are not undoable and the engine runs one at a time.

Compile, update and bake are **jobs**: they return `{id, kind, status}`, and
`{wait=true}` yields until they finish (or `timed_out=true` after the timeout).

## Workflow: compile, customize, update, bake

```lua
local co_path = "/Game/Characters/CO_Hero"          -- an existing Customizable Object
local co = open_asset(co_path)
assert(co, "open the Customizable Object")

-- 1. compile (skips when already up to date)
local compile = co:compile({ wait = true, timeout = 600, skip_if_compiled = true })
assert(compile and (compile.status == "completed" or compile.status == "skipped"),
  "compile: " .. tostring(compile and compile.error))

-- 2. discover parameters and pick values
for _, p in ipairs(co:list("parameters")) do
  log(p.name .. " : " .. p.type .. (p.enum_values and (" " .. table.concat(p.enum_values, "|")) or ""))
end

-- 3. instance + atomic parameter write (one bad key refuses the whole call)
local inst = assert(co:create_instance())
assert(inst:set_parameters({ Hair = "Long", Height = 0.7, HasCape = true }))
assert(inst:get_parameter("Hair") == "Long")

-- 4. generate meshes
local update = inst:update({ wait = true, timeout = 300 })
assert(update and update.update_result == "Success" or update.update_result == "Warning",
  "update: " .. tostring(update and update.update_result))
log(inst:skeletal_mesh().path)   -- transient generated mesh

-- 5. bake to disk (not undoable; one bake at a time)
local bake = inst:bake({ output_path = "/Game/Characters/Baked/Hero", base_name = "Hero", wait = true, timeout = 900 })
assert(bake and bake.bake_success, "bake: " .. tostring(bake and bake.error))
for _, pkg in ipairs(bake.saved_packages) do log(pkg.prefix .. " " .. pkg.asset_path) end

mutable_release(inst.id)
```

## Workflow: create a new Customizable Object

```lua
local co = mutable_new("/Game/Characters/CO_New", { save = true })
log(co:info().compiled)                       -- false: only the base object node exists
co:configure("settings", { bEnableMeshCache = true, MeshCompileType = "LocalAndChildren" })
```

Graph authoring (adding component, material or mesh nodes) is UI-only: the node
classes are private to the engine's editor module, so a `mutable_new` object
cannot be made compilable from Lua. Use it to create the asset shell and set
compile options; author the graph in the Customizable Object editor.

## Value shapes

| type | write | read |
| --- | --- | --- |
| Bool | `true` | boolean |
| Int | option name `"Long"` (see `list("enum_values", {parameter=...})`) | string |
| Float | `0.5` | number |
| Color | `{r=,g=,b=,a=}` or `{x=,y=,z=,w=}` (Vector parameters share the Color type) | table `{r,g,b,a}` with `x,y,z,w` aliases |
| Transform | `{location={x,y,z}, rotation={pitch,yaw,roll}, scale={x,y,z}}` | table |
| Texture / SkeletalMesh / Material | asset path string | asset path |
| Projector | `{position=,direction=,up=,scale=,angle=}` | table with `type` |

Multidimensional parameters take `{range_index=n}`.

## Failure modes

| symptom | cause / fix |
| --- | --- |
| `set_parameter` -> `not compiled` | Compile first (`co:compile({wait=true})`); the engine silently ignores writes on uncompiled objects, so the binding refuses them. |
| `update()` -> `not compiled` | Same; pass `allow_auto_compile=true` only if you want the engine's `ErrorUncompiled` result. |
| job `timed_out=true` | The job keeps running; poll `mutable_job_status(id)` later. |
| `mutable_job_cancel` -> `compile requests are queued` | `CancelCompileRequests` cancels every pending compile in the editor; pass `{force=true}` knowingly. |
| `bake()` -> `bake job N is still running` | One bake at a time; wait for it. |
| `bake()` -> `single-flight bake guard ... editor restarts` | A previous bake aborted after its compile stage (engine keeps the guard set). Restart the editor; only bake objects that compiled successfully. |
| `list("parameters")` empty with `[WARN]` | Object not compiled. |
| `mutable_instance(id)` nil | The instance was released or the registry (32 rooted instances) was cleared; create a new one. |

## Discovery escape hatches

- `help("Mutable")`, `co:help()`, `inst:help()`, `co:info()`, `inst:info()`.
- `mutable_jobs()` lists every retained job; `mutable_support()` reports the
  system state, whether a bake is in flight and the bake-guard state.
- Anything BlueprintCallable that the handles do not wrap is reachable through
  `invoke(open_asset(path), "FunctionName", {...})`.
