using System.Linq;
using Mirror;
using UnityEngine;

// 직접 IP 및 중계 접속 메뉴와 고정 네 자리 방 대기실을 연결한다.
public class CapstoneLobbyUI : MonoBehaviour
{
    private CapstoneRoomManager manager;
    private string directAddress = "";
    private bool useRelay;
    private string relayAddress;
    private string roomPassword = "";
    private bool showLocalAddresses;
    private string copiedAddress;
    public static string PendingConnectionError;
    private string requestedName = "";
    private uint editingPlayerId;
    private bool isEditingText;
    private bool ownsIMEMode;
    private IMECompositionMode previousIMEMode;
    public bool IsEditingText
    {
        get => isEditingText;
        private set
        {
            isEditingText = value;
            // GUI.TextField는 기존 입력 API의 조합 모드를 사용한다.
            // 새 Input System의 SetIMEEnabled만 켜면 IMGUI의 Auto/Off가 남을 수 있다.
            if (value)
            {
                if (!ownsIMEMode)
                {
                    previousIMEMode = UnityEngine.Input.imeCompositionMode;
                    ownsIMEMode = true;
                }
                if (UnityEngine.Input.imeCompositionMode != IMECompositionMode.On)
                    UnityEngine.Input.imeCompositionMode = IMECompositionMode.On;
            }
            else if (ownsIMEMode)
            {
                UnityEngine.Input.imeCompositionMode = previousIMEMode;
                ownsIMEMode = false;
            }
        }
    }
    private GUIStyle title, heading, text, small, button, input;
    private GUIStyle menuTitle, menuSubtitle;
    private string gameTitle;
    private string previousScreen;
    private readonly System.Collections.Generic.List<CapstoneRoomPlayer> roomPlayers =
        new System.Collections.Generic.List<CapstoneRoomPlayer>(CapstoneRoomManager.LobbySeatCount);
    private static readonly System.Comparison<CapstoneRoomPlayer> CompareRoomPlayers =
        (left, right) => left.netId.CompareTo(right.netId);
    private int roomRosterFrame = -1;
    [SerializeField] private Texture2D mainMenuBackground;
    [SerializeField] private LobbyTheme[] lobbyThemes = System.Array.Empty<LobbyTheme>();
    public string CurrentScreen => manager != null && !manager.IsLobby ? "Game" :
        manager != null && !manager.IsSoloTest && !manager.IsOpeningRelayRoom &&
        NetworkClient.isConnected && NetworkClient.localPlayer != null &&
        NetworkClient.localPlayer.GetComponent<CapstoneRoomPlayer>() != null ? "Room" : "Main";
    private GUIStyle statusTitle, statusDetail, countdownNumber;
    private Font lobbyFont;
    [Header("로비 · Stage1 공통 글꼴")]
    [SerializeField] private Font stageFont;
    private Font styleFont;
    [Header("인게임과 공유하는 로비 그림")]
    [SerializeField] private Sprite skySprite;
    [SerializeField] private Sprite grassSprite;
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float idleFramesPerSecond = 6f;
    [Header("중앙 알림")]
    [SerializeField] private float noticeDuration = 2.4f;
    private string noticeTitle, noticeDetail;
    private bool noticeSuccess;
    private float noticeStartedAt = float.NegativeInfinity;
    private GUIStyle noticeHeading, noticeCaption;

