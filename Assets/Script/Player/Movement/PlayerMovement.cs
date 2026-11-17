using UnityEngine;

[SelectionBase]
public class PlayerMovement : MonoBehaviour
{
    // 입력이 아니라 실제 도약이 확정된 순간에만 발생한다. 착지에서는 호출하지 않는다.
    public event System.Action JumpStarted;

    [Header("References")]
    public PlayerMovementStats moveStats;

    private Rigidbody2D _rb;
    private PlayerInputAdapter _input;
    private PlayerJumpMotor _jumpMotor;

    public bool IsFacingRight { get; private set; }
    public bool MovementLocked { get; set; }
    public MovementController Controller { get; private set; }
    [HideInInspector] public Vector2 Velocity;

    // 기존 호출부가 사용하던 일반 점프 상태를 유지한다.
    public bool IsJumping => _jumpMotor != null && _jumpMotor.Kind == JumpKind.Ground;

    // JumpStarted를 받은 쪽이 벽을 찬 도약인지 구분할 때 쓴다.
    public bool IsWallJumping => _jumpMotor != null && _jumpMotor.Kind == JumpKind.Wall;

    /// <summary>기본 이동 입력 방향으로 실제 달리는지 확인한다. 일반 점프 중에도 유지한다.</summary>
    public bool IsRunningWithInput
    {
        get
        {
            if (!isActiveAndEnabled || MovementLocked || moveStats == null ||
                _rb == null || _jumpMotor == null || !_rb.simulated ||
                _jumpMotor.Kind == JumpKind.Wall || _jumpMotor.IsHorizontalControlLocked ||
                Controller.IsTouchingWall() ||
                Mathf.Abs(_input.Move.x) < moveStats.MoveThreshold || moveStats.MaxRunSpeed <= 0f)
                return false;

            float speed = _rb.linearVelocity.x * Mathf.Sign(_input.Move.x);

            return speed > 0.01f;
        }
    }

    /// <summary>물리·접촉 담당을 보관하고 플레이어마다 독립된 입력 및 점프 상태를 만든다.</summary>
    private void Awake()
    {
        IsFacingRight = true;
        _rb = GetComponent<Rigidbody2D>();
        Controller = GetComponent<MovementController>();
        _input = new PlayerInputAdapter();
        _jumpMotor = new PlayerJumpMotor(moveStats);
    }

    /// <summary>렌더 프레임의 입력을 다음 물리 틱에서 소비할 때까지 보관한다.</summary>
    private void Update()
    {
        if (MovementLocked)
        {
            return;
        }

        _input.Read();
    }

    /// <summary>접촉과 입력을 점프 담당에게 전달하고 계산된 속도를 물리에 한 번 적용한다.</summary>
    private void FixedUpdate()
    {
        if (MovementLocked)
        {
            Velocity = Vector2.zero;
            _rb.linearVelocity = Vector2.zero;
            _jumpMotor.Reset();
            _input.Clear();
            return;
        }

        float timeStep = Time.fixedDeltaTime;
        Velocity = _rb.linearVelocity;
        Controller.PollSensors(Velocity * timeStep);

        _jumpMotor.BeginStep(Controller.State, Velocity.y, timeStep);
        _jumpMotor.ReadInput(_input.JumpPressed, _input.JumpReleased, Velocity.y);

        JumpKind startedJump = _jumpMotor.TryStartJump();
        if (startedJump != JumpKind.None)
        {
            Velocity.y = _jumpMotor.InitialVerticalSpeed;

            if (startedJump == JumpKind.Wall)
                Velocity.x = _jumpMotor.WallLaunchSpeedX;

            JumpStarted?.Invoke();
        }

        HandleHorizontalMovement(timeStep);

        Velocity.y = _jumpMotor.CalculateVerticalSpeed(Velocity.y, timeStep);
        Velocity.y = Mathf.Clamp(Velocity.y, -moveStats.MaxFallSpeed, 100f);

        _rb.linearVelocity = Velocity;
        _input.Clear();
    }

    /// <summary>이동이 비활성화된 동안 이전 점프와 미사용 입력이 남지 않도록 비운다.</summary>
    private void OnDisable()
    {
        _jumpMotor?.Reset();
        _input?.Clear();
    }

    /// <summary>부활 위치에서 속도, 점프·입력 버퍼와 접촉 기록을 초기화한다.</summary>
    public void ResetMovement()
    {
        Velocity = Vector2.zero;
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;

        _jumpMotor.Reset();
        _input.Clear();
        Controller.ResetSensors();
    }

    /// <summary>벽점프 제어 제한과 지상·공중 가속 설정에 따라 수평 속도를 계산한다.</summary>
    private void HandleHorizontalMovement(float timeStep)
    {
        if (_jumpMotor.IsHorizontalControlLocked)
        {
            return;
        }

        float acceleration = Controller.IsGrounded()
            ? moveStats.GroundAcceleration
            : moveStats.AirAcceleration;

        float deceleration = Controller.IsGrounded()
            ? moveStats.GroundDeceleration
            : moveStats.AirDeceleration;

        if (_jumpMotor.UsesWallMoveSettings)
        {
            acceleration = moveStats.WallJumpMoveAcceleration;
            deceleration = moveStats.WallJumpMoveDeceleration;
        }

        if (Mathf.Abs(_input.Move.x) >= moveStats.MoveThreshold)
        {
            TurnCheck(_input.Move);

            float moveDirection = Mathf.Sign(_input.Move.x);
            float targetVelocityX = moveDirection * (moveStats.MaxRunSpeed * 0.01f);
            float t = Mathf.Clamp01(acceleration * timeStep);
            Velocity.x = Mathf.Lerp(Velocity.x, targetVelocityX, t);

            if (Mathf.Abs(Velocity.x - targetVelocityX) <= 0.01f)
            {
                Velocity.x = targetVelocityX;
            }
        }
        else
        {
            float t = Mathf.Clamp01(deceleration * timeStep);
            Velocity.x = Mathf.Lerp(Velocity.x, 0f, t);

            if (Mathf.Abs(Velocity.x) <= 0.01f)
            {
                Velocity.x = 0f;
            }
        }
    }

    /// <summary>수평 조작 방향에 맞춰 캐릭터가 바라보는 방향을 기록한다.</summary>
    private void TurnCheck(Vector2 moveInput)
    {
        if (IsFacingRight && moveInput.x < 0f)
        {
            Turn(false);
        }
        else if (!IsFacingRight && moveInput.x > 0f)
        {
            Turn(true);
        }
    }

    /// <summary>바라보는 방향을 저장한다.</summary>
    private void Turn(bool turnRight)
    {
        IsFacingRight = turnRight;
    }

    /// <summary>접촉 담당과 애니메이션이 같은 벽 각도 기준을 사용하도록 한다.</summary>
    public bool IsWallSlideable(float angle)
    {
        return angle >= moveStats.MinAngleForWallSlide &&
            angle <= moveStats.MaxAngleForWallSlide;
    }
}
