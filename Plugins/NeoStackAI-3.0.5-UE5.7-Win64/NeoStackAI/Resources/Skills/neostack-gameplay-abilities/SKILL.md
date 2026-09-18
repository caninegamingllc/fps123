---
name: neostack-gameplay-abilities
description: Author, inspect, and verify Unreal Engine Gameplay Ability System assets through NeoStack `execute_script`. Use for Gameplay Abilities, Gameplay Effects, costs, cooldowns, attributes, effect components, tags, cues, stacking, authority, prediction, cancellation, and replicated GAS behavior in PIE.
---

# NeoStack Gameplay Abilities

Build GAS assets with the native GameplayAbility and GameplayEffect enrichments,
then prove their behavior in the exact server and client PIE worlds.

Start every unfamiliar workflow with live discovery:

```lua
print(help("GameplayAbility"))
print(help("GameplayEffect"))
print(help("Playtest"))
```

Lua handles do not survive between calls. Reopen assets in every mutation or
verification script. Register project gameplay tags before configuring assets;
invalid tags fail instead of being silently discarded.

## Author effects before abilities

Create cost, cooldown, and outcome effects first so the ability can reference
their generated classes:

```lua
local effect = assert(create_asset("/Game/GAS/GE_Heal", "GameplayEffect"))
assert(effect:configure("duration", {
  policy="HasDuration",
  magnitude=3.1,
  period=1.0,
  execute_on_application=true,
  periodic_inhibition_policy="NeverReset",
}))
assert(effect:add("modifier", {
  attribute="MyAttributeSet.Health",
  op="Add",
  value=10.0,
}))
assert(effect:configure("stacking", {
  type="AggregateByTarget",
  limit=1,
  duration_refresh="RefreshOnSuccessfulApplication",
  period_reset="ResetOnSuccessfulApplication",
  expiration="ClearEntireStack",
}))
```

Add and configure components deliberately. Application requirements, ongoing
requirements, granted owner state, and effect identity are different contracts:

```lua
assert(effect:add("component", {type="TargetTagRequirements"}))
assert(effect:configure("component", {
  type="TargetTagRequirements",
  application_require={"Team.Ally"},
  ongoing_require={"Team.Ally"},
}))

assert(effect:add("component", {type="TargetTags"}))
assert(effect:configure("component", {
  type="TargetTags",
  grant_tags={"Effect.Heal.Active"},
}))

assert(effect:add("component", {type="AssetTags"}))
assert(effect:configure("component", {
  type="AssetTags",
  tags={"Effect.Heal.Active"},
}))
```

Do not conflate the last two components. In UE 5.8,
`GetActiveEffectsWithAllTags` constructs an effect-tag query and matches tags
from `FGameplayEffectSpec::GetAllAssetTags`; it does not match granted target
tags. Use `AssetTags` when querying active-effect handles by identity. Use the
ASC owned-tag APIs when asserting state granted to the target. Configure both
only when the workflow genuinely needs both contracts.

Save, reopen, and verify `info()`, `list("modifiers")`,
`list("components")`, and component-specific readback. A component name alone
does not prove its tag contents.

## Configure the ability contract

Choose policy from gameplay authority requirements, not from the current test:

```lua
local ability = assert(create_asset("/Game/GAS/GA_Heal", "GameplayAbility"))
assert(ability:configure("policy", {
  instancing="InstancedPerActor",
  net_execution="ServerOnly",
  replication="ReplicateYes",
  net_security="ServerOnly",
  retrigger_instanced_ability=false,
  replicate_input_directly=false,
  server_respects_remote_ability_cancellation=true,
}))
assert(ability:configure("cooldown", {
  effect="/Game/GAS/GE_HealCooldown.GE_HealCooldown_C",
}))
assert(ability:configure("tags", {
  ability_tags={"Ability.Support.Heal"},
  activation_blocked={"Cooldown.Support.Heal", "Status.Interrupted"},
}))
```

Use `ServerOnly` for authoritative server execution. Use `LocalPredicted` only
when the design includes prediction-key behavior and the runtime test verifies
prediction and reconciliation. Policy readback is necessary but cannot prove
runtime authority.

Build the Gameplay Ability Graph with Blueprint graph operations. Always:

1. Connect `Event ActivateAbility` to `CommitAbility`.
2. Branch on `CommitAbility.Return Value`.
3. End the failed and successful paths explicitly.
4. Set every class pin, including actor enumeration and Gameplay Effect class
   pins; a connected execution chain does not imply a valid class default.
5. Compile with zero errors, save, reopen, and reconstruct the final graph.

Keep filtering data-driven: source/target identity, team requirements, range,
attributes, tags, and immunity belong in general graph or effect semantics, not
in label-specific branches.

## Build a runtime evidence harness

Expose only the minimal replicated state needed for an objective test, such as
health, activation, active-effect count, cooldown state, and cancellation cause.
Initialize the ASC and attribute values on authority. Do not treat editor asset
readback as evidence that an effect applied at runtime.

Run multiplayer in one process and target exact PIE instances:

```lua
assert(playtest_start({
  mode="pie",
  map="/Game/Maps/L_GAS_Test",
  clients=2,
  network_mode="listen_server",
  run_under_one_process=true,
}))
local ready = assert(playtest_wait_for_pie({timeout=20, interval=0.05}))
assert(ready.passed)
```

Discover server and client IDs from `playtest_status().instances`; never assume
array order. At each meaningful phase, read the same actor on both peers and
require matching replicated values.

Use a scenario matrix:

1. Normal activation: cost/cooldown commit once, valid targets receive the
   intended period or instant magnitude, and the final value is exact.
2. Exclusions: self, enemy, full-health, immune, dead, or out-of-range targets
   remain unchanged according to the requested contract.
3. Cooldown: immediate reactivation fails and later reactivation succeeds.
4. Ongoing requirements: removing an ongoing tag or leaving range produces the
   intended removal or inhibition behavior.
5. Cancellation/interruption: effect lifetime, cooldown, and end state match
   the explicit design on server and client.

For periodic effects, sample before application, after the first execution,
during the active duration, and after expiry. Require exact attribute deltas,
active-effect counts, owned tags, and cooldown identity. One final health value
cannot distinguish a correct periodic effect from a single accidental change.

Stop PIE on every success and failure path. Wait until the session is fully
stopped before another run.

## Cold verification

After authoring and runtime variants:

1. Compile every Blueprint with zero errors and warnings.
2. Save and reopen every ability, effect, actor, and map from fresh handles.
3. Run Map Check and require zero errors and warnings.
4. Restart the editor, repeat one baseline multiplayer activation, and compare
   server/client state.
5. Scan the scoped editor log for fatal, assertion, ensure, exception, and GPU
   crash signatures.

If a crash occurs, preserve the exact action sequence and first fatal stack.
Trace the installed engine source before changing the plugin. Fix the lifecycle
or semantic contract generally; do not special-case one asset path, tag, or
prompt.
