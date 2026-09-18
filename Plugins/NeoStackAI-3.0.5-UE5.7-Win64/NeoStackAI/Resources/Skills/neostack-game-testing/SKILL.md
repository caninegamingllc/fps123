---
name: neostack-game-testing
description: How to run autonomous Unreal game playtests through NeoStack Lua. Use when the user asks an agent to test gameplay, play a level, verify input behavior, reproduce a bug in PIE, inspect screenshots/logs during play, or create an automated game-testing loop.
---

# Game Testing

Use `execute_script` with NeoStack's `playtest_*` Lua helpers. Keep a test loop structured: start PIE, wait until ready, mark logs/screens, act, assert, stop PIE.

## Basic Loop

```lua
local target_map = "/Game/Maps/TargetMap"
playtest_start({map=target_map})
local ready = playtest_wait_for_pie({timeout=5})
if not ready.ok then
  playtest_stop()
  playtest_wait_until(function()
    local status = playtest_status()
    return not status.queued and not status.in_progress
  end, {timeout=5, interval=0.05})
  return ready
end

local logs = playtest_log_marker()
local before = playtest_screenshot_marker()

playtest_key({key="W", event="pressed"})
playtest_wait_frames(10)
playtest_key({key="W", event="released"})

local changed = playtest_assert_screenshot_changed(before)
local begin_play = playtest_assert_log_contains("BeginPlay", {since=logs, timeout=2})

playtest_stop()
return {changed=changed, begin_play=begin_play}
```

## What To Use

- `playtest_status()` - check PIE state.
- `playtest_start(opts?)` / `playtest_stop()` - lifecycle. For deterministic
  multiplayer, pass `clients`, `network_mode`, and
  `run_under_one_process=true`.
- `playtest_add_client({timeout=30})` - add one connected late client to an
  already-running in-process listen/dedicated-server PIE session. The result
  reports the exact new `pie_instance`; it refuses standalone PIE and SIE.
- `playtest_wait_for_pie({timeout=5})` - wait before input.
- `playtest_wait(seconds)` / `playtest_wait_frames(n)` - let game/editor tick.
- `playtest_read_state(opts)` - read one live PIE actor, transform, and selected
  reflected properties without relying on console text.
- `playtest_write_state(opts)` - write one exact scalar property on a selected
  live PIE actor/component and return previous/current native readback. When a
  reflected public runtime setter exists, the result reports
  `written.write_mode="runtime_setter"` and its `written.setter`; require that
  path for rendering, collision, replication, or scene-proxy properties whose
  side effects matter.
- `playtest_observe({pie_instance=id, capture_mode="player_view"})` - screenshot
  one exact PIE viewport through the agent image pipeline. Use `player_view` for
  the player's composed view (3D plus screen-space UMG/Slate). Use `scene` only
  when you intentionally need the raw 3D viewport without screen-space UI;
  `scene` remains the backward-compatible default.
- `playtest_screenshot_marker({pie_instance=id})` - lightweight viewport hash
  for one exact PIE instance.
- `playtest_assert_screenshot_changed(before, after?)` - verify visible change.
- `playtest_log_marker()` - scope log checks to new output.
- `invoke({actor_label=..., world="pie", pie_instance=id}, "ServerRpc", args)`
  honors UE network callspace. Invoke a server RPC on the exact autonomous
  proxy, then prove the mutation on the authoritative server and every intended
  peer. For actor object parameters, prefer `{path=state.actor.path}` from an
  exact `playtest_read_state` result; generated pawn display labels can differ
  across PIE worlds even when they represent the same replicated actor.
- `playtest_assert_log_contains(text, {since=marker, timeout=3})` - verify events.
- `playtest_assert(name, fn)` / `playtest_wait_until(fn, opts)` - custom checks.
- `playtest_record_start/mark/burst/stop/review` - film the PIE viewport as a
  timestamped still track plus MP4 video, stamp named marks, and push a bounded
  sample of frames into the chat. See "Record a Timeline".
- `playtest_survey(opts)` - one read-only call describing the live world:
  census, player and camera, game state, hazards, bounds, focus, and per-target
  approach points, navmesh reachability and on-screen visibility.
