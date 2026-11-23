#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>명시적 실행 인자가 있을 때만 동작하는 실제 소유자 Command 기반 통합 시험 도구.</summary>
public sealed class LobbyPrototypeProbe : MonoBehaviour
{
    [Serializable] private sealed class Control
    {
        public int sequence;
        public string action, name, relayAddress, roomPassword;
        public uint playerId;
        public int character, roundOffset;
        public float x, y;
        public bool ready;
    }
    [Serializable] private sealed class PlayerReport
    {
        public uint netId;
        public string name;
        public int seat;
        public bool ready, local;
        public int character;
        public float x, y;
    }
    [Serializable] private sealed class Snapshot
    {
        public string id, scene, screen;
        public bool connected, server, countdown, advertising, searching;
        public string connectionError, racePhase;
        public int turn;
        public uint attacker;
        public GamePlayerReport[] gamePlayers;
        public string[] requestResults, serverChecks;
        public int maxTraps;
        public double now, deadline, gameSceneEnteredTime;
        public int required, lastCommand;
        public uint localPlayerId;
        public string[] errors;
        public PlayerReport[] players;
        public uint[] kickablePlayers, serverPlayerIds;
        public bool serverRegistryValid;
        public string endReason;
        public uint[] winners;
        public float timeScale;
        public bool uiHidden;
        public MissileReport[] missiles;
    }
    [Serializable] private sealed class GamePlayerReport
    {
        public uint id;
        public bool local;
        public int score, cost, count;
        public int[] slots;
        public int character;
        public float x, y;
    }
    [Serializable] private sealed class MissileReport
    {
        public uint id, owner;
        public float aimX, aimY;
    }
    [Serializable] private sealed class EventReport
    {
        public string type, scene, screen;
        public double now, deadline;
        public int command, players;
    }

