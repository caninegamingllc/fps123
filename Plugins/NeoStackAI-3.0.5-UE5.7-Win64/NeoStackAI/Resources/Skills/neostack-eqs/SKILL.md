---
name: neostack-eqs
description: Author, inspect, execute, and verify Unreal Engine Environment Query System assets through NeoStack `execute_script`. Use for EQS generators, filters, scoring, contexts, AI cover or search queries, runtime item diagnostics, dynamic invalidation, navigation movement, no-result fallback, and cold persistence checks.
---

# NeoStack EQS

Use the native EQS asset enrichment and PIE bindings. Begin with:

```lua
print(help("EQS"))
print(help("NavMesh"))
print(help("Playtest"))
```

Lua handles do not survive between calls. Reopen the asset and level in every
mutation or verification call.

## Author the query

Create or reopen an Environment Query asset:

```lua
local path = "/Game/AI/EQS_Search"
local query = open_asset(path)
  or assert(create_asset(path, "EnvironmentQuery"))
```

Discover supported classes before selecting them:

```lua
local generators = query:list("generators")
local test_types = query:list("test_types")
local contexts = query:list("contexts")
```

Add one deliberate generator and compatible tests. Actor generators produce
actor items; grid generators produce point items. An incompatible test must
fail without being appended.

```lua
assert(query:add("option", {generator="ActorsOfClass"}) == 0)
assert(query:configure("generator", {
  option=0,
  SearchedActorClass="/Script/Engine.TargetPoint",
  GenerateOnlyActorsInRadius=true,
  SearchRadius=5000,
  bAutoSortTests=false,
}))

assert(query:add("test", {option=0, type="Pathfinding"}) == 0)
assert(query:add("test", {option=0, type="Trace"}) == 1)
assert(query:add("test", {option=0, type="Distance"}) == 2)
assert(query:add("test", {option=0, type="Dot"}) == 3)
```

Configure both the base test policy and subclass-specific properties. Always
read them back because editor property-change events can alter runtime
descriptions and modes:

```lua
assert(query:configure("test", {
  option=0, index=0,
  purpose="FilterAndScore",
  filter_type="Maximum",
  float_value_max=10000,
  scoring_equation="InverseLinear",
  scoring_factor=0.75,
  TestMode="PathCost",
  Context="/Script/AIModule.EnvQueryContext_Querier",
  PathFromContext=true,
  SkipUnreachable=true,
  comment="Reject unreachable items and prefer lower path cost",
}))

local tests = assert(query:list("tests", {option=0}))
assert(tests[1].purpose == "FilterAndScore")
assert(string.find(tests[1].title, "PathCost", 1, true))
```

For Blueprint EQS contexts, use the generated-class path in saved properties:

```lua
local threat_context =
  "/Game/AI/BP_ThreatContext.BP_ThreatContext_C"
```

Use `query:reorder`, `query:duplicate`, and `query:remove` for topology edits.
After authoring, call `query:save()` and verify `info()`, `list("options")`, and
`list("tests", {option=...})` in a fresh script.

## Prepare navigation and the arena

Create a real navigation bounds volume through the Level Design lifecycle:

```lua
local level = assert(open_level())
assert(level:add("volume", {
  class="/Script/NavigationSystem.NavMeshBoundsVolume",
  location={x=0,y=0,z=300},
  extent={x=3000,y=3000,z=600},
  label="NavBounds",
}))
assert(level:save())
```

Use `navmesh_rebuild_all()` or `navmesh_build()`. Do not infer success from the
presence of a Recast actor. Require completed tile generation and point/path
queries:

```lua
assert(navmesh_rebuild_all())
local nav = assert(navmesh_info())
assert(nav.is_building == false and nav.tile_count > 0)
assert(navmesh_project_point({x=0,y=0,z=50}, {x=100,y=100,z=300}))
```

Keep visual marker collision disabled unless it is intentionally part of the
query. Design each rejection cause independently: one obstacle must not
accidentally shield candidates assigned to a different scenario.

## Execute in an exact PIE world

Start the requested map and wait for its PIE world:

```lua
assert(playtest_start({
  mode="pie",
  map="/Game/Maps/L_AI",
  clients=1,
  network_mode="standalone",
  run_under_one_process=true,
}))
assert(playtest_wait_for_pie({timeout=15, interval=0.05}).passed)
```

Run the saved query through the native manager. Name the exact querier and
override dynamic contexts when the authored context needs a live actor:

```lua
local query = assert(open_asset("/Game/AI/EQS_Search"))
local result = assert(query:run({
  world="pie",
  pie_instance=0,
  querier="AI_Guard",
  context_overrides={{
    context="/Game/AI/BP_ThreatContext",
    actor="Threat",
  }},
  run_mode="AllMatching",
  max_items=128,
}))
```

Do not reduce evaluation to `result.ok`. Record:

- `generated_count`, `valid_count`, and `rejected_count`;
- every item's label/location, normalized and raw scores;
- `failed_test_index` and `failed_description`;
- per-test raw results and weighted scores;
- selected item, query status, exact world, and execution time.

A correct no-result query can return `ok=false`, `status="Failed"`,
`valid_count=0`, and complete rejected-item diagnostics. Treat that as a
verified fallback state when PIE and the editor remain healthy.

## Run a scenario matrix

Use at least these independent scenarios:

1. Baseline: valid and rejected items have the expected causes and ordering.
2. Dynamic invalidation: change one live obstacle and prove one intended item
   changes validity while unrelated causes remain stable.
3. Navigation movement: send the selected location to the live AI controller.
4. No result: disable or move all qualifying cover and require structured
   zero-result evidence without a crash.

Route runtime mutations to the exact PIE world:

```lua
assert(invoke(
  {actor_label="MovableObstacle", world="pie", pie_instance=0},
  "K2_SetActorLocation",
  {NewLocation={x=500,y=-900,z=100}, bSweep=false, bTeleport=true}
))

local request = assert(invoke(
  {actor_label="AIController0", world="pie", pie_instance=0},
  "MoveToLocation",
  {
    Dest={x=100,y=500,z=10},
    AcceptanceRadius=60,
    bStopOnOverlap=true,
    bUsePathfinding=true,
    bProjectDestinationToNavigation=true,
    bAllowPartialPath=false,
  }
))
```

For movement or other temporal behavior, keep one camera fixed and inspect at
least seven ordered original frames spanning start, intermediate positions,
arrival, and a final stable frame. Pair every frame with
`playtest_read_state`; require monotonic progress, stable identity and
environment, nonzero intermediate velocity, zero final velocity, and no
camera-only change. Two hashes or two different frames are not sufficient.

## Cold verification

After stopping PIE:

1. Save the query, Blueprints, and level.
2. Run native Map Check and require zero errors and warnings.
3. Restart the editor.
4. Reopen and reconstruct generators, tests, contexts, weights, comments, and
   arena actors from fresh handles.
5. Rebuild navigation and repeat a baseline PIE query.
6. Scan the scoped editor logs for fatal, assertion, ensure, exception, and GPU
   crash signatures.

Do not accept an editor-world result as runtime proof. Do not accept a
successful mutation log without fresh readback, and do not hardcode behavior
for one query or actor layout.
