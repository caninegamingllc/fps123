---
name: neostack-metahuman
description: Drive MetaHuman Animator Performance processing as pollable jobs (configure, start, status, cancel, export to Animation Sequence or Level Sequence) and export MetaHuman Creator characters (DCC package, DNA, geometry, materials, posed DNA on UE 5.8) through `execute_script`. Use when a task mentions MetaHuman performances, facial capture solving, MetaHuman Animator, or exporting a MetaHuman character to DCC/DNA/skeletal meshes.
---

# MetaHuman Performance jobs and Creator export through `execute_script`

Start with:

```lua
help("MetaHumanPerformance")
help("MetaHumanExport")
help("MetaHuman")            -- Creator editing verbs (sculpt, makeup, rig, textures)
```

Do not wrap `help(...)` in `log(...)`; help already prints its result.

Every verb returns a table with `ok`; failures carry `error` and a stable
`code`. When the engine module is missing you get
`code="Unsupported"` (export verbs add `min_version="5.8"`) instead of a
missing global, so always check `ok` before reading other fields.

## Workflow 1: process a performance and export the animation

Jobs are asynchronous. `start` returns an id, `status(id)` reports progress,
`cancel(id)` stops it, `jobs()` lists the retained history. Only one
performance can process at a time in the editor.

```lua
local perf = "/Game/MetaHumans/Performances/MHP_Take01"      -- UMetaHumanPerformance
local footage = "/Game/MetaHumans/Captures/Take01_Footage"   -- UFootageCaptureData
local identity = "/Game/MetaHumans/Identities/MHI_Actor"     -- UMetaHumanIdentity

local info = assert(metahuman_performance_info(perf), "performance not found")
if not info.can_process then
  -- configure is validate-then-apply: any bad key/value refuses the whole call
  local cfg = metahuman_performance_configure(perf, {
    input_type = "DepthFootage",
    footage = footage,
    identity = identity,
    start_frame = 0, end_frame = 240,
    solve_type = "AdditionalTweakers",
  })
  assert(cfg.ok, cfg.error)
  info = metahuman_performance_info(perf)
  assert(info.can_process, "still cannot process: " .. tostring(info.cannot_process_reason))
end

local job = metahuman_performance_start(perf)          -- { blocking = true } runs synchronously
assert(job.ok, job.code .. ": " .. tostring(job.error))

local status
for _ = 1, 4800 do                                      -- 20 minutes at 0.25 s
  status = metahuman_performance_status(job.id)
  if not status or status.finished then break end
  playtest_wait(0.25)
end
assert(status and status.status == "completed",
  "processing ended with " .. tostring(status and status.status) .. " " .. tostring(status and status.error))

local exported = metahuman_performance_export(perf, {
  package_path = "/Game/MetaHumans/Anims",
  asset_name = "AS_Take01",
  export_range = "ProcessingRange",                     -- default; or "WholeSequence"
  auto_save = true,
})
assert(exported.ok, exported.code .. ": " .. tostring(exported.error))
log("exported " .. exported.path .. " with " .. tostring(exported.curve_count) .. " curves")
```

Read frames back without exporting:

```lua
local frames = metahuman_performance_get_animation_data(perf, {
  start_frame = 0, limit = 8,
  curves = { "CTRL_expressions_jawOpen", "CTRL_expressions_mouthLeft" },
})
for _, f in ipairs(frames.frames) do
  log(string.format("frame %d quality=%s jawOpen=%s", f.index, f.quality, tostring(f.curves["CTRL_expressions_jawOpen"])))
end
```

`start_frame`/`end_frame`/`limit` are clamped to the asset (the engine call
itself has no bounds check), `truncated=true` tells you the limit was hit.

## Workflow 2: export a Creator character (UE 5.8)

Check readiness first; the export verbs refuse predictable failures with a
code instead of opening the Message Log.

```lua
local mh = "/Game/MetaHumans/MyCharacter"                -- UMetaHumanCharacter
local ready = assert(metahuman_export_readiness(mh))
if not ready.export_library_available then
  error("Creator export needs UE " .. ready.min_version)
end
if ready.rigging_state ~= "Rigged" then
  assert(metahuman_auto_rig(mh, { blocking = true }), "auto rig failed")
end
if not ready.has_high_res_textures then
  assert(metahuman_request_textures(mh, { blocking = true }), "texture request failed")
end

local dna = metahuman_export_dna(mh, { project_path = "/Game/MetaHumans/DNA", external_path = "D:/Export/DNA" })
assert(dna.ok, dna.code .. ": " .. tostring(dna.error))
log("head asset " .. tostring(dna.assets.head) .. ", body file " .. tostring(dna.files.body))

local geo = metahuman_export_geometry(mh, { project_path = "/Game/MetaHumans/Meshes", full_body = true })
assert(geo.ok, geo.code .. ": " .. tostring(geo.error))
metahuman_close_editing(mh)                              -- export_geometry opened an editing session

local dcc = metahuman_export_dcc(mh, { external_path = "D:/Export/DCC", compress_zip = true })
assert(dcc.ok, dcc.code .. ": " .. tostring(dcc.error))
```

