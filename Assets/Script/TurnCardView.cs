using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>카드 한 장의 입력, 선택 테두리와 앞뒷면 표시를 담당한다.</summary>
public sealed class TurnCardView : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private TurnCardSelectionUI screen;
    private int index;
    private UnityEngine.UI.Image image;
    private TurnCardFace face;
    private UnityEngine.UI.Outline outline;
    private Sprite back;
    private TMP_Text number;
    private TMP_Text rank;
    private TMP_Text nameLabel;
    private TMP_Text pointer;
    private Material burnMaterial;
    private Material burnTemplate;
    private bool initialized;
    private uint lastOwner;
    private byte lastNumber;
    private bool lastHighlighted;
    private bool lastHighest;
    private bool lastMine;
    private bool lastShowFront;
    private int lastRank;
    private Color lastOwnerColor;
    private string lastOwnerName;
    private string lastLocalName;
    private float lastBurn = -1f;
    private static readonly int BurnProgress = Shader.PropertyToID("_BurnProgress");
    public RectTransform Rect { get; private set; }

    /// <summary>게임에서 사용할 그림과 폰트를 연결하고 텍스트와 외곽선을 한 번만 만든다.</summary>
    public void Initialize(TurnCardSelectionUI owner, int cardIndex, Sprite backSprite, Material burn)
    {
        screen = owner;
        index = cardIndex;
        back = backSprite;
        Rect = (RectTransform)transform;
        Rect.sizeDelta = new Vector2(190f, 285f);

        var hitTarget = gameObject.AddComponent<UnityEngine.UI.Image>();
        hitTarget.color = Color.clear;
        hitTarget.raycastTarget = true;

        var artwork = new GameObject("Artwork", typeof(RectTransform));
        artwork.transform.SetParent(transform, false);
        ((RectTransform)artwork.transform).sizeDelta = Rect.sizeDelta;
        image = artwork.AddComponent<UnityEngine.UI.Image>();
        image.sprite = back;
        image.raycastTarget = false;

        // 평소에는 공통 UI 재질로 그린다. 소각 재질은 별도로 준비하되 소각 중에만 연결한다.
        burnTemplate = burn;

        outline = artwork.AddComponent<UnityEngine.UI.Outline>();
        outline.useGraphicAlpha = true;
        outline.enabled = false;

        var front = new GameObject("CleanFront", typeof(RectTransform));
        front.transform.SetParent(transform, false);
        ((RectTransform)front.transform).sizeDelta = Rect.sizeDelta;
        face = front.AddComponent<TurnCardFace>();
        face.raycastTarget = false;
        front.SetActive(false);
        number = screen.MakeText("Number", Rect, new Vector2(0, 12), new Vector2(180, 150), 100, Color.black);
        rank = screen.MakeText("Rank", Rect, new Vector2(0, -76), new Vector2(180, 55), 23, new Color(0.3f, 0.15f, 0.4f));
        nameLabel = screen.MakeText("Owner", Rect, new Vector2(0, 184), new Vector2(270, 56), 25, Color.white);
        pointer = screen.MakeText("Cursor", Rect, new Vector2(0, -180), new Vector2(220, 45), 24, new Color(1f, 0.92f, 0.62f));
    }

    /// <summary>시작 연출 동안 한 장씩 호출해 재질 생성 비용이 한 프레임에 몰리지 않게 한다.</summary>
    public void PrepareBurnMaterial()
    {
        if (burnMaterial != null || burnTemplate == null)
            return;

        burnMaterial = new Material(burnTemplate);
        burnMaterial.SetFloat("_BurnSeed", index + 1);
    }

    /// <summary>마우스를 올린 빈 카드를 현재 선택 커서로 만든다.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        screen.Hover(index);
    }

    /// <summary>좌클릭만 확정 요청으로 보내며 우클릭은 무시한다.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            screen.Choose(index);
    }

    /// <summary>서버 상태로 카드 주인과 숫자를 표시하고 선택 중·확정 테두리를 구분한다.</summary>
    public void Refresh(TurnCardState card, bool highlighted, bool highest, Color ownerColor, string ownerName,
        bool mine, int attackRank, float burn, float flip, float glow)
    {
        string localName = screen.LocalPlayerName;
        bool showFront = card.Number > 0 && flip >= 0.5f;
        bool changed = !initialized || lastOwner != card.OwnerId ||
            lastHighlighted != highlighted || lastMine != mine ||
            lastOwnerColor != ownerColor || lastLocalName != localName;

        if (changed)
        {
            bool confirmed = card.OwnerId != 0;
            outline.enabled = confirmed || highlighted;
            outline.effectColor = confirmed ? ownerColor : new Color(1f, 0.94f, 0.72f);
            outline.effectDistance = Vector2.one * (confirmed ? 5f : 2f);
            pointer.text = confirmed ? (mine ? "내 선택 완료" : "선택 완료") : (highlighted ? localName : string.Empty);
            pointer.color = confirmed ? ownerColor : new Color(1f, 0.94f, 0.72f);
        }

        if (!initialized || lastOwner != card.OwnerId || lastOwnerName != ownerName)
            nameLabel.text = card.OwnerId != 0 ? ownerName : string.Empty;

        if (!initialized || lastOwnerColor != ownerColor)
            nameLabel.color = ownerColor;

        if (!initialized || lastShowFront != showFront)
        {
            if (image.gameObject.activeSelf == showFront)
                image.gameObject.SetActive(!showFront);

            if (face.gameObject.activeSelf != showFront)
                face.gameObject.SetActive(showFront);
        }

        if (showFront && (!initialized || !lastShowFront || highest || lastHighest || lastOwnerColor != ownerColor))
            face.SetSelectionColor(highest ? Color.Lerp(ownerColor, new Color(1f, 0.95f, 0.62f), glow) : ownerColor);

        // 같은 숫자와 순위의 문자열을 매 프레임 만들지 않고 표시가 달라질 때만 갱신한다.
        if (!initialized || lastShowFront != showFront || lastNumber != card.Number)
            number.text = showFront ? card.Number.ToString() : string.Empty;

        if (!initialized || lastShowFront != showFront || lastRank != attackRank)
            rank.text = showFront ? $"{attackRank}번째 공격" : string.Empty;

        UpdateBurn(burn);

        initialized = true;
        lastOwner = card.OwnerId;
        lastNumber = card.Number;
        lastHighlighted = highlighted;
        lastHighest = highest;
        lastMine = mine;
        lastShowFront = showFront;
        lastRank = attackRank;
        lastOwnerColor = ownerColor;
        lastOwnerName = ownerName;
        lastLocalName = localName;
    }

    /// <summary>소각 중에만 개별 재질을 적용하고, 진행도가 바뀔 때만 셰이더에 전달한다.</summary>
    private void UpdateBurn(float progress)
    {
        if (Mathf.Approximately(lastBurn, progress))
            return;

        if (progress > 0f)
        {
            PrepareBurnMaterial();

            if (burnMaterial != null)
            {
                if (image.material != burnMaterial)
                    image.material = burnMaterial;

                burnMaterial.SetFloat(BurnProgress, progress);
            }
        }
        else if (lastBurn > 0f)
        {
            image.material = null;
        }

        lastBurn = progress;
    }

    /// <summary>카드 화면이 종료되면 카드별 소각 재질도 해제한다.</summary>
    private void OnDestroy()
    {
        if (burnMaterial != null)
            Destroy(burnMaterial);
    }
}
