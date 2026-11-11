using Mirror;
using UnityEngine;

/// <summary>레이스 시작 시각을 기준으로 8초간 옅어지고, 충돌이 없는 3초 동안 다시 나타나는 바닥.</summary>
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
    [Tooltip("밟을 수 없는 시간의 마지막 구간에 다시 나타납니다. 완전히 나타나야 충돌이 켜집니다.")]
    [SerializeField, Min(0.1f)] private float reappearDuration = 2f;
    [SerializeField, Range(0f, 1f)] private float maximumOpacity = 0.65f;

    private BoxCollider2D floorCollider;
    private bool[] originalRespawnEnabled;
    private bool originalColliderEnabled;
    private Color originalColor;
    private double localEpoch;

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

    private void OnEnable()
    {
        localEpoch = Time.timeAsDouble;
    }

    private void FixedUpdate()
    {
        EvaluateCycle(out bool walkable, out _);
        SetCollision(walkable);
    }

    private void LateUpdate()
    {
        if (visual == null) return;
        EvaluateCycle(out _, out float opacity);
        Color color = originalColor;
        color.a = opacity;
        visual.color = color;
    }

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
            opacity = maximumOpacity * (1f - Mathf.SmoothStep(0f, 1f, (float)(age / solidSeconds)));
            return;
        }

        double fadeInSeconds = System.Math.Min(absentSeconds, System.Math.Max(0.1d, reappearDuration));
        double fadeInStart = solidSeconds + absentSeconds - fadeInSeconds;
        opacity = maximumOpacity * Mathf.SmoothStep(0f, 1f, (float)((age - fadeInStart) / fadeInSeconds));
    }

    private void SetCollision(bool walkable)
    {
        // Renderer만 숨기면 보이지 않는 바닥이 남으므로 실제 지면과 부활 후보도 함께 끈다.
        floorCollider.enabled = walkable && originalColliderEnabled;
        for (int i = 0; i < respawnAreas.Length; i++)
            if (respawnAreas[i] != null)
                respawnAreas[i].enabled = walkable && originalRespawnEnabled[i];
    }

    private void OnDisable()
    {
        if (floorCollider != null) SetCollision(true);
        if (visual != null) visual.color = originalColor;
    }
}
