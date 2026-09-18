---
name: neostack-gameplay-cameras
description: Author Unreal Engine Gameplay Cameras (the experimental data-driven camera plugin, UE 5.6+) through `execute_script` - camera rig node trees and transitions, camera assets with Single/StateTree directors, camera shakes, camera variables, placing GameplayCameraActor / GameplayCameraRigActor and verifying the active camera rig in PIE. Use whenever a task mentions camera rigs, camera nodes (Sequence, Offset, Boom Arm, framing), camera directors, camera shakes or Gameplay Camera actors.
---

# Gameplay Cameras through `execute_script`

Start with `help("GameplayCameras")` (it prints its own result) and `camera_support()`.
The plugin is enabled by default; `camera_support().active_rig_readback` is false on UE 5.6,
where the PIE active-rig readback is not exported. Handles come from `create_asset` /
`open_asset`; Lua state is fresh on every call, so re-open assets by path and keep the node
ids (`list("nodes")[i].id`) you got back.

## Work in this order

1. **Discover node classes.** `camera_node_types({filter="Boom"})` lists class, alias
   (`"Sequence"`, `"Offset"`, `"BoomArm"`, `"PerlinNoiseLocationShake"` ...), categories,
   `connectable_properties` (child links such as `Children[]`, `RootNode`) and settable
   `properties` (camera parameters are flagged). Never guess a property name.
2. **Rig node tree.** `create_asset(path, "camerarigasset")`, then
   `rig:add("node", {class=, parent="root"|id, property=, index=, x=, y=, properties={}})`.
   The first node goes into `parent="root", property="RootNode"`; a `Sequence` node exposes
   the `Children` array. Blend nodes are refused here (they belong to transitions).
3. **Values.** `rig:configure(id, {TranslationOffset={x=0,y=0,z=50}, OffsetSpace="World",
   enabled=true, comment="...", x=, y=})`. Camera parameters take a bare value, `{value=}` or
   `{variable="/Game/..._Vars.Height"}` from a `cameravariablecollection` handle. One bad key
   refuses the whole call and writes nothing.
4. **Rewire.** `rig:connect(parent, property, child, index?)`, `rig:disconnect(parent,
   property, index?)` (array slots are nulled, sibling indices stay), `rig:remove(id)`.
5. **Build and verify.** `rig:build()` returns `{ok, status, messages}`; `rig:info()` and
   `rig:list("tree")` read the result. `status` must be `Clean` or `CleanWithWarnings`.
6. **Camera asset.** `camera_create_asset(path, {director="Single", rig=rig_path})` or
   `{director="StateTree", state_tree=tree_path}` (tree must use
   `CameraDirectorStateTreeSchema`); `cam:configure({proxy_redirects={{proxy=, rig=}}})`
   maps `camerarigproxyasset` proxies; `cam:build()`.
7. **Place and prove.** `camera_place_actor(camera_path, {location=, label=,
   run_standalone=true})`, `playtest_start({mode="pie"})`, `camera_activate(label)`, poll
   `camera_active_rig()` until a host's `active.rig` is your rig, `playtest_stop()`.
8. **Persist.** `rig:save()`, then `open_asset(path):list("nodes")` in a fresh call.

## End-to-end example

```lua
local rig_path = "/Game/Cameras/CR_ThirdPerson"
local cam_path = "/Game/Cameras/CA_ThirdPerson"

local rig = create_asset(rig_path, "camerarigasset")
local seq = rig:add("node", { class = "Sequence", parent = "root" })
assert(seq.ok, seq.error)
local boom = rig:add("node", { class = "BoomArm", parent = seq.id, property = "Children",
  properties = { BoomOffset = { x = -300, y = 0, z = 80 } } })
assert(boom.ok, boom.error)
local off = rig:add("node", { class = "Offset", parent = seq.id, property = "Children",
  properties = { TranslationOffset = { x = 0, y = 40, z = 0 }, OffsetSpace = "CameraPose" } })
assert(off.ok, off.error)
local built = rig:build()
assert(built.ok and built.status ~= "WithErrors", built.status)
rig:save()

local cam = camera_create_asset(cam_path, { director = "Single", rig = rig_path })
assert(cam.ok ~= false, cam.error)
assert(cam:build().ok)
cam:save()

local placed = camera_place_actor(cam_path, { location = { x = 0, y = 0, z = 150 }, label = "ThirdPersonCam", run_standalone = true })
assert(placed.ok, placed.error)

playtest_start({ mode = "pie" })
playtest_wait_frames(10)
assert(camera_activate("ThirdPersonCam", { player_index = 0 }).ok)
local active
for _ = 1, 30 do
  local r = camera_active_rig()
  for _, h in ipairs(r.hosts or {}) do
    if h.active and h.active.rig:find(rig_path, 1, true) then active = h end
  end
  if active then break end
  playtest_wait_frames(3)
end
playtest_stop()
assert(active, "rig never became active")
log(active.active.layer .. " " .. active.active.pose.location.z)
```

