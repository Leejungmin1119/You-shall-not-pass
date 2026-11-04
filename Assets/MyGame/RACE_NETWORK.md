# Current game networking

Current integration: [2026-11-04 main update](../../docs/MERGE_MAIN_2026-11-04.md).
The [2026-11-03 integration](../../docs/MERGE_MAIN_2026-11-03.md) and [October status](../../docs/NETWORK_IMPLEMENTATION_STATUS.md) describe earlier gameplay rules and validation.

The current stack uses Mirror Host, `CapstoneRoomManager`, `RaceManager`, `rolechangeManager`, `PlayerState`, `PlayerInventory`, `PlayerHeldItem`, `ConveyorController`, and `SpearNetwork`/`ItemBase`.

Latest main has one attacker who moves and defenders who buy/use traps. Goal completion awards one point and advances the attacker; attacker death also advances the turn. Turn changes clear carried and field items and reset role-dependent cost without extending the match deadline. The developer solo-test mode permits the single attacker to use traps and disables external listening.

Server request validation uses the current trap permission (`CanUseTraps`) and turn number. Purchase cooldown, owner-only inventory/held contents, public inventory count, position checks for goal/fall and replicated invincibility remain integrated. Continuous item state uses Unreliable, while explosion/expiry events use Reliable. Player movement remains client-authoritative.

## Validation

- `RaceNetworkSetup.Validate()` checks scene/prefab wiring inside Unity.
- `RaceNetworkChecks.Run()` checks roles, invalid/stale requests, one-point goal scoring, turn advance, inventory reset and unchanged deadline in a fresh racing Host with at least two players and zero scores. It changes runtime state; stop Play afterwards.
- Development builds can opt in to `-raceSmokeReport /absolute/report.json` and `-raceSmokeActions`. The action probe waits until the local player can request a purchase.

On November 4, standalone C# compilation passed for 64 runtime sources, 3 Editor sources, and the 64-source Player configuration. Static checks covered four enabled build scenes, 12 scenes/prefabs and 172 script references with no missing script GUIDs or duplicate GUIDs. These are not runtime gameplay results.

Unity batch validation stopped before project validation with `No valid Unity Editor license found` (Editor exit 198). New Host/Client regression checks remain pending. October play-test results do not apply to this merge.
