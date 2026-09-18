---
name: neostack-vegetation
description: Author Unreal Engine 5.8 Procedural Vegetation Editor (PVE) assets through `execute_script` - open a ProceduralVegetation asset, edit its inner PCG graph, list/add/configure Export nodes with validated export settings, generate the tree as the PV editor does and read the mesh data reaching each Export node, and trigger the editor Export command. Use for procedural trees, plants and Nanite foliage export pipelines.
---

# Procedural Vegetation through `execute_script`

Requires UE 5.8+ and the
`ProceduralVegetationEditor` plugin (plus the PCG integration). Start with:

```lua
help("Vegetation")
help("PCG")
print(vegetation_support().available, vegetation_support().reason)
```

If `vegetation_support` is not a function, the engine is older than 5.8 or the plugin
is off; there is nothing vegetation-specific to call.

## What a vegetation asset is

`UProceduralVegetation` owns an embedded `UProceduralVegetationGraph`
(`<asset>:ProceduralVegetationGraph`), a `UPCGGraph` subclass that is never
"standalone". `open_asset(path)` on the asset returns a handle whose PCG verbs
(`list("nodes")`, `configure("node", ...)`, `add("parameter", ...)`, `list("edges")`)
run on that inner graph, and whose vegetation verbs manage Export nodes,
generation and export. `add_node/connect/read_graph/delete_node` take the asset
path and resolve to the inner graph.

## End-to-end: new asset, export node, generate, read data back

```lua
local slug = "PVE_R1"                      -- unique per run
local asset = "/Game/NeoStackSkillRuns/" .. slug .. "/PV_" .. slug
assert(not asset_exists(asset), "pick a new slug")
local veg = assert(create_asset(asset, "ProceduralVegetation"))
local info = veg:info()
assert(info.is_vegetation and info.has_graph, "inner graph created")

-- Build the growth graph with the PCG schema titles the PV editor exposes; discover them first:
for _, t in ipairs(veg:list("node_types", { query = "Grow" })) do print(t.display_name) end

-- One Export node, validated before it is spawned (folder must be a long package path).
local exp = assert(veg:add("export_node", {
  mesh_name = "Tree_" .. slug,
  folder = "/Game/NeoStackSkillRuns/" .. slug .. "/Exports",
  replace_policy = "Append",          -- Append | Replace | Ignore
  mesh_type = "StaticMesh",           -- StaticMesh | SkeletalMesh (SkeletalMesh adds wind bones)
  nanite_foliage = true,
  x = 1200, y = 0,
}))
print(exp.handle, exp.index, exp.params.valid, exp.params.error)

-- Wire the mesh output of the last growth node into the Export node with the ordinary connect():
-- connect(last_node.handle, "Out", exp.handle, "In")

assert(veg:save())
```

Generate exactly like the PV editor (execution source, not a level component) and
poll the shared PCG job globals:

```lua
local veg = assert(open_asset("/Game/NeoStackSkillRuns/PVE_R1/PV_PVE_R1"))
local job = assert(veg:generate({ seed = 42, inspect = true }))
local status
for _ = 1, 720 do
  status = assert(pcg_generation_status(job.id))
  if status.finished then break end
  playtest_wait(0.25)
end
print(status.status, status.elapsed_seconds, status.error)
local data = veg:list("export_data", { job = job.id })
for _, item in ipairs(data.items) do
  print(item.title, item.has_mesh_data, item.has_foliage)
  for group, count in pairs(item.groups) do print("  group", group, count) end
end
```

`status.status` is `completed`, `aborted` (see `error`) or `cancelled`
(`pcg_generation_cancel(id)`). Tree growth can take minutes; keep polling in
fresh `execute_script` calls rather than blocking one call.

## Export

`veg:export()` dispatches the PV editor's own **Export** command (context
`PVPCGEditor`), which opens Epic's export dialog and runs `FPVExporter`. It needs
the editor open and an interactive session:

```lua
local veg = assert(open_asset("/Game/NeoStackSkillRuns/PVE_R1/PV_PVE_R1"))
open_editor("/Game/NeoStackSkillRuns/PVE_R1/PV_PVE_R1")
local r = veg:export()
print(r.ok, r.reason, r.hint)
```

Headless or unattended sessions get `{ok=false, reason="editor_only"}`; a closed
editor gets `editor_not_open`; invalid Export nodes get `invalid_export_params`
with an `invalid` list. There is no headless exporter: the export code is
private engine editor code.

## Configure existing Export nodes

```lua
for _, e in ipairs(veg:list("export_nodes")) do
  print(e.index, e.mesh_name, e.folder, e.replace_policy, e.mesh_type, e.valid, e.error)
end
assert(veg:configure("export_node", {
  index = 0,
  mesh_type = "SkeletalMesh",
  collision_generation = "TrunkOnly",       -- None | TrunkOnly | AllGenerations
  wind_settings = veg:list("wind_settings")[1].path,
}))
```

Every key is validated on a copy and the engine's `FPVExportParams::Validate`
runs before anything is written; the first bad key refuses the whole call and the
node is untouched.

## Failure modes

| Symptom | Cause and response |
| --- | --- |
| `vegetation_support` is nil | UE 5.7 or older, or `ProceduralVegetationEditor` disabled. Nothing to do from Lua. |
| `create_asset(path, "ProceduralVegetation")` returns nil | Path exists or the factory alias is unavailable; choose a new slug and check `vegetation_support().available`. |
| `add("export_node")` / `configure("export_node")` returns nil | Read the `[FAIL]` line: unknown key, bad enum name, empty `mesh_name`, relative folder, or a Replace conflict reported by `FPVExportParams::Validate`. |
| `generate()` returns nil with "usage" text | You called the plain PCG `generate_standalone` on the inner graph path; use the vegetation handle's `generate()` (it applies the PVE bypass). |
| `list("export_data")` has no groups | Generate with `inspect = true`, wait for `finished`, and make sure the Export node's input is wired. |
| `export()` -> `editor_only` | Unattended session; run from an interactive editor with the PV editor open. |
| `read_graph(asset)` / `add("export_node")` opened the PV editor | The inner graph had no editor graph yet; the resolver opens the asset editor once. Call `veg:close_editor()` before `delete_asset` or rollback-heavy work. |
| `connect(last.handle, "Out", exp.handle, "In")` refuses | Handles must come from one editor graph. Always address the vegetation **asset** path (`add_node(asset, ...)`, `veg:add(...)`), never the `<asset>:ProceduralVegetationGraph` subobject path, so every spawn goes through the vegetation resolver. |

## Discovery escape hatches

- `veg:help()` - vegetation verbs (including the delegated `remove`, `generate_report`, the
  `generate_standalone` alias and `close_editor`); `help("PCG")` - inner graph verbs.
- `veg:info()` - export node counts and the inner `pcg` summary.
- `veg:list("presets")` / `veg:list("wind_settings")` - shipped PVE content
  (`/ProceduralVegetationEditor/SampleAssets/...`).
- `vegetation_support().sample_asset` - `PVE_Sample_Plant_01`, a complete graph to study.
- `report_issue("...")` after a minimal fresh-call reproduction.