    private string directory, id, previousScene, previousScreen;
    private bool host, connectionStarted, useDiscovery;
    private string directAddress;
    private PlayerHeldItem observedHolder;
    private readonly List<string> requestResults = new List<string>();
    private readonly List<string> serverChecks = new List<string>();
    private int maxTraps;
    private float nextUpdate;
    private int lastCommand;
    private double previousDeadline, gameSceneEnteredTime;
    private readonly List<string> errors = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch()
    {
        string[] args = Environment.GetCommandLineArgs();
        string directory = Argument(args, "-lobbyProbeDirectory"), id = Argument(args, "-lobbyProbeId");
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(id) || !Path.IsPathRooted(directory) ||
            !new[] { "host", "c1", "c2", "c3" }.Contains(id)) return;
        Directory.CreateDirectory(directory);
        var probe = new GameObject("LobbyPrototypeProbe").AddComponent<LobbyPrototypeProbe>();
        DontDestroyOnLoad(probe.gameObject);
        probe.directory = directory;
        probe.id = id;
        probe.host = args.Contains("-lobbyProbeHost");
        probe.useDiscovery = args.Contains("-lobbyProbeDiscovery");
        probe.connectionStarted = args.Contains("-lobbyProbeManual");
        probe.directAddress = Argument(args, "-lobbyProbeDirectAddress") ?? "127.0.0.1";
        Application.runInBackground = true;
        Application.logMessageReceived += probe.RecordLog;
    }

    private static string Argument(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void RecordLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (condition.Contains("[Licensing::")) return;
        if (errors.Count < 30) errors.Add(condition.Length > 1200 ? condition.Substring(0, 1200) : condition);
    }
    private void OnDestroy() => Application.logMessageReceived -= RecordLog;

    private void Update()
    {
        if (Time.unscaledTime < nextUpdate || string.IsNullOrEmpty(directory)) return;
        nextUpdate = Time.unscaledTime + .1f;
        var manager = NetworkManager.singleton as CapstoneRoomManager;
        if (manager == null) return;
        if (!connectionStarted && manager.IsLobby)
        {
            connectionStarted = true;
            manager.networkAddress = "127.0.0.1";
            if (host) manager.BeginLanRoom(true);
            else if (useDiscovery) manager.BeginLanRoom(false);
            else manager.BeginDirectRoom(directAddress);
        }
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<CapstoneRoomPlayer>() : null;
        var holder = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerHeldItem>() : null;
        if (holder != observedHolder)
        {
            if (observedHolder != null) observedHolder.RequestCompleted -= RecordRequest;
            observedHolder = holder;
            if (observedHolder != null) observedHolder.RequestCompleted += RecordRequest;
        }
        TryCommand(manager, local);
        var race = FindFirstObjectByType<RaceManager>();
        maxTraps = Mathf.Max(maxTraps, ItemBase.ActiveCount);
        string scene = SceneManager.GetActiveScene().name;
        var players = manager.roomSlots.OfType<CapstoneRoomPlayer>().Where(player => player != null)
            .OrderBy(player => player.SeatIndex).ToArray();
        double deadline = manager.LobbyCountdownEndsAt;
        string screen = manager.GetComponent<CapstoneLobbyUI>()?.CurrentScreen;
        if (screen != previousScreen)
        {
            WriteEvent("screen", scene, deadline, players.Length);
            previousScreen = screen;
        }
        if (scene != previousScene)
        {
            if (!manager.IsLobby && gameSceneEnteredTime == 0) gameSceneEnteredTime = NetworkTime.time;
            WriteEvent("scene", scene, deadline, players.Length);
            previousScene = scene;
        }
        if (deadline != previousDeadline)
        {
            WriteEvent(deadline > 0 ? "countdown-start" : "countdown-cancel", scene, deadline, players.Length);
            previousDeadline = deadline;
        }
        var snapshot = new Snapshot
        {
            id = id, scene = scene, screen = manager.GetComponent<CapstoneLobbyUI>()?.CurrentScreen, connected = NetworkClient.isConnected, server = NetworkServer.active,
            now = NetworkTime.time, deadline = deadline, countdown = manager.IsLobbyCountdownActive,
            advertising = manager.Lan.Advertising, searching = manager.Lan.Searching,
            connectionError = manager.LastConnectionError,
            racePhase = race != null ? race.Phase.ToString() : "", turn = race != null ? race.RoundNumber : 0,
            attacker = race != null && race.Roles != null ? race.Roles.AttackerNetId : 0,
            requestResults = requestResults.ToArray(), serverChecks = serverChecks.ToArray(), maxTraps = maxTraps,
            gamePlayers = race == null ? new GamePlayerReport[0] : race.Participants.Select(p => new GamePlayerReport {
                id = p.netId, local = p.isLocalPlayer, score = p.GetComponent<PlayerScore>().Score,
                cost = p.GetComponent<PlayerCost>().CurrentCost, count = p.InventoryCount,
                slots = p.GetComponent<PlayerInventory>().Items.ToArray(),
                character = (int)p.GetComponent<PlayerIdleBoil>().Character,
                x = p.transform.position.x, y = p.transform.position.y
            }).ToArray(),
            required = manager.RequiredLobbyPlayers, localPlayerId = local != null ? local.netId : 0,
            lastCommand = lastCommand, gameSceneEnteredTime = gameSceneEnteredTime, errors = errors.ToArray(),
            players = players.Select(player => new PlayerReport
            {
                netId = player.netId, name = player.PlayerName, seat = player.SeatIndex,
                ready = player.IsReady, local = player.isLocalPlayer,
                x = player.transform.position.x, y = player.transform.position.y, character = (int)player.Character
            }).ToArray(),
            kickablePlayers = players.Where(manager.CanKickLobbyPlayer).Select(player => player.netId).ToArray(),
            serverPlayerIds = PlayerState.ServerPlayers.Where(p => p != null).Select(p => p.netId).ToArray(),
            serverRegistryValid = ValidateServerRegistry(),
            endReason = race != null ? race.EndReason.ToString() : "",
            winners = race != null ? race.Winners.ToArray() : new uint[0],
            timeScale = Time.timeScale, uiHidden = DeathSequence.IsUIHidden,
            missiles = FindObjectsByType<Missile>(FindObjectsSortMode.None).Where(m => !m.HasExploded)
                .Select(m => new MissileReport { id = m.InstanceId, owner = m.OwnerNetId,
                    aimX = m.AimPosition.x, aimY = m.AimPosition.y }).ToArray()
        };
        Write(Path.Combine(directory, id + ".json"), JsonUtility.ToJson(snapshot, true), false);
    }

    private static bool ValidateServerRegistry()
    {
        var players = PlayerState.ServerPlayers;
        int count = 0;
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            if (identity != null && identity.TryGetComponent<PlayerState>(out _)) count++;
        if (count != players.Count) return false;
        for (int index = 0; index < players.Count; index++)
        {
            var player = players[index];
            if (player == null || !NetworkServer.spawned.TryGetValue(player.netId, out var identity) ||
                identity != player.netIdentity) return false;
            for (int previous = 0; previous < index; previous++)
                if (players[previous] == player) return false;
        }
        return true;
    }

    private System.Collections.IEnumerator CaptureFrame(CapstoneRoomManager manager)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, id + "-" + manager.GetComponent<CapstoneLobbyUI>().CurrentScreen + "-screen.png"));
    }

    private void RecordRequest(string operation, bool success) => requestResults.Add(operation + ":" + success);

    private void TryCommand(CapstoneRoomManager manager, CapstoneRoomPlayer local)
    {
        string path = Path.Combine(directory, id + ".command.json");
        if (!File.Exists(path)) return;
        Control command;
        try { command = JsonUtility.FromJson<Control>(File.ReadAllText(path)); }
        catch (IOException) { return; }
        catch (ArgumentException) { return; }
        catch (UnauthorizedAccessException) { return; }
        if (command == null || command.sequence <= lastCommand) return;
        switch (command.action)
        {
            case "ready":
                if (local == null || !manager.IsLobby) return;
                local.CmdChangeReadyState(command.ready);
                break;
            case "rename":
                if (local == null || !manager.IsLobby) return;
                local.RequestName(command.name);
                break;
            case "kick":
                if (local == null || !manager.IsLobby) return;
                local.RequestKick(command.playerId);
                break;
            case "character":
                if (local == null || !manager.IsLobby) return;
                local.RequestCharacter(command.character);
                break;
            case "rawCharacter":
                if (local == null || !manager.IsLobby) return;
                typeof(CapstoneRoomPlayer).GetMethod("CmdSetCharacter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(local, new object[] { command.character });
                break;
            case "searchMultipleFixture":
                manager.BeginLanRoom(false);
                var found = typeof(LanRoomConnector).GetMethod("OnFound", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (long serverId in new long[] { 101, 102 })
                    found.Invoke(manager.Lan, new object[] { new Mirror.Discovery.ServerResponse {
                        serverId = serverId, uri = new Uri("kcp://127.0.0.1:7777") } });
                typeof(LanRoomConnector).GetField("searchStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(manager.Lan, Time.unscaledTime - 16f);
                break;
            case "nudge":
                if (local == null || !manager.IsLobby) return;
                local.transform.position += Vector3.right * 3f;
                break;
            case "tryStart":
                if (!NetworkServer.active) return;
                manager.TryStartGame();
                break;
            case "return":
                if (!NetworkServer.active || manager.IsLobby) return;
                manager.ReturnToLobby();
                break;
            case "leave":
                manager.LeaveRoom();
                break;
            case "capture":
                StartCoroutine(CaptureFrame(manager));
                break;
            case "showIp":
                manager.GetComponent<CapstoneLobbyUI>().ToggleLocalAddressPanel();
                break;
            case "solo":
                manager.BeginSoloTest();
                break;
            case "direct":
                manager.BeginDirectRoom(command.name);
                break;
            case "relayHost":
            case "relayJoin":
                manager.BeginRoom(command.action == "relayHost", command.relayAddress, command.roomPassword);
                break;
            case "search":
                manager.BeginLanRoom(false);
                break;
            case "host":
                manager.BeginLanRoom(true);
                break;
            case "buy":
                var conveyor = FindFirstObjectByType<ConveyorController>();
                if (observedHolder == null || conveyor == null || conveyor.Items.Count == 0 || !observedHolder.CanRequestPurchase) return;
                observedHolder.RequestPurchase(conveyor.Items[0].Id, PurchaseDestination.Hand);
                break;
            case "buyMissile":
                var catalog = FindFirstObjectByType<ConveyorController>();
                if (observedHolder == null || catalog == null || !observedHolder.CanRequestPurchase) return;
                var missileOffer = catalog.Items.FirstOrDefault(item => catalog.GetItemData(item.ItemId)?.Prefab is Missile);
                if (missileOffer.Id == 0) return;
                observedHolder.RequestPurchase(missileOffer.Id, PurchaseDestination.Hand);
                // Keep automatic mouse updates from overwriting the explicit aim requests under test.
                typeof(PlayerHeldItem).GetField("nextAimSendTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(observedHolder, float.PositiveInfinity);
                break;
            case "useAt":
                var aimCamera = FindFirstObjectByType<ConveyorController>()?.OutputCamera;
                if (observedHolder == null || aimCamera == null || !observedHolder.HasHeldItem) return;
                observedHolder.RequestUse(aimCamera.ViewportToScreenPoint(new Vector3(command.x, command.y)));
                break;
            case "aim":
                if (observedHolder == null) return;
                var aimRace = FindFirstObjectByType<RaceManager>();
                if (aimRace == null) return;
                typeof(PlayerHeldItem).GetField("nextAimSendTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(observedHolder, float.PositiveInfinity);
                typeof(PlayerHeldItem).GetMethod("CmdUpdateMissileAim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(observedHolder, new object[] { new Vector2(command.x, command.y), aimRace.RoundNumber + command.roundOffset });
                break;
            case "deathFixture":
                var deathRace = FindFirstObjectByType<RaceManager>();
                if (!NetworkServer.active || deathRace == null || !deathRace.IsRunning) return;
                var dying = deathRace.Participants.First(p => deathRace.Roles.IsAttacker(p));
                dying.TryDie((Vector2)dying.transform.position + Vector2.left);
                break;
            case "timeLimitFixture":
                var timedRace = FindFirstObjectByType<RaceManager>();
                if (!NetworkServer.active || timedRace == null || !timedRace.IsRunning) return;
                typeof(RaceManager).GetField("matchEndsAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(timedRace, NetworkTime.time + 1d);
                break;
            case "scoreLimitFixture":
                var scoredRace = FindFirstObjectByType<RaceManager>();
                if (!NetworkServer.active || scoredRace == null || !scoredRace.IsRunning) return;
                scoredRace.Participants.First(p => scoredRace.Roles.IsAttacker(p)).GetComponent<PlayerScore>().AddGoalScore(20);
                break;
            case "use":
                var offers = FindFirstObjectByType<ConveyorController>();
                var traps = FindFirstObjectByType<SpearNetwork>();
                if (observedHolder == null || !observedHolder.HasHeldItem || offers == null || traps == null) return;
                var camera = offers.OutputCamera;
                // 보이는 필드의 유효한 지점으로 실제 소유자 사용 요청을 보낸다.
                bool sent = false;
                for (int x = 4; x <= 8 && !sent; x++)
                    for (int y = 4; y <= 7 && !sent; y++)
                    {
                        Vector3 screen = camera.ViewportToScreenPoint(new Vector3(x / 10f, y / 10f));
                        var world = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z));
                        if (traps.IsValidAim(world)) sent = observedHolder.RequestUse(screen);
                    }
                if (!sent) return;
                break;
            case "staleUse":
                if (observedHolder == null) return;
                var currentRace = FindFirstObjectByType<RaceManager>();
                if (currentRace == null) return;
                typeof(PlayerHeldItem).GetMethod("CmdRequestUse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(observedHolder, new object[] { new Vector2(float.NaN, 0f), 0f, currentRace.RoundNumber - 1 });
                break;
            case "goalFixture":
                if (!NetworkServer.active) return;
                var race = FindFirstObjectByType<RaceManager>();
                var goal = FindFirstObjectByType<GoalZone>();
                if (race == null || !race.IsRunning || goal == null) return;
                var attacker = race.Participants.First(p => race.Roles.IsAttacker(p));
                if (goal.ServerTryContact(attacker.connectionToClient, race.RoundNumber - 1)) throw new Exception("Stale goal accepted");
                serverChecks.Add("stale goal rejected");
                if (goal.ServerTryContact(attacker.connectionToClient, race.RoundNumber)) throw new Exception("Remote goal accepted away from zone");
                serverChecks.Add("goal away from zone rejected");
                attacker.Respawn(goal.GetComponent<BoxCollider2D>().bounds.center);
                Physics2D.SyncTransforms();
                if (!goal.ServerTryContact(attacker.connectionToClient, race.RoundNumber)) throw new Exception("Fixture goal rejected");
                serverChecks.Add("goal contact accepted and turn advanced");
                break;
            default: return;
        }
        lastCommand = command.sequence;
        WriteEvent("command-" + command.action, SceneManager.GetActiveScene().name,
            manager.LobbyCountdownEndsAt, manager.roomSlots.Count);
    }

    private void WriteEvent(string type, string scene, double deadline, int playerCount)
    {
        var value = new EventReport { type = type, scene = scene, screen = (NetworkManager.singleton as CapstoneRoomManager)?.GetComponent<CapstoneLobbyUI>()?.CurrentScreen, now = NetworkTime.time,
            deadline = deadline, command = lastCommand, players = playerCount };
        Write(Path.Combine(directory, id + ".events.jsonl"), JsonUtility.ToJson(value) + "\n", true);
    }
    private static void Write(string path, string value, bool append)
    {
        try { if (append) File.AppendAllText(path, value); else File.WriteAllText(path, value); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
#endif
