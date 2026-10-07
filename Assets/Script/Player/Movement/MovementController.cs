using UnityEngine;

public struct CollisionState
{
    public bool IsGrounded;
    public bool WasGroundedLastFrame;

    public bool IsAgainstWall;
    public bool WasAgainstWallLastFrame;
    public int WallDirection;
    public float WallAngle;

    public void Reset()
    {
        WasGroundedLastFrame = IsGrounded;
        WasAgainstWallLastFrame = IsAgainstWall;

        IsGrounded = false;
        IsAgainstWall = false;
        WallDirection = 0;
        WallAngle = 0f;
    }
}

[RequireComponent(typeof(PlayerMovement))]
public class MovementController : MonoBehaviour
{
    public const float CollisionPadding = 0.015f;

    [Range(2, 100)] public int NumOfHorizontalRays = 4;
    [Range(2, 100)] public int NumOfVerticalRays = 4;

    [Header("Sensors")]
    [SerializeField] private float _verticalProbeDistance = 0.1f;
    [SerializeField] private float _horizontalProbeDistance = 0.1f;

    private float _horizontalRaySpace;
    private float _verticalRaySpace;

    private CapsuleCollider2D _coll;// 이거 나중에 박스로 바꾸면 거기에 맞게 수정실행 해야함
    private PlayerMovement _playerMovement;
    private PlayerMovementStats _moveStats;

    public CollisionState State { get; private set; }
    private CollisionState _internalState;

    public struct RaycastCorners
    {
        public Vector2 topLeft, topRight;
        public Vector2 bottomLeft, bottomRight;
    }

    public RaycastCorners RayCastCorners;

    private void Awake()
    {
        _coll = GetComponent<CapsuleCollider2D>();
        _playerMovement = GetComponent<PlayerMovement>();
        _moveStats = _playerMovement.moveStats;
    }

    private void Start()
    {
        CalculateRaySpacing();
    }

    /// <summary>
    /// 부활 전 접촉 기록을 지우고 현재 위치에서 지면과 벽을 다시 확인한다.
    /// </summary>
    public void ResetSensors()
    {
        _internalState = default;
        CalculateRaySpacing();
        PollSensors(Vector2.zero);
    }

    public void PollSensors(Vector2 moveDelta)
    {
        _internalState.Reset();
        UpdateRaycastCorners();

        GroundProbes(moveDelta);
        HorizontalProbes(moveDelta);

        State = _internalState;
    }

    private void GroundProbes(Vector2 moveDelta)
    {
        float rayLength = _verticalProbeDistance + CollisionPadding;
        float horizontalProjection = moveDelta.y <= 0f ? moveDelta.x : 0f;

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = RayCastCorners.bottomLeft +
                                Vector2.right * (_verticalRaySpace * i + horizontalProjection);
            RaycastHit2D hit = Physics2D.Raycast(
                rayOrigin,
                Vector2.down,
                rayLength,
                _moveStats.GroundLayer);

            if (_moveStats.DebugShowIsGrounded)
            {
                Debug.DrawRay(
                    rayOrigin,
                    Vector2.down * (rayLength + _moveStats.ExtraRayDebugDistance),
                    hit ? Color.cyan : Color.red);
            }

            if (!hit)
            {
                continue;
            }

            float groundAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
            if (groundAngle <= _moveStats.MaxSlopeAngle)
            {
                _internalState.IsGrounded = true;
                break;
            }
        }
    }

    private void HorizontalProbes(Vector2 moveDelta)
    {
        float rayLength = Mathf.Max(
            Mathf.Abs(moveDelta.x) + CollisionPadding,
            _horizontalProbeDistance + CollisionPadding);

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOriginLeft = RayCastCorners.bottomLeft +
                                    Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hitLeft = Physics2D.Raycast(
                rayOriginLeft,
                Vector2.left,
                rayLength,
                _moveStats.GroundLayer);

            if (_moveStats.DebugShowWallHit)
            {
                Debug.DrawRay(
                    rayOriginLeft,
                    Vector2.left * (rayLength + _moveStats.ExtraRayDebugDistance),
                    hitLeft ? Color.cyan : Color.red);
            }

            if (hitLeft)
            {
                // 추후 벽 Tag가 추가되면 이 지점에서 CompareTag 조건을 함께 확인한다.
                float wallAngle = Mathf.Round(Vector2.Angle(hitLeft.normal, Vector2.up));
                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = -1;
                    _internalState.WallAngle = wallAngle;
                    break;
                }
            }

            Vector2 rayOriginRight = RayCastCorners.bottomRight +
                                     Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hitRight = Physics2D.Raycast(
                rayOriginRight,
                Vector2.right,
                rayLength,
                _moveStats.GroundLayer);

            if (_moveStats.DebugShowWallHit)
            {
                Debug.DrawRay(
                    rayOriginRight,
                    Vector2.right * (rayLength + _moveStats.ExtraRayDebugDistance),
                    hitRight ? Color.cyan : Color.red);
            }

            if (hitRight)
            {
                // 추후 벽 Tag가 추가되면 이 지점에서 CompareTag 조건을 함께 확인한다.
                float wallAngle = Mathf.Round(Vector2.Angle(hitRight.normal, Vector2.up));
                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = 1;
                    _internalState.WallAngle = wallAngle;
                    break;
                }
            }
        }
    }

    private void UpdateRaycastCorners()
    {
        Bounds bounds = _coll.bounds;
        bounds.Expand(CollisionPadding * -2f);

        RayCastCorners.bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
        RayCastCorners.bottomRight = new Vector2(bounds.max.x, bounds.min.y);
        RayCastCorners.topLeft = new Vector2(bounds.min.x, bounds.max.y);
        RayCastCorners.topRight = new Vector2(bounds.max.x, bounds.max.y);
    }

    private void CalculateRaySpacing()
    {
        Bounds bounds = _coll.bounds;
        bounds.Expand(CollisionPadding * -2f);

        NumOfHorizontalRays = Mathf.Max(2, NumOfHorizontalRays);
        NumOfVerticalRays = Mathf.Max(2, NumOfVerticalRays);

        _horizontalRaySpace = bounds.size.y / (NumOfHorizontalRays - 1);
        _verticalRaySpace = bounds.size.x / (NumOfVerticalRays - 1);
    }

    public bool IsGrounded() => _internalState.IsGrounded;
    public bool IsTouchingWall() => _internalState.IsAgainstWall;

    public int GetWallDirection()
    {
        return _internalState.WallDirection;
    }
}
