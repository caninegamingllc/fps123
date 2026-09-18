---
name: neostack-epic-toolsets
description: Work with Epic's UE 5.8 experimental toolsets from `execute_script` — read, import, create and update Epic AgentSkill assets, sandbox content saves with sandbox_enter/persist/discard, and map Epic toolset tools to NeoStack verbs with epic_toolsets_matrix. Use when a task mentions AgentSkills, Epic's assistant, the FileSandbox, or "which Epic toolset tools does NeoStack cover".
---

# Epic toolsets through `execute_script`

Start with `epic_toolsets_support()`; if `available` is false the engine is
older than 5.8 or the ToolsetRegistry plugin is disabled and every verb below
returns a structured `unsupported` result. `help("EpicSkills")`,
`help("FileSandbox")` and `help("EpicToolsetsMatrix")` print signatures.
Lua state is fresh per call; class paths (`/Game/Skills/MySkill.MySkill_C`)
and sandbox names are the handles you carry between calls.

## Reading Epic AgentSkills

```lua
local h = epic_skills()
local skills = h:list("epic")            -- what Epic's assistant sees
for _, s in ipairs(skills.skills) do
  log(s.path .. " [" .. s.kind .. "] " .. s.description)
end
local one = h:get(skills.skills[1].path)  -- description, instructions, prompt
log(one.skill.prompt)
```

`list("epic", { include_hidden = true })` also returns skills Epic hides
(empty description or blocked by `UToolsetRegistrySettings`), flagged with
`hidden_by_epic` / `hidden_reason`. `ue_tool("skill_list")` is Epic's own
`ListSkills`; both must agree on paths.

## Leaving a skill behind for Epic's assistant

Write-through is off by default. Enable it (per user, per project) and keep
the folder allow-list tight:

```lua
epic_toolsets_settings({ allow_skill_write_through = true,
                         skill_write_folders = { "/Game/Skills" } })
local h = epic_skills()
local r = h:add({
  folder = "/Game/Skills",
  name = "ForestPass",
  description = "Populate a forest on the selected terrain with PCG.",
  instructions = "1. open_mesh_terrain() ... 2. pcg handle generate() ...",
  save = true,
})
assert(r.ok, r.error)                    -- r.path is the class path
h:configure(r.path, { description = "Populate a forest (v2)." })
```

Refusals are structured: `write_through_disabled`, `folder_not_allowed`,
`invalid_name`, `invalid_folder`, `asset_exists`, `not_editable` (native or
Python skills), `unknown_key`, `invalid_args` / `invalid_path` (missing or
mistyped arguments never raise). `configure` runs inside the segment
transaction, so a later error in the same script rolls the edit back; `add`
registers the asset for rollback too. `remove(path)` deletes the Blueprint;
because asset deletion can reset the editor undo buffer it first commits the
script's earlier work (a `[CHECKPOINT]` line in the log) and opens a fresh
rollback window, so put `remove` last or in its own call.

## Mirroring Epic skills into the NeoStack catalogue

```lua
local report = epic_skills():import()    -- Saved/NeoStackAI/EpicSkills/<name>/SKILL.md
log(report.imported_count .. " imported, " .. #report.skipped .. " skipped")
for _, e in ipairs(epic_skills():list("catalogue").skills) do
  log(e.name .. " <- " .. e.path)        -- names are epic-<sanitised-class-name>
end
```

The mirrors are read-only (`source_id = "ue.agentskill"`); edit the Unreal
asset, then re-import. Nothing is written into `.claude/` or `.agents/`.

## Sandboxing content saves

Only content mount points (`/Game`, `/Engine`, `/<Plugin>`) are sandboxed;
`Config/`, `Source/` and `Saved/` writes are not. One sandbox at a time.

```lua
local s = sandbox_status()
assert(s.available and not s.active, s.reason or ("active: " .. tostring(s.name)))
assert(sandbox_enter("neostack-forest-" .. os.time(), { description = "forest pass" }).ok)

local dup = duplicate_asset("/Engine/BasicShapes/Cube", "ForestProbe", "/Game/Props")
open_asset(dup.path):save()              -- lands in Intermediate/Sandboxes/<name>/Sandbox/Game/...

for _, c in ipairs(sandbox_changes().changes) do log(c.action .. " " .. (c.package or c.path)) end

-- keep it:                              -- or throw it away:
local p = sandbox_persist()              -- local d = sandbox_discard()
assert(p.ok, p.error)                    -- (RevertAll purges/hot-reloads packages)
assert(sandbox_leave().ok)
sandbox_delete("neostack-forest-...")    -- optional; a left sandbox stays on disk
```

`sandbox_persist()` with no files gathers every changed file and persists them
all (`persisted_all=true`, `requested=N`); a clean sandbox refuses with
`code='no_changes'`. It reports per-file results; `Failure`/`NotAllowed`
(read-only files without source control) comes back as `ok=false,
code='partial_failure'` with `failed=[{path, error}]`, and those files stay
sandboxed. Refusals and no-ops (`another_sandbox_active`, `already_active`,
`no_active_sandbox`, `empty_file_list`) are answered before any transaction
checkpoint, so a refused call costs the script nothing. Per-file
discard (`sandbox_discard({files={...}})`) is experimental in 5.8; prefer
discard-all. `sandbox_leave()` can return `code='locked'` with `lock_reason`
when another system holds the sandbox.

## Which Epic toolset tools does NeoStack already cover?

```lua
local m = epic_toolsets_matrix({ write_evidence = true })
log(string.format("full %d, partial %d, none %d, unmapped %d",
  m.summary.full, m.summary.partial, m.summary.none, m.summary.unmapped))
for _, t in ipairs(m.toolsets) do
  log(t.name .. " -> " .. t.coverage .. " (" .. t.mapped_count .. "/" .. t.tool_count .. ")")
end
log(m.evidence_markdown)                 -- Saved/NeoStackSupportEvidence/V4/epic_toolsets/matrix.md
```

`tools[*].neostack_equivalent` names the NeoStack verb to use instead of the
Epic tool; `suggested_allow` lists read-only patterns worth adding to
`UEToolsetAllowedCalls` (Epic's `CreateSkill`/`UpdateSkill` stay blocked;
use `epic_skills()` for writes).

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `epic_skills` is nil | engine < 5.8 or ToolsetRegistry plugin off; check `epic_toolsets_support()` |
| `add` returns `write_through_disabled` | flip `epic_toolsets_settings({allow_skill_write_through=true})` or Editor Preferences > Plugins > NeoStack Epic Toolsets |
| `configure` returns `not_editable` | native/Python skill; edit its source instead |
| `remove` returns `transaction_active` | another editor transaction is open (only possible outside the NeoStack Lua runner); retry after the current editor action completes |
| `sandbox_persist()` returns `no_changes` | the sandbox is clean; nothing to persist |
| `sandbox_enter` returns `another_sandbox_active` | leave it (`sandbox_leave()`) or pass `{leave_active=true}` |
| `sandbox_status().available` false with FileSandbox reason | FileSandbox plugin not loaded; it is pulled in by ToolsetRegistry on 5.8 |
| a Config/Source write is missing from `sandbox_changes()` | expected: only content mount points are sandboxed |
| matrix `available=false` | ToolsetRegistry reports unavailable; rows carry declared grades only (`live_error`) |

Escape hatches: `help()`, `epic_skills():help()`, `report_issue()`.
