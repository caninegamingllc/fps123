---
name: neostack-hello
description: Check whether this app can reach NeoStack in the intended Unreal project and run a read-only Lua call. Use when the user asks to test the connection or confirm setup works.
---

Use the tools exposed by the current app; their names may include an MCP server prefix.

If `unreal_status` is available, call it first. A verified `connectionReceipt` identifies the actual editor instance, project path and plugin version. Compare the project to the user's intended project before doing any further work. If the receipt is absent, report the observed connection problem; a configured server or reachable HTTP port alone does not prove the correct editor is connected. Use `list_unreal_projects` and `select_unreal_project` when available to resolve a known project mismatch, asking which project only when the intended one is unclear.

Then call `execute_script` with this read-only Lua script:

```lua
return {
  lua_version = _VERSION,
  help_available = type(help) == "function",
  asset_read_available = type(open_asset) == "function"
}
```

Report success only after the tool returns successfully with both availability fields true. Say which project was verified, or say project identity was not established if this app connects directly without `unreal_status`. This call proves the current app can execute Lua; it does not prove unrelated providers or accounts work.

If no NeoStack tools are exposed, explain that this app still needs to load or approve its NeoStack connection. Use its ordinary connection/trust controls; do not substitute a terminal command or a separate bridge process and call that app verification. Do not create assets or change project settings for this check.
