---
name: neostack-nanite-assembly
description: Build Nanite Assembly static meshes from instanced part meshes through `execute_script` (UE 5.7+, NaniteAssemblyEditorUtils plugin). Use when asked to combine several static meshes into one Nanite asset with per-instance transforms, to rebuild or inspect an assembly's parts and nodes, or to check whether the project can build Nanite Assemblies at all.
---

# Nanite Assemblies through `execute_script`

Start with `help("NaniteAssembly")`. Three globals, no handle:
`nanite_assembly_support()`, `nanite_assembly_build(params)`,
`nanite_assembly_info(path)`. Lua state is fresh on every call, so verify in
a second call by path.

## Work in this order

1. **Probe first.** `local s = nanite_assembly_support()`; branch on
   `s.available`. When false, `s.reason` says why: engine older than 5.7,
   plugin sources absent at build time, or `plugin_disabled` (the project's
   `.uproject` must enable `NaniteAssemblyEditorUtils`; it is
   `EnabledByDefault=false`). Do not retry the build in that case, report the
   reason.
2. **Pick parts.** Any loadable, non-transient `StaticMesh` that is not itself
   an assembly. Engine shapes work: `/Engine/BasicShapes/Cube`, `Cylinder`,
   `Cone`, `Sphere`. Parts do not need Nanite enabled themselves.
3. **Build.** One transaction, validated before anything is created:

```lua
local out = "/Game/Props/SM_Crate_Assembly"
local r = nanite_assembly_build({
    parts = {
        { mesh = "/Engine/BasicShapes/Cube", transforms = {
            { location = { x = 0, y = 0, z = 0 } },
            { location = { x = 200, y = 0, z = 0 }, rotation = { yaw = 45 } },
            { location = { x = 400, y = 0, z = 0 }, scale = 0.5 },
        } },
        { mesh = "/Engine/BasicShapes/Cylinder",
          transforms = { { location = { x = 0, y = 200, z = 0 }, scale = { x = 1, y = 1, z = 2 } } },
          material_merge = "slot_names" },
    },
    output = out,      -- package path; the asset name is the last segment
    save = true,       -- write the .uasset now (default false); the save is a checkpoint,
                       -- earlier work in this script stays committed
    -- overwrite = true,        -- rebuild the parts of an existing static mesh
    -- wait_for_compile = true, -- default; blocks until the Nanite build finishes
})
assert(r.ok, r.error)
log(("parts=%d nodes=%d nanite=%s assembly=%s assemblies_supported=%s")
    :format(r.part_count, r.node_count, tostring(r.nanite_enabled),
            tostring(r.is_assembly), tostring(r.assemblies_supported)))
if r.warning then log("warning: " .. r.warning) end  -- r.Nanite.AllowAssemblies=0
```

4. **Verify in a fresh call.** `nanite_assembly_info(out)` returns
   `part_count`, `node_count`, `parts[i].mesh`, `nodes[i].location/rotation/scale`,
   `valid_nanite_data`. `asset_exists(out)` and
   `find_assets("/Game/Props", { class = "StaticMesh" })` confirm the registry
   entry. Call `open_asset(out):list("nanite")` only when
   `nanite_assembly_info(out).valid_nanite_data` is true (see Gotchas).

## Transform tables

`{ location = {x,y,z}, rotation = {pitch,yaw,roll}, scale = {x,y,z} | number }`;
every field optional, `{}` is identity, `translation` is an alias of
`location`. `rotation` also accepts Euler degrees `{x=roll, y=pitch, z=yaw}`
(same shape as `location`), but not both key sets in one table. Unknown keys
and non-numeric values refuse the whole call before anything is created.

## Materials

`material_merge` per part: `identical_materials` (default; same
`UMaterialInterface` shares a slot), `slot_names` (same slot name shares a
slot), `material_indices` (slot i of the part maps to slot i of the
assembly). `material_overrides = { "/Game/M_A", "/Game/M_B" }` replaces the
part's material at that source slot index before merging. The same mesh with
identical merge options coalesces into one part; list it twice with
different options to keep two parts.

## Gotchas

| Symptom | Cause / fix |
| --- | --- |
| `ok=false`, error names `NaniteAssemblyEditorUtils` | Plugin not compiled in or not enabled; check `nanite_assembly_support().reason`. |
| `already exists; pass overwrite=true` | Output path is taken. `overwrite=true` replaces the assembly parts of that static mesh (its base geometry stays). |
| `is itself a Nanite Assembly` | Assemblies of assemblies are not supported by the engine; use the original part meshes. |
| `lives in a transient package` | Parts are stored by path and reloaded during the build; save the part asset first. |
| `not a valid writable package path` | Malformed paths and the engine's read-only roots (`/Temp`, `/Script`, `/Config`) are refused by `IsValidLongPackageName`; `/Engine` (and engine-plugin content) is refused by the verb itself, since the engine mounts it writable. Use `/Game/...`. |
| `assemblies_supported=false` and a `warning` in the result | `r.Nanite.AllowAssemblies` (read-only cvar, default 0) is off: the asset stores the assembly and `is_assembly` is true, but the rendered Nanite resource ignores it (`valid_nanite_data=false`) and the editor shows a toast. Set `r.Nanite.AllowAssemblies=1` under `[ConsoleVariables]` in `DefaultEngine.ini` and restart; it cannot be flipped at runtime. |
| `open_asset(out):list("nanite")` crashes the request on an assembly | The core reader asserts on a Nanite-enabled mesh without a valid Nanite resource (the `assemblies_supported=false` case). Check `nanite_assembly_info(out).valid_nanite_data == true` first; use `nanite_assembly_info` for everything else. |
| `rotation: unknown key 'z'` / `use either {pitch,yaw,roll} or Euler {x,y,z}` | Use one naming set per rotation table: `{pitch=,yaw=,roll=}` or `{x=roll,y=pitch,z=yaw}`. |
| Undo after building a brand-new asset leaves an empty mesh | A package cannot be undone away; delete it with `delete_asset(out)`. Undo/Redo fully restores an `overwrite=true` build on an existing mesh. |
| Build blocks for a long time | `wait_for_compile=true` blocks on the static mesh compiler; pass `wait_for_compile=false` for large parts and read `nanite_assembly_info(out).compiling` later. |

Discovery: `help("NaniteAssembly")`, `nanite_assembly_info(path)`,
`open_asset(path):list("nanite")`, `report_issue()`.
