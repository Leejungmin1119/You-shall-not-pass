# Race networking

> This is a networking foundation and existing-spear integration, not completion of every networking requirement in the supplied design PDFs. See [네트워크 구현 현황과 남은 작업](../../docs/NETWORK_IMPLEMENTATION_STATUS.md) for the Korean completion matrix, remaining work, provisional decisions and validation limits.

This branch integrates the simultaneous-race rules with the existing Mirror/KCP LAN room flow. `LobbyScene` still starts `Assets/Scenes/SampleScene.unity`. The existing one-player test minimum and four-player maximum are preserved. Internet relay deployment is separate; the relay URL is still unset.

## Replicated state and authority

- `RaceSession`: server-only phase transitions, absolute synchronized match/phase deadlines, round number, finish order, score-limit/time-limit results and tied winner IDs. Clients derive remaining seconds from `NetworkTime.time`; no per-frame countdown messages.
- `PlayerState`: alive/finished state, respawn protection deadline and public inventory count. All racers retain owner movement and `ClientToServer` NetworkTransform interpolation. The legacy single-attacker manager is bypassed only when a `RaceSession` is present.
- `PlayerScore`: authoritative race score. The existing nameplate reads this score during a race. Practice score/HP/free-bomb shortcuts are rejected during races, including on the server.
- `ConveyorController`: one authoritative typed item list, weighted selection, spawn timestamps, expiration and atomic first-buyer-wins transactions. Initial four items are spaced along the conveyor, then one new item is emitted per interval. Subsequent rounds clear the list without resetting cost/inventory or repeating the initial batch.
- `NetworkRaceInventory`: `SyncMode.Owner` for contents and selected/reserved slot. Remote peers receive only the count on `PlayerState`; the authoritative Host necessarily knows all inventories. Left-click buys into a slot; right-click reserves a free slot and equips; 1/2/3 select/cancel; the existing field click submits use. UI can subscribe to `RequestCompleted` for typed failure feedback.
- `RaceTrapService`: active trap snapshots contain a unique ID, owner net ID, item type, round, world position, spawn time and expiry. Spawn/remove snapshots reconstruct views, including initial full-state deserialization. Moving trap adapters call `ServerMove`; local `TrapUpdated` can drive interpolation. Adapters call `ServerTryHit` only after server-side collision validation. Clients have no hit/score RPC.

Purchase/use Commands require player ownership. The server rejects stale rounds, inactive/dead participants, expired phases, invalid/empty slots, holding another item, insufficient cost, full inventory, missing/expired conveyor items, cooldown violations, non-finite/out-of-field positions and respawn protection areas. A failed use keeps the reserved item. A successful use consumes exactly one item after creating its server view. Duplicate hit victims and invulnerability prevent repeated scoring; self hits award no score. Goal/fall client notifications are checked against Host-side collider bounds. The Host also polls those bounds so notifications arriving before movement snapshots cannot lose a contact permanently.

## Gameplay integration boundary

The existing spear is the supplied working adapter (single/double/triple spear). Its host judges hits, including multiple different victims; clients reconstruct the view. `HitReceived(trapId, ownerNetId, victimNetId)` is the presentation hook. `IRaceTrapAdapter.Supports/Create` is the extension point for bomb/missile gameplay code; register adapters on the session object, then enable their entries in the rules asset. Unsupported items are excluded from conveyor selection and rejected without consumption. New bomb/missile behavior, artwork, inventory HUD, results screen, map difficulty progression and hit animations are not implemented here.

Respawn uses the existing SafeZone search and start-point fallback, rather than inventing new pit geometry. Pit-specific fixed respawn locations and final back distances still need the gameplay/level-design implementation. Client-authoritative movement remains trusted as specified; this change does not add server movement anti-cheat or lag-compensated rewind.

`RaceNetworkRules.asset` contains adjustable proposal defaults: 900-second match, target 20, rank points 4/3/2/1, closing deadline 20 seconds, ready countdown total 7 seconds, starting cost 100, +10 per 5 seconds, cap 300, three slots, 0.5-second use cooldown/protection. The unresolved purchase cooldown (0.25 seconds), protected radius (1.5 units), hit back distance (10 units), and 0.25-second RoundEnd publication interval are explicit test defaults, not agreed design values.

Ambiguous document behavior is resolved as follows: reaching 20 ends the match immediately (including hit points); finish points are applied at arrival. Non-host departure aborts the current match with `PlayerDisconnected` and no winner, because the supplied specification leaves the policy undecided. The session remains in MatchEnd for the existing Leave action; host migration/reconnection is not added.

## Reproducing validation

1. Compile the project. For a fresh copy without the serialized wiring, `Tools > Network Practice > Configure Race Networking` adds the session/service to the existing scene NetworkIdentity and the owner inventory to the existing player prefab. The committed scene/prefab already contain them.
2. Build LobbyScene and SampleScene as a macOS Development build. Start Editor Play in LobbyScene and create one LAN Host.
3. Run the development player with `-raceSmokeReport /absolute/path/client-report.json`. This opt-in probe connects to localhost, readies its room player and writes observed phase/deadline/trap/score/cost/inventory snapshots. Add `-raceSmokeActions` to also execute a real remote buy/equip/use Command round trip and record its TargetRpc results and maximum observed trap count. It is excluded from release builds.
4. Ready the Host and launch. Once Racing with at least two players, run `RaceNetworkChecks.PurchaseChecks()` through the Editor eval command. Inspect the separate Client report: the Host's inventory count is 1 but its `visibleSlots` is empty, and only the local player's slots are serialized.
5. Run `UseAndHitChecks()` and `RoundChecks()` in order. They assert atomic debit, first-buyer wins, stale request and coordinate rejection, retained items on failure, owner-only contents, host spawn ownership, self-hit/no score, multi-victim credit, duplicate-hit/protection rejection, finish order, duplicate-goal rejection, closing deadline, spectator item permissions, round cleanup and phase gating.
6. Stop Client and Host and leave Editor Play. Tests modify only runtime state. Do not save runtime test state into scene/rules assets.

The probe and checks are development instrumentation; normal builds do not auto-connect or run assertions. Multi-machine WAN conditions and hostile movement require separate testing.

## Verified on 2026-10-16

- Unity 6000.3.24f1 compilation and macOS Development build succeeded. Existing package shader warnings, missing optional Runtime Pipeline config and Unity Services symbol-upload warnings can appear; no new game-code compiler error remained.
- Editor Host plus one separate Client: all 36 assertions across `PurchaseChecks`, `UseAndHitChecks` and `RoundChecks` passed. A Client JSON snapshot confirmed one remote item count with zero remote slot contents, and its own three slots.
- Editor Host plus three separate macOS Clients: four players entered the race; an actual remote buy/equip/use Command sequence received two Success TargetRpc results and replicated a trap. Every Client saw only its own slot contents. All four finish ranks awarded 4/3/2/1 and cleared traps at round end.
- Additional runtime checks rejected a full inventory, insufficient balance and a second purchase during cooldown. A score of 20 replicated MatchEnd/ScoreLimit and the winner ID.
- A controlled elapsed-deadline fixture with equal scores replicated TimeLimit and all four winner IDs. A separate racing fixture followed by terminating one Client process triggered the real KCP disconnect timeout and replicated PlayerDisconnected with no winner to the surviving Clients.
- The final four-player run had zero game exceptions in the three Client logs and zero current Editor console errors. Test Clients were stopped and Editor returned to LobbyScene outside Play mode.
