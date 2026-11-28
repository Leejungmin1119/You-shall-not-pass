using Mirror;
using UnityEngine;

/// <summary>서버가 충돌을 결정하고 공통 서버 시각으로 모든 화면에서 수평 이동하는 대포알.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(NetworkIdentity), typeof(CircleCollider2D))]
public sealed class CannonballProjectile : NetworkBehaviour
{
    [SyncVar] private Vector2 origin;
    [SyncVar] private Vector2 direction;
    [SyncVar] private float speed;
    [SyncVar] private double firedAt;
    [SerializeField, Min(0.01f)] private float radius = 0.4f;
    private float lifetime;
    private int roundNumber;
    private RaceManager race;
    private FixedCannonTrap source;
    private Vector2 previousPosition;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[16];

    /// <summary>원격 화면도 같은 레이스의 느려진 이동 시계를 사용한다.</summary>
    private void Awake()
    {
        race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        TimeSkillGrayscale.Register(gameObject);
    }

    public void Initialize(FixedCannonTrap cannon, RaceManager currentRace, Vector2 position,
        Vector2 flightDirection, float flightSpeed, float seconds, LayerMask groundLayer)
    {
        source = cannon;
        race = currentRace;
        roundNumber = race.RoundNumber;
        origin = previousPosition = position;
        direction = flightDirection;
        speed = flightSpeed;
        lifetime = seconds;
        firedAt = (race != null ? race.SimulationTime : NetworkTime.time);
        groundFilter.SetLayerMask(groundLayer);
        groundFilter.useTriggers = false;
    }

    private void Update()
    {
        if (!isServer && isClient)
            transform.position = FlightPosition();
    }

    private Vector2 FlightPosition()
    {
        float age = Mathf.Max(0f, (float)((race != null ? race.SimulationTime : NetworkTime.time) - firedAt));
        return origin + direction * (speed * age);
    }

    private void FixedUpdate()
    {
        if (!isServer) return;
        if (source == null || race == null || !race.IsSimulating || race.RoundNumber != roundNumber ||
            (race != null ? race.SimulationTime : NetworkTime.time) - firedAt >= lifetime)
        {
            NetworkServer.Destroy(gameObject);
            return;
        }

        Vector2 next = FlightPosition();
        float distance = Vector2.Distance(previousPosition, next);
        float wallDistance = float.PositiveInfinity;
        int count = Physics2D.CircleCast(previousPosition, radius, direction,
            groundFilter, groundHits, distance);
        for (int i = 0; i < count; i++)
            wallDistance = Mathf.Min(wallDistance, groundHits[i].distance);

        PlayerState target = null;
        float nearest = wallDistance;
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !identity.TryGetComponent(out PlayerState player) ||
                !player.CanReceiveHit) continue;
            if (TryPlayerContact(player, previousPosition, distance, out float travel) && travel < nearest)
            {
                nearest = travel;
                target = player;
            }
        }
        if (target != null && source.TryKill(target, previousPosition))
        {
            NetworkServer.Destroy(gameObject);
            return;
        }
        if (wallDistance <= distance)
        {
            NetworkServer.Destroy(gameObject);
            return;
        }
        previousPosition = next;
        transform.position = next;
    }

    /// <summary>
    /// 수평 이동 구간과 세로 캡슐의 접촉을 계산한다. 원격 플레이어의 Rigidbody2D는
    /// 서버에서 simulated=false이므로 물리 쿼리만으로는 플레이어를 감지할 수 없다.
    /// </summary>
    private bool TryPlayerContact(PlayerState player, Vector2 from, float distance, out float travel)
    {
        travel = 0f;
        if (!player.TryGetComponent(out CapsuleCollider2D capsule) || !capsule.enabled ||
            capsule.direction != CapsuleDirection2D.Vertical) return false;
        Vector2 center = capsule.transform.TransformPoint(capsule.offset);
        Vector3 scale = capsule.transform.lossyScale;
        float playerRadius = Mathf.Abs(capsule.size.x * scale.x) * 0.5f;
        float halfLine = Mathf.Max(0f, Mathf.Abs(capsule.size.y * scale.y) * 0.5f - playerRadius);
        if (Mathf.Abs(direction.y) > 0.5f)
        {
            // 위·아래로 나는 탄환: 가로 거리가 닿는 폭 안이면, 세로 구간이 캡슐(양끝 둥근 부분 포함)과 겹치는 지점을 구한다.
            float lateral = Mathf.Abs(from.x - center.x);
            float reachRadius = radius + playerRadius;
            if (lateral > reachRadius) return false;
            float endReach = Mathf.Sqrt(reachRadius * reachRadius - lateral * lateral);
            float lowY = center.y - halfLine - endReach;
            float highY = center.y + halfLine + endReach;
            float a = (lowY - from.y) * direction.y;
            float b = (highY - from.y) * direction.y;
            float verticalEntry = Mathf.Min(a, b);
            float verticalExit = Mathf.Max(a, b);
            if (verticalExit < 0f || verticalEntry > distance) return false;
            travel = Mathf.Max(0f, verticalEntry);
            return true;
        }
        float verticalGap = Mathf.Max(0f, Mathf.Abs(from.y - center.y) - halfLine);
        float combinedRadius = radius + playerRadius;
        if (verticalGap > combinedRadius) return false;
        float horizontalReach = Mathf.Sqrt(combinedRadius * combinedRadius - verticalGap * verticalGap);
        float signedCenter = (center.x - from.x) * direction.x;
        float entry = signedCenter - horizontalReach;
        float exit = signedCenter + horizontalReach;
        if (exit < 0f || entry > distance) return false;
        travel = Mathf.Max(0f, entry);
        return true;
    }
}
