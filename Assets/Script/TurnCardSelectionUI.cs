using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>카드 10장의 부채꼴 배치, 선택 조작과 서버 시각에 맞춘 결과 연출을 표시한다.</summary>
public sealed class TurnCardSelectionUI : MonoBehaviour
{
    [SerializeField] private Sprite cardBack;
    [SerializeField] private Material burnMaterial;
    [SerializeField] private TMP_FontAsset font;

    private RaceManager race;
    private rolechangeManager roles;
    private RectTransform root;
    private TMP_Text title;
    private TMP_Text subtitle;
    private TMP_Text timer;
    private TMP_Text instruction;
    private readonly TurnCardView[] cards = new TurnCardView[rolechangeManager.CardCount];
    private readonly Vector2[] positions = new Vector2[rolechangeManager.CardCount];
    private readonly Vector2[] positionVelocities = new Vector2[rolechangeManager.CardCount];
    private readonly Vector2[] poseTargets = new Vector2[rolechangeManager.CardCount];
    private readonly Quaternion[] rotationTargets = new Quaternion[rolechangeManager.CardCount];
    private readonly bool[] moving = new bool[rolechangeManager.CardCount];
    private readonly Vector2[] fanPositions = new Vector2[rolechangeManager.CardCount];
    private readonly float[] fanAngles = new float[rolechangeManager.CardCount];
    private readonly Dictionary<uint, NetworkPlayerProfile> profiles = new Dictionary<uint, NetworkPlayerProfile>(4);
    private NetworkPlayerProfile localProfile;
    private int cursor = 4;
    private int selectedCount;
    private int revealedCount;
    private int highestCard = -1;
    private bool requestPending;
    private int pendingCard = -1;
    private double rejectionUntil;
    private int lastCountdownSeconds = int.MinValue;
    private int preparedMaterials;
    private uint cachedWinnerId;
    private string cachedWinnerName;
    private string cachedWinnerInstruction;

    public rolechangeManager Roles => roles;
    public string LocalPlayerName => localProfile != null ? localProfile.PlayerName : string.Empty;
    public Vector2 CardPosition(int index) => positions[index];
    public int HighestCard => highestCard;
    public float PhaseElapsed => (float)System.Math.Max(0d, NetworkTime.time - roles.CardPhaseStartedAt);
    public float HighestElapsed => roles.CardPhase == TurnCardPhase.Revealing
        ? PhaseElapsed - Mathf.Max(0, selectedCount - 1) * rolechangeManager.RevealInterval
        : rolechangeManager.WinnerDuration + PhaseElapsed;

    /// <summary>현재 씬의 레이스에 연결하고 화면과 카드 오브젝트는 한 번만 만든다.</summary>
    private void Awake()
    {
        race = GetComponent<RaceManager>();
        roles = GetComponent<rolechangeManager>();
        BuildScreen();
    }

    /// <summary>화면 크기에 비례하는 전용 Canvas를 만들고 기존 HUD 위에 카드 선택 화면을 배치한다.</summary>
    private void BuildScreen()
    {
        root = MakeRect("TurnCardCanvas", transform, Vector2.zero, new Vector2(1920, 1080));
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        RectTransform backdrop = MakeRect("Backdrop", root, Vector2.zero, Vector2.zero);
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        var background = backdrop.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.075f, 0.055f, 0.12f, 1f);
        // 빈 공간은 뒤쪽 게임 UI의 입력만 막고 카드를 확정하지 않는다.
        background.raycastTarget = true;

        MakeText("Eyebrow", root, new Vector2(0, 440), new Vector2(1100, 42), 22, new Color(0.76f, 0.57f, 0.91f)).text = "새로운 경기 · 공격 순서 결정";
        title = MakeText("Title", root, new Vector2(0, 365), new Vector2(1500, 100), 62, new Color(1f, 0.9f, 0.68f));
        subtitle = MakeText("Subtitle", root, new Vector2(0, 285), new Vector2(1500, 60), 27, new Color(0.78f, 0.74f, 0.85f));
        timer = MakeText("Timer", root, new Vector2(0, 235), new Vector2(300, 60), 46, Color.white);

