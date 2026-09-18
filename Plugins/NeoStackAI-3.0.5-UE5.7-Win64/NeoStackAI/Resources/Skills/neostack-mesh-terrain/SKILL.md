---
name: neostack-mesh-terrain
description: Author, sculpt, paint, build and inspect Unreal Engine 5.8 Mesh Terrain through NeoStack Lua and MCP. Use for MeshPartition terrain definitions, transformer pipelines, authored source modifiers, channel materials and terrain PIE checks; ordinary Landscape uses separate bindings.
---

# Mesh Terrain through the live editor

Use the user's existing project and requested asset/map scope. Reuse the same
working map and terrain across iterations; a new project per operation is unnecessary.
Mesh Terrain requires UE 5.8 and the enabled MeshPartition integration.
Read the current binding catalog instead of guessing unsupported verbs:

```lua
help("MeshTerrain")
help("MeshTerrainEdit")
help("MeshTerrainAssets")
help("MeshTerrainBuild")
help("MeshTerrainImport")
help("MeshTerrainConversion")
```

`help` prints its own output. Lua state is fresh for every `execute_script` call.
Keep durable asset and component paths, then reopen handles each call. Before
using graph-node handles from another call, `read_graph` to register/rediscover them.

## Discover and author the right representation

Terrain `list(...)` returns a paged object: iterate `result.items`, not the
outer result. Check `has_more` and `includes_unloaded`. These lists currently
cover loaded authoring objects; an empty list is not proof a saved actor is absent.
Use returned exact modifier/component paths for `open_mesh_terrain_source`.
Actor labels and owner actor paths are not interchangeable with component paths.

After a map reload, a saved World Partition source can have a descriptor while
its actor remains unloaded. Before diagnosing lost data, match its saved actor
name against `unreal.WorldPartitionBlueprintLibrary.get_actor_descs()` through
`execute_python`, then call `load_actors([matching_descriptor.guid])` for those
exact actors. Reopen the saved component paths afterward. Do not load every
descriptor or treat a display label as an actor identity.

Base/source modifiers are editable input. Preview sections and compiled runtime
sections are derived output. Do not sculpt or delete a compiled actor as if it
were the terrain's authoritative source.

For a new rectangle, follow `create_mesh_terrain` help and choose bounded mesh
resolution and section counts appropriate to the requested size. Reopen the
returned terrain, inspect its definition and list source modifiers before edits.
Reuse existing geometry rather than recreating a terrain to change one parameter.

## Sculpt, paint and modifier order

Direct source and projected sculpt workflows have different handles/lifecycles;
use the current `MeshTerrainEdit` help for creation and source resolution.
Coordinates/radius are world centimeters. Start with one localized stamp and
sample before/after before applying a broad sequence.

Inflate strength is normalized 0..1, not displacement in centimeters. Default
`delta_time=0.03` represents one native stamp. Its maximum displacement is
`2 * radius * strength * delta_time` along normals. A duration of 1 can move
far more terrain than expected. An explicit longer duration scales one stamp;
it does not perform intermediate geometry iterations. Smooth uses normalized
strength in one iteration and ignores duration. Base boundaries are locked.
Use scalar `direction=1` or `direction=-1` for inflate, not a direction vector.

For `brush='plane'` or `'flatten'`, supply explicit world `plane_origin` and
nonzero `plane_normal` tables. `side='both'|'push_down'|'pull_towards'` is relative
to that normal; `direction` is rejected. These use a supplied plane, not an
automatic fitted/view plane or native HeightFlatten. Start with one small stamp.

Set explicit noise, sculpt and Boolean modifier priorities so evaluation order
matches the intended result. Inspect current priorities and declared definition
layers before `configure_common`; do not assume creation order is sufficient.
A downstream Boolean cut can remove upstream sculpted/painted terrain. Re-sample
and inspect the combined preview rather than judging each modifier independently.

Declare channels in the terrain definition before painting; for example `Grass`
is an authored channel name, not an automatically existing material parameter.
Channel spelling must match across definition, paint and material sampling.
Read layer names/weights before editing a layer; a zero-weight layer will not
produce the intended visible result. Check samples as well as screenshots.

