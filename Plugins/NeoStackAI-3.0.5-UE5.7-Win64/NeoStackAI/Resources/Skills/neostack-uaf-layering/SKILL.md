---
name: neostack-uaf-layering
description: Author UAF Layer Stack assets (UE 5.8 UAFLayering plugin) and drive layer enable/disable/weight events on a running character through `execute_script`. Use when a project layers animation sequences, blend spaces or UAF graphs on top of a base pose and needs the stack created, configured, bound to an actor and verified in Play In Editor.
---

# UAF layer stacks through `execute_script`

Start with `help("UAFLayering")`. If `uaf_layerstack_support` is not a
function the editor is on UE < 5.8 or the UAFLayering plugin is disabled;
`uaf_layerstack_support().reason` says which. Lua state is fresh every call:
re-open assets by path and keep layer names (or indices) from earlier calls.

## Index convention (matters for every ByIndex call)

`info().layers[1]` is the base layer at engine index 0. The first real layer
is index 1. The base layer has no blend provider, so runtime events on index
0 are refused (`status = "base_layer"`); use names or indices >= 1.

## Work in this order

1. **Create the stack.** `create_asset(path, "UAFLayerStack")` (alias
   `uaflayerstack`). The engine factory adds the editor data, an empty base
   layer and compiles. `open_asset(path)` gives the enriched handle.
2. **Add layers.** `uaf_layerstack_add_layer(path, { name = "Walk", asset =
   "/Game/Anim/AS_Walk" })`. Content must be an AnimSequence, BlendSpace or
   UAFAnimGraph. Defaults: weight 1, enabled, `blend_mode = "Blend"`,
   0.5 s blend in/out. Set `blend_in_time = 0, blend_out_time = 0` when you
   want instant readback.
3. **Configure.** `uaf_layerstack_configure_layer(path, "Walk", { weight =
   0.25, blend_mode = "Additive" })`. `enabled = false` is a runtime-toggleable
   default; `state = "Disabled"` removes the layer from the compiled graph
   and runtime enable cannot bring it back (use `PreviewDisabled` to hide it
   only in the editor preview).
4. **Compile and check.** `uaf_layerstack_compile(path)` then
   `uaf_layerstack_info(path)` (`compilation_state` must be
   `CompiledWithSuccess`).
5. **Bind to an actor.** `uaf_layerstack_bind_component(actor_label, {
   layer_stack = path, mesh = "/Game/Characters/SK_Body" })` creates the
   skeletal mesh and UAF components when missing. Editor-world systems stay
   paused unless `init_method = "InitializeAndRun"`; runtime verbs with
   `world = "editor"` refuse any other init method with `system_paused`.
6. **Drive it in PIE.** `playtest_start({ mode = "pie" })`,
   `playtest_wait_for_pie()`, then `uaf_layerstack_set_weight(actor, "Walk",
   0)`, `uaf_layerstack_disable(actor, 1)`, `uaf_layerstack_enable(actor,
   "Walk")`. Every event is validated against the asset because the engine
   silently ignores mismatched names.
   Weight events apply when the layer next activates and weight 0 deactivates
   it. To change a running layer's weight: `uaf_layerstack_set_weight`, then
   `uaf_layerstack_disable`, wait a tick, `uaf_layerstack_enable`.
7. **Verify.** `playtest_wait(1.0)` (longer than the blend time), then
   `uaf_layerstack_read_pose(actor)`; `max_ref_delta_translation` near 0
   means the reference pose, larger means animation is applied.
8. **Persist.** `handle:save()`, then `open_asset(path):info()` in a fresh
   call before claiming success.

## End-to-end example

```lua
local path = "/Game/Anim/LS_Locomotion"
delete_asset(path)
local ls = assert(create_asset(path, "UAFLayerStack"))
local walk = "/Engine/Tutorial/SubEditors/TutorialAssets/Character/Tutorial_Walk_Fwd"
local r = uaf_layerstack_add_layer(path, { name = "Walk", asset = walk, blend_in_time = 0, blend_out_time = 0 })
assert(r.ok, r.error); assert(r.index == 1)
assert(uaf_layerstack_configure_layer(path, "Walk", { weight = 0.5 }).ok)
assert(uaf_layerstack_compile(path).ok)
assert(ls:save())

local label = "LayeredTPP"
assert(open_level():add("actor", { class = "/Script/Engine.Actor", label = label, location = { x = 0, y = 0, z = 0 } }))
local b = uaf_layerstack_bind_component(label, { layer_stack = path, mesh = "/Engine/Tutorial/SubEditors/TutorialAssets/Character/TutorialTPP" })
assert(b.ok, b.error)

assert(playtest_start({ mode = "pie" }).ok)
assert(playtest_wait_for_pie({ timeout = 30 }).ok)
playtest_wait(1.0)
local before = uaf_layerstack_read_pose(label).max_ref_delta_translation
assert(uaf_layerstack_set_weight(label, "Walk", 0).ok)
playtest_wait(1.0)
local at_zero = uaf_layerstack_read_pose(label).max_ref_delta_translation
assert(uaf_layerstack_set_weight(label, 1, 1).ok)      -- same layer by index
playtest_wait(1.0)
local at_one = uaf_layerstack_read_pose(label).max_ref_delta_translation
log(string.format("pose delta: start %.2f, weight 0 %.2f, weight 1 %.2f", before, at_zero, at_one))
assert(at_one > at_zero, "weight 1 must move the pose off the reference")
playtest_stop()
```

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `uaf_layerstack_*` is nil | UE < 5.8 or UAFLayering disabled; check `uaf_layerstack_support()` or enable the plugin (pulls UAF, UAFAnimGraph, Workspace). |
| `status = "duplicate_name"` | Layer names are unique per stack; rename with `configure_layer(path, idx, { name = ... })`. |
| `status = "invalid_request"` with "not a UAF graph-factory asset" | Only AnimSequence, BlendSpace and UAFAnimGraph assets are layer content; static meshes and montages are refused. |
| `status = "base_layer"` | Index 0 / the base layer name was used for remove, blend configure or a runtime event. Use index >= 1. |
| `status = "no_pie"` | Runtime verbs need PIE (`playtest_start`) or `world = "editor"` with `init_method = "InitializeAndRun"`. |
| `status = "no_system"` | Component bound but no system allocated: the stack failed to compile, the asset data is invalid, or the component is unregistered. |
| `status = "system_paused"` | `world = "editor"` on a component whose `init_method` is not `InitializeAndRun` (the result's `init_method` says which): the editor-world system is allocated but paused after its first update, so events are never processed. Rebind with `init_method = "InitializeAndRun"` or drive the layer in PIE. |
| `add_layer`/`configure_layer`/`remove_layer` returned `ok = false` with `status = "compile_failed"` | The edit was applied and is undoable, but the stack no longer compiles (`compilation_state` says how badly); fix the layer content or `configure_layer(..., { state = "Disabled" })` and recompile. |
| `status = "layer_not_compiled"` | The layer is saved with `state = "Disabled"`; set `state = "Enabled"` and recompile. |
| Event returned `ok` but the pose did not change | Events apply next update and blend over the layer's blend time; wait longer than `blend_in_time`/`blend_out_time`, or author them to 0. |
| `status = "pie_active"` on authoring verbs | Stop PIE before editing the asset. |

Discovery: `help("UAFLayering")`, `ls:help()`, `ls:info()`,
`uaf_layerstack_support().layer_content_classes`, `report_issue()`.
