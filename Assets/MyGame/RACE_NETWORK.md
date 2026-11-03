# Race networking after main merge

Current integration notes: [main 병합 기록 - 2026-11-03](../../docs/MERGE_MAIN_2026-11-03.md).
The [2026-10-16 status document](../../docs/NETWORK_IMPLEMENTATION_STATUS.md) is a historical snapshot, not the current component map or validation result.

The active stack now follows main: `RaceManager`, `PlayerState`, `PlayerScore`, `PlayerInventory`, `PlayerHeldItem`, `ConveyorController`, and `SpearNetwork`/`ItemBase`. The main SampleScene, player prefab, inventory/HUD, animations, terrain, item assets and player teleport fix are retained. Old parallel RaceSession/inventory/trap-service components and their settings were removed to prevent competing state machines.

The previous branch's ownership checks, private slot contents with public counts, stale-round rejection, purchase cooldown, server goal/fall contact validation and respawn protection were ported to this stack. The match deadline persists across rounds, closing time is capped by the match deadline, and end reason/tied winner IDs are sent before main's lobby return. Finished racers can request items without moving; practice damage/free-bomb commands remain blocked during races.

The main map-preview/countdown and per-round cost/inventory reset behavior is preserved. Purchase cooldown (0.25 seconds), protected placement radius (1.5 units), and result replication delay (0.5 seconds) are integration defaults, not newly agreed design values. Movement remains client-authoritative; host migration, automatic reconnection, dynamic map progression and public relay deployment are not added.

## Validation

- `RaceNetworkSetup.Validate()` checks current main scene/prefab wiring and does not add a second manager. The old Configure menu now delegates to validation.
- `RaceNetworkChecks.Run()` runs server rejection/finish/deadline checks in a separate racing Host with two or more players. It modifies runtime state; stop Play afterwards.
- Development builds may opt in to `-raceSmokeReport /absolute/report.json` and `-raceSmokeActions` to observe private inventories and exercise real buy/use requests against localhost. It is excluded from release builds.

The merge was checked with Unity's standalone C# compiler and static script/scene references. Unity's batch validation was blocked by an unavailable Editor license. The October 16 Host/Client results apply only to the previous implementation; a new Play/Host/Client regression run is still required after license restoration.
