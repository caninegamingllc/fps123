---
name: neostack-level-design
description: Create, edit, compose, save, and visually verify Unreal Engine levels through `execute_script`. Use when the user asks to build or modify a map, place or configure actors and meshes, arrange folders, add lights or cameras, duplicate level content, manage actor components or properties, inspect level state, or capture clean level screenshots.
---

# Level design through `execute_script`

Use Lua against the live editor. Start with `help("LevelDesign")`, then open a
fresh level handle in every call:

```lua
local level = open_level()
if not level then error("No editor level is available") end
```

Lua state does not survive between calls. Re-run `open_level()` for every
mutation and every verification pass.

## Work in four passes

1. Discover the live signatures with `help("LevelDesign")` and
   `print(open_level():help())`.
2. Build a labeled, foldered composition.
3. Re-open the level in a fresh call and verify actors, properties, and counts.
4. Save, verify the package path, and pass a clean visual gate.

Do not treat a non-nil mutation result as final proof. Read the affected state
again in a separate `execute_script` call.

## Create or open a level

Use `open_level()` to edit the current map. When the task requires a new map,
the current LevelDesign help exposes:

```lua
create_level("/Game/Levels/L_Courtyard", {
  template = "empty",
  open = true,
})
```

Use a `/Game/...` package path without `.umap`. `open=true` makes the new map
the active editor world. Use `template="empty"` or `"blank"` for a blank map,
`"basic"` for Unreal's basic template, `"open_world"` for its open-world
template, or a valid full map package path. Missing templates fail before the
current map is replaced or saved under the requested path.

Require the returned `created_package` to equal the requested package before
adding actors:

```lua
local path = "/Game/Levels/L_Courtyard"
local created = assert(create_level(path, {template="empty", open=true}))
assert(created.created_package == path)
assert(created.lifecycle_stable_ticks >= 3)
local level = assert(open_level())
assert(level:info().package_name == path)
```

`create_level` is an asynchronous editor-lifecycle checkpoint. It yields until
the old editor world is settled for three consecutive ticks, creates and saves
the map after the normal world tick, then resumes only after the exact resulting
world survives another three ordinary ticks. Call it once and use the returned
`lifecycle_stable_ticks`; do not poll, sleep, or immediately retry. Because it
yields, do not call it from a non-yieldable Lua/C callback. With `open=false`,
the current map must already have a persistent package path so the binding can
restore it after proving the new map stable.

If the level already exists, load it explicitly:

```lua
load_level("/Game/Levels/L_Courtyard")
local level = open_level()
```

`load_level` is a safe lifecycle boundary. After PIE or Game Preview it waits
for several settled editor ticks, performs the map replacement after world tick
completion, and then resumes the script. Call it once and use its return value;
do not poll it or insert arbitrary sleeps after `playtest_stop()`.

To preserve an unsaved current world under a new name:

```lua
assert(save_level_as("/Game/Levels/L_Courtyard"))
```

Verify the save in a fresh call:

```lua
local level = open_level()
local info = level:info()
print(info.package_name, info.world_name)
assert(info.package_name == "/Game/Levels/L_Courtyard")
assert(asset_exists("/Game/Levels/L_Courtyard"))
```

## Author layered landscapes

Build the landscape material and layer-info assets before sculpting. The
material needs one `LandscapeLayerBlend` entry for every exact layer name used
by `create_landscape_layer`, `paint`, or `auto_paint`; save and compile it with
zero errors. Keep the reusable material, map, and every layer-info object at
explicit `/Game/...` paths, then configure the landscape with that material.

Landscape height inputs are world-space centimeters even when the actor has a
non-default Z scale. That applies to `flat_height`, procedural-noise amplitude,
`sculpt` height/amplitude, and `auto_paint` `min_height`/`max_height`. Slope
rules are world-space degrees and account for non-uniform landscape scale.
Do not manually divide values by the actor scale.

For a winding corridor, interpolate densely between reusable route anchors and
apply nested broad-to-narrow height bands. Smooth the complete affected region
after the final narrow band so the route does not retain rectangular anchor
gaps or vertical terrace walls. Paint the procedural biome layers first, then
apply the manually controlled path last:

```lua
local level = assert(open_level())
assert(level:auto_paint({
  landscape=0,
  layers={
    {name="Grass", min_slope=0, max_slope=35,
      min_height=-99999, max_height=3600},
    {name="Rock", min_slope=0, max_slope=90,
      min_height=3400, max_height=99999},
  },
}))
for _, point in ipairs(interpolated_route) do
  assert(level:paint({landscape=0, layer="Trail", weight=255, region={
    x1=point.x-4, y1=point.y-5, x2=point.x+4, y2=point.y+5,
  }}))
end
assert(level:save())
```

A successful mutation log is not paint proof. In a separate call, read
`get_heightmap` and every required `get_weightmap`; require a non-flat height
range and nonzero, meaningfully separated samples for every layer. Cold-reopen
the editor and repeat those reads so transient edit-layer data cannot pass.
Also verify the exact material assignment, layer-info asset paths, landscape
resolution/component count, collision-bearing traversal markers, and a clean
Map Check.

Finish with one overview and at least three ground views. Probe the heightmap at
each camera coordinate and place the camera above that exact surface height.
Open every original image and reject clipped cameras, below-terrain voids,
obstructed shots, disconnected paint, harsh popping terraces, or views that do
not clearly show the route continuing through the same landscape.

## Build a small composition

Create every actor with a deliberate label and folder. Labels are the stable
inputs for later configure, selection, duplication, component, and property
operations.

```lua
local level = open_level()
local root = "Courtyard"
local geo = root .. "/Geometry"
local lighting = root .. "/Lighting"
local cameras = root .. "/Cameras"

local function need(value, operation)
  if value == nil or value == false then
    error("Level operation failed: " .. operation)
  end
  return value
end

need(level:add("folder", { path = root }), "root folder")
need(level:add("folder", { path = geo }), "geometry folder")
need(level:add("folder", { path = lighting }), "lighting folder")
need(level:add("folder", { path = cameras }), "camera folder")

need(level:add("actor", {
  mesh = "/Engine/BasicShapes/Cube",
  location = { x = 0, y = 0, z = -25 },
  scale = { x = 12, y = 10, z = 0.25 },
  label = "Courtyard_Ground",
  folder = geo,
}), "ground")

need(level:add("actor", {
  mesh = "/Engine/BasicShapes/Cylinder",
  location = { x = 0, y = 0, z = 60 },
  scale = { x = 1.8, y = 1.8, z = 1.4 },
  label = "Courtyard_Pedestal",
  folder = geo,
}), "pedestal")

need(level:add("actor", {
  mesh = "/Engine/BasicShapes/Sphere",
  location = { x = 0, y = 0, z = 230 },
  scale = { x = 1.35, y = 1.35, z = 1.35 },
  label = "Courtyard_Orb",
  folder = geo,
}), "orb")

need(level:add("light", {
  type = "point",
  location = { x = -320, y = -180, z = 380 },
  intensity = 1800,
  color = "255,80,35",
  label = "Courtyard_WarmKey",
  folder = lighting,
}), "warm key")

need(level:add("light", {
  type = "spot",
  location = { x = 0, y = -700, z = 650 },
  rotation = { pitch = -35, yaw = 90, roll = 0 },
  intensity = 2200,
  color = "255,230,190",
  label = "Courtyard_RimSpot",
  folder = lighting,
}), "rim spot")

need(level:add("actor", {
  class = "CameraActor",
  location = { x = -1100, y = -1100, z = 720 },
  rotation = { pitch = -20, yaw = 45, roll = 0 },
  label = "Courtyard_Camera",
  folder = cameras,
}), "camera")

need(level:save(), "save")
```

`add("actor")` requires either `mesh` or `class`. Missing both returns nil and
must leave no actor behind. `add("light")` accepts `point`, `spot`,
`directional`, or `sky`; provide labels and folders just as for mesh actors.

## Author persistent foliage

Create and save a `FoliageType` asset for every foliage layer before placing
instances. Configure density, radius, scale range, surface alignment, cull
distance, collision, mobility, and World Position Offset policy on that asset:

```lua
local path = "/Game/Foliage/FT_Canopy"
local foliage = open_asset(path) or assert(create_asset(path, "FoliageType"))
assert(foliage:configure({
  mesh = "/Game/Foliage/SM_Canopy",
  density = 72,
  radius = 360,
  scale_x = {min=0.78, max=1.34},
  scale_y = {min=0.78, max=1.34},
  scale_z = {min=0.78, max=1.34},
  align_to_normal = true,
  average_normal = true,
  average_normal_sample_count = 4,
  cull_distance = {min=2600, max=13000},
  collision_with_world = true,
  evaluate_world_position_offset = true,
  world_position_offset_disable_distance = 13000,
}))
assert(foliage:save())
```

Place explicit deterministic transforms through the saved type. Always use
`replace_existing=true` when regenerating the same layer:

```lua
local result = assert(open_level():add("foliage", {
  foliage_type = "/Game/Foliage/FT_Canopy",
  transforms = transforms,
  replace_existing = true,
}))
assert(result.total_count == #transforms)
assert(result.foliage_actor_count >= 1)
```

Partitioned worlds require a saved `foliage_type`; do not use mesh-only
foliage there. Placement routes each transform through Unreal's actor-partition
lookup, so one saved type can span multiple foliage actors. Exact listing
aggregates those actors:

```lua
local rows = assert(open_level():list("foliage", {
  foliage_type = "/Game/Foliage/FT_Canopy",
  include_transforms = true,
  limit = 5000,
}))
assert(#rows == 1)
assert(rows[1].is_asset)
assert(rows[1].instance_count == #transforms)
assert(not rows[1].transforms_truncated)
```

Reapply the same transforms in a separate call and require
`replaced_count == instance_count` with an unchanged `total_count`. Verify
collision from the live `FoliageInstancedStaticMeshComponent`, not only the
asset settings. For a traversable corridor, combine zero geometric intrusions
with a fixed-path line or capsule trace that returns no blocking hit.

For wind-reactive foliage, verify the material graph contains the intended
native wind function connected to the material root's World Position Offset.
Then capture an ordered, fixed-camera sequence of original frames. Inspect
every frame for coherent deformation, stable scene identity, no camera change,
and no popping; different hashes or two different stills are insufficient.

## Author spline roads and paths

Put a real `SplineComponent` on the intended Blueprint, place that Blueprint in
the target level, and author the placed instance through the native spline
surface. Legacy `{x,y,z}` points still work; use rich points when tangents,
banking, width, or exact point modes matter:

```lua
local level = assert(open_level())
assert(level:add("spline", {
  actor = "Road",
  component = "RoadSpline",
  coordinate_space = "world",
  closed = true,
  points = {
    {
      location = {x=-1200,y=-600,z=100},
      tangent = {x=800,y=-500,z=80},
      roll = -10,
      scale = {x=1,y=5,z=0.15},
      type = "curve_custom_tangent",
    },
    -- more points
  },
}))
assert(level:add("spline_meshes", {
  actor = "Road",
  component = "RoadSpline",
  mesh = "/Game/Road/SM_RoadStraight",
  forward_axis = "x",
  collision = true,
  component_prefix = "RoadSegment",
}))
```

`spline_meshes` is idempotent: it reuses the components tagged for that exact
spline and keeps one segment per native spline segment. Endpoints and tangents
are copied in spline-local space. Each mesh attaches to the actor's stable root
with the spline's exact relative transform, so a Blueprint construction-script
rerun cannot orphan it from a reconstructed spline component. Point roll and
cross-section scale become each segment's start/end bank and scale. Do not
approximate a road with unrelated manually rotated meshes when the task requires
continuous joins.

Read the authored curve and native distance evaluation in a fresh call:

```lua
local road = assert(open_level():list("splines", {
  actor = "Road",
  component = "RoadSpline",
  sample_count = 17,
})[1])
assert(road.closed and road.point_count >= 4)
assert(road.mesh_segment_count == road.segment_count)
for _, sample in ipairs(road.samples) do
  print(sample.distance, sample.location.x, sample.rotation.yaw, sample.roll)
  -- sample.up_vector gives the road-normal direction for vehicle/prop offset.
end
```

