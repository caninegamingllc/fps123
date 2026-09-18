---
name: neostack-mover
description: Inspect, author and verify Unreal Engine Mover 2.0 movement components through `execute_script`. Use for movement modes and shared settings on a MoverComponent, queueing layered moves, movement modifiers and instant effects in Play In Editor, and proving a movement change with a frame sampler.
---

# Mover 2.0 through `execute_script`

Start with `help("Mover")` and
`mover_support()`; `mover_catalog('moves'|'modifiers'|'effects'|'modes')`
lists every type the engine (or your plugins/Blueprints) exposes with its
reflected fields, so never guess struct or field names.

Mover has no asset: every verb takes a **target** that names a
`UMoverComponent` on an actor. Use the actor label, a component path, or the
`playtest_read_state` selector shape `{label=, component=, world='editor'|'pie'}`.
`world='auto'` (default) picks the PIE world while PIE runs and the editor
world otherwise, so the same target string works before and after
`playtest_start()`.

## Work in this order

1. **Inspect.** `mover_info(target)` shows `modes` (name, class, gameplay
   tags, shared settings classes), `starting_mode`, `backend_class`,
   `shared_settings`, `registered_moves` (5.7+). `mover_state(target)` is the
   runtime view: `mode`, `speed`, `velocity`, `active_moves`, `modifiers`
   (with handles), `time_step`, `character` predicates.
2. **Author modes in the editor world** (transactional, Undo works):
   `mover_add_mode(target, 'Hover', 'FlyingMode', {replace=true})`,
   `mover_configure_mode(target, '', {settings='CommonLegacyMovementSettings', MaxSpeed=1200})`,
   `mover_set_starting_mode(target, 'Hover')`, `mover_remove_mode(target, 'Hover')`.
   Every property is validated before anything is touched; a bad key refuses
   the whole call.
3. **Play and queue.** `playtest_start({mode='pie'})` -> `playtest_wait_for_pie({timeout=15})`
   -> `playtest_wait_frames(3)` -> `mover_queue_move`, `mover_queue_modifier`,
   `mover_queue_effect`, `mover_queue_mode`. Outside PIE these return
   `status='no_pie'` instead of tripping engine ensures.
4. **Verify with samples.** `mover_sample(target, {frames=15})` yields while the
   editor ticks and returns `first`, `last`, `speed_delta`, `mode_changed`.
   Assert on `last.speed` / `last.mode`, then `playtest_stop()` and
   `playtest_wait_until(...)` for a clean teardown.

## End-to-end example (fixture -> PIE -> queued move changes velocity)

```lua
-- Editor world: self-seeding fixture (capsule root + CharacterMoverComponent + standalone backend)
local fx = assert(mover_create_test_actor({ name = "MoverDemo", location = { x = 0, y = 0, z = 300 }, starting_mode = "Flying" }))
assert(fx.ok, fx.error)
local info = mover_info(fx.component_path)
print(info.starting_mode, #info.modes, info.shared_settings[1] and info.shared_settings[1].class)
assert(mover_configure_mode(fx.component_path, "", { settings = "CommonLegacyMovementSettings", MaxSpeed = 1200 }).ok)

-- PIE proof
local started = playtest_start({ mode = "pie" })
assert(started.ok, started.message)
playtest_wait_for_pie({ timeout = 15 })
playtest_wait_frames(3)
local h = mover_find({ label = "MoverDemo", world = "pie" })
assert(h.ok, h.error)
assert(h:state().speed == 0)
assert(h:queue_move({ struct = "LayeredMove_LinearVelocity", Velocity = { x = 300, y = 0, z = 0 }, DurationMs = 2000, MixMode = "OverrideVelocity" }).ok)
local s = h:sample({ frames = 15 })
print(s.first.speed, s.last.speed, s.last.mode)   -- expect last.speed ~300
assert(h:queue_mode("Falling").ok)
print(h:sample({ frames = 5 }).last.mode)          -- "Falling"
local stance = h:queue_modifier({ struct = "StanceModifier", stance = "Crouch", DurationMs = -1 })
print(stance.handle, h:state().character.is_crouching)
h:cancel_modifier(stance.handle)
playtest_stop()
playtest_wait_until(function() local st = playtest_status(); return not st.queued and not st.in_progress end, { timeout = 10 })

-- Cleanup
mover_destroy_test_actor("MoverDemo")
```

## Rules the binding enforces

- Struct specs name a concrete type: `{struct='LayeredMove_LinearVelocity', ...}`
  (`FLayeredMove_...` also accepted). Unknown fields, transient fields
  (`StartSimTimeMs`), wrong types, bad enum names and NaN/inf refuse the call.
  Vectors are `{x=,y=,z=}`; enums are names (`MixMode='OverrideVelocity'`).
- Queue stance modifiers before switching the mode to Falling; the engine drops
  them while falling even though the queue call succeeds. Confirm with
  `state().modifiers` and the `Mover.Stance.IsCrouching` tag.
- `StanceModifier` needs `stance='Crouch'|'Prone'` (its `ActiveStance` member
  is not reflected). Only one modifier type is active at a time.
- `mover_queue_move({logic='LaunchMoveLogic'})` (5.7+) needs
  `mover_register_move` first and one simulation tick (`playtest_wait_frames(1)`)
  before the registration is flushed; `params=` is refused as `unsupported`
  (the engine does not export the contextual activation path).
- Editor-time `mover_remove_mode` keeps the mode object alive for Undo; in PIE
  the engine's `RemoveMovementMode` runs so the simulation unregisters it.
- `mover_sample` is bounded to 600 frames and returns `status='pie_ended'` if
  PIE stops mid-sample.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `status='no_pie'` from a queue verb | Not in PIE or the component has not begun play. Start PIE, wait, resolve with `world='pie'`. |
| `status='no_backend'` | `BackendClass` is null on the component; set it (the fixture uses `MoverStandaloneLiaisonComponent`). |
| `status='not_registered'` on the logic path | Call `mover_register_move`, then wait a frame. |
| `status='unsupported_engine'` | Feature needs a newer engine (register 5.7+, tag list/has-features 5.8); check `mover_support().features`. |
| `status='exists'` | Mode name or fixture label already used; pass `replace=true` or pick another name. |
| `handle == 0` / `status='no_simulation'` | The simulation is not initialised yet; wait a frame and retry. |
| `playtest_start` refuses with `missing_nanite_sm6` | The active map carries the UE 5.8 Nanite/SM6 PIE risk; open an empty level first. |

Discovery escape hatches: `help("Mover")`, `mover_catalog(kind)`,
`mover_find(target):info()`, `class_properties('FlyingMode')`, `report_issue()`.
