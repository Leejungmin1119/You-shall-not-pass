using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 공용 아이템 이동과 좌클릭 보관·우클릭 즉시 구매, 투명화 연출을 처리한다.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup))]
public class ConveyorItem : MonoBehaviour, IPointerClickHandler
{
    private RectTransform _rectTransform;
    [SerializeField] private UnityEngine.UI.Image itemIcon;
    [SerializeField] private TMPro.TMP_Text priceText;

    private UnityEngine.UI.Image _clickArea;
    private CanvasGroup _canvasGroup;
    private RectTransform _viewport;
    private ConveyorController _controller;
    private ConveyorEntry _entry;
    private Vector2 _startPosition;
    private float _travelDistance;
    private float _moveSpeed;
    private float _fadeElapsed;
    private bool _isCollecting;
    private bool _requestPending;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.35f;

    /// <summary>
    /// 이동과 투명화에 사용할 컴포넌트를 보관한다.
    /// </summary>
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _clickArea = GetComponent<UnityEngine.UI.Image>();
        _canvasGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// UI 삭제 시 컨트롤러에 남아 있는 참조를 정리한다.
    /// </summary>
    private void OnDestroy()
    {
        if (_controller != null)
            _controller.ForgetItem(_entry.Id);
    }

    /// <summary>
    /// Viewport와 기본 이동 속도를 연결한다. 기존 초기화 호출도 유지한다.
    /// </summary>
    public void Initialize(RectTransform viewport, float moveSpeed)
    {
        if (viewport == null || moveSpeed <= 0f || itemIcon == null || priceText == null)
        {
            Debug.LogError("상품의 Viewport, 아이템 이미지, 가격 텍스트와 양수 이동 속도를 연결하세요.", this);
            enabled = false;
            return;
        }
        _viewport = viewport;
        _moveSpeed = moveSpeed;
        _fadeElapsed = 0f;
        _isCollecting = false;
        _requestPending = false;
        _canvasGroup.alpha = 1f;
        _clickArea.raycastTarget = true;
    }

    /// <summary>
    /// 서버의 식별 정보와 생성 시각을 연결하고 화면 밖까지의 이동 거리를 계산한다.
    /// </summary>
    public void BindNetworkItem(ConveyorController controller, ConveyorEntry entry)
    {
        _controller = controller;
        _entry = entry;
        ItemData data = controller.GetItemData(entry.ItemId);
        itemIcon.sprite = data != null ? data.Icon : null;
        itemIcon.preserveAspect = true;
        itemIcon.color = Color.white;

        // 서버가 구매에 사용하는 가격을 생성 시 한 번만 표시한다.
        priceText.SetText("{0}", entry.Price);

        _startPosition = _rectTransform.anchoredPosition;
        // Canvas 배치가 확정된 뒤 실제 아이템 왼쪽 끝을 기준으로 계산한다.
        Canvas.ForceUpdateCanvases();
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_viewport, _rectTransform);
        _travelDistance = Mathf.Max(0f, _viewport.rect.xMax - bounds.min.x) + 0.01f;
        UpdateNetworkPosition();
    }

    /// <summary>
    /// 구매 연출 또는 생성 시각에 따른 이동을 표시한다.
    /// </summary>
    private void Update()
    {
        if (_isCollecting)
        {
            UpdateCollectFade();
            return;
        }
        if (_controller != null)
        {
            UpdateNetworkPosition();
            return;
        }
        // 기존 초기화만 사용한 로컬 아이템의 표시 동작을 유지한다.
        _rectTransform.anchoredPosition += Vector2.right * (_moveSpeed * Time.deltaTime);
        if (HasCompletelyExitedViewport())
            Destroy(gameObject);
    }

    /// <summary>
    /// 서버와 맞춘 시간으로 이동 진행률을 계산한다. 제거 결정은 서버 목록에 맡긴다.
    /// </summary>
    private void UpdateNetworkPosition()
    {
        float progress = Mathf.Clamp01((float)(NetworkTime.time - _entry.SpawnTime) / _entry.Lifetime);
        _rectTransform.anchoredPosition = _startPosition + Vector2.right * (_travelDistance * progress);
        _clickArea.raycastTarget = !_requestPending && progress < 1f &&
            _controller.CanLocalPlayerCollect;
    }

    /// <summary>
    /// 좌클릭은 인벤토리, 우클릭은 손에 들기로 구분해 서버에 구매를 요청한다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isCollecting || _requestPending ||
            _controller == null || (eventData.button != PointerEventData.InputButton.Right &&
            eventData.button != PointerEventData.InputButton.Left))
            return;

        PurchaseDestination destination = eventData.button == PointerEventData.InputButton.Left
            ? PurchaseDestination.Inventory
            : PurchaseDestination.Hand;

        // 호스트는 응답이 즉시 올 수도 있으므로 요청 전 대기 상태를 설정한다.
        _requestPending = true;
        _clickArea.raycastTarget = false;
        if (!_controller.RequestCollect(_entry.Id, destination))
            CompleteCollectRequest();
    }

    /// <summary>
    /// 서버 응답 후 요청 대기를 해제한다. 구매 연출 중에는 클릭을 다시 켜지 않는다.
    /// </summary>
    public void CompleteCollectRequest()
    {
        _requestPending = false;
        if (!_isCollecting)
            UpdateNetworkPosition();
    }

    /// <summary>
    /// 서버가 구매를 확정한 아이템의 이동과 클릭을 막고 투명화를 시작한다.
    /// </summary>
    public void BeginCollect()
    {
        if (_isCollecting)
            return;
        _isCollecting = true;
        _fadeElapsed = 0f;
        _clickArea.raycastTarget = false;
    }

    /// <summary>
    /// 로컬 표시 아이템의 왼쪽 끝이 Viewport의 오른쪽 끝을 넘었는지 확인한다.
    /// </summary>
    private bool HasCompletelyExitedViewport()
    {
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_viewport, _rectTransform);
        return bounds.min.x > _viewport.rect.xMax;
    }

    /// <summary>
    /// 알파를 줄인 뒤 UI를 삭제한다. 성공 로그는 구매자의 응답에서만 출력한다.
    /// </summary>
    private void UpdateCollectFade()
    {
        _fadeElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_fadeElapsed / Mathf.Max(0.01f, fadeDuration));
        // 아이템뿐 아니라 자식 금화와 가격도 함께 사라지게 한다.
        _canvasGroup.alpha = 1f - progress;
        if (progress >= 1f)
            Destroy(gameObject);
    }
}
