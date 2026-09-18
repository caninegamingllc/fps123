---
name: neostack-pcg
description: Create, edit, generate, inspect, and visually verify Unreal Engine PCG graphs and their level components through `execute_script`. Use for procedural point generation, PCG graph nodes and edges, graph parameters or instances, component assignment, regeneration, and debugging empty PCG output.
---

# Authoring PCG through `execute_script`

Use Lua against the live editor. Start with:

```lua
help("PCG")
help("AddNode")
help("Connect")
```

Do not wrap `help(...)` in `log(...)`; help already prints its result.

Lua state is fresh on every `execute_script` call. Keep asset paths and actor
labels as literal strings, then re-open the graph and level in each pass.
Node handles persist because they are editor GUIDs, but discovering them again
with `read_graph` or `pcg:list("nodes")` is usually clearer.

## Work in four passes

1. Create a unique graph and map, then author nodes and edges.
2. Place a bounded host actor, add a `PCGComponent`, and assign the graph.
3. Re-open everything in a fresh call and verify properties, edges, and component
   assignment before generation.
4. Generate, wait for fresh component state, and inspect multiple screenshots.
   Change one node property, regenerate, and prove the visual output changes.

Never accept `generate()`'s returned count as render proof. It counts matching
components that were triggered; UE schedules their work asynchronously. Confirm
fresh `generated=true` state and read the returned images.

## End-to-end visual proof

Choose a run-specific slug once. Do not reuse another agent's path:

```lua
local slug = "PCG_GridProof_R7A" -- replace with a unique task/agent/round slug
local root = "/Game/NeoStackSkillRuns/" .. slug
local graph_path = root .. "/G_" .. slug
local level_path = root .. "/L_" .. slug

assert(not asset_exists(graph_path), "graph path already exists; choose a new slug")
assert(not asset_exists(level_path), "level path already exists; choose a new slug")
assert(create_level(level_path, { open = true }))

local graph = assert(create_asset(graph_path, "PCGGraph"))

local function first_pin(node, field)
  local pins = assert(node[field], "missing " .. field)
  local pin = assert(pins[1], "no pin in " .. field)
  if type(pin) == "table" then
    return assert(pin.name or pin.label or pin.pin or pin.display_name)
  end
  return pin
end

local grid = assert(add_node(graph_path, "Create Points Grid", -300, 0))
local debug = assert(add_node(graph_path, "Debug", 250, 0))
assert(connect(
  grid.handle, first_pin(grid, "pins_out"),
  debug.handle, first_pin(debug, "pins_in")
))

local function node_at(nodes, x, y)
  for _, node in ipairs(nodes) do
    if node.x == x and node.y == y then return node end
  end
end

local grid_entry = assert(node_at(graph:list("nodes"), -300, 0))
local debug_entry = assert(node_at(graph:list("nodes"), 250, 0))

-- Configure one property per call. A warning on one key must not be hidden by
-- another successful key in the same configure call.
assert(graph:configure("node", { index = grid_entry.index, title = "Proof Grid" }))
assert(graph:configure("node", {
  index = grid_entry.index,
  GridExtents = "(X=400,Y=400,Z=0)",
}))
assert(graph:configure("node", {
  index = grid_entry.index,
  CellSize = "(X=160,Y=160,Z=100)",
}))
assert(graph:configure("node", {
  index = grid_entry.index,
  PointSteepness = 1.0,
}))
assert(graph:configure("node", { index = debug_entry.index, title = "Proof Preview" }))
assert(graph:save())

local level = assert(open_level())
local host_label = slug .. "_BoundsHost"
local component_name = slug .. "_PCG"

-- The StaticMeshActor's registered primitive supplies finite actor bounds to
-- the PCG component. A bare Actor with only a scene root does not.
assert(level:add("actor", {
  mesh = "/Engine/BasicShapes/Cube",
  label = host_label,
  location = { x = 0, y = 0, z = -60 },
  scale = { x = 10, y = 10, z = 0.2 },
}))
assert(add_component(host_label, {
  type = "/Script/PCG.PCGComponent",
  name = component_name,
}))
assert(configure_component(host_label, component_name, {
  property = {
    bActivated = "True",
    bIsComponentPartitioned = "False",
    GenerationTrigger = "GenerateOnDemand",
    InputType = "Actor",
    bParseActorComponents = "True",
  },
}))
assert(invoke(
  { actor_label = host_label, component = component_name },
  "SetGraph",
  { { path = graph_path } }
))
assert(level:save())
```