Projected sculpt handles expose `list_layers`, `configure_layer`, `add_layer`,
`remove_layer`, `move_layer` and `merge_layers`; consult help for option tables.
Indices are one-based editable layers; the internal base layer is protected.
Keep at least one editable layer. Removal discards that layer's contribution;
removal and movement shift indices, so list again before subsequent edits.
Merge uses an inclusive `first`..`last` range and preserves weighted shape by
baking into the first layer, whose weight must be nonzero. Check samples and
Undo after editing; native regressions verify weighted geometry and metadata restoration.

Use `brush='erase_layer'` with an explicit editable `layer` to erase only that
layer's contribution within the stamp. Its target is the weighted shape of the
other layers, with movement capped at `strength * radius * delta_time` before
falloff. It requires a nonzero layer weight and preserves other layers and paint
channels. It does not accept `direction` and is not a reset of the whole terrain.

## Import bounded heightmaps

Inspect the PNG on the editor host with `inspect_mesh_terrain_heightmap` before
`import_mesh_terrain_heightmap`. The adapter accepts grayscale PNG8/16, at most
8 MiB encoded and 65536 samples; duplicated section-boundary vertices also count
toward the 65536 output-vertex limit. RGB, alpha, palette and resampling are not
supported. Keep source files on allowed host paths; a remote client's local path
does not identify a file on the editor machine.

Use `definition` for a new terrain or `terrain_path` to append to an existing
loaded terrain. Append requires explicit `seam_policy='independent'`; it does
not weld tiles or replace earlier sources. Save returned `source_ids` to avoid
accumulating duplicate sections when iterating. Input samples are unsigned:
`Z = z_offset + sample / 65535 * height_range`, with PNG8 expanded by257.
The first image row maps to world -Y unless `flip_y=true`. Size and center are
world centimeters. Check boundary samples and Z extrema before saving; import
creates unsaved authored sources and requires a separate compiled build.

## Copy loaded mesh components

Use `inspect_mesh_terrain_component` with an exact loaded component path, then
`import_mesh_terrain_component` with a definition or existing terrain destination.
The component supplies its world transform; do not add a second transform option.
Static components copy authored asset LOD geometry with effective material
overrides. Dynamic components copy their current authored mesh. Both preserve
the input; append requires `seam_policy='independent'`. Inspect returned bounds,
materials and source IDs, save explicitly, and reopen handles after map reload.

Instanced/spline-deformed components, render-time deformation, skinning and custom
attached/sculpt attributes need other workflows. A copied authored mesh is not
proof that evaluated/rendered geometry was captured. Refusal messages distinguish
these cases; don't silently replace a component with a different representation.

## Copy existing static mesh sources

Use `inspect_mesh_terrain_static_mesh` before `import_mesh_terrain_static_mesh`.
These copy an authored source LOD, including build scale and normal/tangent build
settings; a render LOD, Nanite clusters and collision are different representations.
Missing/generated-only LODs are refused without fallback. Do not assume an engine
Cube's authored triangle count from its appearance or rendered topology.

Choose `definition` for a new terrain or explicit independent append as above.
The input mesh asset remains intact. Import accepts world `location`, degree
`rotation={pitch,yaw,roll}` and positive uniform scalar `scale`; set placement here
when possible. `terrain:configure_common` accepts non-base modifiers, so it cannot
reposition an imported base section. Existing actor transform tools are a separate
route; keep positive uniform scale for subsequent source sculpt operations.

The copied source retains material slots/triangle IDs and UV, normal, tangent and
color overlays under the native converter's policies. Definition/pipeline settings
separately determine compiled output materials. Inspect those settings before
promising that a multi-material source will render identically after terrain build.
Copy does not weld, replace, delete inputs, save packages or compile terrain.

## Split selected source triangles

Use `source:split({triangle_ids={...},label="Selected terrain"})` for a loaded
base source. Selection values are native zero-based triangle IDs in a dense Lua
array. Select a nonempty proper subset; this separates whole triangles without
cutting, capping or welding them. Point-shared disconnected triangle fans receive
duplicate vertices so each output remains manifold. The original source keeps the complement.
The result gives `original_source_id`, `new_source_id` and two sorted arrays of
`{old_id,new_id}` triangle mappings. Rediscover IDs after a split; output IDs can
change. Isolated vertices unused by either triangle subset are discarded.

