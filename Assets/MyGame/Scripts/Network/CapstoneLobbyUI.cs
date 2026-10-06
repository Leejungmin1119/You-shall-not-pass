using System.Linq;
using Mirror;
using UnityEngine;

// 우주선 대기실. 실제 네트워크 플레이어의 위치와 준비 상태를 화면에 표시한다.
public class CapstoneLobbyUI : MonoBehaviour
{
    private PracticeNetworkManager manager;
    private string address = "localhost";
    private GUIStyle title, heading, text, small, button, input;
    [Header("중앙 알림")]
    [SerializeField] private float noticeDuration = 2.4f;
    private string noticeTitle, noticeDetail;
    private Color noticeColor;
    private float noticeStartedAt = float.NegativeInfinity;
    private GUIStyle noticeHeading, noticeCaption;

    // 실시간을 사용하므로 게임 시간이 멈춰 있어도 알림이 사라진다.
    public void ShowNotice(string message, string detail, bool success)
    {
        noticeTitle = message;
        noticeDetail = detail;
        noticeColor = success ? Accent : new Color(1f, .7f, .32f);
        noticeStartedAt = Time.unscaledTime;
    }

    private void DrawNotice()
    {
        float elapsed = Time.unscaledTime - noticeStartedAt;
        if (string.IsNullOrEmpty(noticeTitle) || elapsed >= noticeDuration) return;
        float fadeIn = Mathf.Clamp01(elapsed / .16f);
        float fadeOut = Mathf.Clamp01((noticeDuration - elapsed) / .55f);
        float opacity = fadeIn * fadeOut;
        // 완전히 꺼졌다 켜지지 않고 밝기가 부드럽게 변한다.
        float pulse = .82f + .18f * Mathf.Cos(elapsed * Mathf.PI * 2.6f);
        float scale = Mathf.Lerp(.9f, 1f, Mathf.SmoothStep(0f, 1f, elapsed / .2f));
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(640, 360));
        GUI.color = new Color(1, 1, 1, opacity);
        Box(new Rect(280, 260, 720, 200), new Color(.025f, .035f, .065f, .96f));
        Box(new Rect(280, 260, 720, 4), noticeColor);
        noticeHeading.normal.textColor = new Color(noticeColor.r, noticeColor.g, noticeColor.b, pulse);
        GUI.Label(new Rect(300, 287, 680, 90), noticeTitle, noticeHeading);
        GUI.Label(new Rect(315, 382, 650, 55), noticeDetail, noticeCaption);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }
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
        noticeHeading = Style(64, Accent, FontStyle.Bold);
        noticeHeading.alignment = TextAnchor.MiddleCenter;
        noticeCaption = Style(20, new Color(.83f, .88f, .95f));
        noticeCaption.alignment = TextAnchor.MiddleCenter;
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

    private Texture2D circle;
    private GUIStyle centered, badge;
    private static readonly Color[] CrewColors = {
        new Color(.37f, .88f, .82f), new Color(1f, .58f, .38f),
        new Color(.66f, .56f, 1f), new Color(1f, .83f, .35f)
    };

    private void Rounded(Rect rect, Color color, float radius = 12)
    {
        if (circle == null)
        {
            circle = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            circle.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    circle.SetPixel(x, y, new Color(1, 1, 1,
                        Mathf.Clamp01(32f - Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32)))));
            circle.Apply();
        }
        Box(new Rect(rect.x + radius, rect.y, rect.width - radius * 2, rect.height), color);
        Box(new Rect(rect.x, rect.y + radius, rect.width, rect.height - radius * 2), color);
        Color old = GUI.color;
        GUI.color *= color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, radius * 2, radius * 2), circle);
        GUI.DrawTexture(new Rect(rect.xMax - radius * 2, rect.y, radius * 2, radius * 2), circle);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - radius * 2, radius * 2, radius * 2), circle);
        GUI.DrawTexture(new Rect(rect.xMax - radius * 2, rect.yMax - radius * 2, radius * 2, radius * 2), circle);
        GUI.color = old;
    }

    private void DrawCrew(float x, float y, uint id, bool ready, bool local, bool preview = false)
    {
        Color color = CrewColors[(int)(id % 4)];
        // 작은 정비 로봇: 넓은 어깨, 두 눈의 바이저, 안테나.
        Rounded(new Rect(x - 25, y + 27, 50, 12), new Color(0, 0, 0, .3f), 5);
        float step = preview ? 0 : Mathf.Sin(Time.unscaledTime * 10f) * 2f;
        Rounded(new Rect(x - 20, y - 5, 15, 40 + step), color * .7f, 6);
        Rounded(new Rect(x + 5, y - 5, 15, 40 - step), color * .7f, 6);
        Rounded(new Rect(x - 30, y - 38, 60, 58), new Color(.03f, .055f, .09f), 12);
        Rounded(new Rect(x - 26, y - 34, 52, 48), color, 10);
        Rounded(new Rect(x - 21, y - 26, 42, 22), new Color(.08f, .14f, .22f), 7);
        Box(new Rect(x - 14, y - 20, 8, 6), new Color(.75f, 1f, 1f));
        Box(new Rect(x + 6, y - 20, 8, 6), new Color(.75f, 1f, 1f));
        Box(new Rect(x - 2, y - 45, 4, 10), color);
        Rounded(new Rect(x - 5, y - 51, 10, 10), ready ? Accent : color, 5);
        if (preview) return;
        GUI.Label(new Rect(x - 100, y - 84, 200, 28), "P" + id + (local ? " / YOU" : ""), centered);
        badge.normal.textColor = ready ? Accent : new Color(1f, .75f, .4f);
        GUI.Label(new Rect(x - 80, y + 44, 160, 24), ready ? "READY" : "WAITING", badge);
    }

    private void DrawShip()
    {
        // 창밖 별, 금속 벽, 타일 바닥과 화물 상자.
        for (int i = 0; i < 80; i++)
        {
            float x = (i * 173 + 41) % 1280, y = (i * 97 + 13) % 720;
            float alpha = .25f + .2f * Mathf.Sin(Time.unscaledTime * .7f + i);
            Box(new Rect(x, y, i % 5 == 0 ? 3 : 2, 2), new Color(.7f, .85f, 1f, alpha));
        }
        Rounded(new Rect(62, 167, 858, 398), new Color(.025f, .045f, .07f), 36);
        Rounded(new Rect(72, 157, 838, 388), new Color(.23f, .3f, .37f), 32);
        Rounded(new Rect(87, 178, 808, 360), new Color(.12f, .18f, .23f), 24);
        Rounded(new Rect(100, 226, 780, 305), new Color(.27f, .34f, .38f), 16);
        for (int x = 110; x < 880; x += 64)
            Box(new Rect(x, 239, 1, 281), new Color(.34f, .42f, .46f));
        for (int y = 244; y < 520; y += 46)
            Box(new Rect(110, y, 760, 1), new Color(.34f, .42f, .46f));
        Rounded(new Rect(335, 183, 310, 34), new Color(.035f, .07f, .12f), 10);
        GUI.Label(new Rect(335, 187, 310, 28), "DECK 01 / CREW ASSEMBLY", badge);
        for (int i = 0; i < 4; i++)
            Rounded(new Rect(i < 2 ? 120 + i * 100 : 700 + (i - 2) * 100, 191, 54, 10), new Color(.4f, .85f, .92f), 5);
        Rounded(new Rect(430, 344, 150, 82), new Color(.23f, .3f, .34f), 12);
        GUI.Label(new Rect(430, 363, 150, 30), "ASSEMBLY", badge);
        for (int i = 0; i < 3; i++)
        {
            float x = 120 + i * 72;
            Rounded(new Rect(x, 458, 57, 48), new Color(.16f, .23f, .23f), 5);
            Box(new Rect(x + 7, 466, 43, 3), new Color(.46f, .59f, .46f));
            Box(new Rect(x + 26, 466, 4, 34), new Color(.37f, .46f, .35f));
        }
        Rounded(new Rect(714, 450, 140, 70), new Color(.075f, .12f, .18f), 8);
        for (int i = 0; i < 6; i++)
            Box(new Rect(728, 459 + i * 9, 111, 3), new Color(.3f, .39f, .44f));
    }

    private void OnDestroy()
    {
        if (circle != null) Destroy(circle);
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
            DrawNotice();
            GUI.matrix = oldMatrix;
            return;
        }

        Box(new Rect(0, 0, 1280, 720), Background);
        if (centered == null)
        {
            centered = Style(17, Color.white, FontStyle.Bold);
            centered.alignment = TextAnchor.MiddleCenter;
            badge = Style(13, new Color(.62f, .77f, .83f), FontStyle.Bold);
            badge.alignment = TextAnchor.MiddleCenter;
        }
        DrawShip();
        GUI.Label(new Rect(76, 40, 900, 58), "YOU SHALL NOT PASS", title);
        GUI.Label(new Rect(78, 108, 780, 32), "CREW QUARTERS     /     Walk around. Meet your team. Ready up.", small);

        var players = NetworkClient.spawned.Values.Where(n => n != null)
            .Select(n => n.GetComponent<NetworkPlayerLobby>()).Where(n => n != null)
            .OrderBy(n => n.netId).ToArray();
        foreach (var player in players.OrderByDescending(n => n.transform.position.y))
        {
            Vector3 position = player.transform.position;
            DrawCrew(500 + Mathf.Clamp(position.x, -5, 5) * 64,
                390 - Mathf.Clamp(position.y, -2, 2) * 47, player.netId, player.IsReady, player.isLocalPlayer);
        }
        if (players.Length == 0)
        {
            DrawCrew(458, 378, 0, false, false, true);
            DrawCrew(556, 395, 1, false, false, true);
            GUI.Label(new Rect(325, 420, 380, 30), "Create or join a room to board", centered);
        }

        bool connected = NetworkClient.isConnected;
        bool active = NetworkClient.active || NetworkServer.active;
        Rounded(new Rect(948, 155, 275, 410), Panel, 18);
        GUI.Label(new Rect(972, 179, 232, 36), "BOARDING", heading);
        GUI.Label(new Rect(972, 226, 232, 48), connected
            ? (NetworkServer.active ? "HOST / ROOM OPEN" : "CONNECTED")
            : active ? "Connecting..." : "Your crew is waiting.", text);
        GUI.Label(new Rect(972, 292, 220, 24), "HOST IP ADDRESS", small);
        GUI.enabled = !active;
        address = GUI.TextField(new Rect(972, 323, 226, 44), address, input);
        GUI.enabled = true;
        if (Action(new Rect(972, 387, 226, 43), "Create room", !active)) manager.StartHost();
        if (Action(new Rect(972, 441, 226, 43), "Join room", !active && !string.IsNullOrWhiteSpace(address)))
        {
            manager.networkAddress = address.Trim();
            manager.StartClient();
        }
        if (Action(new Rect(972, 502, 226, 40), "Leave / Cancel", active)) Leave();
        int ready = players.Count(n => n.IsReady);
        var local = players.FirstOrDefault(n => n.isLocalPlayer);
        if (Action(new Rect(78, 586, 393, 57), local != null && local.IsReady ? "Cancel ready  [ F ]" : "Ready up  [ F ]", connected && local != null)) local.ToggleReady();
        if (Action(new Rect(500, 586, 397, 57), "Launch game  [ ENTER ]", NetworkServer.active && connected && manager.CanStartGame())) manager.TryStartGame();
        GUI.Label(new Rect(78, 668, 805, 29), $"WASD / ARROWS : MOVE     |     {players.Length} / 4 ON BOARD     |     {ready} READY", small);
        GUI.Label(new Rect(963, 596, 257, 62), "All players ready?\nThe host launches the game.", text);
        DrawNotice();
        GUI.matrix = oldMatrix;
    }
}