`Create Points Grid` produces real point data. The UE 5.8 `Debug` element
materializes that data in the editor using PCG's debug point mesh. This is a
deliberate proof sink: it makes an otherwise data-only graph visible without
requiring a preconfigured Static Mesh Spawner selector.

### Verify the mutation and assignment in a fresh call

```lua
local slug = "PCG_GridProof_R7A"
local root = "/Game/NeoStackSkillRuns/" .. slug
local graph_path = root .. "/G_" .. slug
local level_path = root .. "/L_" .. slug
local host_label = slug .. "_BoundsHost"
local component_name = slug .. "_PCG"

assert(load_level(level_path))
local graph = assert(open_asset(graph_path))

local function find_node(title)
  for _, node in ipairs(graph:list("nodes")) do
    if node.title == title then return node end
  end
end

local function property(node, name)
  for _, entry in ipairs(node.properties or {}) do
    if entry.name == name then return entry.value end
  end
end

local grid = assert(find_node("Proof Grid"))
local debug = assert(find_node("Proof Preview"))
local extents = assert(property(grid, "GridExtents"))
local cell_size = assert(property(grid, "CellSize"))
local steepness = assert(property(grid, "PointSteepness"))
assert(string.find(extents, "X=400", 1, true))
assert(string.find(extents, "Y=400", 1, true))
assert(string.find(cell_size, "X=160", 1, true))
assert(string.find(cell_size, "Y=160", 1, true))
assert(tonumber(steepness) == 1.0)

local edges = graph:list("edges")
local connected = false
for _, edge in ipairs(edges) do
  if edge.from_node == "Proof Grid" and edge.to_node == "Proof Preview" then
    connected = true
  end
end
assert(connected, "fresh PCG edge readback is missing")

local components = graph:list("components")
assert(#components == 1, "expected exactly one component using this unique graph")
local component = components[1]
assert(component.actor_label == host_label)
assert(component.component_name == component_name)
assert(string.find(component.graph_path, graph_path, 1, true))
assert(component.activated == true)
assert(component.generation_trigger == "GenerateOnDemand")
assert(component.input_type == "Actor")
assert(component.parse_actor_components == true)

local level = assert(open_level())
local hosts = level:list("actors", { name = host_label })
assert(#hosts == 1, "bounded host actor is missing")
print("verified", extents, cell_size, component.generated)
```

This fresh read is the mutation gate. Do not generate if the values or edge are
wrong; fix the authoring call first.

### Capture before, dense, and sparse states

Load the level once and capture the untouched baseline before generation. This
should show one finite bounds host but no repeated point grid:

```lua
assert(load_level("/Game/NeoStackSkillRuns/PCG_GridProof_R7A/L_PCG_GridProof_R7A"))
return screenshot({
  mode = "level",
  location = { x = -1200, y = -1200, z = 900 },
  rotation = { pitch = -32, yaw = 45, roll = 0 },
  fov = 58,
  view_mode = "wireframe",
  hide_overlays = true,
  max_dimension = 1600,
  wait_for_ready_ms = 1800,
})
```

Read the returned image. Then trigger the dense state without reloading the
level:

```lua
local graph = assert(open_asset(
  "/Game/NeoStackSkillRuns/PCG_GridProof_R7A/G_PCG_GridProof_R7A"
))
assert(graph:generate(true) == 1)
```

Generation is asynchronous. In subsequent fresh calls, poll without busy
waiting:

```lua
local graph = assert(open_asset(
  "/Game/NeoStackSkillRuns/PCG_GridProof_R7A/G_PCG_GridProof_R7A"
))
local components = graph:list("components")
assert(#components == 1)
print("generated", components[1].generated)
```

Once it prints `true`, capture in a fresh Lua call but **do not call
`load_level` again**. Loading the map resets the editor-only PCG preview and
can change `generated` back to false. Repeat the same Wireframe camera directly:

```lua
return screenshot({
  mode = "level",
  location = { x = -1200, y = -1200, z = 900 },
  rotation = { pitch = -32, yaw = 45, roll = 0 },
  fov = 58,
  view_mode = "wireframe",
  hide_overlays = true,
  max_dimension = 1600,
  wait_for_ready_ms = 1800,
})
```

Read the image and verify a regular, centered grid of debug boxes is visible
above the host. Wireframe is deliberate: the Debug boxes touch at full point
scale, so Unlit renders them as one solid white slab and hides the density
difference. The trigger count and `generated=true` are not substitutes for the
image.

Now change exactly one variable:

```lua
local graph = assert(open_asset(
  "/Game/NeoStackSkillRuns/PCG_GridProof_R7A/G_PCG_GridProof_R7A"
))
local grid
for _, node in ipairs(graph:list("nodes")) do
  if node.title == "Proof Grid" then grid = node end
end
assert(grid)
assert(graph:configure("node", {
  index = grid.index,
  CellSize = "(X=320,Y=320,Z=100)",
}))
assert(graph:save())
```

Verify `CellSize` contains `X=320` and `Y=320` in a fresh call, then call
`generate(true)`, poll fresh component state, and capture the same Wireframe
camera again without reloading the level. Read both generated images side by
side: the second state must be visibly sparser while retaining the same
extents, color family, scale, and center. Capture one alternate Wireframe
viewpoint as a final occlusion check:

```lua
return screenshot({
  mode = "level",
  location = { x = 1200, y = -1200, z = 700 },
  rotation = { pitch = -27, yaw = 135, roll = 0 },
  fov = 58,
  view_mode = "wireframe",
  hide_overlays = true,
  max_dimension = 1600,
  wait_for_ready_ms = 1800,
})
```

Reject blank, unchanged, off-camera, or overlay-obscured captures. Judge gross
correctness: output exists, its density changes in the expected direction, its
scale fits the host bounds, and it is centered where authored.

## Graph operations and readback

PCG graph structure uses the ordinary graph functions:

```lua
local node = add_node(graph_path, "Surface Sampler", 0, 0)
local ok = connect(node_a.handle, output_pin, node_b.handle, input_pin)
local graph_state = read_graph(graph_path)
local ok = delete_node(node.handle)
```

Pass display pin names returned by `pins_in` and `pins_out`; do not guess them.
`read_graph(path)` returns `nodes` and top-level `connections`. The PCG-specific
`pcg:list("edges")` reads backing `UPCGEdge` state. For important edits, check
both views in a fresh call.

Use `pcg:list("node_types", { query = "grid" })` when a schema action name is
uncertain. Use `pcg:list("nodes")` to get each node's zero-based `index`,
settings class, enabled/debug state, editor position, and editable properties.
Lua arrays are one-based, but `configure("node", {index=...})` expects the
returned zero-based `entry.index`.

Add graph parameters through the PCG object:

```lua
assert(pcg:add("parameter", {
  name = "Density",
  type = "Float",
  value = 4.0,
}))
assert(pcg:configure("parameter", {
  name = "Density",
  value = 8.0,
}))
```

Re-open the graph and read `pcg:list("parameters")` before relying on the value.

## Standalone graphs without a component (UE 5.8+)

UE 5.8 lets a graph run through the PCG execution-source API instead of a
placed `UPCGComponent`. The graph's `GraphUsageContext` decides the source:
`Asset` runs on the engine subsystem with no world (procedural assets, data
pipelines); `Level` binds the current editor world's PCG subsystem, the same
object the PCG editor's content-browser "Execute" action uses. `Standard`
graphs are refused with a hint, so set the usage first:

