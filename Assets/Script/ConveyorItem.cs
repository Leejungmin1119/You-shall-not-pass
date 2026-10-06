using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 컨베이어에서 오른쪽으로 이동하고 우클릭 획득 연출을 처리하는 UI 아이템이다.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(UnityEngine.UI.Image))]
public class ConveyorItem : MonoBehaviour,IPointerClickHandler
{
    private RectTransform _rectTransform;
    private UnityEngine.UI.Image _image;
    private RectTransform _viewport;
    private Color _fadeStartColor;

    [Header("Collect Effect")]
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.35f;

    [Header("이동속도")]
    private float _moveSpeed;
    private float _fadeElapsed;

    // 수집요소 확인
    private bool _isCollecting = false;

    /// <summary>
    /// 반복해서 사용할 컴포넌트 참조를 미리 보관한다.
    /// </summary>
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<UnityEngine.UI.Image>();
    }

    /// <summary>
    /// 생성한 컨트롤러와 Viewport, 이동 속도를 전달받아 동작 가능한 상태로 만든다.
    /// </summary>
    public void Initialize(RectTransform viewport, float moveSpeed)
    {
        if ( viewport == null || moveSpeed <= 0f)
        {
            Debug.LogError("ConveyorItem 초기화에 Controller, Viewport, 양수 이동 속도가 필요합니다.", this);
            enabled = false;
            return;
        }

        //초기화
        _viewport = viewport;
        _moveSpeed = moveSpeed;
        _fadeElapsed = 0f;
        _image.raycastTarget = true;
    }

    /// <summary>
    /// 일반 상태에서는 오른쪽으로 이동하고, 획득 중에는 투명화 연출만 진행한다.
    /// </summary>
    private void Update()
    {
        if (_isCollecting)
        {
            UpdateCollectFade();
            return;
        }

        //이동
        _rectTransform.anchoredPosition += Vector2.right * (_moveSpeed * Time.deltaTime);

        if (HasCompletelyExitedViewport())
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 오른쪽 포인터 버튼 입력만 컨트롤러의 획득 요청으로 전달한다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isCollecting || eventData.button != PointerEventData.InputButton.Right)
        {
            return;
        }

        BeginCollect();
    }

    /// <summary>
    /// 중복 요청을 무시하고 이동과 클릭을 즉시 차단한 뒤 투명화를 시작한다.
    /// </summary>
    public void BeginCollect()
    {
        if (_isCollecting)
        {
            return;
        }

        _isCollecting = true;
        _fadeElapsed = 0f;
        _fadeStartColor = _image.color;
        _image.raycastTarget = false;

        // 장비 추가시 여기다가 장비넣는 스크립트 함수를 호출
    }

    /// <summary>
    /// 아이템의 왼쪽 끝이 Viewport의 오른쪽 끝을 넘었는지 같은 로컬 좌표계에서 확인한다.
    /// </summary>
    private bool HasCompletelyExitedViewport()
    {
        Bounds itemBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_viewport, _rectTransform);
        return itemBounds.min.x > _viewport.rect.xMax;
    }

    /// <summary>
    /// 설정 시간 동안 알파를 줄이고 완료 로그를 한 번 남긴 뒤 아이템을 삭제한다.
    /// </summary>
    private void UpdateCollectFade()
    {
        _fadeElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_fadeElapsed / fadeDuration);

        Color color = _fadeStartColor;
        color.a = Mathf.Lerp(_fadeStartColor.a, 0f, progress);
        _image.color = color;

        if (progress < 1f)
        {
            return;
        }

        Debug.Log("습득 완료.");
        Destroy(gameObject);
    }
}