The two halves retain authored materials, UV seams, normals/tangents, colors,
groups and named weight channels. Source ordering, world transform, runtime grid
and spatial-loading setting carry over. Both halves together must fit the 65536
vertex budget, including duplicated shared-boundary vertices. Unsupported custom,
sculpt, skinning, morph and engine-internal triangle-label attributes are refused. Sources must have positive
uniform scale and a plain owner attached only to its own terrain, without
data-layer or content-bundle membership; unsupported ownership is not silently
dropped.

This edit does not save or build. Inspect both returned sources, then explicitly
save and rebuild when compiled output is needed. A failed edit normally restores
the original mesh and removes its new actor. If cleanup cannot complete, the error
identifies the retained actor and preserves Undo; resolve that state before retrying.

## Weld source boundaries

Use `source:boundary_edges({offset=0,limit=128})` to discover native boundary
vertex/edge IDs and world endpoints. Pagination is in ascending edge-ID order;
`limit` is at most 512. Rediscover after each topology change. For a seam between
two sources, merge explicitly, then select two open boundary spans from the
retained source and call `source:weld({keep_vertices={...},discard_vertices={...},
max_distance=...})`. Unequal spans receive native longest-edge midpoint splits;
native winding determines endpoint pairing. The world-distance limit applies
after equalization. Default interpolation zero keeps the selected side's
positions and vertex weights; nonzero interpolation blends both. UV/normal/color
overlay seams remain independent. Triangle deletion is refused unless explicitly
requested with `allow_triangle_deletion=true`; partial seams and invalid output
always refuse. This operation does not fill holes. Check returned added/removed
IDs and inspect the result before saving/building.

## Refine terrain with native brushes

For broad shape refinement, `move` takes `end_center` and applies initial-ROI
falloff to the drag. `kelvin_pull` and `kelvin_sharp_pull` use the current-end ROI
and native elasticity. These drag brushes reject `strength`, `delta_time` and
`direction`; displacement is bounded to four radii. `pinch` requires a world
`normal`, accepts `depth` in [-1,1], and uses ordinary strength/duration.
`kelvin_scale` and `kelvin_twist` use `kelvin_strength` in [0,10] instead of
strength/duration; twist requires an explicit world-normal axis. Each call is
one bounded native stamp, not an interpolated multistamp stroke. Base boundaries
remain locked. Use broad, restrained edits and inspect silhouettes; stronger
deformation is not evidence of better terrain.

## Resection a loaded base source

`source:resection({cells={x=2,y=2,z=1},bounds={min={x,y,z},max={x,y,z}}})`
assigns whole triangles to cells by world centroid. Provide all coordinates and
bounds containing every source vertex. It does not cut triangles that cross cell
boundaries. Cell counts are integers1..64 with at most64 cells in total; a flat
bound axis requires one cell on that axis.

The first occupied cell in x/y/z order retains the original source identity.
Other occupied cells become sources with the same transform, materials and common
metadata. Returned `sections` contain zero-based `cell` coordinates, exact
`source_id` and `triangle_map` entries; rediscover triangle IDs from these maps.
An internal boundary selects the higher cell and the outer maximum selects the
last cell. One occupied cell returns `changed=false` without editing the source.
Outputs preserve the supported split attributes, subject to aggregate budgets.
Save and build explicitly when needed. Inspect reported surviving actors before
retrying incomplete recovery. This new operation is awaiting build/live validation.

## Merge loaded base sources

`source:merge({source_ids={other_provider_path,...}})` retains this source's
identity/transform and deletes the 1..15 named other sources. Use exact loaded
provider paths from the same terrain and level. This joins whole meshes without
welding; returns `retained_source_id`, `consumed_source_ids` and per-source
`triangle_maps` containing `{old_id,new_id}` entries. It does not save or build.

