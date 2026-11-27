using UnityEngine;

[CreateAssetMenu(menuName = "Movement")]
public class PlayerMovementStats : ScriptableObject
{
    //
    [Header("Walk")]
    [Range(0f, 1f)] public float MoveThreshold = 0.25f;
    [Range(0.25f, 50f)] public float GroundAcceleration = 5f;
    [Range(0.25f, 50f)] public float GroundDeceleration = 20f;
    [Range(0.25f, 50f)] public float AirAcceleration = 5f;
    [Range(0.25f, 50f)] public float AirDeceleration = 5f;
    [Range(0.25f, 50f)] public float WallJumpMoveAcceleration = 5f;
    [Range(0.25f, 50f)] public float WallJumpMoveDeceleration = 5f;

    [Header("Run")]
    [Range(1f, 2000f)] public float MaxRunSpeed = 1000f;

    [Header("Grounded/Collision Checks")]
    public LayerMask GroundLayer;
    [Range(0f, 90f)] public float MaxSlopeAngle = 70f;

    [Header("Jump")]
    public float JumpHeight = 6.5f;
    [Range(1f, 1.1f)] public float JumpHeightCompensationFactor = 1.054f;
    public float TimeTillJumpApex = 0.35f;
    [Range(0.01f, 5f)] public float GravityOnReleaseMultiplier = 2f;
    public float MaxFallSpeed = 26f;

    [Header("Jump Cut")]
    [Range(0.02f, 0.3f)] public float TimeForUpwardsCancel = 0.027f;

    [Header("Jump Apex")]
    [Range(0.5f, 1f)] public float ApexThreshold = 0.97f;
    [Range(0.01f, 1f)] public float ApexHangTime = 0.075f;

    [Header("Jump Buffer")]
    [Range(0f, 1f)] public float JumpBufferTime = 0.125f;

    [Header("Jump Coyote Time")]
    [Range(0f, 1f)] public float JumpCoyoteTime = 0.1f;

    [Header("Wall Detection")]
    [Range(70f, 90f)] public float MinAngleForWallSlide = 85f;
    [Range(90f, 135f)] public float MaxAngleForWallSlide = 95f;

    [Header("Wall Jump")]
    public Vector2 WallJumpDirection = new Vector2(-20f, 6.5f);
    [Range(0f, 1f)] public float WallJumpCoyoteTime = 0.125f;
    // 기존 에셋의 저장값을 유지하기 위해 필드 이름은 그대로 사용한다.
    [Tooltip("큰 벽점프에서 수평 속도를 바꾸는 곡선의 완만함입니다. 작을수록 빨리 방향을 바꿉니다.")]
    [Range(0f, 0.5f)] public float WallJumpCancleTime = 0.18f;
    [Range(0.01f, 5f)] public float WallJumpGravityOnReleaseMultiplier = 1f;

    [Header("Short Wall Jump")]
    [Tooltip("이 시간까지 키를 누른 비율로 작은 벽점프부터 큰 벽점프까지 연속적으로 조절합니다.")]
    [Min(0.01f)] public float WallJumpShortPressTime = 0.18f;

    [Tooltip("아주 짧게 누른 벽점프의 공중 수평 속도 비율입니다. 오래 누를수록 원래 힘에 가까워집니다.")]
    [Range(0f, 1f)] public float WallJumpShortSpeedRatio = 0.2f;

    [Tooltip("작은 벽점프에서 감속하거나 방향을 바꿀 때 속도를 부드럽게 연결하는 시간입니다.")]
    [Min(0.01f)] public float WallJumpHorizontalCutTime = 0.06f;

    [Tooltip("작은 벽점프에서 키를 뗀 뒤 상승을 줄이는 시간입니다. 길수록 조금 더 높게 뜁니다.")]
    [Min(0.01f)] public float WallJumpShortUpwardsCancelTime = 0.06f;

    [Header("Debug")]
    public bool DebugShowIsGrounded;
    public bool DebugShowWallHit;
    [Range(0f, 1f)] public float ExtraRayDebugDistance = 0.25f;

    public float Gravity { get; private set; }
    public float InitialJumpVelocity { get; private set; }
    public float AdjustedJumpHeight { get; private set; }

    public float WallJumpGravity { get; private set; }
    public float InitialWallJumpVelocity { get; private set; }
    public float AdjustedWallJumpHeight { get; private set; }

    private void OnValidate()
    {
        CalculateValues();
    }

    private void OnEnable()
    {
        CalculateValues();
    }

    private void CalculateValues()
    {
        AdjustedJumpHeight = JumpHeight * JumpHeightCompensationFactor;
        Gravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeTillJumpApex, 2f);
        InitialJumpVelocity = Mathf.Abs(Gravity) * TimeTillJumpApex;

        AdjustedWallJumpHeight = WallJumpDirection.y * JumpHeightCompensationFactor;
        WallJumpGravity = -(2f * AdjustedWallJumpHeight) / Mathf.Pow(TimeTillJumpApex, 2f);
        InitialWallJumpVelocity = Mathf.Abs(WallJumpGravity) * TimeTillJumpApex;
    }
}