For actors that must follow the road, derive keyed world transforms from these
distance samples. Preserve spacing by applying the same progress curve to every
actor plus a constant wrapped distance offset; use the returned rotation and
`up_vector`, rather than estimating yaw from neighboring screenshots. After
saving, reopen the level and require exact points, tangents, types, segment
count, mesh endpoints, and evaluated samples before visual acceptance.

## Configure and duplicate actors

Use `configure("actor", label, params)` for transforms, labels, folders, mesh,
material, and supported properties:

```lua
local level = open_level()
assert(level:configure("actor", "Courtyard_Orb", {
  location = { x = 0, y = 0, z = 260 },
  scale = { x = 1.5, y = 1.5, z = 1.5 },
  folder = "Courtyard/Geometry",
}))
```

Duplicate one actor with an explicit label:

```lua
local copy = duplicate_actor("Courtyard_Pedestal", {
  offset = { x = 0, y = 420, z = 0 },
  new_label = "Courtyard_Pedestal_Right",
})
assert(copy)

local level = open_level()
assert(level:configure("actor", copy.label, {
  folder = "Courtyard/Geometry",
}))
```

Bulk duplication returns actor tables. Relabel and refolder every result because
the engine generates their initial labels:

```lua
local copies = duplicate_actors(
  { "Courtyard_Pedestal", "Courtyard_Orb" },
  { offset = { x = 0, y = 420, z = 0 } }
)
assert(copies and #copies == 2)

local level = open_level()
local labels = { "Courtyard_Pedestal_Copy", "Courtyard_Orb_Copy" }
for i = 1, #copies do
  assert(level:configure("actor", copies[i].label, {
    label = labels[i],
    folder = "Courtyard/Geometry",
  }))
end
```

Selection is additive until cleared:

```lua
deselect_all()
assert(select_actor("Courtyard_Pedestal"))
assert(select_actor("Courtyard_Orb"))
local selected = get_selected_actors()
assert(#selected == 2)
```

## Compose reusable Level Instances

Keep shared geometry and actors in the source level. A standard Level Instance
does not provide arbitrary per-instance overrides for its child actors, so put
variant-specific lights, markers, controllers, and other presentation actors in
the host level next to each instance. Record the relationship with stable actor
labels and tags, then verify every instance still references the same source
world after save and reload.

Level Instance edit mode is a lifecycle boundary, not an ordinary transactional
mutation. `li_edit`, `li_commit`, and `li_discard` may yield while Unreal enters
or exits the edit world. Do not call them from a callback that must complete in
the same tick, and do not rely on editor Undo for this workflow. Commit or
discard explicitly, then inspect the instances in a fresh call:

```lua
assert(li_edit("Room_A"))
-- Mutate the shared source level through open_level().
assert(li_commit("Room_A"))

local instances = assert(li_list())
assert(#instances == 4)
for _, instance in ipairs(instances) do
  assert(instance.source_world == "/Game/World/LI_ModularRoom")
  assert(instance.loaded)
end
```

For propagation proof, add one uniquely labeled source actor, commit, and
require that every instance contains exactly one copy with identical shared
actor counts. Also exercise a discard-only sentinel: add it during edit, call
`li_discard`, and require that it appears in none of the reloaded instances.
Finish with a cold editor restart, exact transforms and tags, stable source
references, no duplicate labels or broken references, and a clean Map Check.

## Components and properties

Discover component names before addressing them:

```lua
local components = list_actor_components("Courtyard_Orb")
for i = 1, #components do
  print(components[i].name, components[i].class)
end
```

Read a component or actor property in a fresh call:

```lua
print(get_actor_property("Courtyard_Orb", "RelativeScale3D"))

local props = get_component_properties(
  "Courtyard_Orb",
  "StaticMeshComponent0",
  { all = true, changed_only = false }
)
```

`get_actor_property` and `set_actor_property` try the actor first, then its root
component. Check the structured write result and read the stored value again:

```lua
local ok, details = set_actor_property(
  "Courtyard_WarmKey",
  "Intensity",
  1800
)
assert(ok)
assert(details.status == "updated")
print(details.target, details.stored_value)
```

Fresh verification:

```lua
assert(get_actor_property("Courtyard_WarmKey", "Intensity") == "1800.000000")
```

For explicit component lifecycle operations, use the signatures returned by
`help("LevelDesign")`:

```lua
local component = add_component("Courtyard_Pedestal", {
  type = "PointLightComponent",
  name = "AccentLight",
})
assert(component)

assert(configure_component("Courtyard_Pedestal", "AccentLight", {
  intensity = 800,
  color = "80,120,255",
}))

assert(remove_component("Courtyard_Pedestal", "AccentLight"))
```

## Author and prove runtime Data Layers

Data Layer assets require external actor packaging. Create or open the World
Partition map, opt in to conversion, save, and reopen it before creating layers:

```lua
assert(partition("enable", { convert_actors = true }))
assert(save_current_level())
assert(open_level("/Game/World/L_RuntimeLayers"))

assert(partition("create_layer", {
  name = "DL_Base",
  path = "/Game/World/DL_Base",
  type = "runtime",
  initial_state = "activated",
}))
assert(partition("create_layer", {
  name = "DL_Reveal",
  path = "/Game/World/DL_Reveal",
  type = "runtime",
  initial_state = "unloaded",
}))
```

A refusal before conversion is the safe result; do not work around it by
creating an orphaned Data Layer asset. For deterministic gating tests, make the
actors non-spatially-loaded so cell streaming cannot mask the layer transition,
then assign and read back exact membership:

```lua
assert(set_actor_property("RevealBridge", "IsSpatiallyLoaded", false))
assert(partition("add_to_layer", {
  layer = "/Game/World/DL_Reveal",
  actors = { "RevealBridge" },
}))
local members = partition("get_layer_actors", {
  layer = "/Game/World/DL_Reveal",
})
assert(members.count == 1)
```

In UE 5.8 Blueprints, discover `Get Data Layer Manager` in graph context and use
its `Set Data Layer Runtime State` call. Do not depend on the deprecated Data
Layer Subsystem. Prove behavior in PIE with both Blueprint state and engine
state: `playtest_read_state` for the controller, plus
`partition("runtime_state", { world="pie", labels={...} })` for each layer and
representative actor. Require requested state, effective state, loaded state,
actor presence, visibility, and collision to agree after streaming completes.
Exercise wrong-order input, repeated input, successful progression, and reset;
reset must unload only gated layers while the base remains active. Finally save,
reopen, and audit exact membership and external-actor package descriptors so a
working PIE session is not mistaken for durable authoring.

## Build and validate World Partition HLODs

Create separate HLOD layers for content with different aggregation needs, then
assign every source actor to an exact layer object path. For example, use
Instancing for repeated props and MeshMerge for building modules:

```lua
assert(create_asset("/Game/World/HLOD_Props", "hlodlayer"))
assert(create_asset("/Game/World/HLOD_Buildings", "hlodlayer"))
assert(hlod_configure_layer("/Game/World/HLOD_Props", {
  layer_type="Instancing", cell_size=3200, loading_range=7000,
}))
assert(hlod_configure_layer("/Game/World/HLOD_Buildings", {
  layer_type="MeshMerge", cell_size=6400, loading_range=14000,
}))
assert(set_actor_property(
  "Settlement_Crate_01", "HLODLayer",
  "/Game/World/HLOD_Props.HLOD_Props"))
```

For UE 5.8 RuntimeHashSet worlds, layer assets and actor assignments are not
enough. Register every used layer in an explicit runtime HLOD setup or Map
Check reports each source actor as having an invalid HLOD layer:

```lua
local setup = assert(hlod_configure_runtime_partition({
  setup_name="SettlementHLOD",
  layers={"/Game/World/HLOD_Props", "/Game/World/HLOD_Buildings"},
  loading_range=12000,
  cell_size=6400,
}))
assert(setup.layer_count == 2)
assert(open_level():save())
assert(open_level():map_check().clean)
```