Inputs must match priority/layer, disabled state, runtime grid and spatial flag,
and have compatible indexed UV/normal/color and legacy vertex layouts. Materials
use a deterministic union; named weight/polygroup channels are reconciled by name,
with zero for missing channels. Extra authored components, child actors, unsupported
memberships/attributes and nonuniform transforms are refused. Inspect help for
budgets; do not strip user components merely to satisfy the guard.

After full preflight, ordinary `execute_script` commits earlier edits AND created
assets as an in-memory checkpoint, runs merge in its own transaction, then starts
a fresh rollback window. A later Lua error affects only later edits; it cannot
undo the earlier checkpoint or successful merge. External nested transactions
are refused. Failure recovery uses verified native Undo; a compensated failure
leaves the failed-operation Redo record available. Do not blindly Redo/retry an
incomplete recovery: inspect the reported present/missing source identities first.

## Definition, pipeline and material assets

Use generic `open_asset` access for definition properties and the typed pipeline
methods from `MeshTerrainAssets`. Transformer entries are instanced structs,
not UObject children. Discover available types and property names through help
and pipeline listing; do not guess script type paths or attempt object spawning.

For renderable terrain with collision, configure and save the appropriate
compiled build variant with StaticMesh and Collision transformers. A successful
preview alone does not establish that either compiled output exists.

For a channel-driven material, find the native UI node names
`MeshPartitionResource` and `MeshPartitionChannelSample`. Connect the resource's
`Channel Texture` output to the sample's `ChannelTextureInput` input. Set
`MeshPartitionDefinition` and `Channel` (for example `Grass`) on the channel
sample. A channel name on an unconnected sample does not establish usable data.
Use ordinary material graph bindings for connections, compilation and readback.

`set_node_property` vector values use Unreal text syntax, such as
`"(X=1,Y=0,Z=0)"`, rather than a Lua table. Do not generalize the structured
property tables accepted by terrain configuration to every graph setter.

## Attach lake and river carving

Read `help('WaterTerrain')` and the ordinary Water catalog. The separate bridge
requires enabled Water, MeshPartition and MeshPartitionWater on UE5.8. Reuse an
existing body and loaded terrain in the same level. Record exact actor paths:
`water_spawn` returns a label, which must be resolved through actor discovery.
An attachment is a component owned by the water body, not another terrain actor.

Prepare valid geometry before `water_terrain_attach`: closed lake with at least
three points, open river with at least two, and matching river width/depth data.
The bridge requires Width-mode falloff and positive finite falloff/ramp widths.
Inspect reflected settings and stage complete structs before attachment when the
ordinary Water tools do not expose a required field. Native Ocean carving has no
deformation operation and is refused. Do not treat the bridge as Ocean support.

Inspect `effective_enabled`, priority and definition priority layer. Both Water's
`affects_landscape` and the modifier's own disabled flag influence carving.
Repeated identical attachment returns the existing component; use its returned
path with `water_terrain_info/configure/detach`. Detach preserves the water actor.
Lake depth uses `water_set_landscape`; river depth is per-point metadata changed
through `water_river_set_point`. Generic edits after attachment are not guaranteed
to preserve the bridge's stricter preconditions. Repair malformed bodies before
enabling them; exact `{disabled=true}` supports recovery without evaluating their
current bounds. Undo restores the previous enabled flag, not malformed geometry.

Raw base `sample` does not include downstream Water deformation. To verify a
carve, inspect evaluated geometry; a fresh zero-edit projected sculpt above the
Water modifier can serve as an owned temporary probe. Check the old footprint
after movement or disable, and remove only the probe you created. Save with
`save_all_levels`, genuinely unload/reload the map, then change depth again to
verify continuing notifications. Compiled/runtime Water output needs separate
build and runtime checks; preview carving alone does not establish it.

For compiled Water, use `water_subsystem_info({world='pie',
terrain_diagnostics=true,limit=32})` while PIE is active. It reads the actual
Water terrain registry, sorted primitive paths and zone associations. Inspect
counts and truncation flags before interpreting absence. Missing PIE fails
instead of falling back to the editor. Texture presence/dimensions establish
allocation only; they do not prove rendered WaterInfo depths. Compare terrain
collision while ignoring WaterBody actors so a water surface cannot masquerade
as the carved floor.

