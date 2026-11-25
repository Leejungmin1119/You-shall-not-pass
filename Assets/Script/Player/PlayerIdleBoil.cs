using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 충돌과 이동은 그대로 두고 플레이어 그림만 바꾼다.
/// 지면에서 멈춰 있으면 손그림 3장을 반복(보일링), 지면에서 걸으면 걷기 6장을 반복하고,
/// 공중에서는 세로 속도에 따라 도약·정점·낙하 그림을 보여 주며, 벽에 닿아 내려가면 벽 슬라이드 프레임을 반복한다.
/// 진행 방향으로 좌우를 뒤집고, 벽 슬라이드 중에는 벽을 향하도록 표시한다.
/// 대기와 걷기가 같은 스프라이트를 바꾸므로 한 컴포넌트에서 처리한다(나눠 두면 서로 덮어쓴다).
/// 네트워크에서는 소유자가 자세를 결정하고 관찰자는 동기화된 자세를 재생한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(CapsuleCollider2D))]
public sealed class PlayerIdleBoil : MonoBehaviour
{
    public enum CharacterStyle { Doll, Knight, JackOLantern, Ghost }

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

    [Header("Jack-o'-lantern")]
    [SerializeField] private Sprite[] jackIdleFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackWalkFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackRunFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackStandingAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackMovingAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackRunningAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] jackWallSlideFrames = new Sprite[0];
    // 달리기 키 없이 가속하는 이동이라 속도로만 걷기와 달리기 그림을 나눈다. 유령도 같은 기준을 쓴다.
    [SerializeField, Range(0.1f, 1f)] private float jackRunSpeedRatio = 0.75f;
    [SerializeField, Min(1f)] private float jackRunFramesPerSecond = 25f;
    // 공중 시트 3번째 이후 칸을 낙하 중 반복하는 간격. 유령 낙하 4칸도 같은 0.12초로 그려졌다.
    [SerializeField, Min(0.01f)] private float jackFallSecondsPerFrame = 0.12f;

    [Header("Ghost")]
    // 메뉴 Tools > 캐릭터 > 유령 시트 자르고 프리팹에 연결 이 채운다. 공중 시트는 잭오랜턴처럼 도약·정점·낙하 4칸 순서다.
    [SerializeField] private Sprite[] ghostIdleFrames = new Sprite[0];
    [SerializeField] private Sprite[] ghostWalkFrames = new Sprite[0];
    [SerializeField] private Sprite[] ghostRunFrames = new Sprite[0];
    [SerializeField, FormerlySerializedAs("ghostAirFrames")] private Sprite[] ghostStandingAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] ghostMovingAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] ghostRunningAirFrames = new Sprite[0];
    [SerializeField] private Sprite[] ghostWallSlideFrames = new Sprite[0];
    // 시안은 걷기 6칸 0.9초, 달리기 8칸 0.72초로 달리기가 1.25배 빨리 돈다.
    // 게임의 걷기(6칸, 최고 속도 10fps)와 같은 비율이 되는 값: 8 × 1.25 × 10 / 6 ≈ 16.7.
    [SerializeField, Min(1f)] private float ghostRunFramesPerSecond = 16.7f;

    private float walkPhase;
    private float wallSlidePhase;
    private float fallPhase;

    private SpriteRenderer visual;
    private CapsuleCollider2D capsule;
    private Rigidbody2D body;
    private PlayerMovement movement;
    private PlayerState state;
    private NetworkIdentity identity;
    private PlayerAnimationSync animationSync;
    private Vector3 previousPosition;
    private float stationaryTime;

    public PlayerAnimationState CurrentAnimation { get; private set; }

    public bool IsBoiling { get; private set; }
    public bool IsWalking { get; private set; }
    public bool IsAirborne { get; private set; }
    public bool IsWallSliding { get; private set; }
    public int CurrentFrame { get; private set; }
    // 발소리가 걸음 주기의 절반 지점을 찾을 때 쓴다.
    public int CurrentFrameCount { get; private set; }
    public CharacterStyle Character => character;
    public Sprite RestingSprite => character == CharacterStyle.JackOLantern && jackIdleFrames.Length > 0
        ? jackIdleFrames[0] : null;

    public const int CharacterCount = 4;

    /// <summary>로비 표시와 이름표에 쓰는 캐릭터 이름.</summary>
    public static string GetDisplayName(CharacterStyle style) => style switch
    {
        CharacterStyle.Knight => "기사",
        CharacterStyle.JackOLantern => "잭오랜턴",
        CharacterStyle.Ghost => "유령",
        _ => "인형"
    };

    /// <summary>캐릭터별 대기 그림. 로비는 게임 플레이어 프리팹에서 이 값을 읽어 같은 그림을 보여 준다.</summary>
    public Sprite[] GetIdleFrames(CharacterStyle style) => style switch
    {
        CharacterStyle.Knight => knightFrames,
        CharacterStyle.JackOLantern => jackIdleFrames,
        CharacterStyle.Ghost => ghostIdleFrames,
        _ => dollFrames
    };

    /// <summary>걷기와 달리기 그림이 따로 있고, 낙하 그림을 반복하는 캐릭터.</summary>
    private bool HasRunAnimation => character == CharacterStyle.JackOLantern || character == CharacterStyle.Ghost;

    /// <summary>로비에서 고른 캐릭터로 바꾸고 이전 캐릭터의 재생 위치를 비운다.</summary>
    public void SetCharacter(CharacterStyle style)
    {
        if (character == style)
            return;

        character = style;
        walkPhase = 0f;
        wallSlidePhase = 0f;
        fallPhase = 0f;
        Sprite[] idle = GetIdleFrames(style);
        if (visual != null && idle != null && idle.Length > 0 && idle[0] != null)
            visual.sprite = idle[0];
    }

    private void Awake()
    {
        visual = GetComponent<SpriteRenderer>();
        capsule = GetComponent<CapsuleCollider2D>();
        body = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();
        state = GetComponent<PlayerState>();
        identity = GetComponent<NetworkIdentity>();
        animationSync = GetComponent<PlayerAnimationSync>();
    }

    private void OnEnable()
    {
        previousPosition = transform.position;
        stationaryTime = 0f;
        wallSlidePhase = 0f;
        fallPhase = 0f;
    }

    /// <summary>소유자는 표시 상태를 결정하고 전송하며, 관찰자는 받은 상태만 재생한다.</summary>
    private void LateUpdate()
    {
        if (state != null && state.Status == PlayerStatus.Dead)
            return;

        Sprite[] idle = GetIdleFrames(character);
        if (idle == null || idle.Length == 0)
            return;

        bool remote = identity != null && (identity.isClient || identity.isServer) && !identity.isLocalPlayer;
        if (remote)
        {
            previousPosition = transform.position;

            if (animationSync != null)
                RenderAnimation(animationSync.CurrentState);

            return;
        }

        PlayerAnimationState next = EvaluateAnimation();
        RenderAnimation(next);
        animationSync?.Publish(next);
    }

    /// <summary>실제 조작자의 속도와 접촉으로 자세를 결정한다. 위치 차분은 오프라인 표시용이다.</summary>
    private PlayerAnimationState EvaluateAnimation()
    {
        float delta = Time.deltaTime;
        Vector3 displacement = transform.position - previousPosition;
        previousPosition = transform.position;

        float horizontalSpeed = delta > 0f ? displacement.x / delta : 0f;
        float verticalSpeed = delta > 0f ? displacement.y / delta : 0f;
        bool stationary = delta > 0f && displacement.sqrMagnitude <= 0.08f * 0.08f * delta * delta;
        bool grounded;
        bool controlledHere = movement != null && movement.enabled && !movement.MovementLocked;

        if (controlledHere)
        {
            // 벽점프 직후 발밑 Ray가 잠깐 지면을 잡아도 상승 중에는 지상 자세로 바꾸지 않는다.
            grounded = movement.Controller != null && movement.Controller.IsGrounded() && !movement.IsJumping &&
                (body == null || body.linearVelocity.y <= 0.01f);
            stationary &= body == null || Mathf.Abs(body.linearVelocity.x) < 0.08f;

            if (body != null)
            {
                horizontalSpeed = body.linearVelocity.x;
                verticalSpeed = body.linearVelocity.y;
            }
        }
        else if (state != null && !state.CanMove)
        {
            // 시작 대기와 수비자 상태에서도 대기 애니메이션은 유지한다.
            horizontalSpeed = 0f;
            verticalSpeed = 0f;
            grounded = true;
            stationary = true;
        }
        else
        {
            grounded = IsGrounded();
        }

        UpdateFacing(controlledHere, horizontalSpeed);

        bool alive = state == null || state.Status == PlayerStatus.Alive;
        bool hasRun = HasRunAnimation;
        float speed = Mathf.Abs(horizontalSpeed);
        float maxSpeed = movement != null && movement.moveStats != null
            ? Mathf.Max(0.01f, movement.moveStats.MaxRunSpeed * 0.01f) : 10f;
        bool running = hasRun && speed >= maxSpeed * jackRunSpeedRatio;
        PlayerAirPose airPose = !hasRun ? PlayerAirPose.Standing :
            running ? PlayerAirPose.Running :
            speed >= walkSpeedThreshold ? PlayerAirPose.Moving : PlayerAirPose.Standing;

        int wallDirection = 0;
        if (alive && !grounded && verticalSpeed < -0.05f)
            wallDirection = controlledHere && movement.Controller != null
                ? (movement.Controller.IsTouchingWall() ? movement.Controller.GetWallDirection() : 0)
                : FindWallDirection();

        Sprite[] wallFrames = GetWallFrames();
        if (wallDirection != 0 && wallFrames != null && wallFrames.Length >= 2)
        {
            stationaryTime = 0f;
            // 벽에 등을 대는 원본의 방향을 유지한다.
            return new PlayerAnimationState(PlayerAnimationMode.WallSlide, PlayerAirPose.Standing, wallDirection < 0);
        }

        Sprite[] airFrames = GetAirFrames(airPose);
        if (alive && !grounded && airFrames != null && airFrames.Length >= 3)
        {
            stationaryTime = 0f;
            PlayerAnimationMode mode = verticalSpeed > apexSpeedBand ? PlayerAnimationMode.Rising :
                verticalSpeed < -apexSpeedBand ? PlayerAnimationMode.Falling : PlayerAnimationMode.Apex;

            return new PlayerAnimationState(mode, airPose, visual.flipX);
        }

        if (delta > 0f)
            stationaryTime = alive && grounded && stationary ? stationaryTime + delta : 0f;

        PlayerAnimationMode walkMode = running ? PlayerAnimationMode.Run : PlayerAnimationMode.Walk;
        Sprite[] walkFrames = GetWalkFrames(walkMode);
        if (alive && grounded && speed >= walkSpeedThreshold && walkFrames != null && walkFrames.Length > 0)
        {
            float ratio = Mathf.Max(minWalkPlaybackRatio, Mathf.Clamp01(speed / maxSpeed));
            byte playbackStep = (byte)Mathf.Clamp(Mathf.RoundToInt(ratio * PlayerAnimationState.PlaybackSteps),
                1, PlayerAnimationState.PlaybackSteps);

            return new PlayerAnimationState(walkMode, PlayerAirPose.Standing, visual.flipX, playbackStep);
        }

        PlayerAnimationMode idleMode = alive && grounded && stationaryTime >= idleDelay
            ? PlayerAnimationMode.IdleBoil : PlayerAnimationMode.Idle;

        return new PlayerAnimationState(idleMode, PlayerAirPose.Standing, visual.flipX);
    }

    /// <summary>프레임은 각 화면에서 진행하며, 원격 위치로 자세를 다시 판정하지 않는다.</summary>
    private void RenderAnimation(PlayerAnimationState next)
    {
        if (CurrentAnimation.Mode != next.Mode || CurrentAnimation.AirPose != next.AirPose)
        {
            walkPhase = 0f;
            wallSlidePhase = 0f;
            fallPhase = 0f;
        }

        CurrentAnimation = next;
        visual.flipX = next.FlipX;
        IsWallSliding = next.Mode == PlayerAnimationMode.WallSlide;
        IsAirborne = IsWallSliding || next.Mode == PlayerAnimationMode.Rising ||
            next.Mode == PlayerAnimationMode.Apex || next.Mode == PlayerAnimationMode.Falling;
        IsWalking = next.Mode == PlayerAnimationMode.Walk || next.Mode == PlayerAnimationMode.Run;
        IsBoiling = next.Mode == PlayerAnimationMode.IdleBoil;

        Sprite[] frames;
        if (IsWallSliding)
        {
            frames = GetWallFrames();
            CurrentFrame = (int)wallSlidePhase;
            wallSlidePhase += Time.deltaTime * wallSlideFramesPerSecond;
        }
        else if (IsAirborne)
        {
            frames = GetAirFrames(next.AirPose);
            CurrentFrame = next.Mode == PlayerAnimationMode.Rising ? 0 :
                next.Mode == PlayerAnimationMode.Apex ? 1 : 2;

            if (HasRunAnimation && next.Mode == PlayerAnimationMode.Falling &&
                frames != null && frames.Length > 2)
            {
                CurrentFrame += (int)fallPhase % (frames.Length - 2);
                fallPhase += Time.deltaTime / jackFallSecondsPerFrame;
            }
        }
        else if (IsWalking)
        {
            frames = GetWalkFrames(next.Mode);
            float rate = next.Mode != PlayerAnimationMode.Run ? walkFramesPerSecondAtMaxSpeed :
                character == CharacterStyle.Ghost ? ghostRunFramesPerSecond : jackRunFramesPerSecond;
            walkPhase += Time.deltaTime * rate * next.PlaybackRatio;
            CurrentFrame = (int)walkPhase;
        }
        else
        {
            frames = GetIdleFrames(character);
            double clock = NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
            CurrentFrame = IsBoiling && frames != null && frames.Length > 0
                ? (int)(clock * Mathf.Max(1f, framesPerSecond) % frames.Length) : 0;
        }

        if (frames == null || frames.Length == 0)
        {
            frames = GetIdleFrames(character);
            CurrentFrame = 0;
        }

        if (frames == null || frames.Length == 0)
            return;

        CurrentFrameCount = frames.Length;
        CurrentFrame %= frames.Length;
        if (frames[CurrentFrame] != null)
            visual.sprite = frames[CurrentFrame];
    }

    /// <summary>캐릭터와 동기화된 이동 자세에 맞는 지상 프레임을 반환한다.</summary>
    private Sprite[] GetWalkFrames(PlayerAnimationMode mode) => character switch
    {
        CharacterStyle.JackOLantern => mode == PlayerAnimationMode.Run ? jackRunFrames : jackWalkFrames,
        CharacterStyle.Ghost => mode == PlayerAnimationMode.Run ? ghostRunFrames : ghostWalkFrames,
        CharacterStyle.Knight => knightWalkFrames,
        _ => dollWalkFrames
    };

    /// <summary>소유자가 선택한 정지·이동·달리기 공중 자세를 그대로 선택한다.</summary>
    private Sprite[] GetAirFrames(PlayerAirPose pose) => character switch
    {
        CharacterStyle.JackOLantern => pose == PlayerAirPose.Running ? jackRunningAirFrames :
            pose == PlayerAirPose.Moving ? jackMovingAirFrames : jackStandingAirFrames,
        CharacterStyle.Ghost => pose == PlayerAirPose.Running ? ghostRunningAirFrames :
            pose == PlayerAirPose.Moving ? ghostMovingAirFrames : ghostStandingAirFrames,
        CharacterStyle.Knight => knightAirFrames,
        _ => dollAirFrames
    };

    /// <summary>캐릭터별 벽 슬라이드 프레임을 반환한다.</summary>
    private Sprite[] GetWallFrames() => character switch
    {
        CharacterStyle.JackOLantern => jackWallSlideFrames,
        CharacterStyle.Ghost => ghostWallSlideFrames,
        CharacterStyle.Knight => knightWallSlideFrames,
        _ => dollWallSlideFrames
    };

    /// <summary>
    /// 그림은 오른쪽을 보고 있다. 왼쪽으로 가면 뒤집고, 멈추면 마지막 방향을 유지한다(기획서 6-1).
    /// 직접 조종하는 PC는 입력 방향(IsFacingRight)을 따라 즉시 돌아서고(6-2),
    /// 이동 코드가 없는 오프라인 표시만 위치 변화로 판단한다. 관찰자는 받은 방향을 사용한다.
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

        // 오프라인 표시에서 이동 센서를 쓸 수 없을 때만 그림 아래의 지면을 확인한다.
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

    /// <summary>오프라인 표시에서 이동 센서를 쓸 수 없을 때 벽의 방향을 확인한다.</summary>
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
