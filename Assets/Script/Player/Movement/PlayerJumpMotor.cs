using UnityEngine;

public enum JumpKind
{
    None,
    Ground,
    Wall
}

/// <summary>
/// 일반/벽점프의 판단, 타이머와 수직 운동을 관리한다.
/// 입력 장치, PlayerMovement, Rigidbody를 참조하거나 수정하지 않는다.
/// </summary>
public sealed class PlayerJumpMotor
{
    // 접촉 해결 중 생기는 미세한 위쪽 속도는 실제 도약으로 취급하지 않는다.
    private const float GroundedUpwardSpeedTolerance = 0.01f;

    private enum JumpPhase { None, Rising, ApexHold, Cutting, Falling, CutFalling }

    private readonly PlayerMovementStats _stats;
    private JumpPhase _phase;
    private bool _isGrounded;
    private bool _isTouchingWall;
    private bool _jumpPressed;
    private bool _jumpReleasedDuringBuffer;
    private int _lastWallDirection;

    private float _jumpBufferTimer;
    private float _groundCoyoteTimer;
    private float _wallCoyoteTimer;
    private float _wallMoveTimer;
    private float _apexElapsed;
    private float _cutElapsed;
    private float _cutStartSpeed;
    private float _cutDuration;
    private float _wallJumpElapsed;
    private float _wallHorizontalSpeed;
    private float _wallHorizontalCurveTime;
    private float _wallHorizontalSmoothVelocity;

    public JumpKind Kind { get; private set; }
    public bool IsHorizontalControlLocked => Kind == JumpKind.Wall;
    public bool UsesWallMoveSettings => Kind == JumpKind.Wall && _wallMoveTimer > 0f;
    public float InitialVerticalSpeed => Kind == JumpKind.Wall
        ? _stats.InitialWallJumpVelocity : _stats.InitialJumpVelocity;
    public float WallLaunchSpeedX => -_lastWallDirection * Mathf.Abs(_stats.WallJumpDirection.x);

    /// <summary>공유 설정만 보관한다. 플레이어별 점프 상태는 이 인스턴스가 소유한다.</summary>
    public PlayerJumpMotor(PlayerMovementStats stats)
    {
        _stats = stats;
    }

    /// <summary>
    /// 해당 프레임에서의 점프 상태 설정
    /// </summary>
    public void BeginStep(CollisionState contacts, float verticalSpeed, float timeStep)
    {
        _isGrounded = contacts.IsGrounded && verticalSpeed <= GroundedUpwardSpeedTolerance;
        _isTouchingWall = contacts.IsAgainstWall && contacts.WallDirection != 0;

        if (Kind == JumpKind.Wall)
            _wallJumpElapsed += timeStep;

        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - timeStep);
        _groundCoyoteTimer = Mathf.Max(0f, _groundCoyoteTimer - timeStep);
        _wallCoyoteTimer = Mathf.Max(0f, _wallCoyoteTimer - timeStep);
        _wallMoveTimer = Mathf.Max(0f, _wallMoveTimer - timeStep);

        if (_isGrounded)
        {
            ResetJumpMotion();
            _groundCoyoteTimer = _stats.JumpCoyoteTime;
        }

