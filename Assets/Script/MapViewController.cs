using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 전체 맵을 보여주게 하는 스크립트
/// </summary>
public class MapViewController : MonoBehaviour
{
    [Header("카메라 참조")]
    [SerializeField] private CinemachineCamera trackingCamera;// 기존 플래이어 추적 카메라
    [SerializeField] private CinemachineCamera mapCamera;// 맵을 보여줄 카메라
    [SerializeField] private Camera outputCamera;// ?
    [Tooltip("Tab 전체 보기로 표시할 영역입니다. MapCamera에 붙인 BoxCollider2D도 지정할 수 있습니다.")]
    [SerializeField] private BoxCollider2D mapBounds;// 전체 맵

    [Header("재입력 제한")]
    [Tooltip("복귀 전환 시간에 더해 전체 보기를 다시 켜기 전에 기다리는 시간입니다.")]
    [SerializeField, Min(0f)] private float reopenCooldown = 1f;

    private CinemachineBrain brain;
    private PrioritySettings originalMapPriority;// 기존 우선순위(변환 후 되돌릴때 사용)
    private Bounds mapViewBounds;// 전체 맵의 크기를 저장할 변수
    private float lastAspect;//
    private float nextMapViewTime;
    public bool IsMapView { get; private set; }

    /// <summary>
    /// 필수 참조를 확인하고 전체 보기 영역의 월드 좌표와 카메라 우선순위를 보관합니다.
    /// </summary>
    private void Awake()
    {
        exceptionCheck();

        // MapCamera에 붙은 콜라이더가 카메라 이동을 따라가도 기준 영역은 고정합니다.
        mapViewBounds = mapBounds.bounds;
        originalMapPriority = mapCamera.Priority;
        SetMapView(false);
    }
    private void exceptionCheck()
    {
        if (trackingCamera == null || mapCamera == null || outputCamera == null ||mapBounds == null)
        {
            Debug.LogError("필요한 참조가 부족합니다.");
            enabled = false;
            return;
        }

        brain = outputCamera.GetComponent<CinemachineBrain>();
        if (brain == null)
        {
            Debug.LogError("전체 보기 전환을 위해 출력 Camera에 CinemachineBrain이 필요합니다.", this);
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// 입력 확인 InputManager 입력후 확인해야 하기에 LateUpdate() 사용
    /// </summary>
    private void LateUpdate()
    {
        // 이동확인(이동시 원래시점 복귀)
        if (InputManager.Movement.x !=  0f || InputManager.Movement.y != 0f)
        {
            if (IsMapView)
                SetMapView(false);
            return;
        }

        // 탭키 입력확인
        if (InputManager.MapViewWasPressed)
            SetMapView(!IsMapView);
    }

    /// <summary>
    /// Map 시야로 카메라 전환
    /// </summary>
    private void ConfigureMapCamera()
    {
        lastAspect = outputCamera.aspect;// 이걸 왜하지?

        //전환, 카메라 위치 설정
        mapCamera.transform.SetPositionAndRotation(
            new Vector3(mapViewBounds.center.x, mapViewBounds.center.y, trackingCamera.transform.position.z),
            trackingCamera.transform.rotation);

        // 보여줄 시각 구한후 적용
        /*
        만약 전체 카메라의 lens를 미리 설정한다면 할 필요 x
        */
        LensSettings lens = trackingCamera.Lens;
        lens.OrthographicSize = Mathf.Max(
            mapViewBounds.size.y * 0.5f,
            mapViewBounds.size.x * 0.5f / lastAspect);

        mapCamera.Lens = lens;
    }

    /// <summary>
    /// Map 열람상태에 따라 닫기 or 열기 수행
    /// </summary>
    public void SetMapView(bool enabled)
    {
        //활성화시
        if (enabled)
        {
            if (Time.time < nextMapViewTime || brain.IsBlending)
                return;

            ConfigureMapCamera();
        }
        else //비활성화
        {
            // 기본 Blend 시간 동안의 복귀와 추가 대기 시간이 지난 뒤 재진입을 허용합니다.
            float returnDuration = brain.DefaultBlend.Style == CinemachineBlendDefinition.Styles.Cut
                ? 0f
                : brain.DefaultBlend.Time;
            nextMapViewTime = Time.time + returnDuration + reopenCooldown;
        }

        IsMapView = enabled;
        mapCamera.Priority = trackingCamera.Priority.Value + (enabled ? 1 : -1);//우선순위 변환(false시 원상태로)
    }

    /// <summary>
    /// 비활성화 시 전체 보기 상태를 해제하고 카메라 우선순위를 복원합니다.
    /// </summary>
    private void OnDisable()
    {
        IsMapView = false;
        if (mapCamera != null)
            mapCamera.Priority = originalMapPriority;
    }
}
