using Mirror;
using UnityEngine;

/// <summary>화염구 묶음을 일정 각속도로 회전시키고 서버에서 피격·부활을 처리한다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(NetworkIdentity))]
public sealed class RotatingFirebarTrap : NetworkBehaviour
{
    [SerializeField] private Transform rotor;
    [SerializeField] private RaceManager race;
    [SerializeField] private FallZone fallZone;
    [SerializeField] private bool clockwise = false;
    [SerializeField, Min(0.1f)] private float degreesPerSecond = 6f;
    [SerializeField] private float startingAngle = 45f;
    [SyncVar] private double rotationEpoch;
    private CircleCollider2D[] fireballs;
    private Vector3[] localCenters;
    private float[] radii;
    private double previousCheckTime;
    private int observedRound = -1;
    public bool Clockwise => clockwise;
    public float DegreesPerSecond => degreesPerSecond;

    private void Awake()
    {
        if (rotor == null) return;
        fireballs = rotor.GetComponentsInChildren<CircleCollider2D>(true);
        localCenters = new Vector3[fireballs.Length];
        radii = new float[fireballs.Length];
        for (int i = 0; i < fireballs.Length; i++)
        {
            CircleCollider2D ball = fireballs[i];
            localCenters[i] = rotor.InverseTransformPoint(ball.transform.TransformPoint(ball.offset));
            Vector3 scale = ball.transform.lossyScale;
            radii[i] = ball.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (race == null) race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        if (fallZone == null) fallZone = FindFirstObjectByType<FallZone>(FindObjectsInactive.Include);
        rotationEpoch = previousCheckTime = NetworkTime.time;
        observedRound = race != null ? race.RoundNumber : -1;
    }

    private void LateUpdate()
    {
        if (rotor != null && (isServer || isClient))
            rotor.localRotation = Quaternion.Euler(0f, 0f, AngleAt(NetworkTime.time));
    }

    private float AngleAt(double serverTime)
    {
        double angle = (serverTime - rotationEpoch) * degreesPerSecond * (clockwise ? -1d : 1d);
        return startingAngle + (float)(angle % 360d);
    }

    private Vector2 WorldCenter(int index, double serverTime)
    {
        Vector3 local = rotor.localPosition + Quaternion.Euler(0f, 0f, AngleAt(serverTime)) *
            Vector3.Scale(localCenters[index], rotor.localScale);
        return rotor.parent != null ? rotor.parent.TransformPoint(local) : local;
    }

    private void FixedUpdate()
    {
        if (!isServer || rotor == null || fireballs == null) return;
        double now = NetworkTime.time;
        if (race == null || fallZone == null || NetworkServer.isLoadingScene)
        {
            previousCheckTime = now;
            return;
        }
        if (observedRound != race.RoundNumber)
        {
            observedRound = race.RoundNumber;
            rotationEpoch = previousCheckTime = now;
        }
        if (!race.IsRunning)
        {
            previousCheckTime = now;
            return;
        }
        // 긴 프레임도 회전 호를 짧은 선분들로 나누어 화염구가 플레이어를 건너뛰지 않게 한다.
        int steps = Mathf.Clamp(Mathf.CeilToInt((float)(now - previousCheckTime) * degreesPerSecond / 2f), 1, 180);
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !identity.TryGetComponent(out PlayerState player) ||
                !player.CanReceiveHit || !player.TryGetComponent(out CapsuleCollider2D capsule) ||
                !capsule.enabled) continue;
            CapsuleSegment(capsule, out Vector2 a, out Vector2 b, out float playerRadius);
            bool hit = false;
            for (int i = 0; i < fireballs.Length && !hit; i++)
            {
                if (!fireballs[i].enabled) continue;
                Vector2 from = WorldCenter(i, previousCheckTime);
                float radius = radii[i] + playerRadius;
                for (int step = 1; step <= steps; step++)
                {
                    double t = previousCheckTime + (now - previousCheckTime) * step / steps;
                    Vector2 to = WorldCenter(i, t);
                    if (SegmentDistanceSquared(from, to, a, b) <= radius * radius)
                    {
                        hit = true;
                        break;
                    }
                    from = to;
                }
            }
            if (hit && fallZone.TryGetRespawnPosition(player.transform.position, out Vector2 position) && player.TryDie())
                player.Respawn(position);
        }
        previousCheckTime = now;
    }

    /// <summary>서버에서 물리가 비활성인 원격 캐릭터도 동일한 실제 캡슐 크기로 판정한다.</summary>
    private static void CapsuleSegment(CapsuleCollider2D capsule, out Vector2 a, out Vector2 b, out float radius)
    {
        Vector2 center = capsule.transform.TransformPoint(capsule.offset);
        Vector3 scale = capsule.transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(capsule.size.x * scale.x), Mathf.Abs(capsule.size.y * scale.y));
        bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
        radius = (vertical ? size.x : size.y) * 0.5f;
        float halfLine = Mathf.Max(0f, (vertical ? size.y : size.x) * 0.5f - radius);
        Vector2 axis = vertical ? (Vector2)capsule.transform.up : (Vector2)capsule.transform.right;
        a = center - axis * halfLine;
        b = center + axis * halfLine;
    }

    private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a, cd = d - c;
        float cross = Cross(ab, cd);
        if (Mathf.Abs(cross) > 0.000001f)
        {
            Vector2 offset = c - a;
            float t = Cross(offset, cd) / cross;
            float u = Cross(offset, ab) / cross;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }
        return Mathf.Min(Mathf.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
            Mathf.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static float PointSegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) : 0f;
        return (point - (a + delta * t)).sqrMagnitude;
    }
}
