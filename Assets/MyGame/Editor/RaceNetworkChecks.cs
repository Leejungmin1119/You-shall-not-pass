using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>공수 교대와 서버의 방어 조건을 검사한다. 별도 시험 Play 세션에서만 실행한다.</summary>
public static class RaceNetworkChecks
{
    public static string[] Run()
    {
        var race = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
        if (!NetworkServer.active || race == null || !race.IsRunning || race.Participants.Count < 2 ||
            race.Participants.Any(p => p.GetComponent<PlayerScore>().Score != 0))
            throw new InvalidOperationException("Run in a fresh racing Host with two or more players and zero scores.");
        var passed = new List<string>();
        Action<bool, string> check = (ok, name) =>
        {
            if (!ok) throw new InvalidOperationException("MERGE_CHECK_FAILED: " + name);
            passed.Add(name);
        };
        var attacker = race.Participants.First(p => race.Roles.IsAttacker(p));
        var defender = race.Participants.First(p => !race.Roles.IsAttacker(p));
        var holder = defender.GetComponent<PlayerHeldItem>();
        var inventory = defender.GetComponent<PlayerInventory>();
        var conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorController>();
        var traps = UnityEngine.Object.FindFirstObjectByType<SpearNetwork>();
        var goal = UnityEngine.Object.FindFirstObjectByType<GoalZone>();
        var fall = UnityEngine.Object.FindFirstObjectByType<FallZone>();
        check(inventory.syncMode == SyncMode.Owner && holder.syncMode == SyncMode.Owner, "owner-only inventory and held item");
        check(attacker.CanMove && !attacker.CanUseTraps, "attacker moves without trap permission");
        check(!defender.CanMove && defender.CanUseTraps, "defender uses traps without movement");
        check(!attacker.GetComponent<PlayerHeldItem>().ServerAllowsRound(race.RoundNumber), "reject attacker trap request");
        check(!holder.ServerAllowsRound(race.RoundNumber - 1), "reject previous turn request");
        check(holder.ServerAllowsRound(race.RoundNumber), "accept current defender request");
        check(!goal.ServerTryContact(attacker.connectionToClient, race.RoundNumber), "reject forged goal away from zone");
        check(!goal.ServerTryContact(attacker.connectionToClient, race.RoundNumber - 1), "reject stale goal");
        check(!fall.ServerTryContact(attacker.connectionToClient, race.RoundNumber - 1), "reject stale fall");
        check(!race.TryFinish(defender), "reject defender goal");
        check(inventory.FindEmptySlot() >= 0, "initial free inventory slot");
        check(!traps.IsValidAim(new Vector2(float.NaN, 0)), "reject NaN aim");
        check(!traps.IsValidAim(new Vector2(float.PositiveInfinity, 0)), "reject infinite aim");
        check(!traps.IsValidAim(Vector2.one * float.MaxValue), "reject outside field aim");
        var data = conveyor.GetItemData(1);
        check(data != null, "main item catalog retained");
        int before = defender.GetComponent<PlayerCost>().CurrentCost;
        check(!traps.TryUse(data, defender.netIdentity, new Vector2(float.NaN, 0), 10), "reject invalid trap request");
        check(defender.GetComponent<PlayerCost>().CurrentCost == before, "failed use preserves cost");
        check(race.MatchEndsAt > NetworkTime.time && race.EndsAt <= race.MatchEndsAt, "global match deadline bounds turn timer");
        int round = race.RoundNumber;
        double deadline = race.MatchEndsAt;
        check(race.TryFinish(attacker), "attacker finish accepted");
        check(attacker.GetComponent<PlayerScore>().Score == 1, "goal awards one point");
        check(race.RoundNumber == round + 1 && !race.Roles.IsAttacker(attacker), "goal advances attacker and turn");
        check(race.MatchEndsAt == deadline && race.EndsAt == deadline, "turn change preserves match deadline");
        check(!race.TryFinish(attacker), "previous attacker cannot finish again");
        check(!holder.ServerAllowsRound(round), "turn change rejects old item request");
        check(race.Participants.All(p => p.GetComponent<PlayerInventory>().Items.All(id => id == 0)), "turn change clears inventories");
        return passed.ToArray();
    }
}
