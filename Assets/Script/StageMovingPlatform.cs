using Mirror;
using UnityEngine;

/// <summary>공통 경기 시각으로 왕복하며, 위에 서 있는 로컬 공격자를 함께 이동시키는 발판.</summary>
[RequireComponent(typeof(BoxCollider2D), typeof(Rigidbody2D))]
public sealed class StageMovingPlatform : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private Vector2 travelOffset = new Vector2(8f, 0f);
    [SerializeField, Min(0.1f)] private float oneWayDuration = 3f;

    private Rigidbody2D body;
    private BoxCollider2D surface;
    private Vector2 home;
    private PlayerState rider;
    private Rigidbody2D riderBody;
    private Collider2D riderShape;

    /// <summary>발판의 기준 위치와 충돌체를 보관한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        surface = GetComponent<BoxCollider2D>();
        home = body.position;
    }

    /// <summary>모든 화면에서 같은 경로를 계산하고 소유자의 물리 위치만 운반한다.</summary>
    private void FixedUpdate()
    {
        if (race == null || !race.IsRunning)
            return;

        double duration = Mathf.Max(0.1f, oneWayDuration);
        double age = System.Math.Max(0d, NetworkTime.time - race.PhaseStartedAt);
        float progress = (float)((age % (duration * 2d)) / duration);
        float t = progress <= 1f ? progress : 2f - progress;
        Vector2 next = home + travelOffset * t;
        Vector2 delta = next - body.position;

        FindLocalRider();

        if (IsStandingOnTop())
            riderBody.position += delta;

        // 이동량을 직접 운반하므로 발판 자체의 물리 속도로 두 번 밀지 않는다.
        body.position = next;
    }

    /// <summary>플레이어 생성 후 로컬 소유자의 참조를 한 번 보관한다.</summary>
    private void FindLocalRider()
    {
        if (rider != null || NetworkClient.localPlayer == null)
            return;

        rider = NetworkClient.localPlayer.GetComponent<PlayerState>();
        riderBody = NetworkClient.localPlayer.GetComponent<Rigidbody2D>();
        riderShape = NetworkClient.localPlayer.GetComponent<Collider2D>();
    }

    /// <summary>발판 윗면에 서 있는 공격자만 운반하고 점프·옆면 접촉은 제외한다.</summary>
    private bool IsStandingOnTop()
    {
        if (rider == null || !rider.CanMove || riderBody == null || !riderBody.simulated ||
            riderShape == null || !riderShape.enabled || riderBody.linearVelocity.y > 0.1f)
            return false;

        Bounds feet = riderShape.bounds;
        Bounds floor = surface.bounds;
        return feet.max.x > floor.min.x && feet.min.x < floor.max.x &&
            Mathf.Abs(feet.min.y - floor.max.y) <= 0.18f;
    }
}