        RectTransform fan = MakeRect("Cards", root, Vector2.zero, Vector2.zero);
        // 움직이는 카드의 배칭을 제목·설명·배경과 분리한다. 카드 클릭도 이 Canvas에서 받는다.
        fan.gameObject.AddComponent<Canvas>();
        fan.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        for (int index = 0; index < cards.Length; index++)
        {
            RectTransform card = MakeRect($"Card{index + 1:00}", fan, Vector2.zero, new Vector2(190, 285));
            cards[index] = card.gameObject.AddComponent<TurnCardView>();
            cards[index].Initialize(this, index, cardBack, burnMaterial);
            fanAngles[index] = Mathf.Lerp(-46f, 46f, index / (float)(cards.Length - 1));
            fanPositions[index] = FanPosition(fanAngles[index]);
            positions[index] = new Vector2(0f, -200f);
            card.anchoredPosition = positions[index];
        }

        RectTransform effects = MakeRect("CardEffects", root, Vector2.zero, new Vector2(1920, 1080));
        // 입자 메시 갱신이 카드와 제목의 Canvas 배칭까지 갱신하지 않도록 분리한다.
        effects.gameObject.AddComponent<Canvas>();
        effects.gameObject.AddComponent<TurnCardEffects>().Initialize(this);
        instruction = MakeText("Instructions", root, new Vector2(0, -402), new Vector2(1500, 65), 28, new Color(0.9f, 0.85f, 0.95f));
        MakeText("Rule", root, new Vector2(0, -465), new Vector2(1500, 45), 22, new Color(0.62f, 0.57f, 0.71f)).text = "A · D / 마우스 이동     좌클릭 확정     ·     선택 후에는 변경할 수 없습니다";
        root.gameObject.SetActive(false);
    }

    /// <summary>공통 기준 해상도 안에서 중앙 기준 RectTransform을 만든다.</summary>
    private static RectTransform MakeRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>카드와 제목이 같은 한국어 폰트와 정렬 규칙을 사용하도록 만든다.</summary>
    public TMP_Text MakeText(string objectName, Transform parent, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        RectTransform rect = MakeRect(objectName, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.richText = false;
        return text;
    }

    /// <summary>선택 단계에만 화면을 표시하며 경기 중·사망 연출 중에는 입력과 표시를 모두 끈다.</summary>
    private void Update()
    {
        bool visible = NetworkClient.active && race.Phase == RacePhase.CardSelection;

        if (root.gameObject.activeSelf != visible)
            root.gameObject.SetActive(visible);

        if (!visible)
            return;

        BindLocalProfile();

        // 재질을 한꺼번에 생성해 시작 프레임에 비용이 몰리지 않도록 분산한다.
        if (preparedMaterials < cards.Length)
            cards[preparedMaterials++].PrepareBurnMaterial();

        CountCards();

        if (CanChoose() && cursor < roles.Cards.Count && roles.Cards[cursor].OwnerId != 0)
            MoveCursor(1);

        if (CanChoose() && Keyboard.current != null)
        {
            if (Keyboard.current.aKey.wasPressedThisFrame)
                MoveCursor(-1);
            else if (Keyboard.current.dKey.wasPressedThisFrame)
                MoveCursor(1);
        }

        UpdateCards();
        UpdateLabels();
    }

    /// <summary>게임 플레이어가 생성된 뒤 소유자에게만 선택 응답을 연결한다.</summary>
    private void BindLocalProfile()
    {
        if (localProfile != null || NetworkClient.localPlayer == null)
            return;

        localProfile = NetworkClient.localPlayer.GetComponent<NetworkPlayerProfile>();

        if (localProfile != null)
            localProfile.TurnCardChoiceResponded += OnChoiceResponse;
    }

    /// <summary>선택·공개 수와 최고 카드를 계산한다. 아직 미공개인 숫자는 읽지 않는다.</summary>
    private void CountCards()
    {
        selectedCount = 0;
        revealedCount = 0;
        highestCard = -1;
        int highest = 0;

        for (int index = 0; index < roles.Cards.Count; index++)
        {
            TurnCardState card = roles.Cards[index];

            if (card.OwnerId != 0)
                selectedCount++;

            if (card.Number == 0)
                continue;

            revealedCount++;

            if (card.Number > highest)
            {
                highest = card.Number;
                highestCard = index;
            }
        }

        if (revealedCount != selectedCount || selectedCount == 0)
            highestCard = -1;
    }

    /// <summary>참가자 본인이 아직 확정하지 않았고 서버가 선택을 받는 동안에만 조작한다.</summary>
    private bool CanChoose()
    {
        return localProfile != null && roles.CardPhase == TurnCardPhase.Choosing &&
            !requestPending && roles.FindChosenCard(localProfile.netId) < 0;
    }

    /// <summary>이미 선택된 카드는 건너뛰며 좌우로 이동한다.</summary>
    private void MoveCursor(int direction)
    {
        for (int offset = 1; offset <= cards.Length; offset++)
        {
            int next = (cursor + direction * offset + cards.Length * 2) % cards.Length;

            if (next < roles.Cards.Count && roles.Cards[next].OwnerId == 0)
            {
                cursor = next;
                return;
            }
        }
    }

    /// <summary>마우스를 올린 미선택 카드를 키보드와 같은 커서로 강조한다.</summary>
    public void Hover(int index)
    {
        if (CanChoose() && index < roles.Cards.Count && roles.Cards[index].OwnerId == 0)
            cursor = index;
    }

    /// <summary>서버 승인 전에는 중복 요청을 막고 자신의 카드 선택만 전송한다.</summary>
    public void Choose(int index)
    {
        if (!CanChoose() || index < 0 || index >= roles.Cards.Count || roles.Cards[index].OwnerId != 0)
            return;

        cursor = index;
        requestPending = true;
        pendingCard = index;
        localProfile.CmdChooseTurnCard((byte)index);
    }

    /// <summary>경합으로 거절되면 다시 고를 수 있게 하고, 승인되면 변경을 막는다.</summary>
    private void OnChoiceResponse(bool accepted)
    {
        requestPending = accepted;

        if (!accepted)
        {
            pendingCard = -1;
            rejectionUntil = NetworkTime.time + 1.5d;
            MoveCursor(1);
        }
    }

    /// <summary>선택한 플레이어의 이름과 색상을 네트워크 ID로 찾아 보관한다.</summary>
    private NetworkPlayerProfile Profile(uint id)
    {
        if (id == 0)
            return null;

        if (profiles.TryGetValue(id, out NetworkPlayerProfile profile) && profile != null)
            return profile;

        if (NetworkClient.spawned.TryGetValue(id, out NetworkIdentity identity))
        {
            profile = identity.GetComponent<NetworkPlayerProfile>();
            profiles[id] = profile;
            return profile;
        }

        return null;
    }

    /// <summary>부채꼴의 각도에 해당하는 카드 중심 위치를 구한다.</summary>
    private static Vector2 FanPosition(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians) * 790f, Mathf.Cos(radians) * 420f - 430f);
    }

    /// <summary>시작하자마자 왼쪽의 패를 오른쪽으로 연속해서 펼친다.</summary>
    private void IntroPose(int index, Vector2 fan, float angle, out Vector2 position, out float rotation)
    {
        float elapsed = PhaseElapsed;
        float layer = index - (cards.Length - 1) * 0.5f;
        Vector2 deckOffset = new Vector2(layer * 1.2f, layer * 0.9f);
        Vector2 left = fanPositions[0];

        // 카드마다 시작을 조금씩 늦추되 마지막 카드도 선택 시작 전에 펼쳐지게 한다.
        float duration = Mathf.Max(0.01f,
            rolechangeManager.SpreadDuration - (cards.Length - 1) * rolechangeManager.SpreadStagger);
        float spread = Mathf.SmoothStep(0f, 1f,
            (elapsed - index * rolechangeManager.SpreadStagger) / duration);

        position = Vector2.Lerp(left + deckOffset, fan, spread);
        rotation = Mathf.Lerp(46f, -angle, spread);
    }

    /// <summary>왼쪽에서 펼치는 연출 후 선택을 표시하고, 소각 이후 선택된 카드만 모아 뒤집는다.</summary>
    private void UpdateCards()
    {
        bool introducing = roles.CardPhase == TurnCardPhase.Dealing;
        bool choosing = roles.CardPhase == TurnCardPhase.Choosing || roles.CardPhase == TurnCardPhase.Dealing;
        bool showingResults = roles.CardPhase == TurnCardPhase.ResultHold || roles.CardPhase == TurnCardPhase.Dismissing;
        float arrange = choosing ? 0f : Mathf.SmoothStep(0f, 1f, PhaseElapsed / rolechangeManager.BurnDuration);

        if (roles.CardPhase == TurnCardPhase.Revealing || showingResults)
            arrange = 1f;

        int selectedIndex = 0;

        for (int index = 0; index < cards.Length; index++)
        {
            if (index >= roles.Cards.Count)
                continue;

            TurnCardState card = roles.Cards[index];
            bool selected = card.OwnerId != 0;
            bool burnt = !selected && (roles.CardPhase == TurnCardPhase.Revealing || showingResults);
            if (cards[index].gameObject.activeSelf == burnt)
                cards[index].gameObject.SetActive(!burnt);

            if (burnt)
                continue;

            float angle = fanAngles[index];
            Vector2 fan = fanPositions[index];
            // 서버 승인 대기 중에도 커서 표시를 유지해 강조가 잠깐 사라지는 현상을 막는다.
            bool awaitingConfirmation = requestPending && pendingCard == index && localProfile != null &&
                roles.FindChosenCard(localProfile.netId) < 0;
            bool highlighted = !selected && ((CanChoose() && cursor == index) || awaitingConfirmation);

            if (highlighted || selected)
                fan.y += 42f;

            Vector2 destination = selected ? new Vector2((selectedIndex++ - (selectedCount - 1) * 0.5f) * 300f, -65f) : fan;
            Vector2 targetPosition = Vector2.Lerp(fan, destination, selected ? arrange : 0f);
            float rotation = Mathf.Lerp(-angle, 0, selected ? arrange : 0);

            if (introducing)
                IntroPose(index, fan, angle, out targetPosition, out rotation);

            // 결과를 충분히 보여준 뒤 카드와 이름표를 함께 화면 아래로 보낸다.
            if (roles.CardPhase == TurnCardPhase.Dismissing)
                targetPosition += Vector2.down * (900f * Mathf.SmoothStep(0f, 1f, PhaseElapsed / rolechangeManager.DismissDuration));

            ApplyCardPose(index, targetPosition, rotation, introducing);

            int rank = selectedCount;

            foreach (TurnCardState other in roles.Cards)
                if (other.Number > 0 && other.Number < card.Number)
                    rank--;

            float sinceReveal = PhaseElapsed - (selectedCount - rank) * rolechangeManager.RevealInterval;
            float flip = card.Number > 0 ? Mathf.Clamp01(sinceReveal / 0.3f) : 0f;

            if (showingResults)
                flip = 1f;

            float width = card.Number > 0 ? Mathf.Max(0.02f, Mathf.Abs(1f - flip * 2f)) : 1f;
            float emphasis = index == highestCard && roles.CardPhase == TurnCardPhase.Revealing
                ? Mathf.Sin(Mathf.Clamp01(HighestElapsed / 0.45f) * Mathf.PI) * 0.12f : 0f;
            float size = Mathf.Lerp(1f, 1.15f, selected ? arrange : 0f) + emphasis;
            Vector3 scale = new Vector3(width * size, size, 1f);

            if (cards[index].Rect.localScale != scale)
                cards[index].Rect.localScale = scale;

            NetworkPlayerProfile profile = Profile(card.OwnerId);
            float burn = !selected && roles.CardPhase == TurnCardPhase.Burning ? Mathf.Clamp01(PhaseElapsed / rolechangeManager.BurnDuration) : 0f;
            cards[index].Refresh(card, highlighted, index == highestCard,
                profile != null ? profile.PlayerColor : Color.white,
                profile != null ? profile.PlayerName : string.Empty,
                localProfile != null && card.OwnerId == localProfile.netId, rank, burn, flip,
                index == highestCard ? 0.5f + 0.5f * Mathf.Sin(HighestElapsed * 15f) : 0f);
        }
    }

    /// <summary>목표가 바뀐 카드만 이동시키고, 도착하면 보간과 Transform 갱신을 멈춘다.</summary>
    private void ApplyCardPose(int index, Vector2 targetPosition, float angle, bool introducing)
    {
        RectTransform rect = cards[index].Rect;
        Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);

        if (introducing)
        {
            positions[index] = targetPosition;
            positionVelocities[index] = Vector2.zero;
            moving[index] = false;
        }
        else
        {
            if (poseTargets[index] != targetPosition || !rotationTargets[index].Equals(targetRotation))
                moving[index] = true;

            if (!moving[index])
                return;

            positions[index] = Vector2.SmoothDamp(positions[index], targetPosition,
                ref positionVelocities[index], 0.09f, Mathf.Infinity, Time.unscaledDeltaTime);
            targetRotation = Quaternion.Slerp(rect.localRotation, targetRotation,
                1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));

            if ((positions[index] - targetPosition).sqrMagnitude < 0.0001f &&
                positionVelocities[index].sqrMagnitude < 0.0001f &&
                Quaternion.Angle(targetRotation, Quaternion.Euler(0f, 0f, angle)) < 0.02f)
            {
                positions[index] = targetPosition;
                positionVelocities[index] = Vector2.zero;
                targetRotation = Quaternion.Euler(0f, 0f, angle);
                moving[index] = false;
            }
        }

        poseTargets[index] = targetPosition;
        rotationTargets[index] = Quaternion.Euler(0f, 0f, angle);

        if (rect.anchoredPosition != positions[index])
            rect.anchoredPosition = positions[index];

        if (!rect.localRotation.Equals(targetRotation))
            rect.localRotation = targetRotation;
    }

    /// <summary>선택 대기, 확정, 순차 공개 상태에 맞는 안내와 남은 시간을 표시한다.</summary>
    private void UpdateLabels()
    {
        string nextTitle = "운명의 카드를 고르세요";
        string nextSubtitle = "높은 숫자부터 공격합니다 · 숫자는 선택이 끝난 뒤 공개됩니다";
        string nextInstruction;

        int countdownSeconds = roles.CardPhase == TurnCardPhase.Choosing
            ? Mathf.Max(0, Mathf.CeilToInt(rolechangeManager.SelectionDuration - PhaseElapsed)) : -1;

        if (countdownSeconds != lastCountdownSeconds)
        {
            lastCountdownSeconds = countdownSeconds;
            timer.text = countdownSeconds >= 0 ? countdownSeconds.ToString() : string.Empty;
        }

        if (roles.CardPhase == TurnCardPhase.Dealing)
            nextInstruction = "카드를 왼쪽부터 펼치는 중";
        else if (roles.CardPhase == TurnCardPhase.Burning)
            nextInstruction = "선택되지 않은 카드는 재가 됩니다";
        else if (roles.CardPhase == TurnCardPhase.Revealing || roles.CardPhase == TurnCardPhase.ResultHold ||
            roles.CardPhase == TurnCardPhase.Dismissing)
        {
            nextTitle = highestCard >= 0 ? "공격 순서가 정해졌습니다" : "카드를 공개합니다";
            nextSubtitle = "높은 숫자부터 차례대로 공격합니다";
            NetworkPlayerProfile highest = highestCard >= 0 ? Profile(roles.Cards[highestCard].OwnerId) : null;
            if (highest != null)
            {
                if (cachedWinnerId != highest.netId || cachedWinnerName != highest.PlayerName)
                {
                    cachedWinnerId = highest.netId;
                    cachedWinnerName = highest.PlayerName;
                    cachedWinnerInstruction = $"첫 번째 공격자 · {cachedWinnerName}";
                }

                nextInstruction = cachedWinnerInstruction;
            }
            else
            {
                nextInstruction = "낮은 숫자부터 공개합니다";
            }
        }
        else if (NetworkTime.time < rejectionUntil)
            nextInstruction = "다른 플레이어가 먼저 선택했습니다 · 다른 카드를 골라주세요";
        else if (localProfile != null && roles.FindChosenCard(localProfile.netId) >= 0)
            nextInstruction = "선택이 확정되었습니다 · 다른 플레이어를 기다리는 중";
        else
            nextInstruction = "마음에 드는 카드 한 장을 선택하세요";

        // 최종 문구를 먼저 결정한 뒤 달라진 텍스트만 한 번 갱신한다.
        SetLabelIfChanged(title, nextTitle);
        SetLabelIfChanged(subtitle, nextSubtitle);
        SetLabelIfChanged(instruction, nextInstruction);
    }

    /// <summary>같은 문구를 다시 대입해 불필요한 텍스트 갱신을 요청하지 않는다.</summary>
    private static void SetLabelIfChanged(TMP_Text label, string value)
    {
        if (label.text != value)
            label.text = value;
    }

    /// <summary>경기가 끝나 씬이 바뀌면 개인 선택 응답 구독을 해제한다.</summary>
    private void OnDestroy()
    {
        if (localProfile != null)
            localProfile.TurnCardChoiceResponded -= OnChoiceResponse;
    }
}
