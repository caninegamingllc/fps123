---
name: neostack-dynamic-wind
description: Convert Pivot Painter 2 trees into wind-ready skeletal meshes, author Dynamic Wind skeletal data, set and read back per-world wind (direction/speed/amplitude/texture) and place wind-driven instanced foliage through `execute_script` on UE 5.7+ projects with the experimental DynamicWind plugin. Use for "make this tree react to wind", "convert a Pivot Painter mesh", "set the wind direction", "import wind bone groups" or "place wind foliage".
---

# Dynamic Wind through `execute_script`

Start with `dynamic_wind_status()`; it never errors and tells you whether the
plugin is enabled, whether the reflection contract resolved and whether the
editor world has a wind subsystem. `help("DynamicWind")` prints every
signature. Lua state is fresh on every call, so re-open assets by path. Every
verb returns a table with `ok`; on failure `error` says what to fix and
nothing was mutated.

## The pipeline (convert -> import -> place -> set)

```lua
local status = dynamic_wind_status()
assert(status.supported, status.reason)

-- 1. Pivot Painter 2 static mesh + pivot texture -> skeletal mesh + skeleton.
--    uv_index is the 0-based mesh UV channel holding the pivot UVs and is required.
local tree = dynamic_wind_convert(
  "/Game/Trees/SM_Oak", "/Game/Trees/T_Oak_PivotPos",
  { uv_index = 2, output = "/Game/Trees/SK_Oak" })
assert(tree.ok, tree.error)
log(tree.bone_count, tree.bones.trunks, tree.bones.branches, tree.bones.leaves)

-- 2. Wind bone groups. auto_groups maps Trunk* -> 0 (trunk), Branch* -> 1, Leaf* -> 2.
local wind = dynamic_wind_import(tree.skeletal_mesh, { auto_groups = true })
assert(wind.ok, wind.error)
local data = dynamic_wind_skeletal_data(tree.skeletal_mesh)
assert(data.present and data.enabled)

-- 3. Place instances driven by a DynamicWindData transform provider.
local placed = dynamic_wind_place(tree.skeletal_mesh, {
  label = "Oaks", location = { x = 0, y = 0, z = 0 }, count = 5, spacing = 600 })
assert(placed.ok, placed.error)

-- 4. Wind for the editor world; read it back.
assert(dynamic_wind_set({ direction = { x = 1, y = 0.2, z = 0 }, speed = 30, amplitude = 0.8 }).ok)
local now = dynamic_wind_get()
log(now.source, now.parameters.speed, now.blended_amplitude)
```

Verify in a fresh call: `dynamic_wind_skeletal_data(path)` for the asset,
`get_actor_properties(label)` for the placed actor, `dynamic_wind_get()` for
the wind. Save the assets you want to keep with the normal save verbs; the
converter only marks them dirty.

## Hand-authored groups and JSON files

```lua
-- Inline joints (bone names as printed by list("bones") on the skeleton).
dynamic_wind_import("/Game/Trees/SK_Oak", {
  joints = { { joint = "Trunk", group = 0 }, { joint = "Branch", group = 1 }, { joint = "Leaf", group = 2 } },
  simulation_groups = {
    { influence = 1, is_trunk = true },
    { dual_influence = true, min_influence = 0.2, max_influence = 0.9, shift_top = 0.4 },
    { influence = 1 },
  },
  ground_cover = false, gust_attenuation = 0.25,
})
-- Or the engine's JSON contract (same keys as the editor's "Import from file"):
-- {"Joints":[{"JointName":"Trunk","SimulationGroupIndex":0}],
--  "SimulationGroups":[{"Influence":1.0,"bIsTrunkGroup":true}],"bIsGroundCover":false,"GustAttenuation":0.0}
dynamic_wind_import("/Game/Trees/SK_Oak", { file = "C:/Projects/MyGame/Content/Wind/oak.json" })
-- Tune later without re-importing:
dynamic_wind_set_skeletal_data("/Game/Trees/SK_Oak", { enabled = true, gust_attenuation = 0.5 })
```

## Gotchas

- Bone names: the converter numbers with `FName("Trunk", N)`, so the names are
  `Root`, `Trunk`, `Trunk_0`, `Trunk_1`, ... (the first trunk has no suffix).
  `auto_groups` matches on the plain name, so it works either way.
- `uv_index` is checked against the mesh's source UV channels and the pivot
  texture's parent graph before the engine runs. A refusal that mentions
  "parent" or "pivot" means the channel or texture is not the Pivot Painter
  set — try the other channel (the engine sample's texture is named `*_UV_2`).
- The pivot texture must keep editor source data; cooked/stripped/virtual
  textures are refused. Pivot Painter EXR (RGBA16F) is the intended input.
- `dynamic_wind_set` is per world (`{world='pie'}` for a running PIE) and only
  updates the keys you pass. `dynamic_wind_get().source == 'default'` means
  nothing was set this session; `'cached'` values can be stale after a scene
  recreate (UE 5.8 resets the provider silently) — just set again.
- `blended_amplitude` is -1 until the wind provider has simulated a frame.
- `dynamic_wind_place` refuses meshes without enabled wind data; pass
  `require_wind_data = false` to place anyway (you get a `warning`).
- The default `data_asset` (`<mesh package>_WindData`) only applies to meshes
  under `/Game`; for `/Engine` meshes pass `data_asset = "/Game/.../X_WindData"`.
- Visible motion on a non-Nanite instanced skinned mesh is not guaranteed by
  the plugin ("wind for Nanite foliage"); `nanite = true` on convert flips the
  setting and triggers a rebuild.
- `DynamicWind.Enable` is a read-only CVar: when a project turns it off, no
  subsystem exists and set/get/place report `unsupported`.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `status.supported == false`, reason mentions the plugin | Enable `DynamicWind` in the .uproject (UE 5.7+) and restart the editor. |
| `reflection contract changed` | The engine's private Blueprint library changed; report it, do not retry. |
| `uv_index ... out of range` | Pass the 0-based channel that holds the pivot UVs (see `open_asset(mesh):info()` UV channel count). |
| `pivot N has parent pivot M that no vertex references` | Wrong `uv_index` or the texture is not the `*_PivotPos_a_ParentIndexInt` map. |
| `already exists in memory` | `delete_asset(path)` the previous output (or pick another `output`). |
| `unknown joint(s)` with `unknown_joints` listed | Use `open_asset(mesh):list("bones")` names; the engine would silently skip them. |
| `has no Dynamic Wind skeletal data` | Run `dynamic_wind_import` before `dynamic_wind_set_skeletal_data` / `dynamic_wind_place`. |
| `UDynamicWindSubsystem is absent` | `DynamicWind.Enable=false` in an ini, or the plugin is disabled. |

Discovery: `help("DynamicWind")`, `dynamic_wind_status()`,
`dynamic_wind_skeletal_data(path)`, `open_asset(skeleton):list("bones")`,
`report_issue()` when the engine converter refuses with a log-only reason.