- `playtest_track_start/stop/summary/join` - sample actor properties, transforms,
  velocity and sockets every tick on the PIE clock, then summarise or join the
  samples to recording marks.
- `playtest_ui_snapshot(opts)` / `playtest_ui(opts)` - walk the live Slate tree
  (native and UMG) and drive it with selector-based steps. See "Drive UI".

## Read Live State

Use exact, case-sensitive selectors. Supply one or more selectors; they combine
with AND semantics. Use `actor` for an exact object name or path, `label` for an
exact editor label, `class` for an exact class name or path, and `tag` for exact
tag text. Ambiguous matches fail and return path-sorted `candidates`.

```lua
local state = playtest_read_state({
  class="/Game/Runner/BP_Runner.BP_Runner_C",
  properties={"CurrentLane", "Score"},
  pie_instance=0,
})
if not state.ok then return state end

local lane = state.properties.CurrentLane.value
local lane_text = state.properties.CurrentLane.serialized
local property_type = state.properties.CurrentLane.type
local location = state.actor.location
```

Property names are exact and case-sensitive. Each requested property returns
`{type, value, serialized}`: compare `value` for typed assertions and record
`serialized` for stable evidence. Use `component="ExactName"` to target an
exact component name/path/class instead of the actor. Read again after every
action; returned tables are snapshots, not live handles.

## Multiple PIE Clients

Keep all peers in one editor process so NeoStack can target their worlds and
viewports:

```lua
local map = "/Game/Maps/TargetMap"
local started = playtest_start({
  map=map,
  clients=2,
  network_mode="listen_server",
  run_under_one_process=true,
})
if not started.ok then return started end

local ready = playtest_wait_until(function()
  local status = playtest_status()
  if not status.playing or status.instance_count ~= 2 then return false end
  for _, instance in ipairs(status.instances or {}) do
    if not instance.has_viewport
        or not string.find(instance.map or "", "TargetMap", 1, true) then
      return false
    end
  end
  return true
end, {timeout=20, interval=0.1})
if not ready.passed then
  playtest_stop()
  return ready
end

local status = playtest_status()
local server, client
for _, instance in ipairs(status.instances) do
  if instance.net_mode == "listen_server" then server = instance end
  if instance.net_mode == "client" then client = instance end
end

-- Add a real late joiner only after the initial peers and gameplay state are
-- ready. Do not restart PIE: late-join correctness requires the existing
-- server world and replicated state to remain alive.
local late = playtest_add_client({timeout=30})
if not late.ok then
  playtest_stop()
  return late
end
local late_state = playtest_read_state({
  pie_instance=late.pie_instance,
  class="/Game/BP_ReplicatedActor.BP_ReplicatedActor_C",
  properties={"ReplicatedState"},
})

local before = playtest_read_state({
  pie_instance=client.pie_instance,
  class="/Game/BP_Player.BP_Player_C",
  properties={"Controller", "CurrentLane"},
})
playtest_key({
  pie_instance=client.pie_instance,
  key="D",
  event="pressed",
})
playtest_wait_frames(3)
playtest_key({
  pie_instance=client.pie_instance,
  key="D",
  event="released",
})
playtest_wait_frames(3)
local after = playtest_read_state({
  pie_instance=client.pie_instance,
  class="/Game/BP_Player.BP_Player_C",
  properties={"Controller", "CurrentLane"},
})
local client_view = playtest_observe({
  pie_instance=client.pie_instance,
  capture_mode="player_view",
})
```

Discover instance IDs from `playtest_status().instances`; do not assume their
numbers or array order. Require the expected map on every peer before acting.
Client contexts and viewports may exist briefly while the client is still
travelling from a temporary map.

For a late-join claim, establish and record server state first, then call
`playtest_add_client()` without stopping PIE. Require `status="joined"`,
`net_mode="client"`, `connected=true`, exactly one new identity, the expected
map, and replicated-state readback from that exact identity. Merely starting
three clients together does not test late join.

Every targeted input, console, or Enhanced Input result reports its resolved
`pie_instance`, world, and net mode. Check that identity, then read the intended
game state from the same instance. Delivery is not gameplay success. A client
may only see an unpossessed `ROLE_SimulatedProxy`; if its `Controller` property
is empty, fix the game's spawn, possession, or replication setup before
blaming input targeting.

