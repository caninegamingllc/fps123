---
name: neostack-media-capture
description: Capture Unreal render targets, the active scene viewport or media textures to image files (and any Media IO output) through execute_script using UMediaCapture. Use when the user asks to record frames from a render target, dump a viewport or media texture to PNG/EXR, or drive a FileMediaOutput / Media IO output as a job with status polling. Not for Movie Render Queue renders (neostack-movie-render-queue) or viewport screenshots (screenshot()).
---

# Media capture

Everything here is Lua for `execute_script`. The artifact is the file on disk, not a job id. Captures are runtime
state (not undoable) and progress only while the editor ticks, so poll across
separate calls or with `playtest_wait`.

```lua
local sup = media_capture_support()
print(sup.available, sup.engine_version, sup.features.media_texture)   -- media_texture needs 5.8
```

## Capture a render target to a PNG sequence

```lua
local suffix = tostring(math.random(1000, 9999))
local rt_path = "/Game/Capture/RT_Proof_" .. suffix
assert(create_asset(rt_path, "TextureRenderTarget2D"))        -- 256x256 by default
local dir = project_dir() .. "Saved/Captures/Proof_" .. suffix

local out = media_capture_create_output("FileMediaOutput", {
  properties = {
    FilePath = dir,                       -- absolute directory (a bare string is wrapped into {Path=})
    BaseFileName = "proof",
    WriteOptions = { Format = "PNG", bOverwriteFile = true },
  },
})
assert(out.id, out.error)                 -- the verb already ran UMediaOutput::Validate

local job = media_capture_start(out, { render_target = rt_path }, {
  autostop_on_capture = true,
  number_of_frames_to_capture = 4,
})
assert(type(job) == "number", job.error)
```

`FileMediaOutput` starts with `FilePath = <ProjectSaved>/MediaOutput`, so a
create call without `properties` validates and writes there; give `FilePath`
explicitly when you want the frames somewhere else (an empty string is refused).

`media_capture_create_output` defaults `bOverridePixelFormat=true` and
`DesiredPixelFormat="B8G8R8A8"` for `FileMediaOutput`; the engine rejects the
10-bit back-buffer format and PNG for float formats, so set
`DesiredPixelFormat="FloatRGBA"` together with `WriteOptions.Format="EXR"`
when you want HDR frames. `properties` are staged first: an unknown key or a
bad enum name refuses the whole call.

Poll in later calls (or in-script with `playtest_wait`) until the job is
finished, then count files:

```lua
local status
for _ = 1, 40 do
  status = media_capture_status(job)
  if status.finished and status.has_finished_processing then break end
  playtest_wait(0.25)
end
print(status.state, status.elapsed_seconds, status.output_directory)
assert(status.state == "Stopped", status.error)

local frames = {}
for _, row in ipairs(list_files(dir) or {}) do
  local name = row.name or row.path or ""
  if name:match("%.png$") then frames[#frames + 1] = name end
end
assert(#frames >= 4, "expected 4 frames, found " .. #frames)
```

A render target nothing draws into captures its clear colour; open a frame
when the content matters, count files when the pipeline matters.

## Other sources and options

```lua
-- Active scene viewport: only Standalone or "New Editor Window" PIE has one.
local vp = media_capture_start(out, { active_viewport = true }, { capture_phase = "EndFrame" })
-- Media texture (UE 5.8+): rotation in degrees, scale {x,y}.
local mt = media_capture_start(out, { media_texture = "/Game/Media/MT_Plate", rotation = 0, scale = { x = 1, y = 1 } })
```

Option keys map to `FMediaCaptureOptions`: `crop` (`None|Center|TopLeft|Custom`),
`custom_capture_point={x,y}`, `resize_method`, `convert_to_desired_pixel_format`,
`force_alpha_to_one`, `autostop_on_capture`, `number_of_frames_to_capture`,
`capture_phase` (`BeforePostProcessing|AfterMotionBlur|AfterToneMap|AfterFXAA|PostRender|BackBufferReady|EndFrame`),
`overrun_action` (`Flush|Skip`), `apply_linear_to_srgb`, `disable_editor_sprites` (5.8),
`skip_frame_when_running_expensive_tasks`, `auto_restart_on_source_size_change`.

Manage jobs and outputs:

```lua
print(media_capture_jobs().running, media_capture_jobs().total)
assert(media_capture_stop(job, true))                       -- allow the pending frame to flush
assert(media_capture_update_target(job, "/Game/Capture/RT_Other"))   -- running render-target jobs only
for _, o in ipairs(media_capture_outputs().items) do print(o.id, o.class, o.valid) end
assert(out:release())                                       -- refused while a job uses it
```

## Failure modes

| Symptom | Cause and fix |
|---|---|
| `create_output` "output failed validation: ... file path is null" | Set `properties.FilePath`. |
| "doesn't support 10bits format" | Keep the default pixel-format override, or set `DesiredPixelFormat="B8G8R8A8"`. |
| "Only EXR export is currently supported for PF_FloatRGBA" | `FloatRGBA` needs `WriteOptions.Format="EXR"`. |
| `start` "render target ... is not initialised" | The asset has no size/resource; recreate it with `create_asset(path, "TextureRenderTarget2D")`. |
| `start` "CaptureActiveSceneViewport returned false" | No scene viewport; use Standalone/New Editor Window PIE or capture a render target. |
| `status.state == "Preparing"` forever | The editor is not ticking between polls; poll from separate calls or use `playtest_wait`. |
| `state == "Stopped"` but no files yet | Async image writes; wait for `has_finished_processing` and re-list the directory. |
| `media_texture` source refused | UE 5.8 only (`UMediaCapture::CaptureMediaTexture`). |
| `release()` refused | A running job still uses the output; stop it first. |

## Discovery escape hatches

- `help("MediaCapture")`, `media_capture_list_outputs()`, `out:info()` (reflected properties)
- `media_capture_status(id)`, `media_capture_jobs()`
- `report_issue("...")` only for an exact reproducible API gap