## Camera variables

```lua
local vars = create_asset("/Game/Cameras/CV_Common", "cameravariablecollection")
local h = vars:add("variable", { name = "Offset", type = "Vector3d", default = { x = 0, y = 0, z = 75 } })
rig:configure(off.id, { TranslationOffset = { variable = h.path } })   -- drive the parameter
rig:configure(off.id, { TranslationOffset = { variable = false } })    -- back to the literal value
```

Types: `Boolean, Integer32, Float, Double, Vector2f/2d, Vector3f/3d, Vector4f/4d,
Rotator3f/3d, Transform3f/3d`. The variable type must match the parameter: a
`TranslationOffset` (Vector3d parameter) takes a `Vector3d` variable, a `Float` parameter a
`Float` variable. `list("properties", {id=})[i].value_type` shows the parameter's value type; a
mismatch is refused with the expected class named and nothing is written.

## Transitions and shakes

- `rig:add("transition", {direction="enter"|"exit", blend="SmoothBlend", properties={bFreezePreviousCameraRigs=true}})`
  creates the transition in the rig's transitions graph with the blend linked to
  `Transition.Blend`; `rig:list("transitions")` reads them back; `rig:remove(id)` finds
  transition ids automatically. Conditions are added with
  `rig:add("node", {class="<ConditionClass>", parent=transition_id, property="Conditions", graph="transitions"})`.
- Camera assets take shared transitions the same way (`cam:add("transition", ...)`).
- `create_asset(path, "camerashakeasset")` gives a handle with the same graph verbs; its
  graph accepts shake nodes (`PerlinNoiseLocationShake`, `PerlinNoiseRotationShake`,
  `CompositeShake`, `EnvelopeShake`) and fixed-time blends. `shake:configure("shake",
  {single_instance=true})`.

## Gotchas

- Node ids are object names inside the rig (`OffsetCameraNode_0`); `"root"` is the asset.
  Ids survive save/reopen; handles do not survive across `execute_script` calls.
- `list("nodes")[i].registered == false` means a node reached from the tree is missing from
  the rig's internal list (older assets); `connect()` re-registers it.
- `camera_active_rig().hosts[i].active` is nil until the component has an evaluation context.
  Always `camera_activate` first and poll a few frames. Rig actors report a transient
  `camera_asset` path - do not assert on it.
- `camera_configure_component` only edits the editor actor; PIE copies are transient.
- Camera parameter values print as struct text (`(X=1,Y=2,Z=3)`) in `list("properties")`.
- `camera_place_actor` refuses while PIE is running; stop the session first.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `add("node")` -> `cannot be placed in the nodes graph` | Blend node in the node tree. Blends go on transitions (`add("transition", {blend=...})`). |
| `add("node")` -> `has no connectable property accepting` | Pass `property=`; check `camera_node_types()[..].connectable_properties`. |
| `configure` -> `is a node link; use connect()` | You wrote a child link by name. Use `connect`/`disconnect`. |
| `configure` -> `camera variable '...' not found` | Create it with a `cameravariablecollection` handle and pass `h.path`. |
| `configure` -> `expects a Vector3dCameraVariable; '...' is a FloatCameraVariable` | Variable type must match the parameter type; add a variable with the named type (`Vector3d` here). |
| `configure({rig=...})` on a camera -> `requires a Single camera director` | Create the asset with `director="Single"` or pass `director_class="Single"` first. |
| `configure({state_tree=...})` -> `must use the CameraDirectorStateTreeSchema` | Author the tree with the StateTree integration using that schema. |
| `camera_active_rig` -> `No active Play In Editor session` | Call `playtest_start({mode="pie"})` first. |
| `camera_active_rig().supported == false` | UE 5.6: the evaluator accessor is not exported; use `evaluated_pose`. |
| `camera_create_asset` -> `supported=false` | GameplayCamerasEditor module or plugin missing; enable Gameplay Cameras (UE 5.6+). |

Discovery escape hatches: `help("GameplayCameras")`, `rig:help()`, `rig:info()`,
`camera_node_types()`, `report_issue()`.