Externalize the source actors before the native World Partition HLOD build.
For a new or repaired partitioned map, opt in explicitly, save, load another
map, and reopen the target so actor descriptors are rebuilt from disk:

```lua
assert(open_level():partition("enable", {convert_actors=true}))
assert(open_level():save())
assert(load_level("/Game/Maps/AnotherMap"))
assert(load_level("/Game/World/L_Settlement"))
```

Run Unreal's `WorldPartitionHLODsBuilder` with setup and build enabled; do not
treat `hlod_build()` over whichever actors happen to be editor-loaded as a
complete world build. Require the builder to enumerate and process the exact
expected HLOD actor count with zero errors and warnings. Run it a second time
without force: in UE 5.8 every unchanged actor must produce
`RejectRebuild` from the rebuild policy.

Verify in a fresh editor process:

- source actor descriptors, labels, external packages, and exact layer counts;
- all HLOD actor descriptors, including descriptor-only actors not loaded in
  the current editor region;
- loaded proxy source membership and non-zero grouped build statistics;
- `hlod_check_hash()` with `validation_mode="rebuild_policy"` and
  `needs_rebuild=false` on UE 5.8 (its deprecated hash APIs return zero);
- persisted runtime setup values and a clean Map Check.

For visual transition proof, use matched camera transforms. Capture cold proxy
views, temporarily call `partition("set_streaming", {enabled=false,
force=true})` to load the small test world's sources, capture the same views,
restore `enabled=true`, and capture the same views again. Inspect every frame
for matching silhouettes, no holes, and no doubled geometry. Restore streaming
and cold-restart before acceptance; do not disable streaming across a large
world merely to obtain screenshots.

## Verify structure and persistence

Filter by the label prefix and inspect every returned actor:

```lua
local level = open_level()
local actors = level:list("actors", { name = "Courtyard_" })
for i = 1, #actors do
  local actor = actors[i]
  print(
    actor.label,
    actor.class,
    actor.folder,
    actor.location.x,
    actor.location.y,
    actor.location.z
  )
end
-- The full example above creates six originals and three duplicates.
assert(#actors == 9)
```

Use `level:info()` for package and aggregate checks. Its level summary includes
`package_name`, `path`, `world_name`, `actor_count`, `landscape_count`,
`light_count`, `folder_count`, `folders`, and bounds when valid.

Use serialization for deeper inspection or reproducibility:

```lua
local level = open_level()
local actors = level:serialize("table")
local recreation_script = level:serialize("script")
assert(type(actors) == "table")
assert(type(recreation_script) == "string")
```

Run Unreal's native Map Check directly before accepting or saving a composed
level:

```lua
local check = assert(open_level():map_check())
assert(check.passed, "Map Check reported " .. tostring(check.errors) .. " error(s)")
if not check.clean then
  log("Map Check warnings: " .. tostring(check.warnings))
end
```

`passed` means the current run has no errors; `clean` means it has neither
errors nor warnings. The `errors`, `warnings`, and `issue_count` fields always
describe this invocation. By default Map Check clears its message-log page
first. For diagnostics that must append to the existing page, call
`map_check({clear_log=false})`; its `page_error_count`,
`page_warning_count`, and `page_issue_count` fields then expose cumulative page
totals while the non-`page_` fields remain per-run deltas. Use
`deprecated_only=true` only when explicitly checking deprecated actors.

Call `level:save()` after edits to an already-named map. Use `save_level_as(path)`
only when assigning or changing the map package path.

## Pass the visual gate

Capture from deliberate viewpoints with `mode="level"` and
`hide_overlays=true`:

```lua
screenshot({
  mode = "level",
  location = { x = -950, y = -550, z = 260 },
  rotation = { pitch = 0, yaw = 30, roll = 0 },
  fov = 62,
  view_mode = "lit",
  hide_overlays = true,
  max_dimension = 1600,
  wait_for_ready_ms = 1800,
})
```

