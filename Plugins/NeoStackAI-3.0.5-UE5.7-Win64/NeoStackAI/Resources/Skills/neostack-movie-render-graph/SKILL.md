---
name: neostack-movie-render-graph
description: Evaluate and query Unreal Movie Render Graph presets through execute_script — output resolution, frame rate, output directory, variables, node properties, branch lookups, live graph-render progress and graceful shutdown. Use when the user asks what a Movie Render Graph will render, wants to change a graph node or variable value, needs the resolved output path or version, or wants progress of a running graph render. Queue/job CRUD lives in neostack-movie-render-queue.
---

# Movie Render Graph

Everything here is Lua for `execute_script`. A `graph` handle comes from `mrq_open_graph(asset_path)` (see
`neostack-movie-render-queue`); this skill adds the evaluation and query
methods to that handle plus the `mrg_*` globals. `help("MovieRenderGraph")`
lists every signature.

Start with the capability probe; the graph API grew across engine versions:

```lua
local sup = mrg_support()
print(sup.engine_version, sup.features.resolution_queries, sup.features.insert_nodes)
```

`features.resolution_queries` (5.6+) gates overscan/backbuffer/crop numbers,
`features.node_search` (5.6+) gates `nodes_for_branch`/`nodes_for_tag`,
`features.format_arguments` (5.7+) gates `as_path=false`,
`features.insert_nodes` (5.8+) gates `insert_before`/`insert_after`.

## Author a preset and read back what it will render

```lua
local path = "/Game/Cinematics/MRG_Proof"
assert(create_asset(path, "MovieGraphConfig"))
local graph = assert(mrq_open_graph(path))

-- One Global Output Setting node wired into the Output node's Globals branch.
local node = assert(graph:add_node("GlobalOutputSetting", "proof_output"))
assert(graph:connect(node.id, "Globals", "output", "Globals"))

-- Node properties take Unreal export text, numbers, booleans or tables.
-- The matching bOverride_* flag is switched on automatically.
assert(graph:set_node_property(node.id, "OutputResolution",
  '(ProfileName="Custom",Resolution=(X=1280,Y=720))') == true)
assert(graph:set_node_property(node.id, "OutputDirectory",
  '(Path="' .. project_dir() .. 'Saved/Renders/MRGProof")') == true)
assert(graph:set_node_property(node.id, "OutputFrameRate", "(Numerator=30,Denominator=1)") == true)
assert(graph:set_node_property(node.id, "ZeroPadFrameNumbers", 4) == true)
assert(graph:save())

local ev = graph:evaluate({ overscan = 0.1 })
assert(ev.ok, ev.error)
assert(ev.output.resolution.width == 1280)
assert(ev.output.frame_rate.numerator == 30)
print(ev.output.directory, ev.output.desired.width, ev.output.overscanned and ev.output.overscanned.width)
```

`graph:evaluate()` flattens the graph with `CreateFlattenedGraph`. Without
`opts.job` it uses a transient job, so job-level variable overrides are not
applied; pass `{job = queue_index}` (1-based `mrq_get_queue()` index) or call
`job:evaluate()` on an `mrq_get_job(i)` handle that has `set_graph` to see
the real assignments. The result carries `branches`, `output`
(directory, resolution, frame_rate, zero_pad, frame_offset, handle_frames,
custom_range, versioning, desired/overscanned/backbuffer/crop_rect on 5.6+),
`file_outputs` (per-branch `FileNameFormat`), `variables` and
`evaluated_graphs`.

Resolution only:

```lua
local res = graph:resolution({ overscan = 0.1, aspect_ratio = 0 })
if res.ok then
  print(res.desired.width, res.overscanned.width, res.backbuffer.width, res.crop_rect.width)
else
  print(res.error) -- "unsupported: ... needs UE 5.6+"
end
```

## Variables

```lua
assert(graph:add_variable("Quality", "NeoStack"))
local v = graph:get_variable("Quality")
print(v.type, v.value, v.is_global)          -- e.g. Float 0 false
assert(graph:set_variable("Quality", 2.5) == true)
local bad = graph:set_variable("Quality", "high")
assert(bad.ok == false)                      -- type mismatch refused, value untouched
```

Values are validated against the variable's type before anything is
written; global variables (`is_global == true`) and container variables are
refused.

## Find nodes, restructure

```lua
for _, b in ipairs(graph:branches()) do print(b) end
local nodes = graph:nodes_for_branch("Globals")            -- 5.6+
local tagged = graph:nodes_for_tag("proof_output")         -- 5.6+, case sensitive
local inserted = graph:insert_after(node.id, "GlobalGameOverrides", "Globals") -- 5.8+
assert(graph:remove_node(inserted.id) == true)
```

## Resolve file names and versions without rendering

```lua
local r = mrg_resolve_format("{sequence_name}_{frame_number}", {
  graph = "/Game/Cinematics/MRG_Proof", root_frame = 12, zero_pad = 4 })
print(r.resolved, r.arguments.frame_number)   -- ... "0012"
local v = mrg_resolve_version({ graph = "/Game/Cinematics/MRG_Proof", next = true })
print(v.version)                              -- {ok=false, version=-1} is refused up front without a graph/job to evaluate
```

## Watch a graph render and stop it cleanly

```lua
local s = mrg_pipeline_status()
if s.active then
  print(s.state, s.completion, s.frames.current, s.frames.total, s.segments.outer, s.segments.inner)
else
  print(s.note)      -- "executor idle" / "PIE starting" / "legacy pipeline — use mrq_render_progress"
end
```

`mrg_request_shutdown()` asks the running pipeline to finish the current
frame and flush completed work (`UMoviePipelineBase::RequestShutdown`);
`mrq_cancel_render()` cancels at the executor. Both refuse when nothing is
rendering. The same status table is embedded as `jobs[i].graph` in
`mrq_render_progress()`.

## Failure modes

| Symptom | Cause and fix |
|---|---|
| `graph:resolution()` returns `supported=false` | UE 5.5; use `evaluate().output.resolution` (the named resolution) instead. |
| `set_node_property` returns `{ok=false}` "cannot set ... import" | Export text does not match the property type. Read `get_node_property(id, name)` first and copy its shape. |
| `evaluate().output.is_cdo == true` | No output setting node reaches the Globals pin; `connect(node.id, "Globals", "output", "Globals")`. |
| `variables_source == "graph_defaults"` | UE 5.5/5.6 cannot report evaluated values; defaults are shown. |
| `mrg_pipeline_status().active == false` right after `mrq_render_queue()` | PIE is still starting; poll again in a later call. |
| `mrg_resolve_version().version == -1` | Pass `graph=` (or a graph-mode `job=`) so the Output Setting node can be evaluated. |
| `job:evaluate` says "not using a graph configuration" | Call `job:set_graph(path)` first. |

## Discovery escape hatches

- `help("MovieRenderGraph")`, `help("MovieRenderQueue")`
- `graph:info()`, `graph:get_node_property(id, name)`, `mrg_named_resolutions()`
- `report_issue("...")` only for an exact reproducible API gap
