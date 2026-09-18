---
name: neostack-movie-render-queue
description: Configure and run Unreal Movie Render Queue jobs through execute_script. Use when the user asks for MRQ presets, queues, image sequences, rendered frame files, output ranges, or render-progress verification.
---

# Movie Render Queue

Everything here is Lua for `execute_script`. The render artifact is the file on
disk—not an `[OK]` line. A job qualifies only when the expected number of files
exists and the required original frames have been opened and read.

The dependable path is:

1. Use a saved level and a saved `LevelSequence`.
2. Create a `MoviePipelinePrimaryConfig`.
3. Add and configure output, image format, and render-pass settings.
4. Allocate a queue job, set its map and preset, then optionally save the queue.
5. Start the render and wait until rendering has been observed as `true` before
   accepting a later `false` as completion.
6. Count exact output files and open a frame.

Before allocating a job, preflight the saved map and sequence in **Lit** mode.
Open the map, open the sequence, evaluate a representative in-range frame, and
capture from the render camera. The source must already show non-black RGB
content. An Unlit viewport proof is insufficient: a level with no light can
show a white default cube in Unlit while Deferred MRQ writes black RGB with
only an alpha silhouette.

Use the UE 5.8 editor blueprint library to evaluate the representative display
frame before the capture:

```lua
local level_path = "/Game/Maps/L_RenderMe"
local sequence_path = "/Game/Cinematics/SEQ_RenderMe"
local lib = "/Script/LevelSequenceEditor.LevelSequenceEditorBlueprintLibrary"

assert(load_level(level_path))
assert(open_editor(sequence_path))
assert(invoke(lib, "SetCurrentTime", { NewFrame = 40 }))
assert(invoke(lib, "ForceUpdate"))

screenshot({
  mode = "level",
  max_dimension = 1600,
  wait_for_ready_ms = 300,
  hide_overlays = true,
  location = { x = -1400, y = 0, z = 570 },
  rotation = { pitch = -19, yaw = 0, roll = 0 },
  fov = 62,
  view_mode = "lit",
})
```

`NewFrame` is an integer display-frame number. Use an in-range frame and the
same camera transform/FOV that the camera cut is expected to render. Open and
read the returned screenshot before configuring MRQ.

If the Lit preflight is black, repair the source before rendering—add valid
lighting or use an emissive render material, save the level, then repeat the Lit
preflight. MRQ configuration cannot repair an unlit source scene.

## Configure a three-frame PNG preset

`setting:set()` accepts Unreal export text. Use exact reflected property names:

```lua
local config_path = "/Game/Cinematics/CFG_ThreeFramePNG"
assert(create_asset(config_path, "MoviePipelinePrimaryConfig"))

local config = mrq_open_config(config_path)
assert(config)

local output = config:find_or_add_setting("MoviePipelineOutputSetting")
assert(output)
output:set("OutputDirectory", '(Path="' .. project_dir() .. 'Saved/Renders/ThreeFrame")')
output:set("FileNameFormat", "ThreeFrame_{frame_number}")
output:set("OutputResolution", "(X=640,Y=360)")
output:set("ZeroPadFrameNumbers", "4")
output:set("bOverrideExistingOutput", "True")
output:set("bUseCustomPlaybackRange", "True")
output:set("CustomStartFrame", "0")
output:set("CustomEndFrame", "3")

local png = config:find_or_add_setting(
  "MoviePipelineImageSequenceOutput_PNG"
)
assert(png and png:set_enabled(true))

local deferred = config:find_or_add_setting("MoviePipelineDeferredPassBase")
assert(deferred and deferred:set_enabled(true))

assert(config:save())
```

The custom range is half-open: start 0 and end 3 renders exactly frames 0, 1,
and 2. Use a dedicated empty output directory or a unique filename prefix so
old files cannot inflate the count.

Verify the preset in a fresh call:

```lua
local config = mrq_open_config("/Game/Cinematics/CFG_ThreeFramePNG")
assert(config)

local output = config:find_or_add_setting("MoviePipelineOutputSetting")
assert(output:get("OutputResolution") == "(X=640,Y=360)")
assert(output:get("CustomStartFrame") == "0")
assert(output:get("CustomEndFrame") == "3")

local names = {}
for _, row in ipairs(config:list_settings()) do
  names[row.name] = true
  assert(row.is_enabled == true)
end
assert(names.MoviePipelineOutputSetting)
assert(names.MoviePipelineImageSequenceOutput_PNG)
assert(names.MoviePipelineDeferredPassBase)
```

## Allocate and persist the queue

`mrq_allocate_job()` clears the editor queue and returns its only job. Use
`mrq_add_job()` when retaining existing jobs is intentional.

