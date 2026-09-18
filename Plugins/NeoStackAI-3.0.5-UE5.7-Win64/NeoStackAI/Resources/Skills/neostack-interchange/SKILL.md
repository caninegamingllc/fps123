---
name: neostack-interchange
description: Import FBX, OBJ, glTF or USD meshes through Unreal's Interchange pipeline with validated collision and LOD options, choose the importer explicitly, and preview LOD/collision counts with interchange_plan_import before touching /Game. Use when an agent must import a mesh with specific collision, LOD or physics-asset settings, or check what an import would create.
---

# Interchange mesh import through NeoStack Lua

Domain docs: `help("AssetImport")` (prints its own output, do not wrap in `log`).
The mesh verbs are `import_mesh`, `import_skeletal_mesh`, `import_asset`,
`import_assets`; the dry run is `interchange_plan_import`; inspection is
`open_asset(path):info()` and `get_import_data(path)`.

## Plan first, then import

```lua
local src = engine_dir() .. "Content/FbxEditorAutomation/LOD_StaticMesh.fbx"
local opts = { importer = "interchange", import_lods = true, collision = true, collision_type = "Convex18DOP" }

-- 1. Dry run: nothing is created under /Game.
local plan = interchange_plan_import(src, opts)
assert(plan.ok, plan.error)
for _, node in ipairs(plan.factory_nodes) do
    log(string.format("%s %s lods=%d collision=%d", node.class, node.label, node.lod_data_count, node.collision_total))
end

-- 2. Import with the same opts; the result echoes what landed on the asset.
local r = import_mesh(src, "/Game/Imports/Props", opts)
assert(r.success, r.error)
local mesh = r.meshes[1]                      -- {path, class, lod_count, has_collision, collision_shapes, ...}
assert(mesh.lod_count == plan.factory_nodes[1].lod_data_count)

-- 3. Cross-check through the asset handle and the recorded import data.
local info = open_asset(mesh.path):info()     -- lod_count, collision_shapes, collision_complexity
local data = get_import_data(mesh.path)       -- data.interchange.mesh_factory_nodes[1].lod_data_count
```

## Options that are validated before anything is written

`collision` (bool), `collision_by_name` (bool, UBX_/UCP_/USP_/UCX_ prefixes),
`one_convex_hull_per_ucx` (bool), `collision_type`
(`Box|Sphere|Capsule|Convex10DOP_X|Convex10DOP_Y|Convex10DOP_Z|Convex18DOP|Convex26DOP|None`),
`force_collision_primitives` (bool, UE 5.6+), `import_lods` (bool), `lod_group`
(string), `lod_screen_sizes` (`{1.0, 0.5, 0.25}`), `create_physics_asset`
(bool, skeletal), `importer` (`'auto'|'interchange'|'legacy'`).

A wrong type, an unknown enum name or an option the chosen importer cannot
honour returns `{success=false, error="option 'collision_type' has unknown
value ..."}` and imports nothing. Fix the option and call again; there is no
partial state to clean up.

The older aliases still work alongside: `type='static'|'skeletal'`,
`rotation={pitch,yaw,roll}`, `translation={x,y,z}`, `scale`, `skeleton`,
`build_nanite`, `save`, `name`, `replace`, and `properties={bImportSockets=false, ...}`
for any other UPROPERTY on the pipeline chain (an explicit property name wins
over an alias).

## Choosing the importer

- `importer='auto'` (default) keeps the historical behaviour: FBX through
  `UFbxImportUI` (the engine converts it to Interchange internally), everything
  else through NeoStack's Interchange pipeline. If you pass any collision/LOD
  alias for an FBX, `auto` upgrades to `interchange` and `result.note` says so.
