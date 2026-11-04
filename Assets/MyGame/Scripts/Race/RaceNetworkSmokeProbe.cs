#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>개발 빌드 전용. -raceSmokeReport 절대경로로 접속과 복제 결과를 기록한다.</summary>
public sealed class RaceNetworkSmokeProbe : MonoBehaviour
{
    [Serializable] public sealed class Report
    {
        public string phase;
        public int round;
        public uint localPlayer;
        public int traps;
        public double deadline;
        public uint[] winners;
        public string endReason;
        public int maxObservedTraps;
        public string[] requestResults;
        public Player[] players;
    }
    [Serializable] public sealed class Player
    {
        public uint id;
        public bool local;
        public int inventoryCount;
        public int[] visibleSlots;
        public int heldSlot;
        public int cost;
        public int score;
        public string status;
    }
    private string reportPath;
    private float nextUpdate;
    private bool connectStarted;
    private bool exerciseActions;
    private int actionStep;
    private int maxObservedTraps;
    private readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-raceSmokeReport");
        if (index < 0 || index + 1 >= args.Length) return;
        var go = new GameObject("RaceNetworkSmokeProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<RaceNetworkSmokeProbe>();
        probe.reportPath = args[index + 1];
        probe.exerciseActions = Array.IndexOf(args, "-raceSmokeActions") >= 0;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.25f;
        if (!connectStarted && NetworkManager.singleton is CapstoneRoomManager manager)
        {
            connectStarted = true;
            manager.networkAddress = "localhost";
            manager.StartClient();
        }
        if (NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out CapstoneRoomPlayer room) && !room.IsReady)
            room.ToggleReady();
        var race = FindFirstObjectByType<RaceManager>();
        if (race == null) return;
        maxObservedTraps = Mathf.Max(maxObservedTraps, ItemBase.ActiveCount);
        if (exerciseActions && race.IsRunning && NetworkClient.localPlayer != null)
        {
            var inventory = NetworkClient.localPlayer.GetComponent<PlayerHeldItem>();
            var conveyor = FindFirstObjectByType<ConveyorController>();
            if (actionStep == 0 && inventory.CanRequestPurchase && conveyor != null && conveyor.Items.Count > 0)
            {
                inventory.RequestCompleted += (operation, success) => results.Add(operation + ":" + success);
                actionStep = 1;
                inventory.RequestPurchase(conveyor.Items[0].Id, PurchaseDestination.Hand);
            }
            else if (actionStep == 1 && inventory.HasHeldItem && results.Count == 1)
            {
                actionStep = 2;
                var output = conveyor.OutputCamera;
                inventory.RequestUse(new Vector2(output.pixelWidth * 0.65f, output.pixelHeight * 0.4f));
            }
        }
        var report = new Report
        {
            phase = race.Phase.ToString(), round = race.RoundNumber, deadline = race.EndsAt,
            localPlayer = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0,
            maxObservedTraps = maxObservedTraps, requestResults = results.ToArray(),
            traps = ItemBase.ActiveCount, winners = race.Winners.ToArray(), endReason = race.EndReason.ToString(),
            players = race.Participants.Where(p => p != null).Select(p => new Player
            {
                id = p.netId, local = p.isLocalPlayer, inventoryCount = p.InventoryCount,
                visibleSlots = p.GetComponent<PlayerInventory>().Items.ToArray(),
                heldSlot = p.GetComponent<PlayerInventory>().SelectedSlot,
                cost = p.GetComponent<PlayerCost>().CurrentCost,
                score = p.GetComponent<PlayerScore>().Score, status = p.Status.ToString()
            }).ToArray()
        };
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }
}
#endif
