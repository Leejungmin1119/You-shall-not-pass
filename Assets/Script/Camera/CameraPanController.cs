using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/*
/// <summary>
/// 공격자를 화면 안에 유지하면서 방향키로 허용된 끝점까지 시야를 이동한다.
/// </summary>
public class CameraPanController : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private CinemachinePositionComposer positionComposer;
    [SerializeField] private Camera outputCamera;
    [SerializeField] private BoxCollider2D cameraBounds;
    [SerializeField] private InputActionReference panInput;// 리바인딩 할때 Input에서 하도록 교체

    [Header("이동 설정")]
    [SerializeField, Min(0f)] private float panSpeed = 8f;
    [SerializeField, Min(0f)] private float returnSpeed = 10f;
    [Tooltip("공격자와 화면 좌우 가장자리 사이에 확보할 여백. 전체 화면 폭에 대한 비율입니다.")]
    [SerializeField, Range(0f, 0.49f)] private float screenEdgeMargin = 0.2f;

    private CinemachineCamera cinemachineCamera;
    private Transform trackingTarget;
    private Vector3 baseTargetOffset;
    private float currentPanOffset;
    private float minPanOffset;
    private float maxPanOffset;
    private bool hasBaseTargetOffset;
    private bool isInitialized;

    /// <summary>
    /// 필수 참조와 직교 카메라·축 정렬 경계를 확인하고 기본 오프셋을 저장한다.
    /// </summary>
    private void Awake()
    {
        // 예외처리
        if (positionComposer == null)
        {
            Debug.LogError("CameraPanController에 CinemachinePositionComposer가 지정되지 않았습니다.", this);
            enabled = false;
            return;
        }

        if (outputCamera == null || !outputCamera.orthographic)
        {
            Debug.LogError("CameraPanController에는 화면을 출력하는 직교 Camera가 필요합니다.", this);
            enabled = false;
            return;
        }

        if (cameraBounds == null || !cameraBounds.enabled || !cameraBounds.isTrigger)
        {
            Debug.LogError("CameraPanController에는 활성화된 Trigger BoxCollider2D 경계가 필요합니다.", this);
            enabled = false;
            return;
        }

        if (Quaternion.Angle(cameraBounds.transform.rotation, Quaternion.identity) > 0.01f ||
            cameraBounds.bounds.size.x <= 0f || cameraBounds.bounds.size.y <= 0f)
        {
            Debug.LogError("CameraPanController의 경계는 회전하지 않은 유효한 직사각형이어야 합니다.", this);
            enabled = false;
            return;
        }

        if (panInput == null || panInput.action == null)
        {
            Debug.LogError("CameraPanController에 카메라 전용 좌우 입력 액션이 지정되지 않았습니다.", this);
            enabled = false;
            return;
        }

        cinemachineCamera = positionComposer.GetComponent<CinemachineCamera>();
        trackingTarget = cinemachineCamera != null ? cinemachineCamera.Follow : null;

        if (trackingTarget == null)
        {
            Debug.LogError("CinemachineCamera에 Tracking Target이 지정되지 않았습니다.", this);
            enabled = false;
            return;
        }

        if (Vector3.Dot(trackingTarget.right, Vector3.right) < 0.999f)
        {
            Debug.LogError("CameraPanController는 회전하지 않은 XY 평면 추적 대상만 지원합니다.", this);
            enabled = false;
            return;
        }

        baseTargetOffset = positionComposer.TargetOffset;
        hasBaseTargetOffset = true;
        currentPanOffset = 0f;
        isInitialized = true;
    }

    /// <summary>
    /// 초기화가 완료된 카메라 전용 입력 액션을 활성화한다.
    /// </summary>
    private void OnEnable()
    {
        if (isInitialized)
        {
            panInput.action.Enable();
        }
    }

    /// <summary>
    /// 입력을 해제하고 수동 이동량과 TargetOffset을 기본 구도로 복원한다.
    /// </summary>
    private void OnDisable()
    {
        // 초기화 및 입력 해제
        if (panInput != null && panInput.action != null)
        {
            panInput.action.Disable();
        }

        currentPanOffset = 0f;

        if (positionComposer != null && hasBaseTargetOffset)
        {
            positionComposer.TargetOffset = baseTargetOffset;
        }
    }

    /// <summary>
    /// 현재 허용 범위를 갱신하고 입력으로 선택한 목표까지 시야를 이동한다.
    /// </summary>
    private void Update()
    {
        // 외부에서 공격자를 교체하면 Cinemachine의 새 추적 대상을 그대로 사용한다.
        trackingTarget = cinemachineCamera.Follow;
        if (trackingTarget == null) return;

        UpdatePanLimits();

        float inputX = panInput.action.ReadValue<float>();
        float targetOffset = GetTargetPanOffset(inputX);
        float speed = Mathf.Approximately(inputX, 0f) ? returnSpeed : panSpeed;

        MoveToPanOffset(targetOffset, speed);
    }

    /// <summary>
    /// 맵 경계와 공격자의 화면 여백을 함께 만족하는 좌우 이동 한계를 갱신한다.
    /// </summary>
    private void UpdatePanLimits()
    {
        Bounds bounds = cameraBounds.bounds;
        float halfViewWidth = outputCamera.orthographicSize * outputCamera.aspect;
        float viewWidth = halfViewWidth * 2f;

        // 기본 화면 구도를 카메라 중심의 월드 좌표로 환산한다.
        float trackedPointX = trackingTarget.position.x +
                              (trackingTarget.rotation * baseTargetOffset).x;
        float screenPositionOffset = positionComposer.Composition.ScreenPosition.x * viewWidth;
        float baseCameraCenterX = trackedPointX - screenPositionOffset;

        float minCameraCenterX = bounds.min.x + halfViewWidth;
        float maxCameraCenterX = bounds.max.x - halfViewWidth;

        if (minCameraCenterX > maxCameraCenterX)
        {
            // 화면이 맵보다 넓으면 좌우 이동 대신 맵 중앙을 목표로 한다.
            minCameraCenterX = maxCameraCenterX = bounds.center.x;
        }
        else
        {
            float visibleDistance = halfViewWidth - viewWidth * screenEdgeMargin;
            float attackerX = trackingTarget.position.x;
            float minVisibleCenterX = attackerX - visibleDistance;
            float maxVisibleCenterX = attackerX + visibleDistance;

            float minAllowedCenterX = Mathf.Max(minCameraCenterX, minVisibleCenterX);
            float maxAllowedCenterX = Mathf.Min(maxCameraCenterX, maxVisibleCenterX);

            if (minAllowedCenterX <= maxAllowedCenterX)
            {
                minCameraCenterX = minAllowedCenterX;
                maxCameraCenterX = maxAllowedCenterX;
            }
            else
            {
                // 공격자가 맵 밖에 있는 등 두 조건이 충돌하면 맵 경계를 우선한다.
                minCameraCenterX = maxCameraCenterX =
                    Mathf.Clamp(attackerX, minCameraCenterX, maxCameraCenterX);
            }
        }

        minPanOffset = minCameraCenterX - baseCameraCenterX;
        maxPanOffset = maxCameraCenterX - baseCameraCenterX;
    }

    /// <summary>
    /// 입력 방향의 끝점을 선택하고, 입력이 없으면 허용 범위 안의 기본 구도를 선택한다.
    /// </summary>
    private float GetTargetPanOffset(float inputX)
    {
        if (Mathf.Approximately(inputX, 0f))
            return Mathf.Clamp(0f, minPanOffset, maxPanOffset);

        return inputX < 0f ? minPanOffset : maxPanOffset;
    }

    /// <summary>
    /// 현재 이동량을 목표까지 이동시킨 뒤 기본 오프셋에 더해 적용한다.
    /// </summary>
    private void MoveToPanOffset(float targetOffset, float speed)
    {
        currentPanOffset = Mathf.MoveTowards(currentPanOffset, targetOffset, speed * Time.deltaTime);

        // 공격자의 이동으로 범위가 좁아져도 이전 이동량이 경계 밖에 남지 않게 한다.
        currentPanOffset = Mathf.Clamp(currentPanOffset, minPanOffset, maxPanOffset);

        Vector3 targetOffsetVector = baseTargetOffset;
        targetOffsetVector.x += currentPanOffset;
        positionComposer.TargetOffset = targetOffsetVector;
    }
}
*/