        if (_isTouchingWall)
        {
            _lastWallDirection = contacts.WallDirection;
            _wallCoyoteTimer = _stats.WallJumpCoyoteTime;
        }
    }

    /// <summary>
    /// 새롭게 들어온 입력 저장
    /// </summary>
    public void ReadInput(bool pressed, bool released, float verticalSpeed)
    {
        _jumpPressed = pressed;
        if (pressed)
        {
            // 기존처럼 공중 벽 접촉 중의 누름은 벽점프에만 사용한다.
            _jumpBufferTimer = _isTouchingWall && !_isGrounded ? 0f : _stats.JumpBufferTime;
            _jumpReleasedDuringBuffer = false;
        }

        if (!released) return;

        if (_jumpBufferTimer > 0f || pressed)
            _jumpReleasedDuringBuffer = true;

        // 벽 감지가 남아 있어도 키를 떼면 상승을 줄인다.
        // 짧은 벽점프라면 상승과 함께 수평으로 튕기는 힘도 줄인다.
        if (Kind != JumpKind.None && (verticalSpeed > 0f || _phase == JumpPhase.ApexHold))
            BeginJumpCut(verticalSpeed);
    }

    /// <summary>
    /// 종합적으로 점프 가능한지 확인 -> 각 상태마다 다르게 시작.
    /// </summary>
    public JumpKind TryStartJump()
    {

        bool hasGroundRequest = _jumpPressed || _jumpBufferTimer > 0f;

        // 공중에서 새 입력이 있고 벽 접촉/벽 코요테가 유효하면 벽점프
        if (!_isGrounded && _jumpPressed && (_isTouchingWall || _wallCoyoteTimer > 0f))
            return StartJump(JumpKind.Wall);

        // 점프실행중 x, 점프키 입력시.
        if (Kind == JumpKind.None && hasGroundRequest && _groundCoyoteTimer > 0f)
            return StartJump(JumpKind.Ground);

        return JumpKind.None;
    }

    /// <summary>
    /// 점프 시작, 점프 상태로 변경.
    /// </summary>
    private JumpKind StartJump(JumpKind kind)
    {
        bool releasedBeforeStart = _jumpReleasedDuringBuffer;
        ResetJumpMotion();
        Kind = kind;
        _phase = JumpPhase.Rising;
        _isGrounded = false;

        _jumpPressed = false;
        _jumpBufferTimer = 0f;
        _jumpReleasedDuringBuffer = false;
        _groundCoyoteTimer = 0f;
        _wallCoyoteTimer = 0f;

        // 벽점프라면?
        if (kind == JumpKind.Wall)
        {
            // 모든 크기의 벽점프를 처음부터 같은 곡선 제어로 처리한다.
            _wallHorizontalSpeed = Mathf.Abs(_stats.WallJumpDirection.x);
            _wallHorizontalCurveTime = _stats.WallJumpCancleTime;
            _wallMoveTimer = _stats.TimeTillJumpApex;
        }

        if (releasedBeforeStart) BeginJumpCut(InitialVerticalSpeed);
        return kind;
    }

    /// <summary>접지·낙하·점프 단계 중 하나만 계산하여 수직 속도를 반환한다.</summary>
    public float CalculateVerticalSpeed(float verticalSpeed, float timeStep)
    {
        if (_isGrounded) return -2f;

        // 점프 상태가 설정되어 있지 않으면 return
        if (Kind == JumpKind.None) return verticalSpeed + _stats.Gravity * timeStep;

        // 중력 설정
        float gravity = Kind == JumpKind.Wall ? _stats.WallJumpGravity : _stats.Gravity;

        #region 점프컷 or 속도 하강상태

        //페이즈별 속도 적용 선택
        if (_phase == JumpPhase.Cutting)
            return CalculateJumpCut(verticalSpeed, gravity, timeStep);

        if (_phase == JumpPhase.CutFalling)
            return verticalSpeed + gravity * GetCutGravityMultiplier() * timeStep;

        // 속도가 0이하일때.(phase -> Falling)
        if (verticalSpeed < 0f)
        {
            _phase = JumpPhase.Falling;
            // 기존 일반 점프와 벽점프의 자연 하강 배율 차이를 유지한다.
            float multiplier = Kind == JumpKind.Wall ? 1f : _stats.GravityOnReleaseMultiplier;
            return verticalSpeed + gravity * multiplier * timeStep;
        }

        #endregion

        #region 속도 상승상태
        float apexProgress = Mathf.InverseLerp(InitialVerticalSpeed, 0f, verticalSpeed);

        //정점도달?
        if (apexProgress > _stats.ApexThreshold)
        {
            if (_phase != JumpPhase.ApexHold) _apexElapsed = 0f;
            _phase = JumpPhase.ApexHold;
            _apexElapsed += timeStep;
            return _apexElapsed < _stats.ApexHangTime ? 0f : -0.01f;
        }

        _phase = JumpPhase.Rising;
        return verticalSpeed + gravity * timeStep;
        #endregion
    }

    /// <summary>현재 상승 속도에서 컷오프를 시작한다. 정점 유지 중에는 즉시 하강으로 전환한다.</summary>
    private void BeginJumpCut(float verticalSpeed)
    {
        if (_phase == JumpPhase.Cutting || _phase == JumpPhase.CutFalling) return;

        _cutDuration = _stats.TimeForUpwardsCancel;

        if (Kind == JumpKind.Wall)
        {
            float heldRatio = Mathf.Clamp01(_wallJumpElapsed /
                Mathf.Max(0.01f, _stats.WallJumpShortPressTime));

            float speedRatio = Mathf.Lerp(_stats.WallJumpShortSpeedRatio, 1f, heldRatio);

            // 작은 점프와 큰 점프 사이에 별도 제어로 전환되는 경계가 생기지 않게 보간한다.
            _wallHorizontalSpeed = Mathf.Abs(_stats.WallJumpDirection.x) * speedRatio;
            _wallHorizontalCurveTime = Mathf.Lerp(_stats.WallJumpHorizontalCutTime,
                _stats.WallJumpCancleTime, heldRatio);
            _cutDuration = Mathf.Lerp(_stats.WallJumpShortUpwardsCancelTime,
                _stats.TimeForUpwardsCancel, heldRatio);
        }

        _cutStartSpeed = _phase == JumpPhase.ApexHold ? 0f : Mathf.Max(0f, verticalSpeed);
        _cutElapsed = 0f;
        _phase = JumpPhase.Cutting;
    }

    /// <summary>
    /// 모든 벽점프는 착지하거나 새 점프를 시작할 때까지 같은 방식으로 수평 속도를 조절한다.
    /// 벽 방향 입력이 있을 때만 돌아가며, 입력을 놓으면 감속한다.
    /// 도약 크기는 속도와 전환 시간으로 조절하고 일반 공중 이동으로 갑자기 전환하지 않는다.
    /// </summary>
    public bool TryCalculateWallJumpSpeed(float currentSpeed, float moveInput,
        float timeStep, out float speed)
    {
        speed = currentSpeed;

        if (Kind != JumpKind.Wall)
            return false;

        float targetSpeed = Mathf.Abs(moveInput) >= _stats.MoveThreshold
            ? Mathf.Sign(moveInput) * _wallHorizontalSpeed
            : 0f;

        // 초기 속도부터 감속과 방향 전환까지 같은 처리로 이어서 급격한 재가속을 막는다.
        speed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref _wallHorizontalSmoothVelocity,
            Mathf.Max(0.01f, _wallHorizontalCurveTime), Mathf.Infinity, timeStep);

        if (targetSpeed == 0f && Mathf.Abs(speed) < 0.01f)
        {
            speed = 0f;
            _wallHorizontalSmoothVelocity = 0f;
        }

        return true;
    }

    /// <summary>점프컷 상태에서의 속도</summary>
    private float CalculateJumpCut(float verticalSpeed, float gravity, float timeStep)
    {
        float duration = Mathf.Max(0.01f, _cutDuration);

        if (_cutStartSpeed <= 0f || _cutElapsed >= duration)
        {
            _phase = JumpPhase.CutFalling;
            float fallStartSpeed = _cutStartSpeed <= 0f ? 0f : verticalSpeed;
            return fallStartSpeed + gravity * GetCutGravityMultiplier() * timeStep;
        }

        float speed = Mathf.Lerp(_cutStartSpeed, 0f, _cutElapsed / duration);
        _cutElapsed += timeStep;
        return speed;
    }

    /// <summary>현재 점프 종류에 맞는 키 해제 후 중력 배율을 선택한다.</summary>
    private float GetCutGravityMultiplier()
    {
        return Kind == JumpKind.Wall
            ? _stats.WallJumpGravityOnReleaseMultiplier : _stats.GravityOnReleaseMultiplier;
    }

    /// <summary>점프 궤적과 수평 제어 제한만 초기화한다. 착지 때 사용할 입력 버퍼는 건드리지 않는다.</summary>
    private void ResetJumpMotion()
    {
        Kind = JumpKind.None;
        _phase = JumpPhase.None;
        _apexElapsed = 0f;
        _cutElapsed = 0f;
        _cutStartSpeed = 0f;
        _cutDuration = 0f;
        _wallMoveTimer = 0f;
        _wallJumpElapsed = 0f;
        _wallHorizontalSpeed = 0f;
        _wallHorizontalCurveTime = 0f;
        _wallHorizontalSmoothVelocity = 0f;
    }

    /// <summary>잠금·리스폰·비활성화 시 접촉 이력, 입력 버퍼, 모든 타이머까지 초기화한다.</summary>
    public void Reset()
    {
        ResetJumpMotion();
        _isGrounded = false;
        _isTouchingWall = false;
        _jumpPressed = false;
        _jumpReleasedDuringBuffer = false;
        _lastWallDirection = 0;
        _jumpBufferTimer = 0f;
        _groundCoyoteTimer = 0f;
        _wallCoyoteTimer = 0f;
    }
}
