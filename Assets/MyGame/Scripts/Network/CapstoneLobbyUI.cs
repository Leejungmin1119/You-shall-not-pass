using System.Linq;
using Mirror;
using UnityEngine;

// 로비 전용 화면. 기존 네트워크 플레이어의 SyncVar를 읽어 슬롯을 표시한다.
public class CapstoneLobbyUI : MonoBehaviour
{
    private PracticeNetworkManager manager;
    private string address = "localhost";
    private GUIStyle title, heading, text, small, button, input;
    private static readonly Color Background = new Color(.035f, .05f, .09f);
    private static readonly Color Panel = new Color(.07f, .09f, .15f);
    private static readonly Color Accent = new Color(.34f, .91f, .73f);

    private void Awake()
    {
        manager = GetComponent<PracticeNetworkManager>();
        address = manager.networkAddress;
    }

    private GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
    {
        return new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight,
            normal = { textColor = color }, wordWrap = true };
    }

    private void InitStyles()
    {
        if (title != null) return;
        title = Style(42, Color.white, FontStyle.Bold);
        heading = Style(24, Color.white, FontStyle.Bold);
        text = Style(18, new Color(.78f, .83f, .9f));
        small = Style(14, new Color(.52f, .61f, .73f));
        button = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
        input = new GUIStyle(GUI.skin.textField) { fontSize = 20, padding = new RectOffset(12, 12, 10, 10) };
    }

    private void Box(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private bool Action(Rect rect, string label, bool enabled = true)
    {
        GUI.enabled = enabled;
        bool clicked = GUI.Button(rect, label, button);
        GUI.enabled = true;
        return clicked;
    }

    private void Leave()
    {
        if (NetworkServer.active && NetworkClient.active) manager.StopHost();
        else if (NetworkClient.active) manager.StopClient();
        else if (NetworkServer.active) manager.StopServer();
    }

    private void OnGUI()
    {
        if (manager == null) return;
        InitStyles();
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        if (!manager.IsLobby)
        {
            if (Action(new Rect(1100, 20, 160, 40), "Leave game")) Leave();
            GUI.matrix = oldMatrix;
            return;
        }

        Box(new Rect(0, 0, 1280, 720), Background);
        Box(new Rect(0, 0, 8, 720), Accent);
        GUI.Label(new Rect(48, 30, 900, 24), "CAPSTONE / MULTIPLAYER", small);
        GUI.Label(new Rect(48, 62, 1050, 62), "YOU SHALL NOT PASS", title);
        GUI.Label(new Rect(50, 128, 950, 32), "Gather your team. Ready up. Enter the arena.", text);
        Box(new Rect(48, 190, 310, 470), Panel);
        GUI.Label(new Rect(72, 215, 270, 40), "ROOM CONNECTION", heading);
        bool connected = NetworkClient.isConnected;
        bool active = NetworkClient.active || NetworkServer.active;
        GUI.Label(new Rect(72, 270, 265, 52), active
            ? (connected ? (NetworkServer.active ? "HOST / ROOM OPEN" : "CLIENT / CONNECTED") : "Connecting...")
            : "Create a room or join a host.", text);
        GUI.Label(new Rect(72, 345, 265, 24), "HOST IP ADDRESS", small);
        GUI.enabled = !active;
        address = GUI.TextField(new Rect(72, 375, 262, 46), address, input);
        GUI.enabled = true;
        if (Action(new Rect(72, 445, 262, 48), "Create room", !active)) manager.StartHost();
        if (Action(new Rect(72, 505, 262, 48), "Join room", !active && !string.IsNullOrWhiteSpace(address)))
        {
            manager.networkAddress = address.Trim();
            manager.StartClient();
        }
        if (Action(new Rect(72, 578, 262, 42), "Leave / Cancel", active)) Leave();

        var players = NetworkClient.spawned.Values.Where(n => n != null)
            .Select(n => n.GetComponent<NetworkPlayerLobby>()).Where(n => n != null)
            .OrderBy(n => n.netId).ToArray();
        int ready = players.Count(n => n.IsReady);
        GUI.Label(new Rect(390, 193, 550, 38), "WAITING ROOM", heading);
        GUI.Label(new Rect(1040, 203, 190, 30), $"{players.Length} / 4 PLAYERS", small);
        for (int i = 0; i < 4; i++)
        {
            float x = 390 + (i % 2) * 420, y = 255 + (i / 2) * 145;
            Rect card = new Rect(x, y, 395, 125);
            Box(card, Panel);
            var player = i < players.Length ? players[i] : null;
            Color state = player == null ? new Color(.2f, .25f, .33f) : player.IsReady ? Accent : new Color(1f, .7f, .32f);
            Box(new Rect(x, y, 4, 125), state);
            Box(new Rect(x + 20, y + 24, 55, 55), state);
            GUI.Label(new Rect(x + 36, y + 33, 40, 45), (i + 1).ToString(), heading);
            GUI.Label(new Rect(x + 95, y + 20, 290, 35), player == null ? "OPEN SLOT" : $"PLAYER {player.netId}" + (player.isLocalPlayer ? "  / YOU" : ""), text);
            GUI.Label(new Rect(x + 95, y + 63, 290, 35), player == null ? "Waiting for a teammate" : player.IsReady ? "READY" : "NOT READY", Style(16, state));
        }
        var local = players.FirstOrDefault(n => n.isLocalPlayer);
        if (Action(new Rect(390, 568, 395, 52), local != null && local.IsReady ? "Cancel ready  [ F ]" : "Ready up  [ F ]", connected && local != null)) local.ToggleReady();
        if (Action(new Rect(810, 568, 395, 52), "Start game  [ ENTER ]", NetworkServer.active && connected && manager.CanStartGame())) manager.TryStartGame();
        GUI.Label(new Rect(390, 635, 830, 38), $"{ready} / 4 ready  -  All four players must be ready. Only the host can start.", small);
        GUI.matrix = oldMatrix;
    }
}