For actual WaterInfo depth diagnostics, `water_info_sample(zone_path,
{world='pie',samples={{x=0,y=0}}})` reads1..8 world XY points and returns raw RGBA,
pixel-center coordinates, native normalization bounds and decoded heights.
Compare collision at the returned centers. This is an explicit synchronous GPU
readback, currently D3D12/static single-slice zones with UE5.6+ capture-center
validity; it refuses native staging allocations above64MiB. Read the error if
unsupported. Six retained-fixture pixels matched terrain collision within0.25cm,
but an allocated texture or elapsed wait does not establish rebuild freshness.
The diagnostic reports that limitation; do not infer water coverage from a
normalized height channel alone.

If a builder reports zero source modifiers, inspect World Partition external
actor packages and descriptors after save/reload. Fresh WP creation now enables
external packaging. For a preexisting map requiring conversion, the explicit
`partition('enable',{convert_actors=true})` operation converts its actors;
understand that map-wide change before using it. Do not count a zero-section
commandlet exit as successful terrain compilation.

## Build and observe runtime output

Visual quality is an acceptance requirement for landscape creation. A successful
tool call or valid collision mesh does not prove a finished scene. Establish
coherent large landforms before small brush detail, soften unintended stamp
boundaries, and choose materials, texture scale, lighting and cameras together.
Inspect actual engine renders from a wide view, an elevated view and near ground
level. Fix visible tears, spikes, repeated circular brush marks, faceted
silhouettes, stretched materials and broken shading before describing a scene as
finished. Keep and reuse one reference map in the project for visual
regression. Screenshots must show actual Mesh Terrain output, not substitutes.
For world-aligned normal textures, explicitly match output space to the material:
the installed `WorldAlignedNormal` function defaults its `WorldSpace` switch to
false. A material with `tangent_space_normal=false` needs that function input
explicitly true. The function's name alone does not establish its output space;
a mismatch produces visibly incorrect shading.

Save source actors, definition, pipeline, material and map before a managed
build. The build requires clean packages and an idle editor world outside PIE;
read its preflight errors instead of silently saving unrelated user work.

Run the managed build with the requested mode; `mode='full'` forces regeneration.
Poll its status in later tool calls until a terminal state and inspect the
engine summary. A successful process launch or zero exit code alone is not
proof of nonempty terrain output. One verified fixture's full build took about
41 seconds; actual duration depends on the user's terrain and machine.

A UE 5.8 resection fixture exposed an engine `ReferenceBoxProject` channel-UV
packing failure (`TMatrix2::Inverse`, followed by generated UV NaNs). A nonzero
commandlet exit is a failure even when its summary reports built sections.
Preserved source UVs alone do not validate generated channel UVs: this layout
regenerates them. An isolated definition using public `PlaneProject` with a
fixed diagonal normal (-1,0,1) built the verified horizontal/vertical fixture
and produced finite, nonzero-area channel UV0 and correct collision. That normal
is specific to that geometry. Choose and verify a projection suitable for the
requested surfaces; perpendicular faces can collapse and projected regions can
overlap. Do not silently change a shared definition or select hidden layout
modes. The default packing failure remains unresolved.

For owned diagnostic-asset cleanup through Python, use
`execute_python(code,{transaction='isolated'})` around native asset deletion.
It commits earlier work and gives subsequent Lua edits a fresh rollback window;
native force deletion can clear Undo history and remains irreversible. Default
segment execution now stops if a native operation unexpectedly resets that
transaction. Verify owned asset absence and later edit rollback separately.

The build does not globally lock editor writes. Keep this workflow from changing
its map/inputs while the child builder runs, and use the managed cancel/status
path for recovery. Reload through the documented workflow after completion.
An editor may report zero loaded compiled actors while PIE streams one or more;
inspect runtime results rather than treating that count difference as failure.

Call `playtest_preflight()` before starting PIE. UE 5.8 can retain a generated
Nanite PIE mesh through its persistent missing-SM6 notification and crash when
PIE ends. The guard refuses demonstrated risk; do not suppress the notification
or GC assertion. Configure the required SM6 target and restart on capable hardware,
or use appropriate non-Nanite output. This is a project rendering decision.

