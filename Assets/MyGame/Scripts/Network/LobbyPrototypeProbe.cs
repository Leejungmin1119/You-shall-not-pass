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
        public string action, name;
        public bool ready;
    }
    [Serializable] private sealed class PlayerReport
    {
        public uint netId;
        public string name;
        public int seat;
        public bool ready, local;
        public float x, y;
    }
    [Serializable] private sealed class Snapshot
    {
        public string id, scene;
        public bool connected, server, countdown;
        public double now, deadline, gameSceneEnteredTime;
        public int required, lastCommand;
        public uint localPlayerId;
        public string[] errors;
        public PlayerReport[] players;
    }
    [Serializable] private sealed class EventReport
    {
        public string type, scene;
        public double now, deadline;
        public int command, players;
    }

    private string directory, id, previousScene;
    private bool host, connectionStarted;
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
            if (host) manager.BeginLanRoom(true); else manager.StartClient();
        }
        var local = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<CapstoneRoomPlayer>() : null;
        TryCommand(manager, local);
        string scene = SceneManager.GetActiveScene().name;
        var players = manager.roomSlots.OfType<CapstoneRoomPlayer>().Where(player => player != null)
            .OrderBy(player => player.SeatIndex).ToArray();
        double deadline = manager.LobbyCountdownEndsAt;
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
            id = id, scene = scene, connected = NetworkClient.isConnected, server = NetworkServer.active,
            now = NetworkTime.time, deadline = deadline, countdown = manager.IsLobbyCountdownActive,
            required = manager.RequiredLobbyPlayers, localPlayerId = local != null ? local.netId : 0,
            lastCommand = lastCommand, gameSceneEnteredTime = gameSceneEnteredTime, errors = errors.ToArray(),
            players = players.Select(player => new PlayerReport
            {
                netId = player.netId, name = player.PlayerName, seat = player.SeatIndex,
                ready = player.IsReady, local = player.isLocalPlayer,
                x = player.transform.position.x, y = player.transform.position.y
            }).ToArray()
        };
        Write(Path.Combine(directory, id + ".json"), JsonUtility.ToJson(snapshot, true), false);
    }

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
                if (NetworkServer.active) manager.StopHost(); else manager.StopClient();
                break;
            default: return;
        }
        lastCommand = command.sequence;
        WriteEvent("command-" + command.action, SceneManager.GetActiveScene().name,
            manager.LobbyCountdownEndsAt, manager.roomSlots.Count);
    }

    private void WriteEvent(string type, string scene, double deadline, int playerCount)
    {
        var value = new EventReport { type = type, scene = scene, now = NetworkTime.time,
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
