using UnityEngine;

[SelectionBase]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public PlayerMovementStats moveStats;

    private Rigidbody2D _rb;
    private PlayerInputAdapter _input;

    public bool IsFacingRight { get; private set; }
    public bool MovementLocked { get; set; }
    public MovementController Controller { get; private set; }
    [HideInInspector] public Vector2 Velocity;

    public bool IsJumping { get; private set; }

    private bool _isFastFalling;
    private bool _isFalling;
    private float _fastFallTime;
    private float _fastFallReleaseSpeed;

    private float _apexPoint;
    private float _timePastApexThreshold;
    private bool _isPastApexThreshold;

    private float _jumpBufferTimer;
    private bool _jumpReleasedDuringBuffer;
    private float _coyoteTimer;

    private bool _useWallJumpmoveStats;
    private bool _isWallJumping;
    private float _wallJumpTime;
    private bool _isWallJumpFastFalling;
    private bool _isWallJumpFalling;
    private float _wallJumpFastFallTime;
    private float _wallJumpFastFallReleaseSpeed;
    private int _lastWallDir;
    private float _wallJumpCoyoteTimer;
    private float WallJumpCancleTime;

    private float _wallJumpApexPoint;
    private float _timePastWallJumpApexThreshold;
    private bool _isPastWallJumpApexThreshold;

    private void Awake()
    {
        IsFacingRight = true;
        _rb = GetComponent<Rigidbody2D>();
        Controller = GetComponent<MovementController>();
        _input = new PlayerInputAdapter();
    }

    private void Update()
    {
        if (MovementLocked)
        {
            return;
        }

        _input.Read();

        Debug.Log(Controller.IsGrounded());
    }

    private void FixedUpdate()
    {
        if (MovementLocked)
        {
            Velocity = Vector2.zero;
            _rb.linearVelocity = Vector2.zero;
            _input.Clear();
            return;
        }

        Velocity = _rb.linearVelocity;
        Controller.PollSensors(Velocity * Time.fixedDeltaTime);

        CountTimers(Time.fixedDeltaTime);

        JumpChecks();
        LandCheck();
        WallJumpCheck();
        VelocityReset();

        HandleHorizontalMovement(Time.fixedDeltaTime);
        Jump(Time.fixedDeltaTime);
        WallJump(Time.fixedDeltaTime);
        Fall(Time.fixedDeltaTime);

        ClampVelocity();

        _rb.linearVelocity = Velocity;
        _input.Clear();
    }

    private void ClampVelocity()
    {
        Velocity.y = Mathf.Clamp(Velocity.y, -moveStats.MaxFallSpeed, 100f);
    }

    private void HandleHorizontalMovement(float timeStep)
    {
        if (WallJumpCancleTime >= 0f)
        {
            return;
        }

        float acceleration = Controller.IsGrounded()
            ? moveStats.GroundAcceleration
            : moveStats.AirAcceleration;

        float deceleration = Controller.IsGrounded()
            ? moveStats.GroundDeceleration
            : moveStats.AirDeceleration;

        if (_useWallJumpmoveStats)
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

    private void Turn(bool turnRight)
    {
        IsFacingRight = turnRight;
    }

    private void LandCheck()
    {
        if (!Controller.IsGrounded() || Velocity.y > 0f)
        {
            return;
        }

        if (IsJumping || _isFalling || _isWallJumpFalling || _isWallJumping)
        {
            ResetJumpValues();
            ResetWallJumpValues();
        }
    }

    private void Fall(float timeStep)
    {
        if (!Controller.IsGrounded() && !IsJumping && !_isWallJumping)
        {
            _isFalling = true;
            Velocity.y += moveStats.Gravity * timeStep;
        }
    }

    private void VelocityReset()
    {
        if (Controller.IsGrounded() && Velocity.y <= 0f)
        {
            Velocity.y = -2f;
        }
    }

    private void ResetJumpValues()
    {
        IsJumping = false;
        _isFalling = false;
        _isFastFalling = false;
        _fastFallTime = 0f;
        _isPastApexThreshold = false;
    }

    private void JumpChecks()
    {
        if (_input.JumpPressed)
        {
            if (Controller.IsTouchingWall() && !Controller.IsGrounded())
            {
                return;
            }

            _jumpBufferTimer = moveStats.JumpBufferTime;
            _jumpReleasedDuringBuffer = false;
        }

        if (_input.JumpReleased)
        {
            if (_jumpBufferTimer > 0f)
            {
                _jumpReleasedDuringBuffer = true;
            }

            if (IsJumping && Velocity.y > 0f)
            {
                if (_isPastApexThreshold)
                {
                    _isPastApexThreshold = false;
                    _isFastFalling = true;
                    _fastFallTime = moveStats.TimeForUpwardsCancel;
                    Velocity.y = 0f;
                }
                else
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = Velocity.y;
                }
            }
        }

        if (_jumpBufferTimer > 0f && !IsJumping &&
            (Controller.IsGrounded() || _coyoteTimer > 0f))
        {
            InitiateJump();

            if (_jumpReleasedDuringBuffer)
            {
                _isFastFalling = true;
                _fastFallReleaseSpeed = Velocity.y;
            }
        }
    }

    private void InitiateJump()
    {
        IsJumping = true;
        _input.ConsumeJump();
        ResetWallJumpValues();

        _fastFallTime = 0f;
        _isFastFalling = false;
        _isPastApexThreshold = false;
        _jumpBufferTimer = 0f;
        Velocity.y = moveStats.InitialJumpVelocity;
    }

    private void Jump(float timeStep)
    {
        if (IsJumping)
        {
            Debug.Log("실행중");
            if (Velocity.y >= 0f)
            {
                _apexPoint = Mathf.InverseLerp(moveStats.InitialJumpVelocity, 0f, Velocity.y);

                if (_apexPoint > moveStats.ApexThreshold)
                {
                    if (!_isPastApexThreshold)
                    {
                        _isPastApexThreshold = true;
                        _timePastApexThreshold = 0f;
                    }

                    _timePastApexThreshold += timeStep;
                    Velocity.y = _timePastApexThreshold < moveStats.ApexHangTime
                        ? 0f
                        : -0.01f;
                }
                else if (!_isFastFalling)
                {
                    Velocity.y += moveStats.Gravity * timeStep;
                    _isPastApexThreshold = false;
                }
            }
            else if (!_isFastFalling)
            {
                Velocity.y += moveStats.Gravity * moveStats.GravityOnReleaseMultiplier * timeStep;
                _isFalling = true;
            }
        }
        // 빠른 하강(컷오프)
        if (_isFastFalling)
        {
            Debug.Log("실행중2");
            if (_fastFallTime >= moveStats.TimeForUpwardsCancel)
            {
                Velocity.y += moveStats.Gravity * moveStats.GravityOnReleaseMultiplier * timeStep;
            }
            else
            {
                Velocity.y = Mathf.Lerp(
                    _fastFallReleaseSpeed,
                    0f,
                    _fastFallTime / moveStats.TimeForUpwardsCancel);
            }

            _fastFallTime += timeStep;
        }
    }

    private void ResetWallJumpValues()
    {
        _useWallJumpmoveStats = false;
        _isWallJumping = false;
        _isWallJumpFastFalling = false;
        _isWallJumpFalling = false;
        _isPastWallJumpApexThreshold = false;
        _wallJumpFastFallTime = 0f;
        _wallJumpTime = 0f;
    }

    private void WallJumpCheck()
    {
        if (ShouldWallJumpCoyote())
        {
            _wallJumpCoyoteTimer = moveStats.WallJumpCoyoteTime;
        }

        if (_input.JumpReleased && !Controller.IsTouchingWall() && _isWallJumping &&
            Velocity.y > 0f)
        {
            if (_isPastWallJumpApexThreshold)
            {
                _isPastWallJumpApexThreshold = false;
                _isWallJumpFastFalling = true;
                _wallJumpFastFallTime = moveStats.TimeForUpwardsCancel;
                Velocity.y = 0f;
            }
            else
            {
                _isWallJumpFastFalling = true;
                _wallJumpFastFallReleaseSpeed = Velocity.y;
            }
        }

        if (_input.JumpPressed && _wallJumpCoyoteTimer > 0f && !Controller.IsGrounded())
        {
            InitiateWallJump();
        }
    }

    private void InitiateWallJump()
    {
        _isWallJumping = true;
        _useWallJumpmoveStats = true;

        _input.ConsumeJump();
        ResetJumpValues();

        _wallJumpTime = 0f;
        Velocity.y = moveStats.InitialWallJumpVelocity;
        Velocity.x = Mathf.Abs(moveStats.WallJumpDirection.x) * -_lastWallDir;
        WallJumpCancleTime = moveStats.WallJumpCancleTime;
    }

    private void WallJump(float timeStep)
    {
        if (_isWallJumping)
        {
            _wallJumpTime += timeStep;
            if (_wallJumpTime >= moveStats.TimeTillJumpApex)
            {
                _useWallJumpmoveStats = false;
            }

            if (Velocity.y >= 0f)
            {
                _wallJumpApexPoint = Mathf.InverseLerp(
                    moveStats.WallJumpDirection.y,
                    0f,
                    Velocity.y);

                if (_wallJumpApexPoint > moveStats.ApexThreshold)
                {
                    if (!_isPastWallJumpApexThreshold)
                    {
                        _isPastWallJumpApexThreshold = true;
                        _timePastWallJumpApexThreshold = 0f;
                    }

                    _timePastWallJumpApexThreshold += timeStep;
                    Velocity.y = _timePastWallJumpApexThreshold < moveStats.ApexHangTime
                        ? 0f
                        : -0.01f;
                }
                else if (!_isWallJumpFastFalling)
                {
                    Velocity.y += moveStats.WallJumpGravity * timeStep;
                    _isPastWallJumpApexThreshold = false;
                }
            }
            else if (!_isWallJumpFastFalling)
            {
                Velocity.y += moveStats.WallJumpGravity * timeStep;
                _isWallJumpFalling = true;
            }
        }

        if (_isWallJumpFastFalling)
        {
            if (_wallJumpFastFallTime >= moveStats.TimeForUpwardsCancel)
            {
                Velocity.y += moveStats.WallJumpGravity *
                              moveStats.WallJumpGravityOnReleaseMultiplier *
                              timeStep;
            }
            else
            {
                Velocity.y = Mathf.Lerp(
                    _wallJumpFastFallReleaseSpeed,
                    0f,
                    _wallJumpFastFallTime / moveStats.TimeForUpwardsCancel);
            }

            _wallJumpFastFallTime += timeStep;
        }
    }

    private bool ShouldWallJumpCoyote()
    {
        if (!Controller.IsTouchingWall())
        {
            return false;
        }

        bool isWallAngleValid =
            Controller.State.WallAngle >= moveStats.MinAngleForWallSlide &&
            Controller.State.WallAngle <= moveStats.MaxAngleForWallSlide;

        if (!isWallAngleValid)
        {
            return false;
        }

        if (Controller.State.WallDirection != 0)
        {
            _lastWallDir = Controller.GetWallDirection();
        }

        return true;
    }

    private void CountTimers(float timeStep)
    {
        _jumpBufferTimer -= timeStep;
        HandleCoyoteTimer(timeStep);
        _wallJumpCoyoteTimer -= timeStep;
        WallJumpCancleTime -= timeStep;
    }

    private void HandleCoyoteTimer(float timeStep)
    {
        if (Controller.IsGrounded())
        {
            _coyoteTimer = moveStats.JumpCoyoteTime;
        }
        else
        {
            _coyoteTimer -= timeStep;
        }
    }

    public bool IsWallSlideable(float angle)
    {
        return angle >= moveStats.MinAngleForWallSlide &&
            angle <= moveStats.MaxAngleForWallSlide;
    }
}