Native terrain StaticMeshTransformer output disables mesh distance fields in
the verified workflow. If the requested Lumen setup relies on those fields,
inspect its hardware ray tracing requirements; changing a light alone may not
fix dark indirect lighting. Do not impose fixture rendering settings on unrelated work.

Verify the requested result with multiple views, channel samples and runtime
collision/walking checks. The current workflow has passed native/Lua checks,
a painted Boolean cut, and character walking on flat terrain and near the cut;
these are examples of evidence, not universal dimensions or complete coverage.
Heightmap resampling/channel import, section topology, full brush parity and
cooked output remain incomplete. Check current help and report the specific missing operation rather
than substituting Landscape or claiming screenshots prove packaged-game behavior.

### Hole caps

Discover a current `boundary_edge_id` with `source:boundary_edges()` and pass it
to `source:fill_hole`. The default `method="planar"` closes a planar loop.
Use `method="ear_clip_3d"` for a nonplanar loop; omit `plane_tolerance` in that
mode. Both retain every existing vertex and require a simple, nondegenerate
projection into the explicit UV frame. Neither smooths the boundary. Supply a world
frame (origin, normal, x_axis), material_slot, triangle_group, all named
polygroup values, one UV scale_cm/offset per source UV layer, and RGBA color.
Use source help for the exact schema. The planar tolerance defaults to 0.01cm.
Cap tangent frames follow actual per-triangle UV derivatives. Minimal and
smooth caps remain unsupported; do not substitute a different cap for those requests.

Use `method="fan"` to add the native arithmetic-mean boundary center and one
triangle per boundary edge. A three-vertex loop uses the native single-triangle
special case and adds no center. Omit `plane_tolerance`; the simple projection
must place the arithmetic center strictly inside the polygon kernel. The binding
refuses unsuitable geometry rather than relocating that center. Inspect
`added_vertex_ids` (empty for other methods and three-vertex fans).

Fan `center_attributes="boundary_average"` is the default binding policy:
new-center legacy color/UV/named paint channels average the boundary values;
legacy normals use a normalized mean with an area-weighted geometric fallback.
Choose `"native_defaults"` explicitly to retain native appended-vertex defaults,
including zero painted weights. These policies affect only a newly added center;
the return value echoes the policy even for the triangle special case. Existing
vertices remain exact, and explicit cap overlay options apply under either policy.
Other methods reject `center_attributes`.

Existing vertex channels are inherited by the cap, not independently painted
per face. Cap UV/normal/tangent/color overlays are separate from adjacent seams.
If the source has no color overlay, the operation adds one, retaining legacy
vertex colors or implicit white on old faces; `color_overlay_created` reports
this layout change. An empty material list supports implicit default slot0.
Save and compile explicitly, then verify collision over the cap and misses at
holes intentionally left open. The primitive supports ordinary Undo/Redo;
invalid/intersecting geometry is rejected on a detached mesh before commit.

### Native minimal and smooth caps (worker jobs)

`method="minimal"` and `method="smooth"` run Unreal's own hole fillers. Those
algorithms have unchecked internal solver/remesher outcomes, so they never run
inside the editor process. `source:fill_hole` with either method snapshots the
source, writes a bounded request, and launches one disposable
`UnrealEditor-Cmd.exe -NeoStackMeshWorker` child under a job object with an
explicit memory limit and timeout (`memory_mb` 1024..16384, default 4096;
`timeout_seconds` 30..1800, default 300). It returns a job table immediately
and edits nothing. Supply the same explicit cap attributes as other methods;
`plane_tolerance` and `center_attributes` are refused. Native settings go in a
`minimal={...}` or `smooth={...}` table matching the method (see help text).

Poll `mesh_terrain_edit_status(id)` until `finished`. A `ready` job holds
validated output that has not touched the source; `mesh_terrain_edit_apply(id)`
commits it through the same staged, transactional, Undo-capable path as the
synchronous caps and refuses if the source geometry, transform, materials or
world changed since queueing. `mesh_terrain_edit_cancel(id)` terminates a
running child or discards ready output. Only one job exists at a time.