- `importer='interchange'`: always NeoStack's `UInterchangeGenericAssetsPipeline`.
  Use this for reproducible 5.7 vs 5.8 comparisons. `import_materials`,
  `import_textures`, `import_animations`, `import_mesh`, `type`, `skeleton`,
  `build_nanite`, `rotation`/`translation`/`scale` and `properties` are mapped
  onto the pipeline; the FBX-only keys `auto_generate_collision`,
  `combine_meshes`, `convert_scene`, `convert_scene_unit`, `force_front_x_axis`,
  `transform_vertex_to_absolute`, `bake_pivot_in_vertex` are dropped and named
  in `result.note` (`ignored on the interchange route: ...`). Read the note.
- `importer='legacy'`: forces `UFbxFactory` (FBX only). Only `collision`,
  `import_lods`, `lod_group`, `create_physics_asset` apply; the rest refuse.
  Without `type=` the factory detects static vs skeletal from the file, so
  `import_mesh(skeletal.fbx, dest, {importer='legacy', create_physics_asset=true})`
  yields a `SkeletalMesh` with a physics asset.
- `import_assets` with files that route differently reports `importer='mixed'`
  and `routes = {[file] = 'auto'|'interchange'|'legacy'}`; check `routes` when
  a batch mixes `.fbx` and `.glb`.

## Collision recipes

```lua
-- No collision at all
import_mesh(src, dest, { importer = "interchange", collision = false })

-- Author-supplied collision meshes only (UCX_/UBX_ names), one hull per UCX mesh
import_mesh(src, dest, { importer = "interchange", collision = true, collision_by_name = true, one_convex_hull_per_ucx = true })

-- Fallback primitive when the file carries no collision meshes
import_mesh(src, dest, { importer = "interchange", collision = true, collision_type = "Box" })
```

Check the outcome with `r.meshes[1].collision_shapes` or
`open_asset(path):info().collision_shapes`; reconfigure later with the static
mesh handle's `configure("collision", ...)`.

## LOD recipes

```lua
-- Import LODs from the file and pin screen sizes
import_mesh(src, dest, { importer = "interchange", import_lods = true, lod_screen_sizes = { 1.0, 0.5, 0.25 } })

-- Single LOD, assigned to a LOD group
import_mesh(src, dest, { importer = "interchange", import_lods = false, lod_group = "LevelArchitecture" })
```

`plan.factory_nodes[i].lods` lists what each LOD would be built from
(`mesh_count`) and its collision counts before you import.

## Skeletal meshes

```lua
local r = import_skeletal_mesh(src, dest, { importer = "interchange", create_physics_asset = true, skeleton = "/Game/Chars/SK_Hero_Skeleton" })
local skel = r.meshes[1]   -- class='SkeletalMesh', lod_count, physics_asset, physics_bodies
```

## Gotchas

- Import is not transactional. Undo does not remove imported packages; use
  `delete_asset(path)` to roll back.
- `interchange_plan_import` refuses while another import is running (FBX
  translation is single-flight). Wait, then retry.
- The plan reports the pipeline's intent; the imported asset can still differ
  when a build step changes things (Nanite, LOD group settings). Compare
  `plan.lod_data_count` with `r.meshes[1].lod_count` and treat a mismatch as a
  finding to report.
- `lod_group` on `import_texture` is the texture group alias; the mesh alias
  only applies to the mesh verbs.
- OBJ collision-by-name pairing (`o UCX_Cube` next to `o Cube`) is not yet
  confirmed live; if the plan shows two factory nodes and zero convex entries,
  fall back to `collision_type` for generated collision.

## Failure table

| symptom | cause / fix |
| --- | --- |
| `error` mentions `collision_type` | unknown enum name; use one of the names above |
| `error` mentions `lod_screen_sizes` | non-number entry or empty table |
| `unsupported: importer='legacy' ... .obj` | legacy is FBX-only; drop `importer` or use `'interchange'` |
| `unsupported: force_collision_primitives needs UE 5.6+` | engine too old; omit the key |
| `plan.ok=false`, "an Interchange import is currently running" | wait for the running import |
| `interchange_plan_import` is `nil` | InterchangeImport module not loaded; enable the Interchange plugin |

Escape hatches: `help("AssetImport")`, `open_asset(path):help()`,
`get_import_data(path)`, `report_issue()`.