```lua
local pcg = open_asset("/Game/Proc/PCG_Scatter")
assert(pcg:configure("graph", { GraphUsageContext = "Asset" }))
assert(pcg:info().is_standalone)
local job = assert(pcg:generate_standalone({ seed = 7 }))
local status
for _ = 1, 240 do
  status = assert(pcg_generation_status(job.id))
  if status.finished then break end
  playtest_wait(0.25)
end
assert(status.status == "completed", status.error)
print(status.point_total, #status.outputs)   -- outputs[i]: pin, data_class, data_type, point_count, tags
```

`status.status` is `running`, `completed`, `aborted` (no output collection;
`error` explains) or `cancelled`. `pcg_generation_cancel(id)` returns false for
unknown or finished jobs, and `pcg_generation_jobs()` lists retained jobs.
Wire something into the graph's `Output` node: a standalone graph with nothing
connected completes with zero outputs, which is the engine contract, not a
failure. Only point data reports `point_count`; other data reports its class.
On UE 5.7 and older `generate_standalone` fails with the version gate and
`info().standalone_generation_available` is false.

## Scatter meshes on a Mesh Terrain (PCG Mesh Partition interop)

The `PCGMeshPartitionInterop` plugin adds `Mesh Partition Query` (outputs the
terrain as PCG surface data). Sample it, spawn meshes, and read the result
back through the graph. Verified recipe (80 rocks scattered on a Mesh Terrain,
80/80 within 100 cm of the terrain collision):

```lua
local G = "/Game/Proc/PCG_TerrainRocks"
local g = create_asset(G, "PCGGraph")
local q = add_node(G, "Mesh Partition Query", 0, 0)
local s = add_node(G, "Surface Sampler", 400, 0)
local m = add_node(G, "Static Mesh Spawner", 800, 0)
assert(connect(q.handle, "Out", s.handle, "Surface"))
assert(connect(s.handle, "Out", m.handle, "In"))
local nodes = g:list("nodes")
local function idx(title) for _, n in ipairs(nodes) do if n.title == title then return n.index end end end
assert(g:configure("node", { index = idx("Surface Sampler"), PointsPerSquaredMeter = 0.0004 }))
-- Several Mesh Partition actors can overlap (terrain + backdrop): pin the query to one.
assert(g:configure("node", { index = idx("Mesh Partition Query"),
  QueryParams = string.format("(MegaMeshOverride=%q)", terrain_actor_path) }))
assert(g:configure("node", { index = idx("Static Mesh Spawner"), mesh = "/Game/StarterContent/Props/SM_Rock" }))
assert(g:save())
```

Host the component on an actor with real bounds. PCG silently skips a
component whose actor has no bounds, and a `PCGVolume` spawned from class has
no brush geometry, so use a box component sized from the terrain source
bounds (`open_mesh_terrain_source(id):info().bounds`):

```lua
local level = open_level()
level:add("actor", { class = "/Script/Engine.Actor", label = "RockScatter", location = center })
add_component("RockScatter", { type = "/Script/Engine.BoxComponent", name = "Bounds" })
configure_component("RockScatter", "Bounds", { property = { BoxExtent = "(X=24480,Y=24480,Z=6500)" } })
add_component("RockScatter", { type = "/Script/PCG.PCGComponent", name = "PCG" })
configure_component("RockScatter", "PCG", { property = { GenerationTrigger = "GenerateOnDemand", bIsComponentPartitioned = "False" } })
assert(invoke({ actor_label = "RockScatter", component = "PCG" }, "SetGraph", { { path = G } }))
assert(g:generate(true) == 1)
-- poll list("components") until generated and not is_generating, then:
local r = g:list("resources", { actor = "RockScatter", transforms = true, limit = 200 })
print(r.instance_count, r.items[1].resources[1].components[1].mesh)
-- release everything the graph produced:
invoke({ actor_label = "RockScatter", component = "PCG" }, "CleanupLocal", { bRemoveComponents = true })
```

### Writing back into the terrain

