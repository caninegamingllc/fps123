---
name: neostack-animation-retarget
description: Retarget animation between skeletons with IK Rig and IK Retargeter assets through `execute_script`. Use for building source/target IK rigs, wiring a retargeter, authoring UE 5.8 retarget override sets (sparse per-op property overrides such as scale, pelvis floor constraint, chain alphas), and batch-retargeting animation sequences into a target folder with `run_batch_retarget`.
---

# Animation retargeting through `execute_script`

Entry points: `create_asset(path, "IKRigDefinition")`, `create_asset(path, "IKRetargeter")`
and `open_asset(path)` for existing assets. Print the live surface first:

```lua
local rtg = open_asset("/Game/Retarget/RTG_SourceToTarget")
print(rtg:help())          -- includes the "Override sets (UE 5.8+)" block
print(rtg:info())          -- num_ops, override_sets_supported, override_sets, active_override_sets
```

Every `execute_script` call has a fresh Lua state: re-open assets and re-read
`list(...)` before treating anything as evidence. A logged `[FAIL]` returns
`nil` without raising, so check every return.

## Use this order

1. Duplicate or pick a source and a target skeletal mesh (they must differ).
2. One IK Rig per mesh: `set_mesh`, then `auto_retarget()` (auto chains) or
   `add("chain", ...)` by hand; `save()`.
3. Retargeter: `configure("source_ikrig")`, `configure("target_ikrig")`,
   `configure("preview_mesh", {..., ["for"]="source"|"target"})`,
   `add("default_ops")`, `auto_map("fuzzy")`.
4. Optional (UE 5.8+): author override sets instead of editing op settings when
   the tweak should be switchable at runtime or per batch.
5. `run_batch_retarget` into a target folder; read `count`, `assets[i].saved`.
6. Verify in a fresh script: `find_assets(folder, {class="AnimSequence"})`.

## End-to-end example (override set + batch)

```lua
local folder = "/Game/_Retarget"
local src = duplicate_asset("/Engine/Tutorial/SubEditors/TutorialAssets/Character/TutorialTPP", "SK_Src", folder)
local tgt = duplicate_asset("/Engine/Tutorial/SubEditors/TutorialAssets/Character/TutorialTPP", "SK_Tgt", folder)
assert(src and src.path and tgt and tgt.path, "mesh duplicates")
local src_mesh, tgt_mesh = src.path:gsub("%..*$", ""), tgt.path:gsub("%..*$", "")

local src_rig = assert(create_asset(folder .. "/IKR_Src", "IKRigDefinition"))
local tgt_rig = assert(create_asset(folder .. "/IKR_Tgt", "IKRigDefinition"))
assert(src_rig:set_mesh(src_mesh)); src_rig:auto_retarget(); src_rig:save()
assert(tgt_rig:set_mesh(tgt_mesh)); tgt_rig:auto_retarget(); tgt_rig:save()

local rtg = assert(create_asset(folder .. "/RTG_SrcToTgt", "IKRetargeter"))
assert(rtg:configure("source_ikrig", folder .. "/IKR_Src"))
assert(rtg:configure("target_ikrig", folder .. "/IKR_Tgt"))
assert(rtg:configure("preview_mesh", { mesh = src_mesh, ["for"] = "source" }))
assert(rtg:configure("preview_mesh", { mesh = tgt_mesh, ["for"] = "target" }))
assert(rtg:add("default_ops"))
assert(rtg:auto_map("fuzzy"))   -- map chains now; the FK op only auto-maps with rigs assigned at add time
assert(rtg:add("op", { type = "ScaleSource", name = "Scale" }))

-- Override set (UE 5.8+): sparse overrides that leave the op settings untouched.
if rtg:info().override_sets_supported then
  assert(rtg:add("override_set", { name = "Slow", active_by_default = false }) == "Slow")
  -- discover exact paths first; display names are refused
  for _, row in ipairs(rtg:list("overridable_properties", "Scale")) do log(row.path .. " : " .. tostring(row.type)) end
  local r = assert(rtg:configure_override({ set = "Slow", op = "Scale", property = "SourceScaleFactor", value = 0.5 }))
  assert(r.value == 0.5)
  local pelvis  -- list("ops").type is the struct name minus "Op" (e.g. "IKRetargetPelvisMotion")
  for _, op in ipairs(rtg:list("ops")) do if op.type:find("PelvisMotion", 1, true) then pelvis = op.name end end
  if pelvis then assert(rtg:configure_override("Slow", pelvis, "FloorConstraintWeight", 1.0)) end
  local slow = rtg:list("override_sets", "Slow")
  assert(slow.num_overrides >= 1)
end
rtg:save()

-- Batch: one exact output, override set applied (5.8) or omitted (older engines).
local params = { source = "/Engine/Tutorial/SubEditors/TutorialAssets/Character/Tutorial_Walk_Fwd",
                 output = folder .. "/AS_Walk_Tgt", overwrite = true }
if rtg:info().override_sets_supported then params.override_sets = { "Slow" } end
local result = assert(rtg:run_batch_retarget(params))   -- alias: rtg:retarget_batch(params)
assert(result.count == 1 and result.assets[1].saved, "retargeted sequence must be on disk")
assert(result.assets[1].bone_track_count > 0, "a mapped retarget writes bone tracks; 0 means chains were not mapped")
log(result.engine_path .. " -> " .. result.assets[1].path)
```

