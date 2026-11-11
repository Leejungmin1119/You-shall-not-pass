using Mirror;
using UnityEngine;

/// <summary>공격자를 감지해 내려찍고, 바닥에서 대기한 뒤 원래 높이로 복귀하는 고정 함정.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(BoxCollider2D), typeof(Rigidbody2D))]
public sealed class StoneCrusherTrap : NetworkBehaviour
{
    // 기존 상태 번호를 유지하도록 새 상태는 마지막에 추가한다.
    public enum CrusherPhase : byte { Waiting, Falling, Grounded, Returning, Preparing }

    // 상태 전환 정보만 보내고 각 화면은 같은 서버 시각으로 이동 위치를 계산한다.
    public struct Motion
    {
        public CrusherPhase Phase;
        public float FromY;
        public float ToY;
        public double StartedAt;
        public float Duration;
    }

    [Header("씬 연결")]
    [SerializeField] private RaceManager race;
    [SerializeField] private LayerMask groundLayer;

    [Header("공격자 감지")]
    [SerializeField, Min(0.1f)] private float detectionWidth = 4f;
    [SerializeField, Min(0.1f)] private float detectionDistance = 10f;
    [Tooltip("공격자를 감지한 뒤 내려찍기 전까지 기다리는 시간입니다. 0이면 즉시 내려찍습니다.")]
    [SerializeField, Min(0f)] private float preparationDuration = 0.5f;
    [SerializeField, Min(0f)] private float rearmDelay = 0.35f;

    [Header("내려찍기와 복귀")]
    [SerializeField, Min(0.1f)] private float dropSpeed = 18f;
    [SerializeField, Min(0f)] private float groundWait = 0.8f;
    [SerializeField, Min(0.1f)] private float returnSpeed = 3f;
    [SerializeField, Min(0.1f)] private float maxDropDistance = 30f;

    [SyncVar] private Motion motion;
    private BoxCollider2D shape;
    private Rigidbody2D body;
    private Vector3 home;
    private Vector2 halfSize;
    private Vector2 centerOffset;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[16];
    private int observedTurn = -1;
    private float previousY;
    private double canDetectAt;
    private bool reportedMissingGround;

    /// <summary>고정된 시작 위치와 월드 충돌 크기를 보관하고 지형 탐색 필터를 준비한다.</summary>
    private void Awake()
    {
        shape = GetComponent<BoxCollider2D>();
        body = GetComponent<Rigidbody2D>();
        home = transform.position;
        Vector3 scale = transform.lossyScale;
        halfSize = new Vector2(Mathf.Abs(shape.size.x * scale.x), Mathf.Abs(shape.size.y * scale.y)) * 0.5f;
        centerOffset = (Vector2)transform.TransformPoint(shape.offset) - (Vector2)home;
        groundFilter.SetLayerMask(groundLayer);
        groundFilter.useTriggers = false;
    }

