using Mirror;
using UnityEngine;

/// <summary>
/// 충돌과 이동은 그대로 두고 플레이어 그림만 바꾼다.
/// 지면에서 멈춰 있으면 손그림 3장을 반복(보일링), 지면에서 걸으면 걷기 6장을 반복하고,
/// 공중에서는 세로 속도에 따라 도약·정점·낙하 그림을 보여 주며, 벽에 닿아 내려가면 벽 슬라이드 프레임을 반복한다.
/// 진행 방향으로 좌우를 뒤집고, 벽 슬라이드 중에는 벽을 향하도록 표시한다.
/// 대기와 걷기가 같은 스프라이트를 바꾸므로 한 컴포넌트에서 처리한다(나눠 두면 서로 덮어쓴다).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(CapsuleCollider2D))]
public sealed class PlayerIdleBoil : MonoBehaviour
{
    public enum CharacterStyle { Doll, Knight }

    [SerializeField] private CharacterStyle character;
    [SerializeField] private Sprite[] dollFrames = new Sprite[0];
    [SerializeField] private Sprite[] knightFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float framesPerSecond = 6f;
    [SerializeField, Min(0f)] private float idleDelay = 0.15f;

    [Header("Walk")]
    // 메뉴 Tools > 캐릭터 > 걷기 시트 기준점 맞춰 자르고 프리팹에 연결 이 채운다.
    [SerializeField] private Sprite[] dollWalkFrames = new Sprite[0];
    [SerializeField] private Sprite[] knightWalkFrames = new Sprite[0];
    // 최고 속도로 걸을 때의 초당 프레임. 기획서 6-2: 재생 속도를 이동 속도에 비례시킨다.
    [SerializeField, Min(1f)] private float walkFramesPerSecondAtMaxSpeed = 10f;
    // 가속 직후처럼 느릴 때도 다리가 멈춘 듯 보이지 않게 하는 최저 비율.
    [SerializeField, Range(0f, 1f)] private float minWalkPlaybackRatio = 0.35f;
    [SerializeField, Min(0f)] private float walkSpeedThreshold = 0.3f;