Verify in a **new** `execute_script` call:

```lua
assert(asset_exists("/Game/_Retarget/AS_Walk_Tgt"))
local rtg = open_asset("/Game/_Retarget/RTG_SrcToTgt")
local slow = rtg:list("override_sets", "Slow")   -- nil on UE < 5.8
```

## Override-set rules

- Paths are C++ names joined by `->`, arrays as `Name[i]`:
  `SourceScaleFactor`, `ChainsToRetarget[0]->RotationAlpha`. Always read them
  from `list("overridable_properties", op)`; a refusal lists the closest paths.
- Values: numbers, booleans, enum names (`"Bone"`), `{x=,y=,z=}` vectors,
  `{p=,y=,r=}` rotators. Wrong Lua types are refused before any change.
- `configure("op_settings", ...)` edits the op itself; `configure_override`
  edits a set. Use sets for variants (slow/heavy/child) and batch runs.
- Hierarchy: `add("override_set", {name=, parent=})`,
  `configure("override_set", {name=, parent=""})` clears the parent, rename
  cascades to children, removing a set re-parents its children to its parent.
- `active_by_default=true` makes the set apply at runtime without the anim
  node listing it; `info().active_override_sets` shows the current list.
- Sets apply per batch through `run_batch_retarget({override_sets={...}})`;
  unknown names are refused before the engine runs.
- Every verb is one undo step.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `add("override_set")` returns nil with `unsupported_engine_version` | Engine older than 5.8. Use `configure("op_settings")` and omit `override_sets`. |
| `configure_override` says "not overridable" | Display name, typo, `NotOverrideable`/Debug field, or array index beyond the op's current elements. Read `list("overridable_properties", op)`. |
| "value rejected for double: expected a number ... got Lua string" | Lua type mismatch. Numbers are numbers (not `"0.5"`), booleans are booleans, enums are their names, vectors are `{x=,y=,z=}`. Nothing is written on refusal. |
| Returned set name has a `_1` suffix | Name already existed; the engine uniquified it. Use the returned name. |
| `run_batch_retarget` "unknown override set(s)" | Name not in `list("override_sets")` (rename changed it). |
| `run_batch_retarget` "source and target preview meshes must be different" | Both sides use one mesh; duplicate the mesh for the other side. |
| `output ... already exists` | Pass `overwrite=true` or choose a new `output`. |
| Batch runs but chains look wrong | `auto_retarget()` returned nil on an unusual skeleton; author chains on the IK Rig, then `auto_map("fuzzy")`. |

Discovery escape hatches: `help("IKRetargeter")` (partial list; per-verb `help("configure_override")` depends on the shared help fallback), `rtg:help()` (full surface), `rtg:info()`,
`rtg:list("ops")`, `rtg:list("override_sets")`, `report_issue()`.
