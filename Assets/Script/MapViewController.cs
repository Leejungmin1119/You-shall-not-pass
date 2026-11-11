using Mirror;
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
    [Tooltip("전체 보기와 추적 카메라가 사용할 고정 경계입니다. 이동하는 카메라 밖에 배치합니다.")]
    [SerializeField] private BoxCollider2D mapBounds;// 전체 맵

    [Header("재입력 제한")]
    [Tooltip("복귀 전환 시간에 더해 전체 보기를 다시 켜기 전에 기다리는 시간입니다.")]
    [SerializeField, Min(0f)] private float reopenCooldown = 1f;

    [Header("사망 연출 줌")]
    [Tooltip("슬로모션 시작 때 사망한 캐릭터를 확대할 배율입니다.")]
    [SerializeField, Min(1f)] private float deathZoomMultiplier = 2.5f;
    [Tooltip("시간 배율과 관계없이 줌인에 걸리는 실제 시간입니다.")]
    [SerializeField, Min(0.01f)] private float deathZoomDuration = 0.65f;

    private CinemachineBrain brain;
    private PrioritySettings originalMapPriority;// 기존 우선순위(변환 후 되돌릴때 사용)
    private Bounds mapViewBounds;// 전체 맵의 크기를 저장할 변수
    private float lastAspect;//
    private float nextMapViewTime;
    private PlayerState localPlayer;
    private PlayerState observedPlayer;
    private bool wasSpectating;
    [SerializeField] private RaceManager race;
    public bool IsMapView { get; private set; }
    private Transform deathTarget;
    private CinemachineConfiner2D deathConfiner;
    private bool previousConfinerEnabled;
    private LensSettings previousDeathLens;
    private bool hasDeathLens;

    /// <summary>사망 중에는 쓰러진 몸을 추적한다. 맵 끝에서도 연출이 잘리지 않도록 경계 제한만 잠시 해제한다.</summary>
    public void SetDeathTarget(Transform target)
    {
        if (trackingCamera == null)
            return;

        if (target != null && deathTarget == null)
        {
            previousDeathLens = trackingCamera.Lens;
            hasDeathLens = true;
            deathConfiner = trackingCamera.GetComponent<CinemachineConfiner2D>();
            if (deathConfiner != null)
            {
                previousConfinerEnabled = deathConfiner.enabled;
                deathConfiner.enabled = false;
            }
        }

        deathTarget = target;
        if (target != null)
            trackingCamera.Follow = target;
        else
        {
            if (hasDeathLens)
            {
                trackingCamera.Lens = previousDeathLens;
                hasDeathLens = false;
            }
            if (deathConfiner != null)
                deathConfiner.enabled = previousConfinerEnabled;
            deathConfiner = null;
            if (observedPlayer != null)
                trackingCamera.Follow = observedPlayer.transform;
        }
    }

    /// <summary>서버의 사망 경과로 줌인한 뒤 교대까지 유지한다. 슬로모션이 풀려도 축소하지 않는다.</summary>
    private void UpdateDeathZoom()
    {
        if (!hasDeathLens || deathTarget == null || race == null || !race.IsDeathPresentation)
            return;

        float elapsed = (float)(race.DeathElapsed - DeathSequence.HitStopDuration);
        float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / deathZoomDuration));
        LensSettings lens = previousDeathLens;
        lens.OrthographicSize = Mathf.Lerp(previousDeathLens.OrthographicSize,
            previousDeathLens.OrthographicSize / Mathf.Max(1f, deathZoomMultiplier), progress);
        trackingCamera.Lens = lens;
    }

    /// <summary>
    /// 필수 참조를 확인하고 전체 보기 영역의 월드 좌표와 카메라 우선순위를 보관합니다.
    /// </summary>
    private void Awake()
    {
        if (!ValidateReferences())
            return;

        // 카메라와 분리된 고정 경계의 월드 좌표를 보관합니다.
        mapViewBounds = mapBounds.bounds;
        originalMapPriority = mapCamera.Priority;
        SetMapView(false);
    }
    /// <summary>
    /// 필수 참조와 고정 경계 연결을 확인하고 출력 카메라의 Brain을 보관한다.
    /// </summary>
    private bool ValidateReferences()
    {
        if (trackingCamera == null || mapCamera == null || outputCamera == null ||mapBounds == null)
        {
            Debug.LogError("필요한 참조가 부족합니다.");
            enabled = false;
            return false;
        }

        brain = outputCamera.GetComponent<CinemachineBrain>();
        if (brain == null)
        {
            Debug.LogError("전체 보기 전환을 위해 출력 Camera에 CinemachineBrain이 필요합니다.", this);
            enabled = false;
            return false;
        }

        if (mapBounds.transform.IsChildOf(mapCamera.transform))
        {
            Debug.LogError("맵 경계는 이동하는 MapCamera 밖의 고정 오브젝트에 배치해야 합니다.", this);
            enabled = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 실행 중 생성된 플레이어를 기존 추적 카메라의 대상으로 지정한다.
    /// 전체 맵 카메라는 추적 대상을 사용하지 않는다.
    /// </summary>
    public void SetTrackingTarget(Transform target)
    {
        localPlayer = target != null ? target.GetComponent<PlayerState>() : null;
        observedPlayer = localPlayer;
        trackingCamera.Follow = target;
    }

    /// <summary>골인하면 미도착자를 관전하고 다음 라운드에는 자기 캐릭터로 복귀한다.</summary>
    private void UpdateSpectatorTarget()
    {
        if (deathTarget != null && race != null && race.IsDeathPresentation)
        {
            trackingCamera.Follow = deathTarget;
            return;
        }

        if (localPlayer == null)
            return;

        if (race != null && race.Roles != null)
        {
            uint attackerId = race.Roles.AttackerNetId;

            if (NetworkClient.spawned.TryGetValue(attackerId, out NetworkIdentity attacker) &&
                (observedPlayer == null || observedPlayer.netId != attackerId))
            {
                observedPlayer = attacker.GetComponent<PlayerState>();
                trackingCamera.Follow = attacker.transform;
            }

            wasSpectating = !localPlayer.IsAttacker;
            return;
        }

        bool spectating = localPlayer.Status == PlayerStatus.Finished;

        if (!spectating)
        {
            if (wasSpectating)
            {
                observedPlayer = localPlayer;
                trackingCamera.Follow = localPlayer.transform;
                SetMapView(false);
            }
        }
        else if (!wasSpectating || observedPlayer == null || observedPlayer.Status == PlayerStatus.Finished)
        {
            // 관전 대상이 골인하거나 퇴장했을 때만 접속자 목록을 다시 확인한다.
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out PlayerState candidate) ||
                    candidate == localPlayer || candidate.Status == PlayerStatus.Finished)
                    continue;

                observedPlayer = candidate;
                trackingCamera.Follow = candidate.transform;
                break;
            }
        }

        wasSpectating = spectating;
    }

    /// <summary>
    /// 입력 확인 InputManager 입력후 확인해야 하기에 LateUpdate() 사용
    /// </summary>
    private void LateUpdate()
    {
        // 시작 연출과 카운트다운 동안에는 Tab과 관전 처리가 카메라를 바꾸지 않는다.
        if (race != null && race.IsPreparing)
        {
            // 다음 라운드 준비에 들어오면 관전하던 대상을 자기 플레이어로 되돌린다.
            if (wasSpectating && localPlayer != null)
            {
                observedPlayer = localPlayer;
                trackingCamera.Follow = localPlayer.transform;
                wasSpectating = false;
            }

            if (IsMapView)
                SetMapView(false);

            return;
        }

        UpdateSpectatorTarget();
        UpdateDeathZoom();

        // 관전 중 방향키 입력은 Tab 전체 보기를 닫지 않는다.
        if ((localPlayer == null || localPlayer.CanMove) &&
            (InputManager.Movement.x != 0f || InputManager.Movement.y != 0f))
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
        SetDeathTarget(null);
        IsMapView = false;
        if (mapCamera != null)
            mapCamera.Priority = originalMapPriority;
    }
}