For an RPC claim, first read the candidate pawn in the chosen client and require
`local_role="ROLE_AutonomousProxy"`. Call the persisted RPC on that pawn, not a
server-side stand-in or the replicated target actor directly. A local client
state change without authoritative server change is a failed RPC proof even if
the reflected invocation returned successfully.

## Input

```lua
playtest_key({pie_instance=0, key="SpaceBar", event="pressed"})
playtest_wait_frames(1) -- let the player-input tick dispatch the edge
playtest_key({pie_instance=0, key="SpaceBar", event="released"})
playtest_axis({pie_instance=0, key="Gamepad_LeftX", value=1.0})
playtest_click({pie_instance=0, x=0.5, y=0.5, normalized=true})
playtest_console("stat fps", {pie_instance=0})
```

If the Enhanced Input extension is loaded:

```lua
playtest_input_action({
  pie_instance=0,
  action="/Game/Input/IA_Jump",
  value=true,
  mode="pulse",
})
playtest_input_mapping({
  pie_instance=0,
  mapping="Move",
  value={x=1,y=0},
  mode="pulse",
})
```

For mapping-context switches, verify the subsystem state directly:

```lua
playtest_input_context({
  op="add", context="/Game/Input/IMC_OnFoot", priority=10,
  force_immediately=true,
})
local status = playtest_input_context({
  op="status", context="/Game/Input/IMC_OnFoot",
})
local keys = playtest_input_context({
  op="query_keys", action="/Game/Input/IA_Interact",
})
```

Require the exact priority and key set before input. After adding a higher
priority context, prove its shared-key conflict with `query_map_key`; after
removal, require `has_context=false` and the lower-priority keys to return.
Re-adding one context must not increase `key_count` or duplicate action events.

Treat mapping authoring as atomic. For a missing action or malformed
trigger/modifier, compare `#context:list("mappings")` before and after: failure
must not leave a weaker bare mapping. Save and reopen successful mappings, then
verify every trigger/modifier and its ordered properties with
`list("mapping_details", "1")` (the index argument is a string).

Treat `playtest_key().consumed` as advisory. Raw Blueprint key events can run
even when no legacy Action Mapping reports the key as consumed. Prove input by
reading the resulting game state after at least one frame.

## Record a Timeline

A screenshot proves one instant. A recording proves motion, timing and what
happened between two inputs. Recordings survive across `execute_script` calls;
keep the `recording_id`.

```lua
local rec = playtest_record_start({ arm = true, stills = { interval = 0.1 }, label = "jump" })
playtest_start({ map = "/Game/Maps/TargetMap" })
playtest_wait_for_pie({ timeout = 5 })
playtest_record_mark("jump_pressed")
playtest_record_burst({ seconds = 0.5, interval = 0.03 })
playtest_key({ key = "SpaceBar", event = "pressed" })
playtest_wait_frames(1)
playtest_key({ key = "SpaceBar", event = "released" })
playtest_wait(0.8)
local done = playtest_record_stop({ recording_id = rec.recording_id })
playtest_record_review({ recording_id = rec.recording_id, marks = { "jump_pressed" }, around = 0.4, max_frames = 6 })
playtest_stop()
```

- `arm = true` starts the film with the first frame of the next PIE session.
- Every still carries `game_time`, `frame`, `mean_luma` and `near_black_pct`;
  a run of near-black stills means no camera or an unlit level, not a bug in
  the thing you are testing.
- `video` is present when a hardware encoder is available (Video Encoding
  integration plus the engine codec plugin for the GPU); otherwise
  `video_fallback_reason` says why and the still track is still complete.
  Resizing the viewport mid-recording ends the video with `viewport_resized`
  and keeps the frames captured before it. Stills follow the PIE clock, so a
  paused game produces no duplicate stills; video keeps its own framerate.
- `video_index.json` maps every MP4 sample to its PIE `game_time`; the MP4
  itself runs on wall time, so a paused or slowed game plays back at real speed.
- `capture = "scene_fallback"` means the player-view crop could not be proven
  for that viewport and the recording used the scene render target instead
  (no HUD or UMG in the frames).
