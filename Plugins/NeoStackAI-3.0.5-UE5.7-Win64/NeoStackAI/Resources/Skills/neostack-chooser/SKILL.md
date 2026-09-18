---
name: neostack-chooser
description: Author, bind, evaluate and verify Unreal Engine Chooser Tables through `execute_script`, including Pose Match (motion matching) columns that need chooser-owned Pose Search databases. Use for chooser contexts and property bindings, filter/output columns, rows and result assets, proxy tables, debug-row verification, and Pose Search columns.
---

# Chooser Tables through `execute_script`

Start with `help("ChooserTable")` and `help("PoseSearch")`; help prints its
own result. Lua state is fresh on every call, so re-open assets by path and
keep column/row indices you got back from earlier calls (both are 1-based).

## Work in this order

1. **Contexts first.** Columns bind to contexts by index, so add every
   context before any column: `ct:add_context({ kind = "class", class =
   "/Script/Engine.Actor", direction = "ReadWrite" })` or `{ kind = "struct",
   struct = "/Script/CoreUObject.Vector", direction = "Read" }`. Direction is
   enforced: filter/cost columns need Read or ReadWrite, output columns need
   Write or ReadWrite.
2. **Rows and results.** `ct:add_rows(n)`, then `ct:set_row_result(row, {
   kind = "asset", asset_path = "/Game/..." })` (also `soft_asset`,
   `evaluate_chooser` with `chooser_path`). Setting a result auto-populates
   columns that follow the result (Pose Match) exactly like the editor.
3. **Columns.** `ct:add_column(type, { binding = { context = 1, property =
   "bHidden" } })`. Built-in types: Bool, Enum, GameplayTag, GameplayTagQuery,
   FloatRange, FloatDistance, Randomize, MultiEnum, Object, ObjectClass,
   OutputObject, OutputFloat, OutputStruct, OutputBool, OutputEnum,
   OutputGameplayTagQuery. Any other `FChooserColumnBase` struct from an
   enabled plugin is accepted by name (`"PoseSearch"`). Rebind later with
   `ct:bind_column_input(col, binding)`; `root = true` binds the whole struct
   context and is only accepted when the context struct matches the column's
   expected type.
4. **Cells and settings.** `ct:set_cell(row, col, {...})` for filter values,
   `ct:set_output_cell(row, col, {...})` for outputs, `ct:configure_column(col,
   { PropertyName = value })` for reflected column settings (see
   `list("columns")[col].settings` for names).
5. **Compile and verify.** `ct:compile()`, then read back with
   `ct:list("columns")` (type, is_bound, context_index, settings) and
   `ct:list("rows")`. Evaluate with `chooser_evaluate(path, context_object)` or
   `chooser_make_context()` for multi-object contexts, and confirm the row
   with `chooser_set_debug_target` + `chooser_get_debug_row`.
6. **Persist.** `ct:save()`, then `open_asset(path)` and list again in a fresh
   call before claiming success.

## Pose Match columns (motion matching, UE 5.7+, PoseSearch plugin)

The Pose Match column filters rows by motion-matching cost and writes the best
start time. It needs: a `PoseHistoryReference` struct context for its input,
a chooser-owned Pose Search database per row, and Write contexts for outputs.

```lua
local path = "/Game/Anim/CH_Locomotion"
local ct = open_asset(path)
-- contexts: 1 = pose history (read), 2 = writable outputs
assert(ct:add_context({ kind = "struct", struct = "/Script/PoseSearch.PoseHistoryReference", direction = "Read" }) == 1)
assert(ct:add_context({ kind = "class", class = "/Script/Engine.Actor", direction = "ReadWrite" }) == 2)
assert(ct:add_rows(1))
assert(ct:set_row_result(1, { kind = "asset", asset_path = "/Game/Anim/AS_Run" }))
local col = assert(ct:add_column("PoseSearch", { binding = { context = 1, root = true } }))
assert(ct:configure_column(col, { MaxNumberOfResults = 1, PoseReselectHistory = 0.3 }))
-- one chooser-owned database per row (or reuse by name); the row's result animation is mapped in
local db = assert(pose_search_column_set_database(path, 1, col, { schema = "/Game/Anim/PSS_Locomotion", name = "LocomotionDb" }))
assert(db.database.num_animation_assets == 1)
assert(pose_search_column_bind_output(path, col, "start_time", { context = 2, property = "CustomTimeDilation" }))
assert(ct:compile())
local info = pose_search_column_info(path, col)
print(info.rows[1].database, info.rows[1].anim_asset, info.outputs.start_time.is_bound)
```

Rules the engine enforces and the binding mirrors:

- Databases must be owned by the root chooser; external database assets are
  refused. Reuse with `{ database = "LocomotionDb" }`, clear with
  `{ database = false }`. New databases use brute-force search because the
  KD-tree path fails inside chooser columns. On UE 5.7 the column owns a
  single internal database, so `set_database` only assigns its schema
  (`info.per_row_databases` is false there).
- Outputs: `start_time` and `cost` are floats, `mirror` and `force_blend_to`
  are bools, `interrupt_mode` is an enum input. Type mismatches and Read-only
  contexts are refused.
- Place the Pose Match column last (furthest right) so other filters run first.
- `pose_search_column_info(...).nested_databases[i].num_animation_assets` is
  the proof that rows are mapped; a database with 0 assets means the row has
  no result animation yet.

## Failure modes

- `add_column ... root binding requires a struct context parameter`: the
  bound context is a class; Pose Match needs a `PoseHistoryReference` struct.
- `parameter writes to context N, but that context direction is Read`: use a
  Write/ReadWrite context for outputs.
- `configure_column ... no properties were set`: the name is misspelled or is
  an instanced parameter (bind those instead). Nothing was mutated.
- Row/column indices are 1-based; `list("columns")` order is the authoritative
  index after inserts with `{ index = n }`.
- Evaluation returning nil with a Pose Match column is expected without a live
  pose history; verify structure and persistence instead of evaluation.
