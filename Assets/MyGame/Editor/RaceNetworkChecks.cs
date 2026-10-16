using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>별도 Client가 연결된 시험 Host에서 실행한다. Play 종료로 시험 상태를 버린다.</summary>
public static class RaceNetworkChecks
{
    private static readonly List<string> passed = new List<string>();
    private static uint purchasedId;
    private static int purchasedType;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("RACE_CHECK_FAILED: " + name);
        passed.Add(name);
    }

    private static PlayerState[] Players()
    {
        var race = RaceSession.Instance;
        if (!NetworkServer.active || race == null || !race.IsRacing || race.Players.Count < 2)
            throw new InvalidOperationException("Run in a racing Host with at least two connected players.");
        return race.Players.OrderBy(p => p.netId).ToArray();
    }

    public static string[] PurchaseChecks()
    {
        passed.Clear();
        var players = Players();
        var race = RaceSession.Instance;
        var conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorController>();
        var a = players[0].GetComponent<NetworkRaceInventory>();
        var b = players[1].GetComponent<NetworkRaceInventory>();
        var costA = players[0].GetComponent<PlayerCost>();
        var costB = players[1].GetComponent<PlayerCost>();
        Check(a.syncMode == SyncMode.Owner && b.syncMode == SyncMode.Owner, "owner-only inventory serialization");
        Check(!UnityEngine.Object.FindFirstObjectByType<GoalZone>().ServerTryContact(players[0].connectionToClient),
            "reject goal notification away from goal");
        var item = conveyor.Items.First();
        purchasedId = item.Id;
        purchasedType = item.ItemType;
        int beforeA = costA.CurrentCost, beforeB = costB.CurrentCost;
        Check(conveyor.ServerBuy(item.Id, a, race.Round - 1, true) == RaceRequestResult.StaleRound, "reject stale purchase");
        Check(costA.CurrentCost == beforeA, "rejected request keeps cost");
        Check(conveyor.ServerBuy(item.Id, a, race.Round, true) == RaceRequestResult.Success, "purchase and equip");
        Check(costA.CurrentCost == beforeA - item.Price && a.Slots[0] == item.ItemType && players[0].InventoryCount == 1,
            "atomic debit and inventory insert");
        Check(conveyor.ServerBuy(item.Id, b, race.Round, false) == RaceRequestResult.Unavailable && costB.CurrentCost == beforeB,
            "only first buyer wins shared item");
        Check(a.ValidatePurchase(race.Round, out _) == RaceRequestResult.HoldingItem, "holding prevents another purchase");
        Check(a.ServerSelect(race.Round, 999) == RaceRequestResult.InvalidSlot, "reject invalid slot");
        Check(a.ServerUse(race.Round - 1, Vector2.zero) == RaceRequestResult.StaleRound, "reject stale use");
        Check(a.ServerUse(race.Round, new Vector2(float.NaN, 0)) == RaceRequestResult.InvalidPosition,
            "reject NaN world coordinate");
        Check(a.ServerUse(race.Round, Vector2.one * float.MaxValue) == RaceRequestResult.InvalidPosition,
            "reject outside-field coordinate");
        Check(a.Slots[0] == item.ItemType && a.HeldSlot == 0 && costA.CurrentCost == beforeA - item.Price,
            "invalid uses preserve reserved item and cost");
        return passed.ToArray();
    }

    public static string[] UseAndHitChecks()
    {
        passed.Clear();
        var players = Players();
        var race = RaceSession.Instance;
        var inventory = players[0].GetComponent<NetworkRaceInventory>();
        var bounds = ((BoxCollider2D)new SerializedObject(race).FindProperty("fieldBounds").objectReferenceValue).bounds;
        Vector2 position = bounds.center;
        Check(race.ValidatePosition(position) == RaceRequestResult.Success, "valid field target");
        Check(inventory.ServerUse(race.Round, position) == RaceRequestResult.Success, "server spawns selected trap");
        Check(inventory.Slots[0] == 0 && inventory.HeldSlot == -1 && players[0].InventoryCount == 0,
            "successful use consumes exactly one item");
        var trap = race.Traps.Traps.Values.First();
        Check(trap.ownerNetId == players[0].netId && trap.itemId == purchasedType && trap.round == race.Round,
            "trap retains owner type and round");
        int score = players[0].GetComponent<PlayerScore>().Score;
        Check(race.Traps.ServerTryHit(trap.id, players[0]), "host accepts self hit");
        Check(players[0].GetComponent<PlayerScore>().Score == score, "self hit awards no points");
        Check(race.Traps.ServerTryHit(trap.id, players[1]), "one trap can hit another victim");
        Check(players[0].GetComponent<PlayerScore>().Score == score + race.Rules.hitScore,
            "hit credit goes only to trap owner");
        Check(!race.Traps.ServerTryHit(trap.id, players[1]), "duplicate victim cannot score again");
        Check(players[1].IsInvincible, "respawn protection is synchronized");
        var definition = race.Rules.FindItem(purchasedType);
        Check(race.Traps.ServerTrySpawn(definition, players[0].netIdentity, position), "second server trap");
        var other = race.Traps.Traps.Keys.Max();
        Check(!race.Traps.ServerTryHit(other, players[1]), "protection rejects another trap hit");
        Check(race.ValidatePosition(players[1].transform.position) == RaceRequestResult.ProtectedArea,
            "reject placement inside respawn protection");
        return passed.ToArray();
    }

    public static string[] RoundChecks()
    {
        passed.Clear();
        var players = Players();
        var race = RaceSession.Instance;
        int a = players[0].GetComponent<PlayerScore>().Score;
        int b = players[1].GetComponent<PlayerScore>().Score;
        Check(race.TryFinish(players[0]), "first finish accepted");
        Check(players[0].GetComponent<PlayerScore>().Score == a + race.Rules.finishScores[0], "first-place score");
        Check(race.Phase == RacePhase.FinishCountdown && race.PhaseSecondsLeft > 0, "first finish starts shared deadline");
        Check(!race.TryFinish(players[0]), "duplicate finish rejected");
        Check(race.ValidateAction(players[0], race.Round) == RaceRequestResult.Success,
            "finished player can still use items");
        Check(race.TryFinish(players[1]), "second finish accepted");
        Check(players[1].GetComponent<PlayerScore>().Score == b + race.Rules.finishScores[1], "second-place score");
        Check(race.Phase == RacePhase.RoundEnd, "all finishers end round");
        Check(race.Traps.Traps.Count == 0 && UnityEngine.Object.FindFirstObjectByType<ConveyorController>().Items.Count == 0,
            "round end clears shared traps and conveyor");
        Check(race.ValidateAction(players[0], race.Round) == RaceRequestResult.NotRacing, "round end rejects actions");
        return passed.ToArray();
    }
}
