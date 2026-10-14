using Mirror;
using UnityEngine;

public enum PlayerStatus
{
    Alive,
    Dead
}

/// <summary>
/// 서버에서 플레이어의 사망 상태와 지정 위치 배치를 처리한다.
/// </summary>
[RequireComponent(typeof(PlayerScore), typeof(PlayerMovement), typeof(Rigidbody2D))]// 3개의 컴포넌트 요구
[RequireComponent(typeof(NetworkIdentity), typeof(NetworkTransformReliable))]// 2개 +추가요구
public sealed class PlayerState : NetworkBehaviour
{
    private PlayerScore _score;
    private PlayerMovement _movement;
    private Rigidbody2D _body;
    private NetworkTransformReliable _networkTransform;
    private double _lastDeathStep = double.NegativeInfinity;
    private bool _goalCompleted;
    [SyncVar] private PlayerStatus status = PlayerStatus.Alive;
    public PlayerStatus Status => status;
    private rolechangeManager _roleManager;

    public bool IsRoleManaged => _roleManager != null;
    public bool IsAttacker => _roleManager != null && _roleManager.IsCurrentAttacker(netIdentity);

    private void Awake()
    {
        _score = GetComponent<PlayerScore>();
        _movement = GetComponent<PlayerMovement>();
        _body = GetComponent<Rigidbody2D>();
        _networkTransform = GetComponent<NetworkTransformReliable>();
        // Mirror가 아직 활성화하지 않은 씬 관리자도 찾아 초기 생성 순서에 영향을 받지 않는다.
        _roleManager = FindFirstObjectByType<rolechangeManager>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// 서버 시작시 초기화
    /// </summary>
    public override void OnStartServer()
    {
        status = PlayerStatus.Alive;
        _lastDeathStep = double.NegativeInfinity;
        _goalCompleted = false;
        _roleManager?.RegisterPlayer(this);
    }

    /// <summary>
    /// 클라이언트에서 역할 관리 대상에 등록하고 현재 공격자 상태를 적용한다.
    /// </summary>
    public override void OnStartClient()
    {
        if (_roleManager == null)
            _roleManager = FindFirstObjectByType<rolechangeManager>(FindObjectsInactive.Include);
        _roleManager?.RegisterPlayer(this);
    }

    /// <summary>
    /// 제거된 플레이어를 역할 관리 목록에서 해제한다.
    /// </summary>
    public override void OnStopClient()
    {
        _roleManager?.UnregisterPlayer(this);
    }

    /// <summary>
    /// 서버에서 연결이 종료된 플레이어를 역할 관리 목록에서 해제한다.
    /// </summary>
    public override void OnStopServer()
    {
        _roleManager?.UnregisterPlayer(this);
    }

    /// <summary>
    /// 골인 되었을시 서버에서 골인 처리,점수 획득 메서드 호출
    /// </summary>
    [Server]
    public bool TryCompleteGoal()
    {
        if (status != PlayerStatus.Alive || _goalCompleted ||
            _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        _goalCompleted = true;
        _score.AddGoalScore();
        return true;
    }

    /// <summary>
    /// 서버에서 중복 사망을 막고 기존 공격자를 사망 상태로 변경한다.
    /// </summary>
    [Server]
    public bool TryDie()
    {
        if (status == PlayerStatus.Dead || _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        _lastDeathStep = Time.fixedTimeAsDouble;
        status = PlayerStatus.Dead;// 사망으로 변경

        Debug.Log("낙사로 인한 사망");

        return true;
    }

    /// <summary>
    /// 서버와 모든 관찰자의 위치를 순간 이동시키고 소유자의 이동 상태도 초기화한다.
    /// </summary>
    [Server]
    public void Respawn(Vector2 position)
    {
        Vector3 destination = new Vector3(position.x, position.y, transform.position.z);
        _networkTransform.ServerTeleport(destination, transform.rotation);
        RpcResetMovement(position);
        _goalCompleted = false;

        // 부활 처리 후 상태 변경
        status = PlayerStatus.Alive;
    }

    /// <summary>
    /// 실제 이동을 실행하는 소유자만 물리 위치와 속도, 점프와 입력 상태를 초기화한다.
    /// </summary>
    [ClientRpc]
    private void RpcResetMovement(Vector2 position)
    {
        if (!isLocalPlayer)
            return;
        _body.position = position;
        _movement.ResetMovement();
        Physics2D.SyncTransforms();
    }

}