Verified behavior: a worker round trip takes about five seconds on the test
machine; the parent reads the durable response file and then reaps the child,
so `exit_code` 1223 (cancelled after response) is normal alongside 0. Minimal
adds no vertices on a simple loop; smooth adds interior vertices whose painted
channels start at native zero, not an average. Paint the new vertices after
applying if the cap must match surrounding weights. Unrestricted smooth
(`constrain_to_hole_interior=false`) may move neighboring geometry; the result
is validated for intersections and closed boundaries before commit. Compile and
verify collision over the cap afterwards as with any other topology edit.

### Grounding props and instanced cover

`source:sample({position,...})` returns the nearest **vertex** in 3D. It is useful
for inspecting vertex channels, but it is not a vertical surface hit. Keeping
an object's original XY while copying that vertex's Z can float or bury it on a
slope. For props and grass, intersect a world-down ray with actual terrain
triangles and verify the runtime compiled collision separately.

The verified Python path uses `GeometryScript_MeshSpatial.build_bvh_for_mesh`
once for a batch, then `find_nearest_ray_intersection_with_mesh`. Its rays use
mesh-local coordinates: convert both origin/direction and returned positions
when the source transform is not identity. Keep the BVH tied to the unchanged
mesh and release mesh/actor references before switching maps. Surface normals
can be obtained from the hit triangle; Unreal's native geometric normal uses
edge2 cross edge1. Reject misses, nonfinite outputs and slopes inappropriate
for the requested cover. Root burial is an explicit art choice, not a substitute
for correct sampling.

Use one `open_level():add('instances',{mesh=...,transforms=...,label=...})` HISM
actor for bounded local cover. It validates 1..32768 ordered transforms, returns
exact actor/component paths, and supports ordinary Undo. Configure collision,
shadows and finite culling distances intentionally; verify all transforms and
settings after disk reload. Larger streamed areas should use the appropriate
foliage/partition workflow. Placed instances do not automatically follow later
terrain edits: regenerate or resample owned placements after changing terrain.

A small grass clump can be authored with actual blade triangles through
`GeometryScript_MeshEdits.append_buffers_to_mesh`, then saved with
`GeometryScript_NewAssetUtils.create_new_static_mesh_asset_from_mesh`. These
are the reflected Python names, distinct from the C++ class names. Use owned
asset paths, explicit normals/UVs/colors and explicit collision/Nanite options.
Asset creation and placement have separate lifecycles; actor Undo does not
remove saved assets. Native asset-creation failure is not proven atomic.

Judge ground cover in a low camera view as well as the overview. Correct
placement, a saved instance count and a passing build do not prove visual
quality. Check silhouette, density, repeated patterns, shading and grounded
roots. Compare the same camera with cover hidden/visible when measuring cost;
report the sample's scope rather than extrapolating to packaged performance.

### Composing a landscape that reads well

Passing geometry tests is not visual acceptance. Judge a composed landscape
from rendered PIE captures at three cameras (wide hero, eye-level verge,
elevated look-down), and apply changes such as these:

* Heightfield: generate numerically at the import budget (255 samples over
  480 m, 65025 vertices) with domain-warped coordinates before every mountain
  term, two large masses with wandering crest axes, ridged spurs only where a
  mass is present, low-frequency base hills, a few noisy drainage channels that
  deepen uphill and merge into the corridor, and a talus apron near the floor.
  Uniform ridged noise reads as a web; periodic tributaries read as gullies.
  Preview the 8-bit heightfield before any editor round trip.
* World edge: a small terrain shows a fog wall from any elevated camera. Add a
  coarse backdrop terrain (129 samples over 1.9 km) that stays below the inner
  terrain inside its footprint, continues the inner border heights outward,
  and rises into distant ridges. Share the definition/material and fade the
  corridor mask past the inner edge so the track does not continue.
* Cover: ground every instance with a vertical ray against the source mesh
  (one BVH per batch), reject steep slopes, vary scale per instance, tilt scree
  to the surface normal, and bias density toward the verges. Keep cover in a
  few `add('instances')` actors with explicit cull distances; 9000 grass and
  220 scree instances cost well under a millisecond on the test machine.
