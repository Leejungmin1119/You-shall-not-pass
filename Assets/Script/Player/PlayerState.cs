using UnityEngine;

public enum PlayerStatus
{
    Alive,
    Dead
}

/// <summary>
/// 골인과 낙사의 중복 처리를 막고 플레이어를 지정 위치에 부활시킨다.
/// </summary>
[RequireComponent(typeof(PlayerScore), typeof(PlayerMovement), typeof(Rigidbody2D))]
public sealed class PlayerState : MonoBehaviour
{
    private PlayerScore _score;
    private PlayerMovement _movement;
    private Rigidbody2D _body;
    // 물리시간을 저장하는 역할
    private double _lastDeathStep = double.NegativeInfinity;

    public PlayerStatus Status { get; private set; } = PlayerStatus.Alive;

    /// <summary>
    /// 점수, 이동 및 물리 컴포넌트의 참조를 확보한다.
    /// </summary>
    private void Awake()
    {
        _score = GetComponent<PlayerScore>();
        _movement = GetComponent<PlayerMovement>();
        _body = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// 살아 있는 플레이어의 골인을 한 번 처리하고 개인 점수를 추가한다.
    /// </summary>
    public bool TryCompleteGoal()
    {
        if (Status != PlayerStatus.Alive ||
            _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        _score.AddGoalScore();
        return true;
    }

    /// <summary>
    /// 같은 물리 프레임의 중복 낙사를 막고 지정 위치에 즉시 부활시킨다.
    /// </summary>
    public bool TryDie(Vector2 respawnPosition)
    {
        if (Status == PlayerStatus.Dead ||
            _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        // 죽은 시간 저장
        _lastDeathStep = Time.fixedTimeAsDouble;

        // 사망 처리
        Status = PlayerStatus.Dead;

        Respawn(respawnPosition);
        return true;
    }

    /// <summary>
    /// 지정 위치로 이동하고 이동 상태를 초기화한다. 기존 점수는 유지한다.
    /// </summary>
    private void Respawn(Vector2 position)
    {
        _body.position = position;
        Physics2D.SyncTransforms();
        _movement.ResetMovement();
        Status = PlayerStatus.Alive;
    }
}