Capture at least two materially different viewpoints. For animated content,
capture a temporally ordered fixed-camera sequence with enough original frames
to cover the prompted action or loop; two different frames do not prove
coherent motion. Inspect every returned image and verify:

- All intended geometry is visible.
- Object count and duplication count match the fresh actor listing.
- Scale and spacing read correctly from the chosen viewpoints.
- Intended light or material colors are visible without destructive clipping.
- No editor icons, selection outlines, widgets, or transform gizmos remain.

If the image is empty, aimed at unrelated terrain, or still contains editor
overlays, do not accept it. Adjust the camera only when the scene data is
correct; otherwise stop and localize the capture defect.

## Failure modes

| Symptom | Cause and response |
| --- | --- |
| `open_level()` returns nil | No usable editor world is active. Open a map and retry. |
| `load_level()` resumes a few ticks after PIE stops | Expected; it waits for the old world's tick task state to detach before replacing the map. |
| `create_level()` resumes a few ticks later | Expected; map replacement is an asynchronous lifecycle checkpoint. Call it once, require `lifecycle_stable_ticks >= 3`, and continue from its result. |
| `create_level(..., {open=false})` cannot restore the prior map | Save the current map to a persistent `/Game/...` package first; an untitled/transient world has no safe restore target. |
| `create_level()` rejects a template | Use `empty`, `blank`, `basic`, `open_world`, or a valid full map package path. The binding leaves the current map unchanged on rejection. |
| `create_level()` returns a different package | Stop before adding actors. Require both `created_package` and a fresh `level:info().package_name` to match the requested path. |
| `add("actor")` returns nil | Supply a valid `mesh` or actor `class`; verify the path/class with discovery first. |
| A mutation logs success but the read looks stale | Re-open the level and verify in a new `execute_script` call. |
| Duplicate labels are engine-generated | Relabel and refolder each table returned by `duplicate_actors`. |
| Property write fails | Inspect `details.status` and `details.error`; do not assume actor and root-component properties share a target. |
| Map Check returns nil | Stop PIE and verify an editor world is active; malformed options are rejected instead of coerced. |
| Map Check passes but is not clean | Errors are zero but warnings remain. Inspect the native `MapCheck` message log instead of treating warnings as errors. |
| Save fails | Confirm a writable `/Game/...` package path and inspect the exact editor log. |
| Level Instance commit reports cancellation in unattended MCP execution | Use `li_commit` as its own lifecycle checkpoint. The binding follows Unreal's non-interactive editor-scripting save path; do not replace it with direct package manipulation. |
| A shared Level Instance child needs a different material in each placement | Standard Level Instances do not expose arbitrary child overrides. Keep shared children in the source and author variant lights, markers, or controllers in the host level. |
| Saved foliage works but the log contains an actor-partition ensure | Treat the run as failed. Placement and removal must route spatially; do not use a current-level foliage shortcut in a partitioned world. |
| Foliage regeneration duplicates instances | Use one saved type per layer, set `replace_existing=true`, then require replaced and total counts to remain exact after a second call. |
| Map Check says every source actor has an invalid HLOD layer | In a UE 5.8 RuntimeHashSet world, register those HLOD layers with `hlod_configure_runtime_partition`, save, and rerun Map Check. |
| `hlod_check_hash()` reports only zero hashes on UE 5.8 | Zero is Epic's deprecated compatibility stub. Require `validation_mode="rebuild_policy"`, policy data, and `needs_rebuild=false`. |
| Only some built HLOD actors appear in `hlod_list_actors()` | That function reports editor-loaded actors. Audit all HLOD descriptors and use the native builder's full-world policy pass for total coverage. |
| Screenshot misses the scene | Use explicit camera transforms or `focus_actor`, then inspect the returned image rather than trusting the text response. |
| `hide_overlays=true` still shows editor chrome | Treat the screenshot as failed and report the capture defect; do not use it as visual proof. |

Use `help("LevelDesign")`, `print(open_level():help())`, `level:info()`, component
property reads, and fresh actor listings as discovery escape hatches. When the
binding behaves differently from its help or cannot complete the requested
task, call `report_issue(...)` with the minimal reproduction before reporting
the gap.
