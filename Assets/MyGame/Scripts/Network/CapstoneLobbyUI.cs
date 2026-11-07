using System.Linq;
using Mirror;
using UnityEngine;

// 고정된 네 자리와 서버가 동기화한 준비 상태·시작 시각을 표시하는 시험 로비.
public class CapstoneLobbyUI : MonoBehaviour
{
    private CapstoneRoomManager manager;
    private string roomPassword = "";
    private string directAddress = "";
    private bool localWifi = true;
    public static string PendingConnectionError;
    private string requestedName = "";
    private uint editingPlayerId;
    public bool IsEditingText { get; private set; }
    private GUIStyle title, heading, text, small, button, input;
    private GUIStyle statusTitle, statusDetail, countdownNumber;
    private Font lobbyFont;
    [Header("인게임과 공유하는 로비 그림")]
    [SerializeField] private Sprite skySprite;
    [SerializeField] private Sprite grassSprite;
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float idleFramesPerSecond = 6f;
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
        Box(new Rect(280, 260, 720, 200), Panel);
        Box(new Rect(280, 260, 720, 4), noticeColor);
        noticeHeading.normal.textColor = new Color(noticeColor.r, noticeColor.g, noticeColor.b, pulse);
        Label(new Rect(300, 287, 680, 90), noticeTitle, noticeHeading);
        Label(new Rect(315, 382, 650, 55), noticeDetail, noticeCaption);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }
    private static readonly Color Background = new Color(.76f, .88f, .96f);
    private static readonly Color Ink = new Color(.28f, .23f, .19f);
    private static readonly Color Muted = new Color(.43f, .42f, .34f);
    private static readonly Color Paper = new Color(1f, .97f, .87f);
    private static readonly Color Panel = new Color(1f, .97f, .87f, .96f);
    private static readonly Color Accent = new Color(.30f, .48f, .27f);

    private void Awake()
    {
        manager = GetComponent<CapstoneRoomManager>();
        if (!string.IsNullOrEmpty(PendingConnectionError))
        {
            ShowNotice("ROOM CONNECTION", PendingConnectionError, false);
            PendingConnectionError = null;
        }
    }

    private GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
    {
        return new GUIStyle(GUI.skin.label) { font = lobbyFont, fontSize = size, fontStyle = weight,
            normal = { textColor = color }, wordWrap = true };
    }

    private void InitStyles()
    {
        if (title != null && statusTitle != null && statusDetail != null && countdownNumber != null) return;
        // 한글 카운트다운도 macOS/Windows의 시스템 글꼴로 표시한다.
        if (lobbyFont == null)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            string[] preferred = { "Apple SD Gothic Neo", "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "Arial" };
            string available = preferred.FirstOrDefault(name => installed.Any(fontName =>
                string.Equals(fontName, name, System.StringComparison.OrdinalIgnoreCase)));
            if (available != null) lobbyFont = Font.CreateDynamicFontFromOSFont(available, 24);
        }
        title = Style(42, Ink, FontStyle.Bold);
        noticeHeading = Style(64, Accent, FontStyle.Bold);
        noticeHeading.alignment = TextAnchor.MiddleCenter;
        noticeCaption = Style(20, Muted);
        noticeCaption.alignment = TextAnchor.MiddleCenter;
        heading = Style(24, Ink, FontStyle.Bold);
        text = Style(18, Ink);
        small = Style(14, Muted);
        button = Style(18, Ink, FontStyle.Bold);
        button.alignment = TextAnchor.MiddleCenter;
        input = new GUIStyle(GUI.skin.textField) { font = lobbyFont, fontSize = 20,
            padding = new RectOffset(12, 12, 7, 7), normal = { background = Texture2D.whiteTexture, textColor = Ink },
            focused = { background = Texture2D.whiteTexture, textColor = Ink },
            hover = { background = Texture2D.whiteTexture, textColor = Ink } };
        statusTitle = Style(22, Ink, FontStyle.Bold);
        statusDetail = Style(14, Muted);
        countdownNumber = Style(48, Accent, FontStyle.Bold);
        countdownNumber.alignment = TextAnchor.MiddleCenter;
    }

    private void Box(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private readonly System.Collections.Generic.Dictionary<string, GUIContent> guiContent =
        new System.Collections.Generic.Dictionary<string, GUIContent>();

    // Dynamic OS fonts may rebuild their atlas while drawing. Avoid Unity's shared temporary content.
    private GUIContent Content(string value)
    {
        if (!guiContent.TryGetValue(value, out GUIContent content))
        {
            if (guiContent.Count > 256) guiContent.Clear();
            guiContent[value] = content = new GUIContent(value);
        }
        return content;
    }

    private void Label(Rect rect, string value, GUIStyle style) => GUI.Label(rect, Content(value), style);

    private bool Action(Rect rect, string label, bool enabled = true)
    {
        bool hover = enabled && rect.Contains(Event.current.mousePosition);
        Rounded(new Rect(rect.x, rect.y + 3, rect.width, rect.height), new Color(.38f, .31f, .22f, .23f), 9);
        Rounded(rect, enabled ? new Color(.48f, .48f, .34f) : new Color(.66f, .65f, .56f), 9);
        Rounded(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4),
            hover ? new Color(.86f, .92f, .73f) : enabled ? Paper : new Color(.89f, .88f, .79f), 7);
        GUI.enabled = enabled;
        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        button.normal.textColor = enabled ? Ink : new Color(.58f, .57f, .49f);
        Label(rect, label, button);
        GUI.enabled = true;
        if (clicked) { GUI.FocusControl(null); IsEditingText = false; }
        return clicked;
    }

    private void Leave() => manager.LeaveRoom();

    private Texture2D circle;
    private GUIStyle centered, badge;
    private static readonly Color[] CrewColors = {
        new Color(.37f, .88f, .82f), new Color(1f, .58f, .38f),
        new Color(.66f, .56f, 1f), new Color(1f, .83f, .35f)
    };

    /// <summary>로비와 게임 HUD에서 동일한 플레이어 식별 색상을 사용한다.</summary>
    public static Color GetCrewColor(uint playerId)
    {
        return CrewColors[(int)(playerId % 4)];
    }

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

    /// <summary>인게임과 동일한 스프라이트의 UV를 사용하며 원본 비율을 유지한다.</summary>
    private static void DrawSprite(Rect rect, Sprite sprite, bool fit = false)
    {
        if (sprite == null) return;
        Rect source = sprite.textureRect;
        if (fit)
        {
            float scale = Mathf.Min(rect.width / source.width, rect.height / source.height);
            float width = source.width * scale, height = source.height * scale;
            rect = new Rect(rect.center.x - width * .5f, rect.yMax - height, width, height);
        }
        GUI.DrawTextureWithTexCoords(rect, sprite.texture,
            new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                source.width / sprite.texture.width, source.height / sprite.texture.height), true);
    }

    private void DrawCrew(float x, uint id, bool ready, bool local, string playerName)
    {
        Rounded(new Rect(x - 42, 450, 84, 10), new Color(.27f, .25f, .16f, .16f), 5);
        if (idleFrames != null && idleFrames.Length > 0)
        {
            int frame = (Mathf.FloorToInt(Time.unscaledTime * idleFramesPerSecond) + (int)(id % 3)) % idleFrames.Length;
            DrawSprite(new Rect(x - 63, 310, 126, 150), idleFrames[frame], true);
        }
        string displayName = string.IsNullOrEmpty(playerName) ? "P" + id : playerName;
        centered.fontSize = 17;
        float nameWidth = centered.CalcSize(Content(displayName)).x;
        if (nameWidth > 160) centered.fontSize = Mathf.Clamp((int)(17 * 160 / nameWidth), 8, 17);
        centered.normal.textColor = Ink;
        centered.wordWrap = false;
        Label(new Rect(x - 85, 507, 170, 28), displayName, centered);
        centered.fontSize = 17;
        badge.normal.textColor = ready ? Color.white : Ink;
        Rounded(new Rect(x - 58, 274, 116, 28), ready ? Accent : new Color(.97f, .88f, .64f), 8);
        Label(new Rect(x - 58, 274, 116, 28), ready ? "READY" : "준비 중", badge);
        if (local)
        {
            badge.normal.textColor = Accent;
            Label(new Rect(x - 30, 535, 60, 22), "나", badge);
        }
    }

    private void DrawLandscape()
    {
        Box(new Rect(0, 0, 1280, 720), Background);
        DrawSprite(new Rect(0, 0, 1280, 720), skySprite);
        Box(new Rect(0, 0, 1280, 720), new Color(1f, .99f, .94f, .10f));
        Rounded(new Rect(62, 174, 858, 390), new Color(1f, .98f, .90f, .55f), 24);
        badge.normal.textColor = Muted;
        Label(new Rect(90, 194, 220, 28), "모험의 출발점", badge);
        Label(new Rect(676, 194, 220, 28), "READY로 함께 출발해요", badge);
    }

    /// <summary>빈 자리도 고정 위치에 그리며 서버가 배정한 좌석 번호로 참가자를 연결한다.</summary>
    private void DrawSeats(CapstoneRoomPlayer[] players, bool connected)
    {
        for (int seat = 0; seat < CapstoneRoomManager.LobbySeatCount; seat++)
        {
            float x = 200 + seat * 194;
            var player = players.FirstOrDefault(p => p.SeatIndex == seat);
            bool occupied = player != null;
            // 그림과 캐릭터 모두 인게임의 발판·대기 애니메이션 에셋을 공유한다.
            DrawSprite(new Rect(x - 88, 460, 176, 88), grassSprite);
            if (grassSprite == null) Rounded(new Rect(x - 88, 460, 176, 72), new Color(.62f, .72f, .44f), 8);
            badge.normal.textColor = Muted;
            Label(new Rect(x - 80, 239, 160, 24), $"{seat + 1:00} 번 자리", badge);
            if (occupied) DrawCrew(x, player.netId, player.IsReady, player.isLocalPlayer, player.PlayerName);
            else
            {
                Rounded(new Rect(x - 31, 373, 62, 62), new Color(1f, .98f, .89f, .75f), 20);
                Box(new Rect(x - 12, 402, 24, 3), new Color(.56f, .64f, .46f));
                Box(new Rect(x - 1.5f, 391, 3, 25), new Color(.56f, .64f, .46f));
                badge.normal.textColor = Muted;
                Label(new Rect(x - 80, 507, 160, 26), connected ? "친구를 기다려요" : "빈 자리", badge);
            }
        }
    }

    /// <summary>일시 알림과 달리 카운트다운은 서버 종료 시각이 취소될 때까지 유지한다.</summary>
    private void DrawStartStatus(int playerCount, int readyCount, bool connected)
    {
        bool counting = connected && manager.IsLobbyCountdownActive;
        Rounded(new Rect(500, 579, 397, 76), counting ? new Color(.90f, .96f, .79f) : Panel, 14);
        if (counting)
        {
            int seconds = Mathf.Clamp(Mathf.CeilToInt((float)(manager.LobbyCountdownEndsAt - NetworkTime.time)), 1, 5);
            Label(new Rect(518, 589, 280, 32), "게임을 시작합니다", statusTitle);
            Label(new Rect(518, 625, 285, 22), "준비 해제 시 카운트다운 취소", statusDetail);
            Label(new Rect(809, 584, 70, 62), seconds.ToString(), countdownNumber);
        }
        else
        {
            string label = !connected ? "방을 만들거나 참가해 주세요" :
                playerCount < manager.RequiredLobbyPlayers ? "참가자를 기다리는 중" : "전원 READY를 기다리는 중";
            Label(new Rect(518, 589, 360, 32), label, statusTitle);
            Label(new Rect(518, 625, 360, 22), connected
                ? $"{readyCount}/{playerCount} READY · {manager.RequiredLobbyPlayers}명 이상, 전원 준비 시 자동 시작"
                : "2~4명이 함께 준비하는 네 자리 로비입니다.", statusDetail);
        }
    }

    private void OnDestroy()
    {
        if (circle != null) Destroy(circle);
        if (lobbyFont != null) Destroy(lobbyFont);
    }

    private void OnGUI()
    {
        if (manager == null) return;
        InitStyles();
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        if (!manager.IsLobby)
        {
            IsEditingText = false;
            if (Action(new Rect(1100, 20, 160, 40), "Leave game")) Leave();
            DrawNotice();
            GUI.matrix = oldMatrix;
            return;
        }

        Box(new Rect(0, 0, 1280, 720), Background);
        if (centered == null)
        {
            centered = Style(17, Ink, FontStyle.Bold);
            centered.alignment = TextAnchor.MiddleCenter;
            badge = Style(13, Muted, FontStyle.Bold);
            badge.alignment = TextAnchor.MiddleCenter;
        }
        DrawLandscape();
        Label(new Rect(76, 40, 900, 58), "세상에서 가장 더러운 게임", title);
        Label(new Rect(78, 108, 780, 32), "구름 아래 출발점  ·  네 자리에 모여 함께 준비해요", small);

        var players = NetworkClient.spawned.Values.Where(n => n != null)
            .Select(n => n.GetComponent<CapstoneRoomPlayer>()).Where(n => n != null)
            .OrderBy(n => n.netId).ToArray();
        bool connected = NetworkClient.isConnected;
        DrawSeats(players, connected);
        bool searching = manager.Lan != null && manager.Lan.Searching;
        bool active = NetworkClient.active || NetworkServer.active || searching;
        // 개발자 시험용이라 에디터와 Development Build에서만 보인다.
        if (Debug.isDebugBuild && Action(new Rect(948, 100, 275, 40), "혼자 시험하기 (DEV)", !active))
            manager.BeginSoloTest();
        Rounded(new Rect(948, 155, 275, 528), Panel, 18);
        Label(new Rect(972, 179, 232, 36), connected ? "우리들의 로비" : "모험 준비", heading);
        Label(new Rect(972, 226, 232, 48), connected
            ? (NetworkServer.active ? (!(manager.transport is PasswordRelayTransport) || ((PasswordRelayTransport)manager.transport).RoomOpen ? "방장 · 참가를 기다리는 중" : "방을 여는 중...") : "방에 참가했어요")
            : searching ? "가까운 방을 찾는 중..." : active ? "연결하는 중..." : "친구들과 함께 모험을 준비해요.", text);
        if (Action(new Rect(972, 286, 107, 34), localWifi ? "LAN ✓" : "LAN", !active)) localWifi = true;
        if (Action(new Rect(1091, 286, 107, 34), localWifi ? "인터넷" : "인터넷 ✓", !active)) localWifi = false;
        if (localWifi)
        {
            Label(new Rect(972, 330, 226, 55), "같은 내부망에서 자동 검색합니다.\n유선·Wi-Fi 모두 사용할 수 있어요.", small);
        }
        else
        {
            GUI.enabled = !active;
            GUI.SetNextControlName("RoomPassword");
            roomPassword = GUI.PasswordField(new Rect(972, 331, 226, 43), roomPassword, '*', 64, input);
            GUI.enabled = true;
        }
        if (Action(new Rect(972, 387, 226, 43), "방 만들기", !active))
        {
            if (localWifi) manager.BeginLanRoom(true); else manager.BeginRoom(true, roomPassword);
        }
        if (Action(new Rect(972, 441, 226, 43), localWifi ? "방 찾아 참가" : "방 참가", !active && (localWifi || roomPassword.Length >= 8)))
        {
            if (localWifi) manager.BeginLanRoom(false); else manager.BeginRoom(false, roomPassword);
        }
        if (Action(new Rect(972, 502, 226, 40), "나가기 / 취소", active)) Leave();
        int ready = players.Count(n => n.IsReady);
        var local = players.FirstOrDefault(n => n.isLocalPlayer);
        if (local != null && editingPlayerId != local.netId)
        {
            editingPlayerId = local.netId;
            requestedName = local.PlayerName;
        }
        if (local == null) editingPlayerId = 0;
        Label(new Rect(972, 564, 226, 22), "플레이어 이름 · 최대 16자", small);
        GUI.enabled = local != null;
        GUI.SetNextControlName("PlayerName");
        requestedName = GUI.TextField(new Rect(972, 591, 226, 38), requestedName, 16, input);
        GUI.enabled = true;
        if (Action(new Rect(972, 640, 226, 30), "이름 저장", local != null))
            local.RequestName(requestedName);
        if (localWifi)
        {
            Rounded(new Rect(78, 661, 819, 47), Panel, 10);
            if (NetworkServer.active)
                Label(new Rect(92, 666, 790, 37), "방장 IP: " + manager.Lan.LocalAddresses, small);
            else if (!connected)
            {
                Label(new Rect(92, 668, 105, 32), "IP 직접 참가", small);
                GUI.enabled = !active;
                GUI.SetNextControlName("DirectAddress");
                directAddress = GUI.TextField(new Rect(199, 668, 180, 32), directAddress, 15, input);
                GUI.enabled = true;
                if (Action(new Rect(388, 668, 105, 32), "IP로 참가", !active)) manager.BeginDirectRoom(directAddress);
                Label(new Rect(507, 664, 375, 42), string.IsNullOrEmpty(manager.LastConnectionError)
                    ? "검색되지 않으면 방장의 IPv4를 입력하세요."
                    : manager.LastConnectionError, small);
            }
            else Label(new Rect(92, 666, 790, 37), "방에 연결되었습니다.", small);
        }
        string focused = GUI.GetNameOfFocusedControl();
        IsEditingText = focused == "PlayerName" || focused == "RoomPassword" || focused == "DirectAddress";
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            GUI.FocusControl(null);
            IsEditingText = false;
        }
        if (Action(new Rect(78, 586, 393, 57), local != null && local.IsReady ? "준비 취소  [ F ]" : "준비 완료  [ F ]", connected && local != null)) local.ToggleReady();
        DrawStartStatus(players.Length, ready, connected);
        if (!localWifi) Label(new Rect(78, 668, 820, 29), $"참가 {players.Length} / {CapstoneRoomManager.LobbySeatCount}명     ·     준비 완료 {ready}명     ·     F : 준비 / 취소", small);

        if (searching && manager.Lan.Rooms.Length > 1)
        {
            var available = manager.Lan.Rooms;
            Rounded(new Rect(280, 225, 620, 335), Panel, 16);
            Label(new Rect(315, 245, 550, 38), "참가할 방을 선택하세요", heading);
            for (int i = 0; i < Mathf.Min(available.Length, 5); i++)
                if (Action(new Rect(315, 290 + i * 46, 550, 38), "Wi-Fi 방 참가 " + (i + 1)))
                    manager.Lan.Join(available[i]);
        }
        DrawNotice();
        GUI.matrix = oldMatrix;
    }
}