    // 실시간을 사용하므로 게임 시간이 멈춰 있어도 알림이 사라진다.
    public void ShowNotice(string message, string detail, bool success)
    {
        noticeTitle = message;
        noticeDetail = detail;
        noticeSuccess = success;
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
        Color noticeColor = noticeSuccess
            ? (visibleTheme != null ? new Color(.52f, .81f, .44f) : Accent)
            : new Color(1f, .7f, .32f);
        Box(new Rect(280, 260, 720, 200), Surface);
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

    [System.Serializable]
    private sealed class LobbyTheme
    {
        public string id;
        public string displayName;
        public Texture2D scenes;
        [Range(0f, 1f)] public float previewGround = .64f;
        public Color panel;
        public Color border;
        public Color primary;
        public Color selected;
        public Color platform;
    }

    private const string ThemePreference = "Capstone.LobbyTheme";
    private LobbyTheme selectedTheme;
    private LobbyTheme visibleTheme;
    private LobbyTheme styleTheme;
    private bool showThemes;
    private static readonly Rect LobbyFrame = new Rect(0, 0, 1280, 720);
    private static readonly Color DarkOutline = new Color(.09f, .06f, .12f);
    private Color Foreground => visibleTheme != null ? new Color(1f, .94f, .83f) : Ink;
    private Color SecondaryText => visibleTheme != null ? new Color(.88f, .82f, .77f) : Muted;
    private Color Surface => visibleTheme != null ? visibleTheme.panel : Panel;
    private Color Border => visibleTheme != null ? visibleTheme.border : new Color(.48f, .48f, .34f);
    private Color Selection => visibleTheme != null ? visibleTheme.selected : new Color(.75f, .84f, .65f);
    private Color Primary => visibleTheme != null ? visibleTheme.primary : new Color(1f, .76f, .41f);

    private void Awake()
    {
        manager = GetComponent<CapstoneRoomManager>();
        gameTitle = Application.productName;
        string savedTheme = PlayerPrefs.GetString(ThemePreference, "castle");
        selectedTheme = System.Array.Find(lobbyThemes, theme =>
            theme != null && theme.scenes != null && theme.id == savedTheme);
        if (selectedTheme == null)
            selectedTheme = System.Array.Find(lobbyThemes, theme => theme != null && theme.scenes != null);
        if (!string.IsNullOrEmpty(PendingConnectionError))
        {
            ShowNotice("ROOM CONNECTION", PendingConnectionError, false);
            PendingConnectionError = null;
        }
    }

    private GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
    {
        return new GUIStyle(GUI.skin.label) { font = styleFont, fontSize = size,
            fontStyle = stageFont != null && styleFont == stageFont ? FontStyle.Normal : weight,
            normal = { textColor = color }, wordWrap = true };
    }

    private void InitStyles()
    {
        // 한글 카운트다운도 macOS/Windows의 시스템 글꼴로 표시한다.
        if (lobbyFont == null)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            string[] preferred = { "Apple SD Gothic Neo", "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "Arial" };
            string available = preferred.FirstOrDefault(name => installed.Any(fontName =>
                string.Equals(fontName, name, System.StringComparison.OrdinalIgnoreCase)));
            if (available != null) lobbyFont = Font.CreateDynamicFontFromOSFont(available, 24);
        }
        Font requestedFont = stageFont != null ? stageFont : lobbyFont;
        if (title != null && styleFont == requestedFont && styleTheme == visibleTheme)
            return;
        styleFont = requestedFont;
        styleTheme = visibleTheme;
        // 글꼴 또는 로비 테마가 바뀔 때만 스타일을 다시 만든다.
        centered = null;
        badge = null;
        title = Style(42, Foreground, FontStyle.Bold);
        menuTitle = Style(66, Foreground, FontStyle.Bold);
        menuSubtitle = Style(20, Foreground);
        noticeHeading = Style(64, Accent, FontStyle.Bold);
        noticeHeading.alignment = TextAnchor.MiddleCenter;
        noticeCaption = Style(20, SecondaryText);
        noticeCaption.alignment = TextAnchor.MiddleCenter;
        heading = Style(24, Foreground, FontStyle.Bold);
        text = Style(18, Foreground);
        small = Style(14, SecondaryText);
        button = Style(18, Ink, FontStyle.Bold);
        button.alignment = TextAnchor.MiddleCenter;
        input = new GUIStyle(GUI.skin.textField) { font = styleFont, fontSize = 20,
            padding = new RectOffset(12, 12, 7, 7), normal = { background = Texture2D.whiteTexture, textColor = Ink },
            focused = { background = Texture2D.whiteTexture, textColor = Ink },
            hover = { background = Texture2D.whiteTexture, textColor = Ink } };
        statusTitle = Style(22, Foreground, FontStyle.Bold);
        statusDetail = Style(14, SecondaryText);
        countdownNumber = Style(48, visibleTheme != null ? Primary : Accent, FontStyle.Bold);
        countdownNumber.alignment = TextAnchor.MiddleCenter;
    }

    private void Box(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = ToGuiColor(color);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private Color ToGuiColor(Color color)
        => visibleTheme != null && QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

    private void DrawMenuShade(Rect rect, bool darkAtTop, float opacity)
    {
        if (menuShade == null)
        {
            menuShade = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < 64; y++)
                menuShade.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, y / 63f)));
            menuShade.Apply(false, true);
        }

        Color old = GUI.color;
        Color shade = DarkOutline;
        shade.a = opacity;
        GUI.color *= ToGuiColor(shade);
        var uv = darkAtTop ? new Rect(0, 0, 1, 1) : new Rect(0, 1, 1, -1);
        GUI.DrawTextureWithTexCoords(rect, menuShade, uv);
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

    private bool Action(Rect rect, string label, bool enabled = true, Color? fillColor = null)
    {
        bool hover = enabled && rect.Contains(Event.current.mousePosition);
        Rounded(new Rect(rect.x, rect.y + 3, rect.width, rect.height), new Color(.38f, .31f, .22f, .23f), 9);
        Color fill = fillColor ?? (visibleTheme != null ? Surface : Paper);
        Color disabled = visibleTheme != null ? Color.Lerp(Surface, Border, .16f) : new Color(.89f, .88f, .79f);
        Rounded(rect, enabled ? Border : Color.Lerp(Border, disabled, .55f), 9);
        Rounded(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4),
            hover ? Color.Lerp(fill, Color.white, .14f) : enabled ? fill : disabled, 7);
        GUI.enabled = enabled;
        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        button.normal.textColor = enabled
            ? (fillColor.HasValue ? Ink : Foreground)
            : (visibleTheme != null ? SecondaryText : new Color(.58f, .57f, .49f));
        Label(rect, label, button);
        GUI.enabled = true;
        if (clicked) { GUI.FocusControl(null); IsEditingText = false; }
        return clicked;
    }

    private void Leave() => manager.LeaveRoom();

    private Texture2D circle;
    private Texture2D menuShade;
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
        radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * .5f);
        float middleWidth = rect.width - 2f * radius;
        // Paint each section once, so translucent borders do not stack at the corners.
        Box(new Rect(rect.x + radius, rect.y, middleWidth, radius), color);
        Box(new Rect(rect.x, rect.y + radius, rect.width, rect.height - 2f * radius), color);
        Box(new Rect(rect.x + radius, rect.yMax - radius, middleWidth, radius), color);
        Color old = GUI.color;
        GUI.color *= ToGuiColor(color);
        GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y, radius, radius), circle, new Rect(0f, .5f, .5f, .5f));
        GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - radius, rect.y, radius, radius), circle, new Rect(.5f, .5f, .5f, .5f));
        GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.yMax - radius, radius, radius), circle, new Rect(0f, 0f, .5f, .5f));
        GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - radius, rect.yMax - radius, radius, radius), circle, new Rect(.5f, 0f, .5f, .5f));
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

    private PlayerIdleBoil characterLook;

    /// <summary>
    /// 이 PC에서 고른 캐릭터. -1이면 아직 안 골라서 자리 순서대로 기본 캐릭터를 받는다.
    /// 방에 들어가면 자동으로 이 값을 요청하고, 1인 시험은 방장 PC이므로 이 값을 바로 쓴다.
    /// </summary>
    public static int PreferredCharacter = -1;

    /// <summary>입장 전 캐릭터를 이름으로 골라 메인 미리보기와 게임에 사용한다.</summary>
    private void DrawCharacterPicker(bool enabled)
    {
        Label(new Rect(560, 73, 340, 23), "캐릭터 선택", small);
        for (int index = 0; index < PlayerIdleBoil.CharacterCount; index++)
        {
            var style = (PlayerIdleBoil.CharacterStyle)index;
            Color? fill = PreferredCharacter == index ? Selection : null;
            if (Action(new Rect(560 + index * 116, 100, 108, 42), PlayerIdleBoil.GetDisplayName(style), enabled, fill))
                PreferredCharacter = index;
        }
    }

    /// <summary>대기실에서는 자신의 캐릭터만 고르며 준비를 마치면 선택을 잠근다.</summary>
    private void DrawRoomCharacterPicker(CapstoneRoomPlayer local)
    {
        Label(new Rect(972, 270, 226, 23), "내 캐릭터 선택", small);
        int previousFontSize = button.fontSize;
        button.fontSize = 14;
        for (int index = 0; index < PlayerIdleBoil.CharacterCount; index++)
        {
            var style = (PlayerIdleBoil.CharacterStyle)index;
            Color? fill = local != null && local.Character == style ? Selection : null;
            if (Action(new Rect(972 + index * 76, 298, 70, 32), PlayerIdleBoil.GetDisplayName(style),
                local != null && !local.IsReady, fill))
            {
                PreferredCharacter = index;
                local.RequestCharacter(index);
            }
        }
        button.fontSize = previousFontSize;
    }

    /// <summary>게임 플레이어 프리팹의 캐릭터별 대기 그림을 그대로 쓴다. 없으면 로비에 지정한 그림을 쓴다.</summary>
    private Sprite[] GetCrewFrames(PlayerIdleBoil.CharacterStyle style)
    {
        if (characterLook == null && manager != null && manager.playerPrefab != null)
            characterLook = manager.playerPrefab.GetComponent<PlayerIdleBoil>();
        Sprite[] frames = characterLook != null ? characterLook.GetIdleFrames(style) : null;
        return frames != null && frames.Length > 0 ? frames : idleFrames;
    }

    private void DrawCrew(float x, CapstoneRoomPlayer player)
    {
        uint id = player.netId;
        bool ready = player.IsReady, local = player.isLocalPlayer;
        string playerName = player.PlayerName;
        Rounded(new Rect(x - 42, 450, 84, 10), new Color(.27f, .25f, .16f, .16f), 5);
        Sprite[] crewFrames = GetCrewFrames(player.Character);
        if (crewFrames != null && crewFrames.Length > 0)
        {
            int frame = (Mathf.FloorToInt(Time.unscaledTime * idleFramesPerSecond) + (int)(id % 3)) % crewFrames.Length;
            DrawSprite(new Rect(x - 63, 310, 126, 150), crewFrames[frame], true);
        }
        string displayName = string.IsNullOrEmpty(playerName) ? "P" + id : playerName;
        centered.fontSize = 17;
        float nameWidth = centered.CalcSize(Content(displayName)).x;
        if (nameWidth > 160) centered.fontSize = Mathf.Clamp((int)(17 * 160 / nameWidth), 8, 17);
        centered.normal.textColor = Ink;
        centered.wordWrap = false;
        // 이름은 캐릭터를 가리지 않도록 발판 흙 부분의 중상단에 배치한다.
        Color nameplate = visibleTheme != null ? Primary : new Color(1f, .97f, .87f, .94f);
        Rounded(new Rect(x - 88, 476, 176, 31), nameplate, 9);
        Label(new Rect(x - 85, 477, 170, 28), displayName, centered);
        centered.fontSize = 17;
        badge.normal.textColor = ready ? Color.white : Ink;
        Rounded(new Rect(x - 58, 274, 116, 28), ready ? Accent : new Color(.97f, .88f, .64f), 8);
        Label(new Rect(x - 58, 274, 116, 28), ready ? "READY" : "준비 중", badge);
        string characterName = PlayerIdleBoil.GetDisplayName(player.Character);
        badge.normal.textColor = visibleTheme != null
            ? (local ? Selection : Foreground) : (local ? Accent : Muted);
        Label(new Rect(x - 80, 512, 160, 22), local ? "나 · " + characterName : characterName, badge);
    }

    private void DrawLandscape()
    {
        if (visibleTheme != null)
        {
            DrawThemeScene(LobbyFrame, visibleTheme, true);
            DrawMenuShade(new Rect(0, 0, 1280, 172), true, .39f);
            return;
        }

        Box(LobbyFrame, Background);
        DrawSprite(LobbyFrame, skySprite);
        Box(LobbyFrame, new Color(1f, .99f, .94f, .10f));
        Rounded(new Rect(62, 174, 858, 390), new Color(1f, .98f, .90f, .55f), 24);
        badge.normal.textColor = SecondaryText;
        Label(new Rect(90, 194, 220, 28), "모험의 출발점", badge);
        Label(new Rect(676, 194, 220, 28), "READY로 함께 출발해요", badge);
    }

    /// <summary>빈 자리도 고정 위치에 그리며 서버가 배정한 좌석 번호로 참가자를 연결한다.</summary>
    private void DrawSeats(System.Collections.Generic.IReadOnlyList<CapstoneRoomPlayer> players, bool connected, CapstoneRoomPlayer local)
    {
        for (int seat = 0; seat < CapstoneRoomManager.LobbySeatCount; seat++)
        {
            float x = 200 + seat * 194;
            CapstoneRoomPlayer player = null;
            for (int index = 0; index < players.Count; index++)
                if (players[index] != null && players[index].SeatIndex == seat)
                {
                    player = players[index];
                    break;
                }
            bool occupied = player != null;
            if (visibleTheme != null)
            {
                Rounded(new Rect(x - 90, 459, 180, 90), Border, 9);
                Rounded(new Rect(x - 88, 461, 176, 86), visibleTheme.platform, 7);
                Box(new Rect(x - 81, 463, 162, 4), Color.Lerp(visibleTheme.platform, Border, .63f));
            }
            else if (grassSprite != null) DrawSprite(new Rect(x - 88, 460, 176, 88), grassSprite);
            else Rounded(new Rect(x - 88, 460, 176, 72), new Color(.62f, .72f, .44f), 8);
            badge.normal.textColor = SecondaryText;
            if (visibleTheme != null)
                Rounded(new Rect(x - 55, 239, 110, 24), Surface, 7);
            Label(new Rect(x - 80, 239, 160, 24), $"{seat + 1:00} 번 자리", badge);
            if (occupied)
            {
                DrawCrew(x, player);
                if (local != null && manager.CanKickLobbyPlayer(player) &&
                    Action(new Rect(x + 58, 316, 28, 28), "×", true, new Color(1f, .82f, .76f)))
                    local.RequestKick(player.netId);
            }
            else
            {
                Color emptySeat = visibleTheme != null ? Surface : new Color(1f, .98f, .89f, .75f);
                Color plus = visibleTheme != null ? Primary : new Color(.56f, .64f, .46f);
                Rounded(new Rect(x - 31, 373, 62, 62), emptySeat, 20);
                Box(new Rect(x - 12, 402, 24, 3), plus);
                Box(new Rect(x - 1.5f, 391, 3, 25), plus);
                badge.normal.textColor = SecondaryText;
                Label(new Rect(x - 80, 507, 160, 26), connected ? "친구를 기다려요" : "빈 자리", badge);
            }
        }
    }

    /// <summary>일시 알림과 달리 카운트다운은 서버 종료 시각이 취소될 때까지 유지한다.</summary>
    private void DrawStartStatus(int playerCount, int readyCount, bool connected)
    {
        bool counting = connected && manager.IsLobbyCountdownActive;
        Color statusBackground = visibleTheme != null || !counting ? Surface : new Color(.90f, .96f, .79f);
        Rounded(new Rect(500, 579, 397, 76), statusBackground, 14);
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

    private void OnDisable()
    {
        IsEditingText = false;
    }

    private void OnDestroy()
    {
        if (circle != null) Destroy(circle);
        if (menuShade != null) Destroy(menuShade);
        if (lobbyFont != null) Destroy(lobbyFont);
    }

    private void OnGUI()
    {
        if (DeathSequence.IsUIHidden || manager == null)
        {
            IsEditingText = false;
            return;
        }
        string screen = CurrentScreen;
        visibleTheme = screen == "Game" ? null : selectedTheme;
        InitStyles();
        if (screen != previousScreen)
        {
            previousScreen = screen;
            roomRosterFrame = -1;
            roomPlayers.Clear();
            showLocalAddresses = false;
            showThemes = false;
            copiedAddress = null;
            GUI.FocusControl(null);
            IsEditingText = false;
        }
        Matrix4x4 oldMatrix = GUI.matrix;
        // Keep the same 16:9 composition on windowed, ultrawide and 4:3 screens.
        if (screen != "Game")
            Box(new Rect(0, 0, Screen.width, Screen.height), visibleTheme != null ? DarkOutline : Background);
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) * .5f,
            (Screen.height - 720 * scale) * .5f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        if (screen == "Game")
        {
            IsEditingText = false;
            if (Action(new Rect(1100, 20, 160, 40), "로비로 나가기")) Leave();
            DrawNotice();
            GUI.matrix = oldMatrix;
            return;
        }
        if (centered == null)
        {
            centered = Style(17, Foreground, FontStyle.Bold);
            centered.alignment = TextAnchor.MiddleCenter;
            badge = Style(13, SecondaryText, FontStyle.Bold);
            badge.alignment = TextAnchor.MiddleCenter;
        }
        if (screen == "Main") DrawMainMenu();
        else DrawRoomLobby();
        if (showLocalAddresses) DrawLocalAddresses();
        if (showThemes && screen == "Main") DrawThemePicker();
        DrawNotice();
        string focused = GUI.GetNameOfFocusedControl();
        IsEditingText = focused == "PlayerName" || focused == "DirectAddress" ||
            focused == "RelayAddress" || focused == "RoomPassword";
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            showLocalAddresses = false;
            showThemes = false;
            GUI.FocusControl(null);
            IsEditingText = false;
        }
        GUI.matrix = oldMatrix;
    }

    public void ToggleLocalAddressPanel()
    {
        showLocalAddresses = !showLocalAddresses;
        showThemes = false;
        if (showLocalAddresses) { manager.Lan.RefreshLocalAddresses(); copiedAddress = null; }
    }

    private void DrawMenuTitle(Rect rect, string label)
    {
        menuTitle.normal.textColor = visibleTheme != null ? DarkOutline : Paper;
        Label(new Rect(rect.x - 3, rect.y, rect.width, rect.height), label, menuTitle);
        Label(new Rect(rect.x + 3, rect.y, rect.width, rect.height), label, menuTitle);
        Label(new Rect(rect.x, rect.y - 3, rect.width, rect.height), label, menuTitle);
        Label(new Rect(rect.x, rect.y + 3, rect.width, rect.height), label, menuTitle);
        menuTitle.normal.textColor = Foreground;
        Label(rect, label, menuTitle);
    }

    private void DrawMainMenu()
    {
        if (visibleTheme != null)
        {
            DrawThemeScene(LobbyFrame, visibleTheme, false);
            DrawMenuShade(new Rect(0, 0, 1280, 314), true, .45f);
            DrawMenuShade(new Rect(0, 642, 1280, 78), false, .8f);
        }
        else if (mainMenuBackground != null)
            GUI.DrawTexture(LobbyFrame, mainMenuBackground, ScaleMode.StretchToFill);
        else
            DrawSprite(LobbyFrame, skySprite);
        DrawMenuTitle(new Rect(72, 50, 480, 88), gameTitle);
        Label(new Rect(78, 139, 630, 40), "친구들과 함께, 과연 끝까지 지나갈 수 있을까?", menuSubtitle);
        var selectedStyle = (PlayerIdleBoil.CharacterStyle)Mathf.Max(0, PreferredCharacter);
        Sprite[] previewFrames = GetCrewFrames(selectedStyle);
        if (previewFrames != null && previewFrames.Length > 0)
        {
            int frame = Mathf.FloorToInt(Time.unscaledTime * idleFramesPerSecond) % previewFrames.Length;
            float feet = visibleTheme != null ? visibleTheme.previewGround * LobbyFrame.height : 459f;
            DrawSprite(new Rect(302, feet - 151f, 118, 151), previewFrames[frame], true);
        }
        if (selectedTheme != null && Action(new Rect(867, 34, 168, 38), showThemes ? "테마 닫기" : "테마 선택"))
            ToggleThemePicker();
        if (Action(new Rect(1045, 34, 165, 38), showLocalAddresses ? "내 IP 닫기" : "내 IP 보기"))
            ToggleLocalAddressPanel();
        bool busy = NetworkClient.active || NetworkServer.active || manager.IsCheckingRelay ||
            (manager.Lan != null && manager.Lan.Searching);
        DrawCharacterPicker(!busy);
        DrawConnectionMode(busy);
        Rounded(new Rect(68, 552, 1148, 112), new Color(.30f, .23f, .15f, .25f), 20);
        Rounded(new Rect(62, 546, 1156, 112), visibleTheme != null ? Border : Ink, 18);
        Rounded(new Rect(65, 549, 1150, 106), visibleTheme != null ? Surface : Paper, 16);
        if (Action(new Rect(87, 582, 159, 50), "방 만들기", !busy, visibleTheme != null ? Primary : new Color(.75f, .82f, .47f)))
        {
            if (useRelay) ConnectViaRelay(true);
            else manager.BeginLanRoom(true);
        }
        var addressField = new Rect(267, 587, 310, 45);
        Label(new Rect(267, 558, 310, 23), useRelay ? "방 비밀번호 · 8~64자" : "호스트 IP", small);
        GUI.enabled = !busy;
        if (useRelay)
        {
            GUI.SetNextControlName("RoomPassword");
            roomPassword = GUI.PasswordField(addressField, roomPassword, '*', 64, input);
        }
        else
        {
            GUI.SetNextControlName("DirectAddress");
            directAddress = GUI.TextField(addressField, directAddress, 15, input);
        }
        GUI.enabled = true;
        if (Action(new Rect(594, 582, 128, 50), "참가", !busy, visibleTheme != null ? Primary : new Color(.75f, .82f, .47f)))
        {
            if (useRelay) ConnectViaRelay(false);
            else manager.BeginDirectRoom(directAddress);
        }
        Box(new Rect(747, 572, 2, 66), new Color(.60f, .49f, .34f, .45f));
        if (Action(new Rect(775, 582, 415, 50), "테스트 게임 실행", !busy, visibleTheme != null ? null : Primary))
            manager.BeginSoloTest();
        string status = manager.LastConnectionError;
        if (manager.IsCheckingRelay) status = "중계 서버 응답을 기다리는 중… 무료 서버는 첫 연결에 약 1분 걸릴 수 있어요.";
        else if (manager.IsOpeningRelayRoom) status = "중계 서버에서 방을 여는 중…";
        else if (manager.Lan != null && manager.Lan.Searching) status = "방을 찾고 있어요…";
        else if (manager.IsSoloTest) status = "테스트 게임을 준비하는 중…";
        else if (NetworkServer.active) status = "방을 만들고 있어요…";
        else if (NetworkClient.active) status = useRelay ? "중계 방에 연결하는 중…" : "호스트에 연결하는 중…";
        else if (string.IsNullOrEmpty(status)) status = useRelay
            ? "중계 주소(wss://서버주소/relay)와 같은 방 비밀번호를 입력해 주세요."
            : "방을 만들거나, 친구의 호스트 IP로 참가해 주세요.";
        Label(new Rect(78, 673, 925, 36), status, small);
        if (busy && Action(new Rect(1045, 672, 165, 32), "연결 취소")) Leave();
    }

    private void DrawConnectionMode(bool busy)
    {
        if (Action(new Rect(87, 497, 142, 39), "직접 IP", !busy, useRelay ? null : Selection))
        {
            useRelay = false;
            roomPassword = "";
        }
        if (Action(new Rect(241, 497, 142, 39), "중계 서버", !busy, useRelay ? Selection : null))
            useRelay = true;
        if (!useRelay) return;

        Rounded(new Rect(419, 497, 771, 39), Surface, 10);
        Label(new Rect(431, 497, 119, 39), "중계 주소", small);
        GUI.enabled = !busy;
        GUI.SetNextControlName("RelayAddress");
        relayAddress = GUI.TextField(new Rect(550, 500, 632, 33),
            relayAddress ?? manager.DefaultRelayEndpoint, 256, input);
        GUI.enabled = true;
    }

    private void DrawThemePicker()
    {
        var outer = new Rect(916, 82, 296, 326);
        Rounded(outer, Border, 14);
        Rounded(new Rect(918, 84, 292, 322), Surface, 12);
        Label(new Rect(934, 94, 262, 34), "테마 선택", heading);
        float row = 136;
        foreach (LobbyTheme theme in lobbyThemes)
        {
            if (theme == null || theme.scenes == null) continue;
            bool selected = theme == selectedTheme;
            var option = new Rect(932, row, 264, 60);
            bool choose = Action(option, "", true, selected ? Selection : null);
            DrawThemeScene(new Rect(940, row + 8, 77, 44), theme, false);
            text.normal.textColor = selected ? Ink : Foreground;
            Label(new Rect(1032, row + 16, 153, 31), theme.displayName, text);
            text.normal.textColor = Foreground;
            if (choose) SelectTheme(theme);
            row += 65;
        }
    }

    private void ToggleThemePicker()
    {
        showThemes = !showThemes;
        showLocalAddresses = false;
    }

    private void SelectTheme(LobbyTheme theme)
    {
        if (selectedTheme != theme)
        {
            selectedTheme = theme;
            PlayerPrefs.SetString(ThemePreference, theme.id);
            PlayerPrefs.Save();
        }
        showThemes = false;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal string TestLobbyTheme => selectedTheme?.id;

    internal bool TrySelectTestTheme(string id)
    {
        LobbyTheme theme = System.Array.Find(lobbyThemes, candidate =>
            candidate != null && candidate.id == id && candidate.scenes != null);
        if (theme == null) return false;
        SelectTheme(theme);
        return true;
    }

    internal void ToggleTestThemePicker() => ToggleThemePicker();
#endif

    // Each texture contains the entrance on the left and the waiting area on the right.
    private static void DrawThemeScene(Rect destination, LobbyTheme theme, bool room)
    {
        Texture2D texture = theme.scenes;
        float halfWidth = (texture.width - 2f) * .5f;
        float sourceRatio = halfWidth / texture.height;
        float targetRatio = destination.width / destination.height;
        float width = Mathf.Min(1f, targetRatio / sourceRatio);
        float height = Mathf.Min(1f, sourceRatio / targetRatio);
        float x = (room ? .5f : 0f) + .5f / texture.width + (1f - width) * .25f;
        float y = (1f - height) * (room ? .5f : 1f - theme.previewGround);
        Color old = GUI.color;
        // The project's linear GUI output lifts the dark midtones in the painted backgrounds.
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            GUI.color *= new Color(.72f, .72f, .72f);
        GUI.DrawTextureWithTexCoords(destination, texture,
            new Rect(x, y, width * halfWidth / texture.width, height));
        GUI.color = old;
    }

    private void ConnectViaRelay(bool host)
    {
        if (manager.BeginRoom(host, relayAddress ?? manager.DefaultRelayEndpoint, roomPassword))
            roomPassword = "";
    }

    private System.Collections.Generic.IReadOnlyList<CapstoneRoomPlayer> CollectRoomPlayers()
    {
        if (roomRosterFrame == Time.frameCount) return roomPlayers;
        roomRosterFrame = Time.frameCount;
        roomPlayers.Clear();
        foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            if (identity != null && identity.TryGetComponent(out CapstoneRoomPlayer player))
                roomPlayers.Add(player);
        roomPlayers.Sort(CompareRoomPlayers);
        return roomPlayers;
    }

    private void DrawRoomLobby()
    {
        DrawLandscape();
        Label(new Rect(76, 40, 900, 58), gameTitle, title);
        Label(new Rect(78, 108, 790, 32), "방 대기실 · 이름을 정하고 READY로 함께 출발해요", small);
        if (!manager.IsRelayRoom && Action(new Rect(948, 100, 275, 40), showLocalAddresses ? "내 IP 닫기" : "내 IP 보기"))
            ToggleLocalAddressPanel();
        var players = CollectRoomPlayers();
        int ready = 0;
        CapstoneRoomPlayer local = null;
        for (int index = 0; index < players.Count; index++)
        {
            var player = players[index];
            if (player == null) continue;
            if (player.IsReady) ready++;
            if (player.isLocalPlayer) local = player;
        }
        DrawSeats(players, true, local);
        if (visibleTheme != null)
            Rounded(new Rect(946, 153, 279, 532), Border, 19);
        Rounded(new Rect(948, 155, 275, 528), Surface, 18);
        Label(new Rect(972, 179, 226, 36), "우리들의 대기실", heading);
        Label(new Rect(972, 231, 226, 34), (NetworkServer.active ? "방장" : "참가자") + $" · {players.Count} / 4명", text);
        DrawRoomCharacterPicker(local);
        if (local != null && editingPlayerId != local.netId)
        {
            editingPlayerId = local.netId;
            requestedName = local.PlayerName;
        }
        Label(new Rect(972, 335, 226, 23), "플레이어 이름 · 최대 16자", small);
        GUI.enabled = local != null;
        GUI.SetNextControlName("PlayerName");
        requestedName = GUI.TextField(new Rect(972, 366, 226, 43), requestedName, 16, input);
        GUI.enabled = true;
        if (Action(new Rect(972, 424, 226, 39), "이름 저장", local != null)) local.RequestName(requestedName);
        Label(new Rect(972, 490, 226, 76), "캐릭터 변경은 준비 해제 후\n2~4명 전원 READY 시\n5초 뒤 시작 · 준비 해제 시 취소", small);
        if (Action(new Rect(972, 600, 226, 43), "방 나가기")) Leave();
        if (Action(new Rect(78, 586, 393, 57), local != null && local.IsReady ? "준비 취소  [ F ]" : "준비 완료  [ F ]",
            local != null, visibleTheme != null ? Primary : null))
            local.ToggleReady();
        DrawStartStatus(players.Count, ready, true);
        string details = $"참가 {players.Count} / 4명 · 준비 완료 {ready}명 · F : 준비 / 취소";
        if (manager.IsRelayRoom) details = "중계 서버 연결됨 · " + details;
        else if (NetworkServer.active) details = "방장 IP: " + manager.Lan.LocalAddresses;
        if (visibleTheme != null)
            Rounded(new Rect(71, 669, 831, 34), new Color(.07f, .055f, .10f, .85f), 8);
        Label(new Rect(78, 668, 820, 35), details, small);
    }

    private void DrawLocalAddresses()
    {
        if (visibleTheme != null)
            Rounded(new Rect(504, 146, 395, 239), Border, 15);
        Rounded(new Rect(506, 148, 391, 235), Surface, 14);
        Label(new Rect(522, 158, 358, 32), "내 IPv4 주소 · 내부망", heading);
        Label(new Rect(522, 194, 358, 38), "방장이 이 주소를 알려주면 상대가 IP로 참가할 수 있어요.", small);
        var addresses = manager.Lan.LocalIPv4Addresses;
        if (addresses.Count == 0)
            Label(new Rect(522, 241, 358, 68), "연결된 IPv4를 찾지 못했습니다.\nWi-Fi 또는 랜선 연결을 확인해 주세요.", small);
        for (int i = 0; i < addresses.Count; i++)
        {
            string address = addresses[i];
            Label(new Rect(522, 236 + i * 34, 245, 30), address, text);
            if (Action(new Rect(779, 236 + i * 34, 101, 29), copiedAddress == address ? "복사됨" : "복사"))
            {
                GUIUtility.systemCopyBuffer = address;
                copiedAddress = address;
            }
        }
        if (Action(new Rect(522, 344, 169, 28), "새로고침"))
        {
            manager.Lan.RefreshLocalAddresses();
            copiedAddress = null;
        }
        if (Action(new Rect(703, 344, 177, 28), "닫기")) showLocalAddresses = false;
    }

}