* Material: slope rock from about 14 to 40 degrees, an altitude tint (richer
  valley green, dry straw on heights), macro-variation noise shared across
  color/normal/roughness, and no additional texture samples.
* Atmosphere: low warm key light (pitch about -21), brighter sky fill, thin
  height fog (density about 0.004) with a cool inscattering tint.

Judge the result from all three cameras and record what still falls short;
this scene remains stylized, not photoreal.

## Definition channels and physical materials

Paint channels are declared on the terrain definition asset. Add them there
first, then declare them on the sculpt source you paint with:

```lua
local definition = open_asset(definition_path)
assert(definition:add("channel", { name = "Rock" }))
assert(definition:add("physical_material", { channel = "Rock",
  material = "/Game/Physics/PM_Rock", minimum_weight = 0.5 }))
print(#definition:list("channels"), definition:terrain_info().channels[1])
local src = create_mesh_terrain_sculpt(terrain_path, { channels = { { name = "Rock", method = "add" } } })
assert(src:paint({ channel = "Rock", value = 0.75, mode = "set", radius = 300, strength = 1 }))
assert(math.abs(src:sample({ position = center, channel = "Rock" }).weight - 0.75) < 1e-4)
assert(definition:save())
```

Names are alphanumeric/underscore and unique; a channel referenced by a
physical-material entry cannot be renamed or removed until that entry is
removed. `configure("channel", { name=, rename= })` renames. Save the
definition explicitly; a rebuild (`mesh_terrain_build`) is what makes the
channel reach the compiled material.

## Any modifier family by kind

Discover what the engine offers, then create by kind with reflected settings:

```lua
local terrain = open_mesh_terrain(terrain_path)
for _, t in ipairs(terrain:list("modifier_types", { limit = 100 }).items) do
  if t.creatable then print(t.kind, t.mesh_based, t.properties.TargetEdgeLength) end
end
local remesh = assert(terrain:add("remesh", { properties = { TargetEdgeLength = 250, SmoothingStrength = 0.5 } }))
local stamp  = assert(terrain:add("mesh_project", { mesh = "/Game/Props/SM_Rock" }))
assert(terrain:configure_modifier(remesh.id, { properties = { TargetEdgeLength = 125 } }))
assert(terrain:configure_common(stamp.id, { location = { x = 500, y = 0, z = 200 }, priority = 5 }))
assert(terrain:remove(remesh.id))
```

Numbers must be Lua numbers, booleans Lua booleans, enums their names, and
vectors `{x=,y=,z=}` tables; a bad key or value refuses the whole call and
leaves the modifier untouched. `noise` and `boolean` keep their typed verbs.
Read `list("modifiers")[i].properties` back before claiming a setting stuck,
and build (`mesh_terrain_build`) to see the compiled effect.

## Selecting and clearing sculpt layers

Every sculpt on a projected source lands on its active layer. Select it
explicitly instead of relying on the last add/move:

```lua
local src = open_mesh_terrain_source(source_id)
assert(src:set_active_layer({ index = 2 }))        -- one-based editable layers; 0 (base) is refused
assert(src:info().active_layer == 2)
assert(src:clear_layer({ index = 2 }))             -- zero the whole layer, keep its name and weight
assert(src:clear_layer({}))                        -- default: the active layer; empty layers are a no-op
```

Both operations are transactional (Undo restores the previous selection and
the exact heights). Use `erase_layer` for a localized brush erase and
`remove_layer` to drop the layer itself.

## PCG scatter on the terrain

Use PCG for procedural cover instead of hand placement: the `neostack-pcg`
skill has the verified `Mesh Partition Query -> Surface Sampler -> Static Mesh
Spawner` recipe. Size the PCG host's box component from
`open_mesh_terrain_source(id):info().bounds` (the terrain actor itself reports
empty bounds because sections are streamed), pin the query with
`QueryParams="(MegaMeshOverride=<terrain actor path>)"` when a backdrop
terrain overlaps, and prove the result with `pcg:list("resources")` plus a
downward trace per instance.