    /// <summary>서버 시작 시 원래 높이와 대기 상태를 동기화한다.</summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        if (race == null)
            race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);

        ResetForTurn();
    }

    /// <summary>서버만 감지·지면 도착·피격·복귀 전환을 결정한다.</summary>
    private void FixedUpdate()
    {
        if (!isServer || race == null || NetworkServer.isLoadingScene)
            return;

        if (observedTurn != race.RoundNumber)
            ResetForTurn();

        if (!race.IsSimulating)
        {
            if (motion.Phase != CrusherPhase.Waiting)
                ResetForTurn();

            return;
        }

        double now = (race != null ? race.SimulationTime : NetworkTime.time);
        float nextY = PositionAt(now);
        PlayerState attacker = FindAttacker();

        if (motion.Phase == CrusherPhase.Waiting)
        {
            if (now >= canDetectAt && IsBelow(attacker))
            {
                if (preparationDuration > 0f)
                    SetMotion(CrusherPhase.Preparing, home.y, home.y, preparationDuration, now);
                else
                    BeginDrop(now);
            }
        }
        else if (motion.Phase == CrusherPhase.Preparing)
        {
            // 한 번 감지하면 공격자가 벗어나도 준비시간이 끝난 뒤 내려찍는다.
            // 준비 중에는 같은 높이를 유지하며 시작 시각과 지속시간을 함께 동기화한다.
            if (now >= motion.StartedAt + motion.Duration)
                BeginDrop(now);
        }
        else if (motion.Phase == CrusherPhase.Falling)
        {
            CheckCrush(attacker, previousY, nextY);

            if (now >= motion.StartedAt + motion.Duration)
            {
                SetMotion(CrusherPhase.Grounded, motion.ToY, motion.ToY, groundWait, now);

                // 하강 완료 시 한 번만 전달한다. 바닥 대기·복귀 중에는 재생하지 않는다.
                Vector2 impact = new Vector2(home.x, motion.ToY) + centerOffset - Vector2.up * halfSize.y;
                RpcPlayImpactSound(impact);
            }
        }
        else if (motion.Phase == CrusherPhase.Grounded)
        {
            if (now >= motion.StartedAt + motion.Duration)
                SetMotion(CrusherPhase.Returning, motion.ToY, home.y,
                    Mathf.Abs(home.y - motion.ToY) / Mathf.Max(0.1f, returnSpeed), now);
        }
        else if (now >= motion.StartedAt + motion.Duration)
        {
            SetMotion(CrusherPhase.Waiting, home.y, home.y, 0f, now);
            canDetectAt = now + rearmDelay;
        }

        previousY = PositionAt(now);
        body.position = new Vector2(home.x, previousY);
    }

    /// <summary>서버가 확정한 지면 도착 순간에 모든 클라이언트에서 충격음을 한 번 재생한다.</summary>
    [ClientRpc]
    private void RpcPlayImpactSound(Vector2 position)
    {
        SoundPlayer.Play(SoundType.StoneCrusherImpact, position);
    }

    /// <summary>호스트와 클라이언트가 같은 서버 시각으로 위치를 계산해 연속 이동을 표시한다.</summary>
    private void Update()
    {
        if (isServer || isClient)
            transform.position = new Vector3(home.x, PositionAt((race != null ? race.SimulationTime : NetworkTime.time)), home.z);
    }

    /// <summary>동기화된 구간을 보간해 NetworkTransform 없이 현재 높이를 계산한다.</summary>
    private float PositionAt(double now)
    {
        float progress = motion.Duration > 0f ? (float)((now - motion.StartedAt) / motion.Duration) : 1f;
        return Mathf.Lerp(motion.FromY, motion.ToY, Mathf.Clamp01(progress));
    }

    /// <summary>교대되면 설치된 함정은 유지하고 동작만 원래 위치에서 초기화한다.</summary>
    [Server]
    private void ResetForTurn()
    {
        observedTurn = race != null ? race.RoundNumber : -1;
        previousY = home.y;
        canDetectAt = (race != null ? race.SimulationTime : NetworkTime.time) + rearmDelay;
        SetMotion(CrusherPhase.Waiting, home.y, home.y, 0f, (race != null ? race.SimulationTime : NetworkTime.time));
        body.position = home;
        transform.position = home;
    }

    /// <summary>역할 관리자에 등록된 현재 공격자만 조회한다. 수비자는 탐색·피격 대상이 아니다.</summary>
    private PlayerState FindAttacker()
    {
        if (race.Roles != null && NetworkServer.spawned.TryGetValue(race.Roles.AttackerNetId, out NetworkIdentity identity))
            return identity.GetComponent<PlayerState>();

        return null;
    }

    /// <summary>공격자의 캡슐이 본체 아래 감지 사각형과 겹치면 내려찍기를 시작할 수 있다.</summary>
    private bool IsBelow(PlayerState player)
    {
        if (player == null || !player.CanMove || !TryCapsule(player, out Vector2 center, out float radius, out float halfLine))
            return false;

        float bottom = home.y + centerOffset.y - halfSize.y;
        return Mathf.Abs(center.x - (home.x + centerOffset.x)) <= detectionWidth * 0.5f + radius &&
            center.y < bottom && center.y + halfLine + radius >= bottom - detectionDistance;
    }

    /// <summary>본체 너비 전체로 지형을 탐색해 관통하지 않고 멈출 높이를 구한다.</summary>
    [Server]
    private void BeginDrop(double now)
    {
        Vector2 origin = new Vector2(home.x, home.y) + centerOffset;
        int count = Physics2D.BoxCast(origin, halfSize * 2f, 0f, Vector2.down,
            groundFilter, groundHits, maxDropDistance);
        float distance = float.PositiveInfinity;

        for (int index = 0; index < count; index++)
        {
            RaycastHit2D hit = groundHits[index];

            if (hit.collider != shape && hit.normal.y > 0.5f)
                distance = Mathf.Min(distance, hit.distance);
        }

        if (float.IsPositiveInfinity(distance))
        {
            if (!reportedMissingGround)
                Debug.LogWarning("쿵쿵이 아래에 내려찍을 Ground가 없습니다. 위치와 Ground Layer를 확인하세요.", this);

            reportedMissingGround = true;
            return;
        }

        distance = Mathf.Max(0f, distance - 0.01f);
        SetMotion(CrusherPhase.Falling, home.y, home.y - distance,
            distance / Mathf.Max(0.1f, dropSpeed), now);
        previousY = home.y;
    }

    /// <summary>내려간 구간 전체와 캡슐의 접촉을 계산해 빠른 하강에서도 공격자를 놓치지 않는다.</summary>
    private void CheckCrush(PlayerState player, float fromY, float toY)
    {
        if (player == null || !player.CanReceiveHit || !TryCapsule(player, out Vector2 center, out float radius, out float halfLine))
            return;

        float gapX = Mathf.Max(0f, Mathf.Abs(center.x - home.x - centerOffset.x) - halfSize.x);

        if (gapX > radius)
            return;

        float reachY = halfLine + Mathf.Sqrt(Mathf.Max(0f, radius * radius - gapX * gapX));
        float minY = Mathf.Min(fromY, toY) + centerOffset.y - halfSize.y - reachY;
        float maxY = Mathf.Max(fromY, toY) + centerOffset.y + halfSize.y + reachY;

        if (center.y >= minY && center.y <= maxY)
            player.TryDie(transform.position);
    }

    /// <summary>서버에서 물리가 꺼진 원격 플레이어도 실제 캡슐 크기로 검사한다.</summary>
    private static bool TryCapsule(PlayerState player, out Vector2 center, out float radius, out float halfLine)
    {
        center = default;
        radius = halfLine = 0f;

        if (!player.TryGetComponent(out CapsuleCollider2D capsule) || !capsule.enabled ||
            capsule.direction != CapsuleDirection2D.Vertical)
            return false;

        center = capsule.transform.TransformPoint(capsule.offset);
        Vector3 scale = capsule.transform.lossyScale;
        radius = Mathf.Abs(capsule.size.x * scale.x) * 0.5f;
        halfLine = Mathf.Max(0f, Mathf.Abs(capsule.size.y * scale.y) * 0.5f - radius);
        return true;
    }

    /// <summary>서버의 한 번의 상태 변경으로 이동 구간 전체를 동기화한다.</summary>
    [Server]
    private void SetMotion(CrusherPhase phase, float fromY, float toY, float duration, double now)
    {
        motion = new Motion { Phase = phase, FromY = fromY, ToY = toY, StartedAt = now, Duration = duration };
    }
}
