using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>새 main API로 서버의 방어 조건을 검사한다. 별도 시험 Play 세션에서만 실행한다.</summary>
public static class RaceNetworkChecks
{
    public static string[] Run()
    {
        var race = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
        if (!NetworkServer.active || race == null || !race.IsRunning || race.Participants.Count < 2)
            throw new InvalidOperationException("Run in a racing Host with two or more players.");
        var passed = new List<string>();
        Action<bool, string> check = (ok, name) =>
        {
            if (!ok) throw new InvalidOperationException("MERGE_CHECK_FAILED: " + name);
            passed.Add(name);
        };
        var actor = race.Participants[0];
        var holder = actor.GetComponent<PlayerHeldItem>();
        var inventory = actor.GetComponent<PlayerInventory>();
        var conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorController>();
        var traps = UnityEngine.Object.FindFirstObjectByType<SpearNetwork>();
        var goal = UnityEngine.Object.FindFirstObjectByType<GoalZone>();
        check(inventory.syncMode == SyncMode.Owner && holder.syncMode == SyncMode.Owner, "owner-only inventory and held item");
        check(!holder.ServerAllowsRound(race.RoundNumber - 1), "reject previous round request");
        check(holder.ServerAllowsRound(race.RoundNumber), "accept current round participant");
        check(!goal.ServerTryContact(actor.connectionToClient, race.RoundNumber), "reject forged goal away from zone");
        check(!goal.ServerTryContact(actor.connectionToClient, race.RoundNumber - 1), "reject stale goal");
        check(inventory.FindEmptySlot() >= 0, "initial free inventory slot");
        check(!traps.IsValidAim(new Vector2(float.NaN, 0)), "reject NaN aim");
        check(!traps.IsValidAim(new Vector2(float.PositiveInfinity, 0)), "reject infinite aim");
        check(!traps.IsValidAim(Vector2.one * float.MaxValue), "reject outside field aim");
        var data = conveyor.GetItemData(1);
        check(data != null, "main item catalog retained");
        int before = actor.GetComponent<PlayerCost>().CurrentCost;
        check(!traps.TryUse(data, actor.netIdentity, new Vector2(float.NaN, 0), 10), "reject invalid trap request");
        check(actor.GetComponent<PlayerCost>().CurrentCost == before, "failed use preserves cost");
        check(race.MatchEndsAt > NetworkTime.time && race.EndsAt <= race.MatchEndsAt, "global match deadline bounds phase timer");
        var other = race.Participants.First(p => p != actor);
        int score = actor.GetComponent<PlayerScore>().Score;
        check(race.TryFinish(actor), "first finish accepted");
        check(actor.GetComponent<PlayerScore>().Score == score + 4, "first rank awards four");
        check(!race.TryFinish(actor), "duplicate finish rejected");
        check(actor.CanUseItems && !actor.CanAct, "spectator items without movement");
        check(race.Phase == RacePhase.FinishCountdown && race.EndsAt <= race.MatchEndsAt, "closing deadline capped by match");
        int otherScore = other.GetComponent<PlayerScore>().Score;
        check(race.TryFinish(other), "second finish accepted");
        check(other.GetComponent<PlayerScore>().Score == otherScore + 3, "second rank awards three");
        return passed.ToArray();
    }
}
