---
name: neostack-composure
description: Author legacy Composure compositing shots through execute_script — create and nest compositing element actors, add input/transform/output passes, set render resolution, opacity and target cameras. Use when the user mentions Composure, comp shots, compositing elements, media/plate inputs, chroma keying passes or render-target outputs in the level.
---

# Composure (legacy plugin)

Everything here is Lua for `execute_script`. The verbs drive the legacy Composure plugin's element manager
(`ICompElementManager`); on UE 5.7/5.8 the plugin is labelled "Legacy
Composure" and the newer Composite plugin is not covered. Probe first:

```lua
local sup = composure_support()
assert(sup.available, sup.reason)   -- false when the plugin or its editor module is missing
```

## Build a parent shot with a keyed plate child

```lua
local suffix = tostring(math.random(1000, 9999))
local shot = composure_create_element("CompShot_" .. suffix)
assert(shot.name, shot.error)

local plate = composure_create_element("Plate_" .. suffix, "CompositingElement", {
  parent = shot.name,
  resolution = { x = 1920, y = 1080 },
  opacity = 1.0,
})
assert(plate.name, plate.error)

-- Passes: kind is inputs | transforms | outputs; class from composure_list_pass_classes(kind).
local input = plate:add("inputs", { name = "MediaPlate", class = "MediaTextureCompositingInput" })
assert(input.name, input.error)
local keyer = plate:add("transforms", { name = "Keyer", class = "MultiPassChromaKeyer" })
assert(keyer.name, keyer.error)
local out = shot:add("outputs", { name = "ToRT", class = "RenderTargetCompositingOutput" })
assert(out.name, out.error)

assert(plate:configure("Keyer", { enabled = true }) == true)
local info = plate:info()
print(info.parent, info.resolution.x, info.inputs, info.transforms)
```

`element:add` and `element:configure` stage `properties` on a probe copy and
refuse the whole call on the first unknown key or bad value, so a `{ok=false}`
result means nothing changed. Pass names must be unique per element because
the engine deletes passes by name.

Set a camera (the actor must exist in the level; `set_camera(nil)` reverts
to the inherited camera):

```lua
local level = assert(open_level())
assert(level:add("actor", {
  class = "CineCameraActor",
  location = { x = 0, y = 0, z = 200 },
  label = "CompCam_" .. suffix,
}))
assert(shot:set_camera("CompCam_" .. suffix) == true)   -- actor path or label
```

## Inspect, rename, detach, delete

```lua
for _, e in ipairs(composure_list_elements().items) do
  print(e.name, e.class, e.parent, #e.children, e.resolution.x, e.opacity)
end
assert(composure_rename_element("CompShot_" .. suffix, "Hero_" .. suffix) == true)
assert(composure_delete_element("Hero_" .. suffix) == true)   -- children go too
```

Every mutating verb is one undoable transaction. `composure_refresh({redraw=true})`
rebuilds the manager list and asks the compositing viewport to redraw.

## Verification

`element:render({camera_cut=true})` returns the composited texture path when
the compositing preview is active; outside it the verb reports an error and
that is expected. Use `element:named_target("Proof", 1.0)` only while the
element is rendering. For a file on disk, add a `CompositingMediaCaptureOutput`
pass whose `CaptureOutput` is a `FileMediaOutput` and let the preview run, or
capture the output render target with `media_capture_*`
(`neostack-media-capture`).

## Failure modes

| Symptom | Cause and fix |
|---|---|
| `composure_support().available == false` | Composure plugin disabled or `ComposureLayersEditor` not loaded (commandlet). Enable the plugin and restart. |
| `composure_create_element` "CreateElement returned nullptr" | An actor with that name already exists in the level. Pick another name. |
| `add()` refused with "already exists" | Pass names are unique per element. |
| `add()` refused with "is abstract" | Pick a concrete class from `composure_list_pass_classes(kind)`. |
| `info().resolution` differs from what you set | `set_resolution` switches `ResolutionSource` to Override; a child created without `resolution` inherits the parent's. |
| `set_camera` ignored | The verb sets `CameraSource=Override`; check `info().camera` in a fresh call. |
| `composure_create_player_target` unsupported | Needs PIE; it binds a player camera manager. |

## Discovery escape hatches

- `help("Composure")`, `composure_list_pass_classes()`, `element:help()`
- `element:info()`, `element:list("inputs" | "transforms" | "outputs" | "children")`
- `report_issue("...")` only for an exact reproducible API gap
