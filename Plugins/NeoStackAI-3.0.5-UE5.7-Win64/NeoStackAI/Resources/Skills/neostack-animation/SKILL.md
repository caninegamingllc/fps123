---
name: neostack-animation
description: Author and verify Unreal Engine animation sequences and montages through `execute_script`. Use for bone tracks and keys, root motion, montage slots and segments, animation notifies or notify states, Motion Warping windows and dynamic targets, restart-persistent animation assets, or continuous visual proof of character animation.
---

# Animation assets through `execute_script`

Open an AnimSequence or AnimMontage and inspect its live method surface first:

```lua
local asset = open_asset("/Game/Animations/AS_Action")
assert(asset, "animation asset not found")
print(asset:help())
```

Every `execute_script` call has a fresh Lua state. Re-open every asset and
re-read every list in a new call before treating it as evidence.

## Use this order

1. Resolve the exact skeleton and source animation assets.
2. Duplicate or create the sequence and make it restart-persistent.
3. Author root-motion keys and sequence properties.
4. Create the montage, preserve at least one slot, and add its sequence segment.
5. Add fully configured notify states only after the event has its duration.
6. Build gameplay logic that calculates runtime targets from the current world.
7. Compile and save every Blueprint and animation asset.
8. Verify multiple valid scenarios plus invalid rejection in PIE.
9. Inspect an uninterrupted ordered frame sequence.
10. Cold-restart the editor and repeat structural and runtime checks.

Check every mutating return. A logged `[FAIL]` commonly returns nil without
raising a Lua exception.

## Make duplicated sequences persistent

`duplicate_asset` follows Unreal AssetTools behavior: it creates a dirty editor
package but does not promise a file on disk. A successful return is not restart
evidence.

```lua
local result = duplicate_asset(
  "/Game/Animations/AS_Source",
  "AS_Action",
  "/Game/Animations")
assert(result and result.success)
assert(result.package_dirty == true)
assert(result.requires_save == true)

local sequence = open_asset(result.path)
assert(sequence)
assert(sequence:save(), "duplicated sequence did not persist")
```

After a cold editor restart, require `asset_exists(result.path)` and open the
asset again. Never infer persistence from an in-memory handle or a montage that
still caches the missing sequence length.

## Author sequence transforms and root motion

Use `set_bone_keys` to replace a complete track and `update_bone_keys` for a
bounded range. Positions and scales accept numeric arrays or named
`x/y/z` fields. Rotations accept Euler `pitch/yaw/roll` or `p/y/r`; a fourth
component or `w` selects quaternion input.

```lua
local sequence = open_asset("/Game/Animations/AS_Action")
assert(sequence)

assert(sequence:set("bEnableRootMotion", true))
assert(sequence:set("bForceRootLock", false))

assert(sequence:set_bone_keys("root", {
  positions = {
    { x = 0, y = 0, z = 0 },
    { x = 80, y = 0, z = 35 },
    { x = 180, y = 0, z = 0 },
  },
  rotations = {
    { pitch = 0, yaw = 0, roll = 0 },
    { pitch = 0, yaw = 0, roll = 0 },
    { pitch = 0, yaw = 0, roll = 0 },
  },
  scales = {
    { x = 1, y = 1, z = 1 },
    { x = 1, y = 1, z = 1 },
    { x = 1, y = 1, z = 1 },
  },
}))
assert(sequence:save())
```

The example values demonstrate the table shape only. Derive key count, timing,
distance, height, and rotation from the requested animation rather than copying
those constants.

## Build a montage with engine-native invariants

Create the montage for the exact skeleton, retain at least one slot, and add
the sequence as a segment:

```lua
local montage = create_asset("/Game/Animations/AM_Action", "AnimMontage", {
  TargetSkeleton = "/Game/Characters/SK_Character_Skeleton",
})
assert(montage)

local slots = montage:list("slots")
if #slots == 0 then
  assert(montage:add("slot", { name = "DefaultSlot" }))
end

assert(montage:add("segment", {
  slot = "DefaultSlot",
  animation = "/Game/Animations/AS_Action",
  start_pos = 0,
  play_rate = 1,
}))
```

Never remove the montage's final slot. Removing another slot relinks remaining
notifies and sections, so immediately re-read segments, sections, and notify
start/end timing.

## Add notify states and Motion Warping windows

Create a notify state with its final duration before editor initialization.
Configure instanced/edit-inline objects with a nested table and an explicit
compatible class:

```lua
local montage = open_asset("/Game/Animations/AM_Action")
assert(montage)

assert(montage:add("notify", {
  name = "WarpAction",
  time = 0.15,
  type = "AnimNotifyState_MotionWarping",
  duration = 0.45,
  RootMotionModifier = {
    class = "RootMotionModifier_SkewWarp",
    WarpTargetName = "ActionTarget",
    bWarpTranslation = true,
    bWarpRotation = false,
  },
}))

assert(montage:save())
```

Unknown properties, non-instanced object slots, missing classes, and
incompatible classes must reject the whole add. Verify that the notify count
does not increase after a rejected request.

Use `configure("notify", index, {time=..., duration=...})` for timing changes.
A time-only move must preserve the existing state duration. Re-read
`list("notifies")` and assert the exact type, start, duration, track, and count.

For multi-phase actions, use separate semantically named targets and windows
when each phase needs a different constraint. Keep names and timings derived
from the gameplay design; do not encode one task's obstacle dimensions in the
skill or plugin.

## Calculate targets at runtime

Motion Warping is not proved by serialized notify windows alone.

- Add a real Motion Warping component to the character.
- Trace or query the current obstacle/world state at action time.
- Reject incompatible geometry before montage playback.
- Calculate every warp target from the current hit, lip, floor, or requested
  landing—not from fixed coordinates that happen to satisfy one test level.
- Add or update the named targets before the matching montage window begins.
- Preserve the caller's intended horizontal destination when a floor query only
  supplies vertical grounding.
- Record requested targets, resolved targets, outcome, actor transform, and
  final velocity from the exact PIE instance.

Exercise at least three materially different valid inputs and one invalid input.
The invalid case must remain stationary or follow the specified fallback and
must not report a successful action.

## Prove continuous animation visually

For a complete action, capture at least 13 ordered original frames from one
uninterrupted slow run with a fixed camera. Complex or subtle motion should use
more frames.

Open every original frame individually and verify:

- the same character, mesh, environment, and camera remain identifiable;
- approach, contact, clearance, landing, and recovery occur in order;
- translation and pose advance continuously rather than teleporting;
- no long frozen interval is hidden between two changed frames;
- feet and capsule finish grounded with stable final velocity;
- camera-only or background-only change is not counted as character motion.

Do not approve from hashes, contact sheets, two different frames, or numeric
state alone. Avoid pause/unpause capture loops when pausing changes montage
evaluation; prefer continuous slow playback and capture on successive ticks.

## Cold-restart gate

After saving:

1. Stop PIE cleanly.
2. Restart the editor.
3. Re-open the sequence, montage, character Blueprint, and level.
4. Assert the segment still resolves to the intended sequence.
5. Assert exact notify count, types, starts, and durations.
6. Re-run all valid and invalid runtime scenarios.
7. Capture and inspect another ordered sequence when persistence could alter
   animation evaluation.
8. Require zero new fatal, assertion, ensure, access-violation, or GPU-crash
   marker and no new crash directory in the accepted run.

## Failure modes

| Symptom | Cause and response |
| --- | --- |
| Montage retains length but the character does not move after restart | The duplicated sequence was never saved. Re-create or open it, call `:save()`, restart again, and verify the segment resolves. |
| Motion Warping notify exists but has no valid target | Use a nested `RootMotionModifier` table with an explicit compatible class and a target name that runtime code updates before the window. |
| Notify-state duration collapses after moving its start | Use `configure("notify", ...)`, then re-read both start and duration; never reconstruct the end from stale cached timing. |
| Removing a slot corrupts timing | Never remove the final slot. After removing a non-final slot, re-read and verify relinked events and sections. |
| Unknown nested property appears to succeed | Treat this as a defect: the add must return nil and leave notify count and package state unchanged. |
| One obstacle/input works but variants fail | Target calculation is hardcoded or under-constrained. Derive targets from current traces and test independent valid and invalid scenarios. |
| Two screenshots differ but motion is implausible | Capture one uninterrupted fixed-camera sequence and inspect every original frame in order. |
| In-memory checks pass but cold restart fails | A sequence, montage, Blueprint, or level was not explicitly saved. Re-open every required package after restart. |

## Discovery

- Call `asset:help()` after opening each AnimSequence or AnimMontage.
- Use `asset:list_properties()` before setting plain UPROPERTY fields.
- Use `list("bone_tracks")`, `list("notifies")`, `list("slots")`,
  `list("segments")`, and `list("sections")` for fresh structural readback.
- Use `asset:info()` for sequence or montage summary data.
- Use Blueprint node discovery for Motion Warping component/target calls; do
  not guess node IDs or pin names.
- Use `report_issue(...)` when a valid engine-native workflow cannot survive
  save/restart or a failed mutation leaves partial state.