Materials (`metahuman_export_materials`, needs high-resolution textures; with
`apply_as_overrides=true` the character is modified inside one transaction) and
posed DNA (`metahuman_export_posed_dna`, needs a prior
`metahuman_conform_body` / `ConformToTargetMeshes` with the same
`target_mesh_key`) follow the same pattern.

## Gotchas

- `metahuman_performance_configure{identity=}` wipes processed results
  (`SetIdentity` resets the output); `footage=` with an invalid frame rate
  (zero `Metadata.FrameRate`, or an image sequence whose `FrameRateOverride`
  numerator is 0) is refused while staging on 5.8 (`code="Rejected"`), before
  anything is written.
- `export` never overwrites: an existing `package_path/asset_name` is refused
  with `code="Exists"` (the engine would otherwise open an overwrite dialog, or
  fail with a `LogAssetTools` error when unattended). Re-runs need a fresh
  `asset_name` or a `delete_asset` first.
- `status`/`cancel` need the integer id from `start`; `nil`, strings or
  fractions log `[FAIL]` and return `nil`/`false` rather than erroring.
- `blocking=true` runs the whole pipeline inside `start`; the asset's blocking
  flag is restored afterwards, so later toolbar processing stays asynchronous.
- Configure is refused while a job is running; cancel or wait first.
- `export` refuses when the target skeleton lacks the performance curves
  (`code="MissingCurves"`); pass `allow_missing_curves=true` to export anyway.
  The default target is `/MetaHuman/IdentityTemplate/Face_Archetype_Skeleton`.
- `export_body=true` with `export_skeleton ~= "Raw"` opens an engine modal
  dialog; the verb refuses unless `allow_modal=true`.
- `auto_save` defaults to false: the Animation Sequence is dirty in memory
  until it is saved (pass `auto_save=true`, or save from the editor).
- UE 5.7: no `SetProcessingRange` (frames are written directly), no body
  tracking keys, and every `metahuman_export_*` verb returns `code="Unsupported"`
  with `min_version="5.8"`.
- Live processing needs FootageCaptureData + MetaHumanIdentity assets; the
  engine ships none, so a fresh project can only exercise refusal paths.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `start` -> `code="Disabled"` | `cannot_process_reason` names the missing input (footage, audio, identity, invalid frame rate, depth plugin). Configure it and retry. |
| `start` -> `code="AlreadyProcessing"` | Another performance is running; `metahuman_performance_jobs().running_id`, then `cancel`. |
| `status` returns nil | Unknown id (history is bounded to 64 jobs) or a non-integer id (`nil`, `"7"`, `1.5`); use the numeric id from `start`. |
| `configure` -> `code="Rejected"` mentioning frame rate | The FootageCaptureData has a zero frame rate or an image sequence with a zero `FrameRateOverride` numerator; fix the capture data asset, nothing was written. |
| `export` -> `NoAnimationData` | Process first; `metahuman_performance_info(perf).processed_frames` must be > 0. |
| `export` -> `code="Exists"` | `package_path/asset_name` already exists (`existing` names it); pass a new `asset_name` or `delete_asset` it first. The verb never overwrites. |
| `export_dcc` -> `NotRigged` / `NoTextures` | Run `metahuman_auto_rig` / `metahuman_request_textures` and wait for them. |
| `export_geometry` -> `NotEditing` | `TryAddObjectToEdit` failed; the character asset is invalid or its DNA is missing. |
| `export_posed_dna` -> `NoPosedState` | No conform stored under that `target_mesh_key`; run `metahuman_conform_body` with the same meshes. |
| any export -> `EngineFailed` | The library logged errors; read `engine_errors` (captured from the MetaHuman message log). |

Discovery escape hatches: `help("MetaHumanPerformance")`,
`metahuman_performance_info(asset)`, `metahuman_export_readiness(asset)`,
`report_issue()`.