```lua
local job = mrq_allocate_job("/Game/Cinematics/SEQ_RenderMe")
assert(job)

job:set_map("/Game/Maps/L_RenderMe")
job:set_job_name("ThreeFrame_Render")
job:set_author("NeoStack")
job:set_comment("Three-frame 640x360 PNG proof")
assert(job:set_config("/Game/Cinematics/CFG_ThreeFramePNG"))

local info = job:info()
assert(info.is_enabled)
assert(info.job_name == "ThreeFrame_Render")
assert(info.map:find("/Game/Maps/L_RenderMe", 1, true))
assert(info.preset_origin:find("CFG_ThreeFramePNG", 1, true))

assert(mrq_save_queue("/Game/Cinematics/QUEUE_ThreeFrame"))
```

Saved queues are reusable. Test persistence in a fresh call:

```lua
assert(mrq_delete_all_jobs())
assert(mrq_load_queue("/Game/Cinematics/QUEUE_ThreeFrame"))

local queue = mrq_get_queue()
assert(queue.count == 1)
assert(#queue.jobs == 1)
local job = mrq_get_job(1)
assert(job and job:info().job_name == "ThreeFrame_Render")
```

Queue and job indices are 1-based.

## Multi-shot handles require an editorial hierarchy

Do not author a handle-bearing multi-shot render as only several root camera-cut
sections. Build a master `LevelSequence` with top-level `CinematicShot`
sections, and put each shot's camera cut and animated subject tracks in an inner
sequence. UE expands the top-level shot section for `HandleFrameCount`, but the
inner camera/transform sections must already cover the same support interval.

For a 30-frame shot with two handles, a dependable layout is:

- master shot section: frames `[0,30)`;
- inner playback/edit range: frames `[0,30)`;
- inner camera cut and animated tracks/keys: frames `[-2,32)`;
- root master playback envelope: wide enough for exterior handles (for a
  three-shot 90-frame edit, `[-2,92)`);
- `FileNameFormat`: include `{shot_name}`, for example
  `MultiShot_{shot_name}_{frame_number}`, so overlapping handle frame numbers
  cannot overwrite one another.

When the master playback envelope intentionally includes exterior handle
support, leave `bUseCustomPlaybackRange` false. UE applies a true custom range
to the root sequence before it builds the shot list; clamping it back to
`[0,90)` can leave final handle frames outside root evaluation even when the
inner tracks are long enough. Verify the finished output numerically per shot
and visually across every cut, including the first and last exterior handles.

`MoviePipelineDeferredPassBase.bAddDefaultLayer` must remain false unless the
pass also has an Actor or Data Layer selection. Enabling it without either
causes the PIE executor to abort during shot initialization.

## Render and wait correctly

Start one selected job:

```lua
local job = mrq_get_job(1)
assert(job)
assert(mrq_render_job(job))
```

Or render the whole queue:

```lua
assert(mrq_render_queue())
```

The default UE 5.8 editor executor requests PIE asynchronously.
`mrq_is_rendering()` can therefore be `false` immediately after the start call;
the engine does not set its render flag until `PostPIEStarted`. Never interpret
that first `false` as completion.

Poll in later tool calls. The completion gate is a state transition: observe
`true` at least once, then observe `false`.

```lua
local rendering = mrq_is_rendering()
local progress = mrq_render_progress()
print("rendering", rendering, "percent", progress.progress)
```

If the first poll is false, wait briefly and poll again. If no poll ever sees
true, inspect the project log and output directory instead of claiming success.
Do not start a second render while the current one is active.

For live verification, make the render long enough that the `true` state is
observable (for example, more frames or a larger output resolution). Wait about
one second **outside** the editor between separate poll calls. Do not issue a
tight burst of scripts: each script runs on the editor thread and can starve
the PIE startup/render work being measured. If all expected files appear before
any `true` observation, the files prove that render but the transition gate is
still unmet; discard that measurement or rerun a deliberately longer job.

`config:list_settings()` rows expose `name`, `display_name`, and
`is_enabled`. The enablement field is `is_enabled`, not `enabled`.

## Verify files, frame count, and pixels

Use the exact output prefix:

```lua
local dir = project_dir() .. "Saved/Renders/ThreeFrame"
local files = list_files(dir)
local frames = {}

for _, row in ipairs(files or {}) do
  local name = row.name or row.path or ""
  if name:match("^ThreeFrame_%d%d%d%d%.png$") then
    frames[#frames + 1] = name
  end
end

table.sort(frames)
assert(#frames == 3)
assert(frames[1] == "ThreeFrame_0000.png")
assert(frames[3] == "ThreeFrame_0002.png")

for _, name in ipairs(frames) do
  local f = file_info(dir .. "/" .. name)
  assert(f and f.exists and f.size > 1000)
end
```