    [Header("Air")]
    // 순서: 도약(올라가는 중) · 정점 · 낙하(내려가는 중).
    // 메뉴 Tools > 캐릭터 > 공중(도약·정점·낙하) 시트 기준점 맞춰 자르고 프리팹에 연결 이 채운다.
    [SerializeField] private Sprite[] dollAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] knightAirFrames = new Sprite[0];
    // 세로 속도가 이 값보다 작으면 정점 그림. 점프 초속이 약 12.6유닛/초라
    // 2면 올라갈 때 약 0.08초, 내려갈 때(중력 2.5배) 약 0.03초 동안 정점이 보인다.
    [SerializeField, Min(0f)] private float apexSpeedBand = 2f;

    [Header("Wall Slide")]
    [SerializeField] private Sprite[] dollWallSlideFrames = new Sprite[0];
    [SerializeField] private Sprite[] knightWallSlideFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float wallSlideFramesPerSecond = 6f;

    private float walkPhase;
    private float wallSlidePhase;

    private SpriteRenderer visual;
    private CapsuleCollider2D capsule;
    private Rigidbody2D body;
    private PlayerMovement movement;
    private PlayerState state;
    private NetworkIdentity identity;
    private Vector3 previousPosition;
    private float stationaryTime;

    public bool IsBoiling { get; private set; }
    public bool IsWalking { get; private set; }
    public bool IsAirborne { get; private set; }
    public bool IsWallSliding { get; private set; }
    public int CurrentFrame { get; private set; }

    private void Awake()
    {
        visual = GetComponent<SpriteRenderer>();
        capsule = GetComponent<CapsuleCollider2D>();
        body = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();
        state = GetComponent<PlayerState>();
        identity = GetComponent<NetworkIdentity>();
    }

    private void OnEnable()
    {
        previousPosition = transform.position;
        stationaryTime = 0f;
        wallSlidePhase = 0f;
    }

    private void LateUpdate()
    {
        Sprite[] frames = character == CharacterStyle.Knight ? knightFrames : dollFrames;
        if (frames == null || frames.Length == 0)
            return;

        float delta = Time.deltaTime;
        Vector3 displacement = transform.position - previousPosition;
        previousPosition = transform.position;
        bool local = identity == null || !identity.isClient || identity.isLocalPlayer;
        bool alive = state == null || state.Status == PlayerStatus.Alive;
        bool stationary = delta > 0f && displacement.sqrMagnitude <= 0.08f * 0.08f * delta * delta;

        // 점프 정점도 속도만 보면 정지로 보이므로 접지 여부를 함께 확인한다.
        bool grounded = IsGrounded();
        // 원격 플레이어는 물리가 꺼져 있어서 동기화된 위치 변화로 속도를 구한다.
        float horizontalSpeed = delta > 0f ? displacement.x / delta : 0f;
        float verticalSpeed = delta > 0f ? displacement.y / delta : 0f;
        bool controlledHere = local && movement != null && movement.enabled && !movement.MovementLocked;
        if (controlledHere)
        {
            grounded = movement.Controller != null && movement.Controller.IsGrounded() && !movement.IsJumping;
            stationary &= body == null || Mathf.Abs(body.linearVelocity.x) < 0.08f;
            if (body != null)
            {
                horizontalSpeed = body.linearVelocity.x;
                verticalSpeed = body.linearVelocity.y;
            }
        }

        UpdateFacing(controlledHere, horizontalSpeed);

        Sprite[] wallFrames = character == CharacterStyle.Knight ? knightWallSlideFrames : dollWallSlideFrames;
        int wallDirection = 0;
        if (alive && !grounded && verticalSpeed < -0.05f)
            wallDirection = controlledHere && movement.Controller != null
                ? (movement.Controller.IsTouchingWall() ? movement.Controller.GetWallDirection() : 0)
                : FindWallDirection();

        IsWallSliding = wallDirection != 0 && wallFrames != null && wallFrames.Length >= 2;
        if (IsWallSliding)
        {
            IsAirborne = true;
            IsWalking = false;
            IsBoiling = false;
            stationaryTime = 0f;
            walkPhase = 0f;
            visual.flipX = wallDirection < 0;
            CurrentFrame = (int)wallSlidePhase % wallFrames.Length;
            wallSlidePhase += delta * wallSlideFramesPerSecond;
            if (wallFrames[CurrentFrame] != null)
                visual.sprite = wallFrames[CurrentFrame];
            return;
        }
        wallSlidePhase = 0f;

        Sprite[] airFrames = character == CharacterStyle.Knight ? knightAirFrames : dollAirFrames;
        IsAirborne = alive && !grounded && airFrames != null && airFrames.Length >= 3;
        if (IsAirborne)
        {
            IsWalking = false;
            IsBoiling = false;
            stationaryTime = 0f;
            walkPhase = 0f;
            // 0 도약(상승) · 1 정점 · 2 낙하. 점프 없이 발판에서 떨어질 때도 낙하 그림이 나온다.
            CurrentFrame = verticalSpeed > apexSpeedBand ? 0 : verticalSpeed < -apexSpeedBand ? 2 : 1;
            if (airFrames[CurrentFrame] != null)
                visual.sprite = airFrames[CurrentFrame];
            return;
        }

        if (delta > 0f)
            stationaryTime = alive && grounded && stationary ? stationaryTime + delta : 0f;

        Sprite[] walkFrames = character == CharacterStyle.Knight ? knightWalkFrames : dollWalkFrames;
        IsWalking = alive && grounded && walkFrames != null && walkFrames.Length > 0 &&
                    Mathf.Abs(horizontalSpeed) >= walkSpeedThreshold;
        IsBoiling = !IsWalking && alive && grounded && stationaryTime >= idleDelay;

        if (IsWalking)
        {
            float maxSpeed = movement != null && movement.moveStats != null
                ? movement.moveStats.MaxRunSpeed * 0.01f
                : 10f;
            float ratio = Mathf.Max(minWalkPlaybackRatio, Mathf.Clamp01(Mathf.Abs(horizontalSpeed) / Mathf.Max(0.01f, maxSpeed)));
            walkPhase += delta * walkFramesPerSecondAtMaxSpeed * ratio;
            CurrentFrame = (int)walkPhase % walkFrames.Length;
            if (walkFrames[CurrentFrame] != null)
                visual.sprite = walkFrames[CurrentFrame];
            return;
        }

        // 다음에 걷기 시작할 때 첫 장면부터 나오게 한다.
        walkPhase = 0f;
        double clock = NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        CurrentFrame = IsBoiling ? (int)(clock * Mathf.Max(1f, framesPerSecond) % frames.Length) : 0;
        if (frames[CurrentFrame] != null)
            visual.sprite = frames[CurrentFrame];
        // Renderer.enabled와 color는 골인 숨김 및 원격 플레이어 투명도 처리가 관리한다.
    }

    /// <summary>
    /// 그림은 오른쪽을 보고 있다. 왼쪽으로 가면 뒤집고, 멈추면 마지막 방향을 유지한다(기획서 6-1).
    /// 직접 조종하는 PC는 입력 방향(IsFacingRight)을 따라 즉시 돌아서고(6-2),
    /// 다른 PC는 위치 변화로 판단한다. 기준점이 머리 중심이라 뒤집어도 캐릭터가 옆으로 밀리지 않는다.
    /// </summary>
    private void UpdateFacing(bool controlledHere, float horizontalSpeed)
    {
        if (controlledHere)
        {
            visual.flipX = !movement.IsFacingRight;
            return;
        }

        if (Mathf.Abs(horizontalSpeed) >= walkSpeedThreshold)
            visual.flipX = horizontalSpeed < 0f;
    }

    private bool IsGrounded()
    {
        if (movement == null || movement.moveStats == null || capsule == null)
            return false;

        // 원격 플레이어는 물리/이동 센서가 꺼져 있으므로 동기화된 위치 아래를 검사한다.
        Vector3 center = transform.TransformPoint(capsule.offset);
        Vector3 scale = transform.lossyScale;
        float halfWidth = capsule.size.x * Mathf.Abs(scale.x) * 0.4f;
        float footY = center.y - capsule.size.y * Mathf.Abs(scale.y) * 0.5f;
        for (int index = -1; index <= 1; index++)
        {
            Vector2 origin = new Vector2(center.x + index * halfWidth, footY + 0.04f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 0.16f, movement.moveStats.GroundLayer);
            if (hit.collider != null && !hit.collider.isTrigger &&
                Vector2.Angle(hit.normal, Vector2.up) <= movement.moveStats.MaxSlopeAngle)
                return true;
        }
        return false;
    }

    /// <summary>물리 센서가 꺼진 원격 캐릭터도 동기화된 위치에서 벽의 방향을 확인한다.</summary>
    private int FindWallDirection()
    {
        if (movement == null || movement.moveStats == null || capsule == null)
            return 0;

        Vector3 center = transform.TransformPoint(capsule.offset);
        Vector3 scale = transform.lossyScale;
        float halfWidth = capsule.size.x * Mathf.Abs(scale.x) * 0.5f;
        float halfHeight = capsule.size.y * Mathf.Abs(scale.y) * 0.5f;
        for (int direction = -1; direction <= 1; direction += 2)
        {
            for (int row = -1; row <= 1; row++)
            {
                Vector2 origin = new Vector2(center.x + direction * (halfWidth - 0.015f),
                    center.y + row * halfHeight * 0.8f);
                RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.right * direction, 0.13f,
                    movement.moveStats.GroundLayer);
                if (hit.collider != null && !hit.collider.isTrigger &&
                    movement.IsWallSlideable(Vector2.Angle(hit.normal, Vector2.up)))
                    return direction;
            }
        }
        return 0;
    }
}
