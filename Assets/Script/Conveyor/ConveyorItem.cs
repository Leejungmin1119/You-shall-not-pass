using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 공용 아이템의 이동을 표시하고 우클릭 구매 요청과 투명화 연출을 처리한다.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(UnityEngine.UI.Image))]
public class ConveyorItem : MonoBehaviour, IPointerClickHandler
{
    private RectTransform _rectTransform;
    private UnityEngine.UI.Image _image;
    private RectTransform _viewport;
    private ConveyorController _controller;
    private ConveyorEntry _entry;
    private Vector2 _startPosition;
    private float _travelDistance;
    private float _moveSpeed;
    private float _fadeElapsed;
    private Color _fadeStartColor;
    private bool _isCollecting;
    private bool _requestPending;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.35f;

    /// <summary>
    /// 이동과 투명화에 사용할 컴포넌트를 보관한다.
    /// </summary>
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<UnityEngine.UI.Image>();
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
        if (viewport == null || moveSpeed <= 0f)
        {
            Debug.LogError("아이템에 Viewport와 양수 이동 속도가 필요합니다.", this);
            enabled = false;
            return;
        }
        _viewport = viewport;
        _moveSpeed = moveSpeed;
        _fadeElapsed = 0f;
        _isCollecting = false;
        _requestPending = false;
        _image.raycastTarget = true;
    }

    /// <summary>
    /// 서버의 식별 정보와 생성 시각을 연결하고 화면 밖까지의 이동 거리를 계산한다.
    /// </summary>
    public void BindNetworkItem(ConveyorController controller, ConveyorEntry entry)
    {
        _controller = controller;
        _entry = entry;
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
        _image.raycastTarget = !_requestPending && progress < 1f &&
            _controller.CanLocalPlayerCollect;
    }

    /// <summary>
    /// 우클릭 시 중복 요청을 막고 서버에 구매를 요청한다. 즉시 습득하지 않는다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isCollecting || _requestPending ||
            (eventData.button != PointerEventData.InputButton.Right &&
             eventData.button != PointerEventData.InputButton.Left) || _controller == null)
            return;

        // 호스트는 응답이 즉시 올 수도 있으므로 요청 전 대기 상태를 설정한다.
        _requestPending = true;
        _image.raycastTarget = false;
        bool sent;
        if (RaceSession.Instance != null)
            sent = NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.GetComponent<NetworkRaceInventory>()?.RequestBuy(
                    _entry.Id, eventData.button == PointerEventData.InputButton.Right) == true;
        else sent = _controller.RequestCollect(_entry.Id);
        if (!sent) CompleteCollectRequest();
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
        _fadeStartColor = _image.color;
        _image.raycastTarget = false;
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
        Color color = _fadeStartColor;
        color.a = Mathf.Lerp(_fadeStartColor.a, 0f, progress);
        _image.color = color;
        if (progress >= 1f)
            Destroy(gameObject);
    }
}
