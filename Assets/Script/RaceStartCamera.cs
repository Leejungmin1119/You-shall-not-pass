using Mirror;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>각 화면에서 서버의 라운드 시각에 맞춰 출발점부터 골인점까지 보여준다.</summary>
[DefaultExecutionOrder(-100)]
public sealed class RaceStartCamera : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private CinemachineCamera previewCamera;
    [SerializeField] private CinemachineCamera trackingCamera;
    [SerializeField] private Transform startView;
    [SerializeField] private Transform goalView;
    [Tooltip("Tab 전체 보기와 같은 맵 경계. 지정하면 줌아웃→골인 클로즈업→플레이어 복귀 순서로 연출한다. 비워 두면 출발→골인 직선 이동(기존 동작).")]
    [SerializeField] private BoxCollider2D overviewBounds;
    [Tooltip("골인 지점을 클로즈업할 카메라 크기(Orthographic Size).")]
    [SerializeField, Min(1f)] private float goalCloseUpSize = 5f;

    private PrioritySettings originalPriority;
    private float baseOrthographicSize;
    private bool showingPreview;

    /// <summary>씬에서 지정한 연출 카메라의 기본 우선순위를 보관한다.</summary>
    private void Awake()
    {
        originalPriority = previewCamera.Priority;
    }

    /// <summary>공통 시각으로 카메라를 이동하고, 종료 시 기존 추적 카메라로 복귀한다.</summary>
    private void LateUpdate()
    {
        double elapsed = NetworkTime.time - race.PhaseStartedAt;
        bool shouldPreview = race.Phase == RacePhase.MapPreview && elapsed < race.MapPreviewDuration;

        if (!shouldPreview)
        {
            if (showingPreview)
                EndPreview();

            return;
        }

        if (!showingPreview)
            BeginPreview();

        UpdatePreview((float)elapsed / race.MapPreviewDuration);
    }

    /// <summary>출발 지점부터 보여주도록 배치하고 연출 카메라를 활성 우선순위로 올린다.</summary>
    private void BeginPreview()
    {
        previewCamera.transform.SetPositionAndRotation(startView.position, trackingCamera.transform.rotation);
        previewCamera.Lens = trackingCamera.Lens;
        baseOrthographicSize = previewCamera.Lens.OrthographicSize;
        previewCamera.Priority = trackingCamera.Priority.Value + 10;
        showingPreview = true;
    }

    /// <summary>처음과 끝에서 부드럽게 감속하도록 출발·골인 시점 사이를 보간한다.</summary>
    private void UpdatePreview(float progress)
    {
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        if (overviewBounds == null)
        {
            previewCamera.transform.position = Vector3.Lerp(startView.position, goalView.position, t);
            return;
        }

        // 줌아웃(전체 보기) → 정지 → 골인 클로즈업 → 정지 → 출발점 복귀 순서로 진행한다.
        Bounds bounds = overviewBounds.bounds;
        float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
        float overviewSize = Mathf.Max(bounds.size.y * 0.5f, bounds.size.x * 0.5f / Mathf.Max(0.1f, aspect));
        Vector3 startPos = startView.position;
        Vector3 overviewPos = new Vector3(bounds.center.x, bounds.center.y, startPos.z);
        Vector3 goalPos = goalView.position;
        float p = Mathf.Clamp01(progress);

        Vector3 position;
        float size;
        if (p < 0.22f)
        {
            float k = Mathf.SmoothStep(0f, 1f, p / 0.22f);
            position = Vector3.Lerp(startPos, overviewPos, k);
            size = Mathf.Lerp(baseOrthographicSize, overviewSize, k);
        }
        else if (p < 0.38f)
        {
            position = overviewPos;
            size = overviewSize;
        }
        else if (p < 0.62f)
        {
            float k = Mathf.SmoothStep(0f, 1f, (p - 0.38f) / 0.24f);
            position = Vector3.Lerp(overviewPos, goalPos, k);
            size = Mathf.Lerp(overviewSize, goalCloseUpSize, k);
        }
        else if (p < 0.74f)
        {
            position = goalPos;
            size = goalCloseUpSize;
        }
        else
        {
            float k = Mathf.SmoothStep(0f, 1f, (p - 0.74f) / 0.26f);
            position = Vector3.Lerp(goalPos, startPos, k);
            size = Mathf.Lerp(goalCloseUpSize, baseOrthographicSize, k);
        }

        previewCamera.transform.position = position;
        LensSettings lens = previewCamera.Lens;
        lens.OrthographicSize = size;
        previewCamera.Lens = lens;
    }

    /// <summary>Cinemachine 기본 블렌드를 사용해 자기 플레이어 시점으로 돌아간다.</summary>
    private void EndPreview()
    {
        previewCamera.Priority = originalPriority;
        showingPreview = false;
    }

    /// <summary>씬 종료나 컴포넌트 해제 시 연출 우선순위가 남지 않도록 복원한다.</summary>
    private void OnDisable()
    {
        if (previewCamera != null)
            EndPreview();
    }
}