- `review` selectors: `marks` (with `around` seconds), `range = {from, to}`,
  `every_nth`, `first_last`, `indices`. Frames over the wire budget are listed
  in `dropped_for_budget` with paths for `read_file`.
- Inspect the pushed frames in order. A changed frame is evidence of change,
  never proof of the intended action.

## Survey the World

Call `playtest_survey` before planning inputs so the agent acts on the live
world rather than on assumptions about the map.

```lua
local s = playtest_survey({
  targets = { { label = "Door_01" } },
  hazards = { radius = 3000 },
})
local door = s.vantages.vantages[1]
-- door.approach.location is a standing point facing the door; door.line_of_sight says whether it is visible from there
-- s.reachability.entries[1].reachable is a navmesh answer, not a guess
-- s.visibility.entries[1].in_frustum and .occluded describe the player's current view
```

- `hazards[].certainty` is `engine` when the class proves it (kill volume,
  pain-causing volume, water volume, streaming volume, AI-controlled pawn) and
  `heuristic` when only a name matched. Treat heuristic hazards as leads.
- Targets use the same exact selectors as `playtest_read_state`; an ambiguous
  target fails the call with `candidates`.
- With no navmesh, `nav_projected` and `reachable` are false and `query`
  says `no_navdata`. That is a level fact worth reporting, not a survey error.
- Reachability paths to `goal`, the target's nearest navmesh point
  (`goal_nav_projected`), so a target floating above the floor or buried in
  its own mesh still answers correctly.

## Track State Over Time

Tracks sample every editor tick on the same PIE clock as recordings, so a
value curve can be lined up with the frames that show it.

```lua
local t = playtest_track_start({
  tracks = {
    { label = "BP_Runner_C_0", properties = { "CurrentLane", "bIsJumping" }, transform = true, velocity = true },
  },
  max_seconds = 10,
})
-- act here
local data = playtest_track_stop({ id = t.id })
local stats = playtest_track_summary({ id = t.id })
local aligned = playtest_track_join({ id = t.id, recording = done.recording })
```

- `series[column]` arrays are dense; a missing sample is `false`. Vectors and
  rotators expand to `.x/.y/.z` and `.pitch/.yaw/.roll` columns.
- `summary` gives first, last, min, max, mean, delta and change_count per
  column; use it before paging through `time[]` and `series`.
- `allow_late_spawn = true` keeps a track alive while a target has not spawned
  yet and records the missing span in `gaps[]`.

## Drive UI

`playtest_ui` reaches widgets, not pixels. Selectors are exact:
`{umg="StartButton"}`, `{type="SButton"}`, `{text="Play"}`,
`{text_contains="Pla"}`, `{user_widget="WBP_Menu_C"}`, `{path="0/3/1"}`,
plus `index` to pick one of several matches.

```lua
local r = playtest_ui({
  timeout = 10,
  steps = {
    { op = "wait_for", selector = { umg = "StartButton" }, condition = "visible" },
    { op = "click", selector = { umg = "StartButton" } },
    { op = "type", selector = { umg = "NameEntry" }, text = "Player1\n" },
    { op = "drag", selector = { umg = "Volume" }, offset = { x = -100, y = 0 }, to = { selector = { umg = "Volume" } } },
    { op = "assert", selector = { umg = "Score" }, condition = "text_contains", value = "10" },
  },
})
```

- Events route straight to the widget path. The OS cursor is never moved and
  the editor's click-to-capture overlay is bypassed, so scenarios do not depend
  on window focus.
- Use `playtest_ui_snapshot({ selector = ... })` first when a selector might be
  ambiguous; `widget_ambiguous` returns the candidates.
- Keys route like real keys: `Escape` stops PIE in the editor. Prefer game
  actions through `playtest_key` and reserve `playtest_ui` `key` steps for
  widget navigation such as `Tab` and `Enter`.
- `playtest_click` is game input through the viewport client; `playtest_ui`
  is Slate input. Use `playtest_ui` for anything that is a widget.

## Testing Rules

- Pass the exact target asset path in `playtest_start({map=...})` unless the
  current editor map is itself the behavior under test. The editor can be on
  an unsaved `Untitled` world and silently spawn a default pawn otherwise.
