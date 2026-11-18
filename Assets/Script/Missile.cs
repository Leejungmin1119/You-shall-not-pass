using Mirror;
using UnityEngine;

/// <summary>발사자의 커서를 향해 회전하며 비행한다. 이동과 접촉 판정은 서버만 수행한다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Missile : ItemBase
{
    [SerializeField, Min(0f)] private float preparationTime = 0.5f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
    [SerializeField, Min(1f)] private float turnSpeed = 180f;
    [SerializeField, Min(0.01f)] private float hitRadius = 0.15f;
    private const float SnapshotInterval = 0.05f;
    private const float ExplosionDuration = 0.25f;
    private PlayerHeldItem owner;
    private SpriteRenderer visual;
    private SpriteRenderer explosion;
    private TrailRenderer trail;
    private LayerMask groundLayer;
    private Vector2 aim;
    private float age;
    private double nextSnapshot;
    private float explosionAge;
    private bool exploded;
    private Vector2 remoteFrom;
    private Vector2 remoteTo;
    private float remoteFromAngle;
    private float remoteToAngle = -90f;
    private float remoteBlend;
    private double lastSnapshotTime = double.NegativeInfinity;
    public bool HasExploded => exploded;
    public bool IsPreparing => !exploded && age < preparationTime;
    public Vector2 AimPosition => aim;

    /// <summary>커서 지점에서 아래를 향해 대기하고 서버에 발사자를 등록한다.</summary>
    public override void Initialize(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        base.Initialize(data, spawn, isJudge, initialAge);
        visual = GetComponent<SpriteRenderer>();
        transform.rotation = Quaternion.Euler(0f, 0f, -90f);
        aim = spawn.Position;
        age = initialAge;
        remoteFrom = remoteTo = spawn.Position;
        remoteFromAngle = remoteToAngle;
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = visual.sharedMaterial;
        trail.startWidth = 0.12f;
        trail.endWidth = 0f;
        trail.time = 0.25f;
        trail.minVertexDistance = 0.1f;
        trail.startColor = new Color(0.65f, 0.65f, 0.65f, 0.5f);
        trail.endColor = new Color(0.65f, 0.65f, 0.65f, 0f);
        trail.sortingOrder = visual.sortingOrder - 1;
        trail.emitting = false;
        if (!IsJudge) return;
        SpearNetwork network = FindFirstObjectByType<SpearNetwork>();
        groundLayer = network != null ? network.GroundLayer : 0;
        if (NetworkServer.spawned.TryGetValue(OwnerNetId, out NetworkIdentity identity))
            owner = identity.GetComponent<PlayerHeldItem>();
        if (owner != null) owner.RegisterMissile(this);
    }

    /// <summary>검사된 소유자의 최신 커서 위치를 서버의 목표 좌표로 저장한다.</summary>
    public void SetAim(Vector2 position)
    {
        if (IsJudge && !exploded) aim = position;
    }

    /// <summary>서버는 비행을 계산하고 원격 화면은 수신 위치 사이를 보간한다.</summary>
    private void Update()
    {
        if (visual == null) return;
        if (exploded) { UpdateExplosion(); return; }
        float previousAge = age;
        age += StageDeltaTime;
        if (IsJudge)
        {
            if (owner == null) { Explode(); return; }
            // 준비가 끝난 프레임에서는 실제 비행에 해당하는 시간만 이동한다.
            float flightStep = Mathf.Max(0f, age - preparationTime) - Mathf.Max(0f, previousAge - preparationTime);
            if (flightStep > 0f) Fly(flightStep);
            if (!exploded && age >= preparationTime + Data.Lifetime) Explode();
            if (!exploded && NetworkTime.localTime >= nextSnapshot)
            {
                PublishState(false);
                nextSnapshot = NetworkTime.localTime + SnapshotInterval;
            }
        }
        else InterpolateMovement();
        trail.emitting = !exploded && age >= preparationTime;
        if (!exploded)
        {
            float remaining = preparationTime + Data.Lifetime - age;
            visual.color = remaining <= 0.5f && Mathf.Repeat(age * 10f, 1f) < 0.5f ? Color.red : Color.white;
        }
    }

    /// <summary>회전 속도를 제한하면서 일정 속도로 진행하고 가장 먼저 닿는 대상을 처리한다.</summary>
    private void Fly(float timeStep)
    {
        Vector2 position = transform.position;
        Vector2 delta = aim - position;
        if (delta.sqrMagnitude > 0.0001f)
        {
            float desired = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float angle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, desired, turnSpeed * timeStep);
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
        Vector2 direction = transform.right;
        float distance = moveSpeed * timeStep;
        Vector2 next = position + direction * distance;
        RaycastHit2D ground = Physics2D.CircleCast(position, hitRadius, direction, distance, groundLayer);
        bool playerHit = FindPlayerContact(position, next, hitRadius, out PlayerState player, out Vector2 contact);
        if (ground && (!playerHit || ground.distance <= Vector2.Distance(position, contact)))
        {
            transform.position = ground.centroid;
            Explode();
        }
        else if (playerHit)
        {
            transform.position = contact;
            ReportHit(player);
            Explode();
        }
        else transform.position = next;
    }

    /// <summary>순서가 뒤바뀐 이동 패킷은 무시하고 폭발은 최종 상태로 적용한다.</summary>
    public override void ApplyState(ItemStateMessage message)
    {
        if (exploded) return;
        if (message.Exploded)
        {
            transform.SetPositionAndRotation(message.Position, Quaternion.Euler(0f, 0f, message.Angle));
            BeginExplosion();
            return;
        }
        if (message.ServerTime <= lastSnapshotTime) return;
        lastSnapshotTime = message.ServerTime;
        remoteFrom = transform.position;
        remoteTo = message.Position;
        remoteFromAngle = transform.eulerAngles.z;
        remoteToAngle = message.Angle;
        remoteBlend = 0f;
    }

    /// <summary>서버가 보낸 위치와 회전 사이를 짧게 보간해 원격 표시의 끊김을 줄인다.</summary>
    private void InterpolateMovement()
    {
        remoteBlend += Time.deltaTime / SnapshotInterval;
        transform.position = Vector2.Lerp(remoteFrom, remoteTo, remoteBlend);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(remoteFromAngle, remoteToAngle, remoteBlend));
    }

    /// <summary>충돌 또는 수명 만료 시 범위 피해 없이 작은 폭발을 전파한다.</summary>
    private void Explode()
    {
        if (exploded) return;
        BeginExplosion();
        PublishState(true);
    }

    /// <summary>몸통과 연기 생성을 끄고 미사일 크기의 소멸 연출을 시작한다.</summary>
    private void BeginExplosion()
    {
        exploded = true;
        visual.enabled = false;
        trail.emitting = false;
        if (owner != null) owner.UnregisterMissile(this);
        explosion = ItemVisuals.CreateCircle("MissileExplosion", transform.position, 0.35f,
            new Color(1f, 0.5f, 0.1f, 0.8f), visual.sortingOrder + 1);
    }

    /// <summary>추가 적중 판정 없이 폭발 표시와 잔여 연기를 지운다.</summary>
    private void UpdateExplosion()
    {
        explosionAge += StageDeltaTime;
        if (explosion != null)
            explosion.color = new Color(1f, 0.5f, 0.1f, 0.8f * (1f - Mathf.Clamp01(explosionAge / ExplosionDuration)));
        if (explosionAge >= ExplosionDuration) Destroy(gameObject);
    }

    /// <summary>씬 종료 시에도 발사자의 전송 여부와 별도 폭발 표시를 정리한다.</summary>
    protected override void OnDestroy()
    {
        if (owner != null) owner.UnregisterMissile(this);
        if (explosion != null) Destroy(explosion.gameObject);
        base.OnDestroy();
    }
}
