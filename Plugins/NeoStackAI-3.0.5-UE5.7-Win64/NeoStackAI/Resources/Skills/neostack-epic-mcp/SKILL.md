---
name: neostack-epic-mcp
description: Connect NeoStack with Epic's in-editor MCP server (UE 5.8 experimental ModelContextProtocol plugin). Use when the user asks whether Epic's MCP server is running, wants NeoStack tools or skills reachable from Epic's MCP clients or assistant, wants to call an Epic MCP tool or MCP Tool Library function from NeoStack, or hits a port clash between the two servers.
tags: [mcp, epic, interop, experimental]
---

# Epic MCP interop

Everything runs through one Lua global inside `execute_script`:
`epic_mcp(action, opts?)`. The same actions exist as the `epic_mcp` MCP tool
(`{action, ...}`) for clients that cannot run Lua.

Availability: UE 5.8 with the experimental `ModelContextProtocol` plugin
enabled (it pulls `ToolsetRegistry`). Without it `type(epic_mcp) ~= "function"`
and the MCP tool answers `{available=false, reason=...}`.

## 1. Check coexistence first

```lua
local s = epic_mcp("status")
log(string.format("epic available=%s running=%s port=%s neostack=%s conflict=%s",
  tostring(s.available), tostring(s.server_running), tostring(s.port),
  tostring(s.neostack_port), tostring(s.port_conflict)))
if not s.available then return s.reason end
return s
```

Read `port_conflict`: both servers parse `-ModelContextProtocolPort=`, so a
launch line meant for NeoStack also re-points Epic's server. Epic's default is
8000 on `/mcp`, NeoStack's is 9315. The clash only bites once Epic's server is
started (`bAutoStartServer` or `ModelContextProtocol.StartServer <port>`).

## 2. Discover and call Epic's tools

```lua
local list = epic_mcp("list")
for _, t in ipairs(list.tools) do
  log(t.name .. "  [" .. t.source .. "]  allowed=" .. tostring(t.execution_allowed))
end
local d = epic_mcp("describe", { tool_name = "list_toolsets" })
log(json_encode and json_encode(d.input_schema) or "schema fetched")
local r = epic_mcp("call", { tool_name = "list_toolsets", arguments = {} })
return r.ok and r.text or r.error
```

- `source` is `epic_meta` (list_toolsets / describe_toolset / call_tool),
  `tool_library` (a Blueprint "MCP Tool Library" function), `neostack`
  (something NeoStack published) or `other`.
- Discovery is never gated. Execution is allow-listed in Editor Preferences >
  Plugins > NeoStack AI - Epic MCP; defaults allow only `list_toolsets` and
  `describe_toolset`. `call_tool` is always refused: it dispatches to any
  top-level tool and would let NeoStack loop into itself. Use
  `ue_tool("call", ...)` for toolset execution.
- To call a studio-authored MCP Tool Library function, add its function name
  to "Allowed Epic MCP Calls" first; the refusal text tells you which pattern
  was missing.

## 3. Publish NeoStack into Epic's server (deliberately)

```lua
local p = epic_mcp("publish", { tools = { "execute_script", "list_skills", "get_skill" }, skill_resources = true })
log("published: " .. table.concat(p.published, ", "))
for _, rej in ipairs(p.rejected) do log("rejected " .. rej.name .. ": " .. rej.reason) end
-- Epic's clients now see neostack_execute_script, neostack_list_skills, neostack_get_skill
-- and the resources neostack://skills/<name>.
-- ... later, when done:
return epic_mcp("unpublish")
```

This is session-only. The persistent switch is `Publish NeoStack Tools To Epic
MCP` in the settings page, off by default because Epic's server has no bearer
token or confirmation prompt: a published `neostack_execute_script` is editor
Lua for any local process that can open Epic's port. Say this to the user
before turning it on.

`tools` is explicit when present: `{ tools = {}, skill_resources = true }`
registers the skill resources and publishes no tool. Leave `tools` out only
when you mean "the configured Published Tools list" (which includes
`execute_script` by default). A wrongly typed option (`skill_resources =
"true"`, `tools = 7`) refuses the whole call with `error` naming the key.

## 4. Read skills as MCP resources

```lua
local res = epic_mcp("resources")
for _, r in ipairs(res.resources) do log(r.uri .. "  " .. tostring(r.mime_type)) end
local body = epic_mcp("read_resource", { uri = "neostack://skills/neostack-hello", max_bytes = 4096 })
return body.ok and body.text or body.error
```

Only listed uris resolve; anything path-like in the name is refused.

## Failure modes

| Symptom | Cause / fix |
| --- | --- |
| `epic_mcp` is nil | Engine < 5.8 or ModelContextProtocol plugin disabled; enable it in the .uproject and restart |
| `status.available=false`, reason mentions the module | Plugin enabled but module not loaded yet; retry after editor startup finishes |
| `call` returns `policy_reason` | Add the tool name to Allowed Epic MCP Calls (never `call_tool`) |
| `call` error mentions "recursive" | Another Epic MCP or ToolsetRegistry call is in flight; wait for it |
| `call` timed out | Raise `timeout_seconds` (max 300); `CancelAsync` was requested but the tool may still finish |
| `publish` rejects with "already serves a tool" | Epic already has that name (case-insensitive); unpublish or rename on Epic's side |
| `list` shows no `list_toolsets` | `bEnableToolSearch=false` (eager mode) or Epic's editor integration not set up yet |
| `port_conflict=true` | Both servers resolved the same port; change one of `-ModelContextProtocolPort=`, NeoStack's Server Port or Epic's Server Port Number |

Escape hatches: `help("EpicMCP")`, `epic_mcp("help")`, `report_issue()`.