- Put the dependent lifecycle—start, baseline read, input, later reads,
  observations, and stop—inside one `execute_script` call. Lua state is not
  shared across calls, and fragmented probes can strand PIE on failure.
- Prefer `playtest_read_state` and scoped logs for exact assertions.
- For visual behavior, also call
  `playtest_observe({capture_mode="player_view"})` and inspect the returned
  image; a compile result or property read cannot prove rendering. Use
  `capture_mode="scene"` only to isolate raw world rendering from UI composition.
- For HUD, menu, or runtime UMG claims, use
  `playtest_observe({pie_instance=id, capture_mode="player_view"})`. The scene
  backbuffer omits screen-space UI. Treat failure to obtain a Slate-composited capture as
  a failed visual gate; never silently substitute a scene-only frame.
- For motion, animation, simulation, or rendered video, inspect a dense ordered
  sequence of original frames. Cover setup, onset, intermediate motion,
  completion, and the settled result while keeping actor identity and camera
  continuity visible. For a short clip, inspect every frame; for a longer clip,
  inspect a uniform dense sample plus every transition region, then expand any
  suspicious interval to its adjacent frames. A changed hash or different first
  and last frame never proves the prompted action.
- Screenshot hash changes can happen from TAA, sky, particles, or camera jitter; use it as "frame changed", not proof of intent.
- For multiplayer visuals, capture and inspect each exact `pie_instance`.
  Headless `-nullrhi` tests cannot provide meaningful framebuffer evidence.
- Validate renderer-specific claims on a supported RHI and shader model. In UE
  5.8 on Windows, Lumen evidence requires D3D12/SM6; a D3D11/SM5 image is not
  Lumen proof even if the project setting says Lumen. Record the editor command
  line and read back the rendering settings. On a host where the engine/driver
  async-compute device-removal path is independently reproduced, an evaluation
  runner may opt into `-DisableAsyncCompute`, but must record that mitigation,
  run the editor survival gate, and must not describe it as a plugin fix.
- Always stop PIE on failure paths when the script started it, then wait until
  both `queued` and `in_progress` are false before starting another session.
- Return structured tables with `ok`, `passed`, `message`, and useful evidence.

## Failure Modes

| Symptom | Cause / fix |
|---|---|
| `status="no_pie"` | Start PIE and wait with `playtest_wait_for_pie`. |
| World map is `Untitled` or actor is `DefaultPawn` | The wrong editor world started. Stop PIE and restart with the exact `map` asset path. |
| `actor_ambiguous` | Add an exact label, class, tag, or object path; inspect sorted `candidates`. |
| `property_not_found` | Use the exact reflected property name and casing. |
| `write_failed` says the property cannot be edited on instances | Keep the safety boundary. If runtime control is intentional, expose a one-input BlueprintCallable `Set<Property>` function with the same scalar type; `playtest_write_state` discovers it and reports `write_mode="runtime_setter"`. Only make a property instance-editable when designers should genuinely edit it. |
| Key call succeeds but state does not change | Wait at least one frame, then inspect state; do not use `consumed` as the behavior assertion. |
| Two instances exist but a client still reports a temporary map | Wait until every instance reports the requested map and a viewport. |
| `playtest_add_client` reports `invalid_session` | Start in-process PIE with a listen or dedicated server; standalone PIE and SIE cannot prove network late join. |
| `playtest_add_client` times out | Stop PIE, inspect networking/output logs, and fix server startup, travel, login, or connection state before retrying. Do not count an unconnected new context as a joined client. |
| Correct client identity but gameplay does not change | Read the target pawn's `Controller` and role. An empty controller on a simulated proxy is a game spawn or possession problem. |
| Explicit instance reports no viewport | The target may be a dedicated server or still starting. Never fall back to another client. |
| Runtime UMG is absent from the screenshot | Capture with `capture_mode="player_view"`; the scene backbuffer is not UI evidence. |

## Good Failure Report

```lua
return {
  ok = false,
  message = "Jump input did not produce expected log",
  screenshot = playtest_observe({capture_mode="player_view", max_dimension=512}),
  status = playtest_status(),
}
```