`MeshPartition Write` moves terrain vertices: feed points carrying a
`SourcePositions` vector attribute (Add Attribute with
`InputSource='(Selection=Property,PropertyName="Position")'` and
`OutputTarget='(Selection=Attribute,AttributeName="SourcePositions")'`), offset
them with Transform Points, and set `AffectedMegaMesh` to the terrain actor
path. Generation spawns a PCG-managed `SimpleWriteModifier` that
`open_mesh_terrain(path):list("modifiers")` shows and `CleanupLocal` removes.
Only vertices of the built section mesh within 0.01 cm of a source position
move, so sampled surface points and authored-source vertices do not write;
treat a modifier that appears without a height change as "no vertex matched".

`list("resources")` is the proof: instance counts per ISM component, meshes,
and world transforms you can trace against the terrain. Verify placement with
a downward trace that ignores the host actor and check the hit is the
terrain (`PreviewSection` in the editor), not another Mesh Partition actor.
`generate()` warns when a matched component has no bounds.

## Typed interop nodes

The interop plugins (`PCGGeometryScriptInterop`, `PCGBiomeCore`,
`PCGExternalDataInterop`, `PCGPythonInterop`, `PCGMeshPartitionInterop`) are
optional. Check before authoring, then spawn typed nodes by kind instead of
guessing display titles and ImportText strings:

```lua
local s = pcg_interops_support()
print(s.geometry_script, s.biome_core, s.external_data, s.python, s.mesh_partition, s.reason)
for _, k in ipairs(pcg:list("interop_kinds").items) do
  print(k.kind, k.available, k.reason)      -- e.g. "plugin PCGGeometryScriptInterop not enabled"
end

-- Geometry Script: one point per cube vertex, wired into Output
local sampler = assert(pcg:add("node", {
  type = "mesh_sampler", mesh = "/Engine/BasicShapes/Cube",
  method = "OnePointPerVertex", x = 0, y = 0,
}))
-- sampler.handle is an editor node handle: connect(sampler.handle, "Out", ...) works.
```

Kinds: `mesh_sampler`, `mesh_to_dynamic_mesh`, `get_dynamic_mesh_data`,
`spawn_dynamic_mesh`, `save_dynamic_mesh_to_asset`, `append_meshes_from_points`,
`load_data_table`, `load_alembic`, `execute_python`, `subgraph`, `biome_core`,
`biome_core_local`, `mesh_partition_query`, `mesh_partition_write`,
`projection_spawner`, `get_mesh_terrain_section_actor`. Options are validated on
a transient probe first: a bad enum, a missing asset or an unknown key refuses
the call and no node is spawned. Unknown keys that name a reflected property
pass through, so `add("node", { type="mesh_sampler", PointSteepness=1 })` works.
The `mesh` option writes `Mesh` on 5.8 and `StaticMesh` on older engines.

### BiomeCore preset

```lua
local biome = assert(pcg:add("node", { type = "biome_core_local", x = 0, y = 300 }))
-- A Subgraph node bound to /PCGBiomeCore/LocalBiomeCore. Feed it the biome tables:
-- create Biome Definition / Biome Asset / Biome Generator data assets with
-- create_asset(path, "/PCGBiomeCore/Core/BiomeDefinition") style paths and set
-- their properties with configure(); BiomeCore generators sample Landscape/RVT,
-- so on a Mesh Terrain swap the generator's sampler for a Mesh Partition Query.
```

### External data (CSV -> DataTable -> points)

```lua
local dt = assert(create_asset("/Game/Proc/DT_Points", "DataTable", { Struct = "/Game/Proc/S_Point" }))
dt:add("row", { row_name = "A", values = { Pos = "(X=100,Y=0,Z=50)" } })
local load = assert(pcg:add("node", {
  type = "load_data_table", data_table = "/Game/Proc/DT_Points",
  output_type = "Point", attribute_mapping = { Pos = "$Position" },
}))
```

