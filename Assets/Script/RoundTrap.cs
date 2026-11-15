using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 낙하·반사·구름을 계산하는 원형 함정. 탄성 설정으로 농구공과 바위를 구분한다.
/// 모든 화면의 Kinematic 콜라이더는 발판으로 사용하되 플레이어가 함정의 궤적을 바꾸지는 않는다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class RoundTrap : ItemBase
{
    [Header("움직임")]
    [SerializeField, Min(0.1f)] private float gravity = 20f;
    [SerializeField, Range(0f, 1f)] private float restitution = 0.65f;
    [SerializeField, Min(0f)] private float groundFriction = 3f;
    [SerializeField, Min(0.1f)] private float minBounceSpeed = 1.2f;

    [Header("바운스 사운드")]
    [SerializeField] private bool playBounceSound;
    [SerializeField, Min(0f)] private float minSoundBounceHeight = 0.1f;

    [Header("피격")]
    [Tooltip("함정 자체의 이동 속도가 이 값 이상일 때만 옆면과 아래쪽에 피해를 줍니다.")]
    [SerializeField, Min(0.1f)] private float minHitSpeed = 0.5f;
    private const float Skin = 0.01f;
    private const float SnapshotInterval = 0.04f;
    private const float SnapshotHeartbeatInterval = 0.25f;
    private const float SnapshotPositionThreshold = 0.01f;
    private const float SnapshotAngleThreshold = 0.5f;
    private bool hasSentSnapshot;
    private Vector2 lastSentPosition;
    private float lastSentAngle;
    private float nextSnapshotHeartbeat;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[12];
    private ContactFilter2D groundFilter;
    private Rigidbody2D body;
    private CircleCollider2D circle;
    private SpriteRenderer warning;
    private Vector2 velocity;
    private float radius;
    private float age;
    private float nextSnapshot;
    private bool expired;
    private bool grounded;
    private float firstBounceHeight;
    private double lastBounceSoundStep = double.NegativeInfinity;
    private Vector2 remoteFrom;
    private Vector2 remoteTo;
    private float remoteFromAngle;
    private float remoteToAngle;
    private float remoteBlend;
    private double lastSnapshotTime = double.NegativeInfinity;
    public Vector2 Velocity => velocity;
    public bool IsGrounded => grounded;

    /// <summary>양쪽 화면에 같은 발판 콜라이더를 준비하고 서버에만 지형 필터를 설정한다.</summary>
    public override void Initialize(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        base.Initialize(data, spawn, isJudge, initialAge);
        hasSentSnapshot = false;
        nextSnapshot = 0f;
        nextSnapshotHeartbeat = 0f;
        firstBounceHeight = 0f;
        lastBounceSoundStep = double.NegativeInfinity;
        body = GetComponent<Rigidbody2D>();
        circle = GetComponent<CircleCollider2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        radius = circle.radius * Mathf.Abs(transform.lossyScale.x);
        body.position = remoteFrom = remoteTo = spawn.Position;
        if (spawn.HasGround) warning = CreatePlacementPreview(new Vector2(spawn.Position.x, spawn.GroundY));
        if (!IsJudge) return;
        SpearNetwork network = FindFirstObjectByType<SpearNetwork>();
        groundFilter.SetLayerMask(network != null ? network.GroundLayer : 0);
        groundFilter.useTriggers = false;
    }

    /// <summary>서버에서 고정 간격으로 이동·피격·수명을 처리하고 원격 물리 위치는 보간한다.</summary>
    private void FixedUpdate()
    {
        if (body == null || expired) return;
        if (!IsJudge) { InterpolateMovement(); return; }
        float step = Time.fixedDeltaTime;
        age += step;
        if (age >= Data.Lifetime) { Expire(); return; }
        Vector2 previous = body.position;
        velocity += Vector2.down * gravity * step;
        Vector2 next = MoveAgainstGround(previous, step);
        CheckPlayerHits(previous, next, (next - previous) / step);
        body.MovePosition(next);
        float angle = body.rotation - velocity.x / radius * Mathf.Rad2Deg * step;
        body.MoveRotation(angle);
        if (age >= nextSnapshot)
        {
            // MovePosition은 다음 물리 단계에 적용되므로 이번에 계산한 목적지를 메시지로 보낸다.
            PublishMovement(next, angle);
            nextSnapshot = Mathf.Min(age + SnapshotInterval, nextSnapshotHeartbeat);
        }
    }

    /// <summary>남은 이동 구간을 최대 3회 검사해 낙하 반사와 경사면을 따라가는 이동을 처리한다.</summary>
    private Vector2 MoveAgainstGround(Vector2 position, float timeStep)
    {
        grounded = false;
        Vector2 supportNormal = Vector2.up;
        float remaining = timeStep;
        for (int attempt = 0; attempt < 3 && remaining > 0.0001f; attempt++)
        {
            Vector2 delta = velocity * remaining;
            float distance = delta.magnitude;
            if (distance < 0.00001f) break;
            Vector2 direction = delta / distance;
            if (!FindGroundHit(position, direction, distance + Skin, out RaycastHit2D hit))
            {
                position += delta;
                break;
            }
            position += direction * Mathf.Clamp(hit.distance - Skin, 0f, distance);
            remaining *= 1f - Mathf.Clamp01(hit.distance / distance);
            float inward = Vector2.Dot(velocity, hit.normal);
            bool bounce = restitution > 0f && -inward >= minBounceSpeed;
            velocity -= hit.normal * inward * (bounce ? 1f + restitution : 1f);

            if (bounce && hit.normal.y > 0.1f)
                RequestBounceSound(hit.point);

            if (hit.normal.y > 0.1f && !bounce)
            {
                grounded = true;
                supportNormal = hit.normal;
            }
            if (warning != null) Destroy(warning.gameObject);
        }
        if (grounded)
        {
            Vector2 tangent = new Vector2(supportNormal.y, -supportNormal.x);
            float speed = Mathf.MoveTowards(Vector2.Dot(velocity, tangent), 0f, groundFriction * timeStep);
            velocity = tangent * speed;
        }
        return position;
    }

    /// <summary>반사 후 위쪽 속도로 다음 꼭대기까지 올라갈 높이를 구한다.</summary>
    private float EstimateBounceHeight()
    {
        float upwardSpeed = Mathf.Max(0f, velocity.y);
        return upwardSpeed * upwardSpeed / (2f * gravity);
    }

    /// <summary>첫 바운스 높이로 상·중·하 음원을 선택하고 아주 작은 바운스는 무음으로 만든다.</summary>
    private bool TrySelectBounceSound(float height, out SoundType type)
    {
        type = SoundType.BasketballHigh;

        if (firstBounceHeight <= 0f)
            firstBounceHeight = height;

        if (height <= 0f || height < minSoundBounceHeight)
            return false;

        float ratio = height / firstBounceHeight;
        type = ratio <= 1f / 3f ? SoundType.BasketballLow :
            ratio <= 0.5f ? SoundType.BasketballMiddle : SoundType.BasketballHigh;
        return true;
    }

    /// <summary>서버의 실제 바닥 반사에서만 요청하며 같은 물리 단계의 중복 충돌음을 막는다.</summary>
    private void RequestBounceSound(Vector2 position)
    {
        if (!IsJudge || !playBounceSound || lastBounceSoundStep == Time.fixedTimeAsDouble ||
            !TrySelectBounceSound(EstimateBounceHeight(), out SoundType type))
            return;

        lastBounceSoundStep = Time.fixedTimeAsDouble;
        RequestSound(type, position);
    }

    /// <summary>자기 콜라이더와 진행을 막지 않는 접촉을 제외하고 가장 가까운 지형을 고른다.</summary>
    private bool FindGroundHit(Vector2 position, Vector2 direction, float distance, out RaycastHit2D nearest)
    {
        nearest = default;
        float closest = float.PositiveInfinity;
        int count = Physics2D.CircleCast(position, radius, direction, groundFilter, groundHits, distance);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = groundHits[i];
            if (hit.collider == circle || hit.distance >= closest || Vector2.Dot(direction, hit.normal) >= -0.0001f) continue;
            nearest = hit;
            closest = hit.distance;
        }
        return nearest.collider != null;
    }

    /// <summary>움직이는 함정의 옆면·아래쪽 접촉만 적중시키고 위에서 밟는 플레이어는 보호한다.</summary>
    private void CheckPlayerHits(Vector2 previous, Vector2 next, Vector2 impactVelocity)
    {
        if (impactVelocity.sqrMagnitude < minHitSpeed * minHitSpeed) return;
        Vector2 delta = next - previous;
        float distance = delta.magnitude;
        var players = PlayerState.ServerPlayers;
        for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
        {
            PlayerState player = players[playerIndex];
            if (player == null || !CanHit(player)) continue;
            Collider2D collider = player.GetComponent<Collider2D>();
            if (collider == null) continue;
            Bounds bounds = collider.bounds;
            Bounds expanded = bounds;
            // 물리 접촉 직전에도 검사해 로컬 플레이어가 콜라이더에 막혀 피격을 누락하지 않게 한다.
            expanded.Expand(new Vector3((radius + 0.06f) * 2f, (radius + 0.06f) * 2f, 0f));
            Vector3 from = new Vector3(previous.x, previous.y, expanded.center.z);
            float entry = 0f;
            if (!expanded.Contains(from) && (distance <= 0f ||
                !expanded.IntersectRay(new Ray(from, delta / distance), out entry) || entry > distance)) continue;
            Vector2 contact = distance > 0f ? previous + delta.normalized * entry : previous;
            bool onTop = bounds.min.y >= contact.y + radius * 0.25f - 0.1f &&
                bounds.center.y > contact.y;
            if (!onTop) ReportHit(player);
        }
    }

    /// <summary>움직이는 동안 기존 주기를 유지하고, 정지한 몸체는 복구용 상태만 주기적으로 보낸다.</summary>
    private void PublishMovement(Vector2 position, float angle)
    {
        bool changed = !hasSentSnapshot ||
            (position - lastSentPosition).sqrMagnitude >= SnapshotPositionThreshold * SnapshotPositionThreshold ||
            Mathf.Abs(Mathf.DeltaAngle(lastSentAngle, angle)) >= SnapshotAngleThreshold;
        if (!changed && age < nextSnapshotHeartbeat) return;

        PublishState(false, position: position, angle: angle);
        lastSentPosition = position;
        lastSentAngle = angle;
        hasSentSnapshot = true;
        nextSnapshotHeartbeat = age + SnapshotHeartbeatInterval;
    }

    /// <summary>서버의 최신 위치를 수신하고 소멸 메시지는 즉시 충돌 판정에도 적용한다.</summary>
    public override void ApplyState(ItemStateMessage message)
    {
        if (expired) return;
        if (message.Expired)
        {
            expired = true;
            circle.enabled = false;
            Destroy(gameObject);
            return;
        }
        if (message.ServerTime <= lastSnapshotTime) return;
        lastSnapshotTime = message.ServerTime;
        remoteFrom = body.position;
        remoteTo = message.Position;
        remoteFromAngle = body.rotation;
        remoteToAngle = message.Angle;
        remoteBlend = 0f;
        if (warning != null && message.Position.y <= warning.transform.position.y + radius + 0.1f)
            Destroy(warning.gameObject);
    }

    /// <summary>표시용 위치뿐 아니라 실제 발판 콜라이더도 수신 위치 사이로 이동한다.</summary>
    private void InterpolateMovement()
    {
        remoteBlend += Time.fixedDeltaTime / SnapshotInterval;
        body.MovePosition(Vector2.Lerp(remoteFrom, remoteTo, remoteBlend));
        body.MoveRotation(Mathf.LerpAngle(remoteFromAngle, remoteToAngle, remoteBlend));
    }

    /// <summary>10초 유지 시간이 끝나면 모든 화면의 몸체와 발판을 함께 제거한다.</summary>
    private void Expire()
    {
        expired = true;
        circle.enabled = false;
        PublishState(false, expired: true);
        Destroy(gameObject);
    }

    /// <summary>보유 중과 생성 직후에 원형 낙하 예고를 표시한다.</summary>
    public override SpriteRenderer CreatePlacementPreview(Vector2 position)
    {
        float previewRadius = GetComponent<CircleCollider2D>().radius * Mathf.Abs(transform.lossyScale.x);
        return ItemVisuals.CreateCircle("RoundTrapWarning", position, previewRadius, new Color(1f, 0.2f, 0.2f, 0.25f), 9);
    }

    /// <summary>파괴 시 별도 예고와 공통 메시지 조회 기록을 정리한다.</summary>
    protected override void OnDestroy()
    {
        if (warning != null) Destroy(warning.gameObject);
        base.OnDestroy();
    }
}
