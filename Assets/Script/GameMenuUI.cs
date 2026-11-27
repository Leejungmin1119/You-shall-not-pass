using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>자기 화면에서만 열고 닫는 게임 메뉴. 경기 시간이나 다른 참가자는 멈추지 않는다.</summary>
[DefaultExecutionOrder(-200)]
public sealed class GameMenuUI : MonoBehaviour
{
    private static GameMenuUI instance;
    private static int lastInteractionFrame = -1;
    private CapstoneRoomManager manager;
    private RectTransform root;
    private RectTransform gearRoot;
    private RectTransform mainPanel;
    private RectTransform settingsPanel;
    private Font menuFont;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    // 닫기 클릭이 같은 프레임의 아이템 사용으로 이어지지 않게 한다.
    public static bool BlocksGameplayInput => instance != null &&
        (instance.IsOpen || lastInteractionFrame == Time.frameCount);

    private bool CanShow => manager != null && !manager.IsLobby &&
        NetworkClient.active && !DeathSequence.IsUIHidden;

    /// <summary>도메인 재로드를 생략한 에디터에서도 이전 메뉴 상태가 남지 않게 한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        lastInteractionFrame = -1;
    }

    /// <summary>로비 담당이 보유한 이미지와 글꼴로 메뉴를 한 번만 구성한다.</summary>
    public void Initialize(CapstoneRoomManager roomManager, Texture2D board, Font font, Texture2D gearIcon)
    {
        if (root != null)
            return;

        manager = roomManager;
        menuFont = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        instance = this;

        root = CreateCanvas("GameMenuCanvas", 1000);
        CreateGear(gearIcon);

        var shade = MakeRect("InputBlocker", root, Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        var blocker = shade.gameObject.AddComponent<UnityEngine.UI.Image>();
        blocker.color = new Color(0.06f, 0.04f, 0.09f, 0.55f);
        blocker.raycastTarget = true;

        float originalWidth = board != null ? 600f * board.width / board.height : 490f;
        float height = 300f;
        float width = originalWidth * (2f / 3f);
        mainPanel = MakePanel("PaperSign", board, new Vector2(width, height));

        MakeButton("Continue", mainPanel, 65f, "계속하기", Close, true);
        MakeButton("Settings", mainPanel, 0f, "설정", ShowSettings, true);
        MakeButton("LeaveGame", mainPanel, -65f, "게임 나가기", LeaveGame, true);

        settingsPanel = MakePanel("AudioSettings", board, new Vector2(460f, height));
        MakeLabel("Title", settingsPanel, new Vector2(0f, 111f), new Vector2(240f, 34f), "설정", 26);
        MakeVolumeSlider("MasterVolume", 60f, "마스터 볼륨", GameAudioChannel.Master);
        MakeVolumeSlider("MusicVolume", 0f, "배경음", GameAudioChannel.Music);
        MakeVolumeSlider("EffectsVolume", -60f, "효과음", GameAudioChannel.Effects);
        MakeButton("Back", settingsPanel, -110f, "뒤로", ShowMainMenu, true);
        settingsPanel.gameObject.SetActive(false);
    }

    /// <summary>같은 종이와 테두리 이미지를 크기만 다르게 사용한다.</summary>
    private RectTransform MakePanel(string objectName, Texture2D board, Vector2 size)
    {
        var panel = MakeRect(objectName, root, Vector2.zero, size);
        var artwork = panel.gameObject.AddComponent<GameMenuBoardImage>();
        artwork.texture = board;
        artwork.color = board != null ? Color.white : new Color(0.98f, 0.98f, 0.96f);
        artwork.raycastTarget = true;
        return panel;
    }

    /// <summary>메뉴 글꼴과 색상을 공유하는 고정 문구를 만든다.</summary>
    private UnityEngine.UI.Text MakeLabel(string objectName, RectTransform parent, Vector2 position,
        Vector2 size, string caption, int fontSize)
    {
        var rect = MakeRect(objectName, parent, position, size);
        var text = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
        text.font = menuFont;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.25f, 0.18f, 0.28f);
        text.raycastTarget = false;
        text.text = caption;
        return text;
    }

    /// <summary>음량 이름·가로 슬라이더·백분율을 한 줄에 배치하고 실제 소리에 연결한다.</summary>
    private void MakeVolumeSlider(string objectName, float y, string caption, GameAudioChannel channel)
    {
        var row = MakeRect(objectName, settingsPanel, new Vector2(0f, y), new Vector2(420f, 46f));
        var label = MakeLabel("Label", row, new Vector2(-142f, 0f), new Vector2(120f, 42f), caption, 22);
        label.alignment = TextAnchor.MiddleLeft;
        var percentage = MakeLabel("Percentage", row, new Vector2(174f, 0f), new Vector2(52f, 42f), "", 20);

        var rect = MakeRect("Slider", row, new Vector2(35f, 0f), new Vector2(190f, 34f));
        var hitArea = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        hitArea.color = Color.clear;
        var track = MakeRect("Track", rect, Vector2.zero, new Vector2(190f, 3f));
        var trackImage = track.gameObject.AddComponent<UnityEngine.UI.Image>();
        trackImage.color = new Color(0.80f, 0.75f, 0.80f);
        trackImage.raycastTarget = false;

        var fillArea = MakeRect("FillArea", rect, Vector2.zero, new Vector2(190f, 3f));
        var fill = MakeRect("Fill", fillArea, Vector2.zero, fillArea.sizeDelta);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        var fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>();
        fillImage.color = new Color(0.62f, 0.48f, 0.63f);
        fillImage.raycastTarget = false;

        var handleArea = MakeRect("HandleArea", rect, Vector2.zero, new Vector2(190f, 16f));
        // Slider가 세로 앵커를 늘리므로 높이 보정값은 0으로 두어 실제 높이를 16으로 유지한다.
        var handle = MakeRect("Handle", handleArea, Vector2.zero, new Vector2(16f, 0f));
        var handleImage = handle.gameObject.AddComponent<VolumeSliderHandle>();
        handleImage.color = new Color(0.74f, 0.59f, 0.34f);
        var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
        slider.targetGraphic = handleImage;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        float value = GameAudioSettings.GetVolume(channel);
        slider.SetValueWithoutNotify(value);
        percentage.text = Mathf.RoundToInt(value * 100f) + "%";
        slider.onValueChanged.AddListener(volume =>
        {
            GameAudioSettings.SetVolume(channel, volume);
            percentage.text = Mathf.RoundToInt(volume * 100f) + "%";
        });
    }

    /// <summary>가로가 넓은 음량 설정 화면으로 전환한다.</summary>
    private void ShowSettings()
    {
        mainPanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(true);
        InputManager.ClearInputs();
    }

    /// <summary>음량을 저장하고 이전의 계속하기·설정·게임 나가기 화면으로 돌아간다.</summary>
    private void ShowMainMenu()
    {
        GameAudioSettings.Save();
        settingsPanel.gameObject.SetActive(false);
        mainPanel.gameObject.SetActive(true);
        InputManager.ClearInputs();
    }

    /// <summary>해상도에 맞춰 크기가 조정되는 독립 UI Canvas를 만든다.</summary>
    private RectTransform CreateCanvas(string objectName, int order)
    {
        var rect = MakeRect(objectName, transform, Vector2.zero, new Vector2(1280f, 720f));
        rect.gameObject.SetActive(false);
        var canvas = rect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = rect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        rect.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        return rect;
    }

    /// <summary>톱니바퀴도 같은 UI 입력 계통으로 옮겨 클릭이 함정 사용으로 먼저 전달되지 않게 한다.</summary>
    private void CreateGear(Texture2D icon)
    {
        gearRoot = CreateCanvas("GameMenuGearCanvas", 1001);
        var rect = MakeRect("SettingsGear", gearRoot, new Vector2(-8f, -8f), new Vector2(48f, 48f));
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = icon;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        button.onClick.AddListener(Toggle);
    }

    /// <summary>화면 중앙 기준의 메뉴 요소를 만든다.</summary>
    private static RectTransform MakeRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>종이 위의 문구를 클릭 가능한 메뉴 버튼으로 만든다.</summary>
    private void MakeButton(string objectName, RectTransform parent, float y, string caption,
        UnityEngine.Events.UnityAction action, bool interactable)
    {
        var rect = MakeRect(objectName, parent, new Vector2(0f, y), new Vector2(parent.sizeDelta.x * 0.8f, 54f));
        var hitArea = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        hitArea.color = Color.clear;

        var labelRect = MakeRect("Label", rect, Vector2.zero, rect.sizeDelta);
        var label = labelRect.gameObject.AddComponent<UnityEngine.UI.Text>();
        label.font = menuFont;
        label.fontSize = 27;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = caption;
        label.raycastTarget = false;

        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = label;
        button.interactable = interactable;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = new Color(0.25f, 0.18f, 0.28f);
        colors.highlightedColor = new Color(0.56f, 0.38f, 0.58f);
        colors.pressedColor = new Color(0.66f, 0.46f, 0.22f);
        colors.disabledColor = new Color(0.65f, 0.61f, 0.65f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        if (action != null)
            button.onClick.AddListener(action);
    }

    /// <summary>게임 중에만 Esc로 열고 닫으며 로비 복귀나 사망 연출에서는 닫는다.</summary>
    private void Update()
    {
        bool showGear = CanShow && !IsOpen;
        if (gearRoot != null && gearRoot.gameObject.activeSelf != showGear)
            gearRoot.gameObject.SetActive(showGear);

        if (!CanShow)
        {
            if (IsOpen)
                Close();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsOpen && settingsPanel.gameObject.activeSelf)
                ShowMainMenu();
            else
                Toggle();
        }
    }

    /// <summary>톱니바퀴 또는 Esc 입력으로 자기 화면의 메뉴만 전환한다.</summary>
    public void Toggle()
    {
        if (CanShow)
            SetOpen(!IsOpen);
    }

    /// <summary>계속하기 또는 Esc로 메뉴를 닫고 게임 화면으로 돌아간다.</summary>
    public void Close()
    {
        SetOpen(false);
    }

    /// <summary>메뉴 전환 프레임의 입력을 비워 클릭과 키가 뒤쪽 게임 조작으로 이어지지 않게 한다.</summary>
    private void SetOpen(bool open)
    {
        if (root == null || IsOpen == open)
            return;

        if (!open)
            GameAudioSettings.Save();

        settingsPanel.gameObject.SetActive(false);
        mainPanel.gameObject.SetActive(true);
        root.gameObject.SetActive(open);
        if (gearRoot != null)
            gearRoot.gameObject.SetActive(!open && CanShow);
        lastInteractionFrame = Time.frameCount;
        InputManager.ClearInputs();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>현재 게임의 연결을 종료하고 기존 방 관리자의 로비 복귀 절차를 따른다.</summary>
    private void LeaveGame()
    {
        Close();
        manager.LeaveRoom();
    }

    /// <summary>담당 오브젝트가 꺼지면 메뉴와 입력 차단을 함께 해제한다.</summary>
    private void OnDisable()
    {
        GameAudioSettings.Save();
        if (root != null)
            root.gameObject.SetActive(false);

        if (gearRoot != null)
            gearRoot.gameObject.SetActive(false);

        if (instance == this)
            instance = null;
    }

    /// <summary>앱이 일시 중단될 때 변경된 음량을 저장한다.</summary>
    private void OnApplicationPause(bool paused)
    {
        if (paused)
            GameAudioSettings.Save();
    }

    /// <summary>게임을 종료할 때 마지막 음량 설정을 저장한다.</summary>
    private void OnApplicationQuit()
    {
        GameAudioSettings.Save();
    }

    /// <summary>다시 활성화된 담당을 입력 차단 조회에 연결한다.</summary>
    private void OnEnable()
    {
        instance = this;
    }
}
