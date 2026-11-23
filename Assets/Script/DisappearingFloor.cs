using Mirror;
using UnityEngine;

/// <summary>공통 레이스 시각에 맞춰 일반 표시·느린 깜박임·빠른 깜박임 후 즉시 사라지는 바닥.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
public sealed class DisappearingFloor : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private RaceManager race;
    [Tooltip("사라진 바닥 위에서 재부활하지 않도록 같은 주기로 꺼 둘 SafeZone입니다.")]
    [SerializeField] private Collider2D[] respawnAreas = new Collider2D[0];

    [Header("사라짐 주기")]
    [SerializeField, Min(0.1f)] private float walkableDuration = 8f;
    [SerializeField, Min(0.1f)] private float unavailableDuration = 3f;
    [SerializeField, Range(0f, 1f)] private float maximumOpacity = 0.65f;

    [Header("사라지기 전 깜박임")]
    [Tooltip("깜박임이 옅어질 때의 불투명도입니다. 0.3이면 30%까지 보이며, 실제로 사라진 동안에는 0%가 됩니다. 최대 불투명도를 넘지 않습니다.")]
    [SerializeField, Range(0f, 1f)] private float minimumBlinkOpacity = 0.3f;
    [Tooltip("밟을 수 있는 시간의 마지막에 배치됩니다. 기본값은 일반 4초, 느린 2.5초, 빠른 1.5초입니다.")]
    [SerializeField, Min(0f)] private float slowBlinkDuration = 2.5f;
    [SerializeField, Min(0f)] private float fastBlinkDuration = 1.5f;
    [Tooltip("보임과 숨김이 바뀌는 간격입니다. 한 번의 전체 깜박임은 이 값의 두 배입니다.")]
    [SerializeField, Min(0.02f)] private float slowBlinkInterval = 0.4f;
    [SerializeField, Min(0.02f)] private float fastBlinkInterval = 0.1f;

    private BoxCollider2D floorCollider;
    private bool[] originalRespawnEnabled;
    private bool originalColliderEnabled;
    private Color originalColor;
    private double localEpoch;

    /// <summary>표시와 충돌의 원래 상태를 기억해 비활성화 시 복구한다.</summary>
    private void Awake()
    {
        floorCollider = GetComponent<BoxCollider2D>();
        if (race == null) race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        originalColliderEnabled = floorCollider.enabled;
        originalColor = visual != null ? visual.color : Color.white;
        originalRespawnEnabled = new bool[respawnAreas.Length];
        for (int i = 0; i < respawnAreas.Length; i++)
            originalRespawnEnabled[i] = respawnAreas[i] != null && respawnAreas[i].enabled;
    }

    /// <summary>레이스 관리자가 없는 단독 실행에서는 활성화 시각부터 주기를 센다.</summary>
    private void OnEnable()
    {
        localEpoch = Time.timeAsDouble;
    }

    /// <summary>깜박임과 별개로 밟을 수 있는 구간 전체에 충돌을 유지한다.</summary>
    private void FixedUpdate()
    {
        EvaluateCycle(out bool walkable, out _);
        SetCollision(walkable);
    }

    /// <summary>현재 단계의 불투명도를 화면에 적용한다.</summary>
    private void LateUpdate()
    {
        if (visual == null) return;
        EvaluateCycle(out _, out float opacity);
        Color color = originalColor;
        color.a = opacity;
        visual.color = color;
    }

    /// <summary>공통 시각으로 현재 단계와 충돌 여부를 계산한다. 주기마다 별도 타이머를 누적하지 않는다.</summary>
    private void EvaluateCycle(out bool walkable, out float opacity)
    {
        walkable = true;
        opacity = maximumOpacity;
        double elapsed;
        if (race != null)
        {
            // 맵 소개·카운트다운에서는 바닥을 유지하고, 라운드 시작 때마다 같은 주기를 시작한다.
            if (race.Phase != RacePhase.Racing && race.Phase != RacePhase.FinishCountdown && !race.IsDeathPresentation)
                return;
            elapsed = System.Math.Max(0d, race.SimulationTime - race.PhaseStartedAt);
        }
        else
        {
            elapsed = Time.timeAsDouble - localEpoch;
        }

        // 매 프레임 전송하거나 PC마다 타이머를 누적하지 않고 기존 공통 레이스 시각을 사용한다.
        double solidSeconds = System.Math.Max(0.1d, walkableDuration);
        double absentSeconds = System.Math.Max(0.1d, unavailableDuration);
        double age = elapsed % (solidSeconds + absentSeconds);
        walkable = age < solidSeconds;
        if (walkable)
        {
            // 전체 8초 안에서 마지막부터 빠른 깜박임, 느린 깜박임을 배치한다.
            // 인스펙터에서 시간을 줄여도 경고 구간이 전체 시간을 초과하지 않는다.
            double fastSeconds = System.Math.Min(solidSeconds, System.Math.Max(0d, fastBlinkDuration));
            double slowSeconds = System.Math.Min(solidSeconds - fastSeconds, System.Math.Max(0d, slowBlinkDuration));
            double fastStart = solidSeconds - fastSeconds;
            double slowStart = fastStart - slowSeconds;

            if (age >= fastStart)
                opacity = BlinkOpacity(age - fastStart, fastBlinkInterval);
            else if (age >= slowStart)
                opacity = BlinkOpacity(age - slowStart, slowBlinkInterval);

            return;
        }

        // 없는 3초 동안은 완전히 숨긴다. 다음 주기가 시작되면 표시와 충돌을 즉시 복구한다.
        opacity = 0f;
    }

    /// <summary>최대 불투명도와 설정한 최소 불투명도를 번갈아 반환한다.</summary>
    private float BlinkOpacity(double elapsed, float interval)
    {
        double step = System.Math.Floor(elapsed / System.Math.Max(0.02d, interval));
        float maximum = Mathf.Clamp01(maximumOpacity);
        float minimum = Mathf.Clamp(minimumBlinkOpacity, 0f, maximum);
        return step % 2d < 1d ? maximum : minimum;
    }

    /// <summary>실제 발판과 연결된 부활 영역의 충돌을 같은 상태로 맞춘다.</summary>
    private void SetCollision(bool walkable)
    {
        // Renderer만 숨기면 보이지 않는 바닥이 남으므로 실제 지면과 부활 후보도 함께 끈다.
        floorCollider.enabled = walkable && originalColliderEnabled;
        for (int i = 0; i < respawnAreas.Length; i++)
            if (respawnAreas[i] != null)
                respawnAreas[i].enabled = walkable && originalRespawnEnabled[i];
    }

    /// <summary>컴포넌트를 끄면 원래의 충돌과 색상으로 복구한다.</summary>
    private void OnDisable()
    {
        if (floorCollider != null) SetCollision(true);
        if (visual != null) visual.color = originalColor;
    }
}