`attribute_mapping` values are `$Property` selectors or attribute names; the
helper writes the `(Selection=...)` struct text PCG expects. `load_alembic`
takes `{file, setup, scale, rotation, flip_handedness, attribute_mapping}`; the
engine ships no `.abc` fixture. `execute_python { script = "..." }` is the
escape hatch: it runs arbitrary Python during generation, so prefer typed nodes.

### Terrain writeback recipe (intermediate vertices)

`MeshPartition Write` only moves vertices of the intermediate mesh at its own
layer that sit within 0.01 cm of a `SourcePositions` value. Feed it the exact
vertices from an `Intermediate` query instead of sampled points:

```lua
local r = assert(pcg:add("terrain_writeback", {
  terrain = terrain_actor_path,       -- Mesh Partition actor path or label
  layer = "SimpleWrite",              -- must be in the definition's Modifier Layer Priorities
  offset = { x = 0, y = 0, z = 400 }, -- absolute offset applied by Transform Points
  priority = 10,
}))
print(r.edge_count, table.concat(r.layers_available, ","))
-- r.query / r.to_point / r.add_attribute / r.transform / r.write are node handles.
```

A refused layer lists the valid ones. Host the PCG component on an actor whose
bounds contain the raised positions (the write clamps destinations to the host
bounds), generate, then `open_mesh_terrain(path):list("modifiers")` shows the
managed SimpleWrite modifier and `list("resources")` reports it as
`modifier=true`. `CleanupLocal` releases it.

### Bounds reasons

```lua
local report = pcg:generate_report(true, { dry_run = true })
for _, s in ipairs(report.skipped) do print(s.actor_label, s.component, s.reason, s.hint) end
```

`generate()` still returns the triggered count, but components the engine would
skip silently are no longer triggered: `reason` is `no_bounds` (bare actor or a
`PCGVolume` spawned from class), `not_activated` or `runtime_managed`.
`generate(true, { require_bounds = false })` restores the old warn-and-trigger.

## Failure modes

| Symptom | Cause and response |
| --- | --- |
| `create_asset` returns nil | The path exists or PCG is unavailable. Choose a unique path; do not overwrite another run. |
| `add_node` is ambiguous or returns nil | Use `pcg:list("node_types", {query=...})`, then pass the exact schema action title. |
| `configure("node")` warns that a property was not found | Read `entry.properties` and use the exact case-sensitive property name. A typo must not be documented as a workaround. |
| A multi-key configure returns true but one key warned | Retry one property per call and verify every value freshly. Success means at least one property changed, not that every supplied key changed. |
| `generate()` returns `0` | No level component references this graph. Check `graph:list("components")` and the exact `SetGraph` target. |
| `generate()` returns `1` but the image is blank | The call only triggered a component. Check fresh `generated` state, valid primitive-backed host bounds, graph edges, and a visual sink such as `Debug`. Do not reload the level after the preview reaches `generated=true`. |
| A bare host actor generates nothing | A scene root has no useful primitive bounds. Use a StaticMeshActor/volume or add a registered primitive component before the `PCGComponent`. |
| Dense and sparse screenshots look identical | The `CellSize` mutation did not persist, regeneration has not completed, the level was reloaded, or Unlit merged touching Debug boxes into one slab. Verify one variable at a time and reuse the same Wireframe camera. |
| Graph state looks stale after mutation | End the script and re-open the graph in a new call. Do not certify an in-script snapshot. |
| `pcall` reports success after a failed operation | Binding failures usually return `nil` and log `[FAIL]`; check every return value explicitly. |

## Discovery escape hatches

- `help("PCG")` — enrichment signatures.
- `pcg:help()` — graph-specific examples.
- `pcg:info()` — graph summary, node count, parameters, and grid metadata.
- `pcg:list("node_types", {query="..."})` — available settings classes/actions.
- `pcg:list("nodes")` / `pcg:list("edges")` / `pcg:list("components")` — fresh
  structural and level assignment evidence.
- `read_graph(graph_path)` — editor-node handles, pins, and connections.
- `report_issue("...")` — use only after a minimal fresh-call reproduction shows
  the API cannot perform a required operation.