Then open actual PNGs returned by the run at original resolution. For a static
render, inspect every requested still. For motion, animation, simulation, or a
video, inspect a dense ordered sequence that covers setup, onset, intermediate
motion, completion, and settling. Inspect every frame for a short clip; for a
longer clip, use a uniform dense sample plus every transition region, expanding
any suspicious interval to adjacent frames. Confirm the same intended subjects,
camera, environment, and action remain coherent through the sequence. Different
first and last frames, changed hashes, or a contact sheet alone do not prove the
prompted action.

## Settings discovery and readback

```lua
local hits = mrq_list_settings("Output")
for _, row in ipairs(hits) do
  print(row.name, row.display_name)
end

local output = mrq_open_config(config_path)
  :find_or_add_setting("MoviePipelineOutputSetting")

for _, p in ipairs(output:list_properties()) do
  print(p.name, p.type, p.value)
end
```

Use the concrete class returned by discovery. Partial names can resolve, but
exact names such as `MoviePipelineOutputSetting` and
`MoviePipelineImageSequenceOutput_PNG` make the script auditable.

`setting:set()` logs `[FAIL]` instead of throwing. It currently has no useful
Lua return value, so perform a fresh `get()` readback for every property that
determines output correctness.

`job:set_map()`, `set_author()`, `set_job_name()`, and `set_comment()` are also
void Lua methods. Do not wrap them in `assert`. Save the map before allocating
the job, then verify their stored values through `job:info()`; `set_map()` stores
a soft path and does not validate that the map asset exists.

## Failure modes

| Symptom | Cause and fix |
|---|---|
| `mrq_is_rendering()` is false immediately after start | Expected PIE startup window. Poll until true has been observed, then wait for false. |
| A short render finishes between polls | Rerun a longer validation range/resolution and pause outside the editor between calls. Do not tight-poll on the editor thread. |
| A setting row's `enabled` field is nil | The fresh list field is named `is_enabled`. Require `row.is_enabled == true`. |
| Render finishes but file count is too high | Old files share the prefix. Use an empty/dedicated directory or unique prefix before rendering. |
| Zero frames | Check job map, sequence, preset origin, PNG and Deferred settings, then read the project log. `[OK] render started` is not output proof. |
| Wrong frame count by one | MRQ custom playback range is half-open: `[CustomStartFrame, CustomEndFrame)`. |
| Black or default-looking images | Open and read a rendered frame; verify camera cuts and level content in the source sequence. |
| Exterior handle frames show sky/default camera | Use master `CinematicShot` sections, give inner camera/animated sections the full handle support range, widen the master playback envelope, and avoid a custom root range that clamps the support frames. |
| Render aborts with `Add Default Layer` error | Set `MoviePipelineDeferredPassBase.bAddDefaultLayer=False` unless Actor/Data Layer selections are configured. |
| RGB is black but alpha contains the moving subject | The Deferred pass is seeing geometry but the source is unlit. Add source lighting or an emissive material, save, and require a non-black Lit preflight before rerendering. |
| Lit preflight opens the sequence but does not evaluate the requested frame | Invoke `LevelSequenceEditorBlueprintLibrary.SetCurrentTime`, then `ForceUpdate`, before capture. |
| `setting:set()` appears to succeed but value is wrong | Reopen the config and call `get()` with the exact reflected property name. |
| `mrq_render_job(job)` says the handle is not in the queue | Handles are tied to the current queue instance. Reacquire with `mrq_get_job(index)`. |
| Loading a saved queue changes the current jobs | This is intentional: `mrq_load_queue()` replaces the editor queue. |
| A failed call does not raise | NeoStack failures return `nil` or emit `[FAIL]`; check return values and harness reply text. |

## Movie Render Graph presets

`mrq_open_graph(path)` handles also carry the Movie Render Graph methods
(`evaluate`, `output_settings`, `resolution`, `output_directory`, `branches`,
`nodes_for_branch`, `nodes_for_tag`, `get_node_property`/`set_node_property`,
`get_variable`/`set_variable`, `remove_node`, `insert_before`/`insert_after`),
and `job:evaluate()` / `job:output_directory()` work on graph-mode jobs. Live
graph-render progress is `mrg_pipeline_status()` (also embedded as
`jobs[i].graph` in `mrq_render_progress()`), and `mrg_request_shutdown()` is
the graceful stop. See `neostack-movie-render-graph` for the recipes and the
per-engine-version gates (`mrg_support().features`).

## Discovery escape hatches

- `help("MovieRenderQueue")`, `help("MovieRenderGraph")` and `mrq_list_settings(filter)`
- `mrq_get_queue()` and `mrq_get_job(index):info()`
- `setting:list_properties()`, `setting:get(name)`, `setting:info()`
- `mrq_render_progress()`
- `report_issue("...")` — only after an exact reproducible API gap remains

Do not treat a compile, queue row, or self-reported file count as render proof.
