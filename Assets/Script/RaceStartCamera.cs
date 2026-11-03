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

    private PrioritySettings originalPriority;
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
        previewCamera.Priority = trackingCamera.Priority.Value + 10;
        showingPreview = true;
    }

    /// <summary>처음과 끝에서 부드럽게 감속하도록 출발·골인 시점 사이를 보간한다.</summary>
    private void UpdatePreview(float progress)
    {
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        previewCamera.transform.position = Vector3.Lerp(startView.position, goalView.position, t);
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
