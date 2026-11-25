using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum PlayerStatus
{
    Alive,
    Dead,
    Finished
}

/// <summary>개인 사망·골인 상태와 서버가 지정한 위치 복구를 처리한다.</summary>
[RequireComponent(typeof(PlayerScore), typeof(PlayerMovement), typeof(Rigidbody2D))]
[RequireComponent(typeof(NetworkIdentity), typeof(NetworkTransformReliable))]
public sealed class PlayerState : NetworkBehaviour
{
    private static readonly List<PlayerState> serverPlayers = new List<PlayerState>(4);
    public static IReadOnlyList<PlayerState> ServerPlayers => serverPlayers;

    /// <summary>Domain Reload 없이 다시 실행해도 이전 서버 참가자를 남기지 않는다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServerPlayers() => serverPlayers.Clear();

    private PlayerMovement _movement;
    private Rigidbody2D _body;
    private NetworkTransformReliable _networkTransform;
    private Renderer[] _renderers;
    private Canvas[] _canvases;
    private Collider2D[] _colliders;
    private bool? _previousVisible;
    private double _lastDeathStep = double.NegativeInfinity;
    [SyncVar] private double _invincibleUntil;
    [SyncVar] private int inventoryCount;
    [SyncVar] private bool facingRight = true;
    private bool? lastSentFacing;
    public bool FacingRight => isLocalPlayer ? _movement.IsFacingRight : facingRight;
    public int InventoryCount => inventoryCount;
    public bool IsInvincible => NetworkTime.time < _invincibleUntil;
    private const double RespawnInvincibleTime = 0.5;

    [SyncVar(hook = nameof(OnStatusChanged))] private PlayerStatus status = PlayerStatus.Alive;

    public PlayerStatus Status => status;
    public RaceManager Race { get; private set; }
    public bool CanAct => status == PlayerStatus.Alive && (Race == null || Race.IsRunning);
    public bool IsAttacker => Race != null && Race.Roles != null && Race.Roles.IsAttacker(this);
    public bool IsVisible => status != PlayerStatus.Finished &&
        (Race == null || Race.Roles == null || IsAttacker);
    public bool CanMove => CanAct && (Race == null || Race.Roles == null || IsAttacker);
    // 개발자 1인 시험에서는 혼자 공격자로 달리면서 함정도 써서 맵과 함정을 함께 시험한다.
    public bool UsesTraps => !IsAttacker || IsSoloTest;
    public bool CanUseTraps => CanAct && UsesTraps;
    private static bool IsSoloTest => NetworkManager.singleton is CapstoneRoomManager room && room.IsSoloTest;
    public bool CanReceiveHit => CanMove && NetworkTime.time >= _invincibleUntil;

    [Server]
    public void ServerSetInventoryCount(int count) => inventoryCount = count;

    /// <summary>상태 표시와 위치 복구에 필요한 자기 컴포넌트와 레이스 담당을 보관한다.</summary>
    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _body = GetComponent<Rigidbody2D>();
        _networkTransform = GetComponent<NetworkTransformReliable>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _canvases = GetComponentsInChildren<Canvas>(true);
        _colliders = GetComponentsInChildren<Collider2D>(true);
        // Mirror가 씬 오브젝트를 활성화하기 전에 플레이어가 생성될 수도 있다.
        Race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
    }

    /// <summary>서버의 개인 기록을 초기화하고 레이스 참가자로 등록한다.</summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        status = PlayerStatus.Alive;
        _lastDeathStep = double.NegativeInfinity;
        _invincibleUntil = 0;
        if (!serverPlayers.Contains(this))
            serverPlayers.Add(this);

        if (Race != null)
            Race.RegisterPlayer(this);
    }

    /// <summary>접속 종료 시 레이스 참가 목록에서 자신을 제거한다.</summary>
    public override void OnStopServer()
    {
        serverPlayers.Remove(this);
        if (Race != null && Race.isServer)
            Race.UnregisterPlayer(this);

        base.OnStopServer();
    }

    /// <summary>처음 수신한 상태가 이미 골인 상태여도 캐릭터 표시를 맞춘다.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyStatus();
    }

    /// <summary>서버에서 같은 물리 프레임의 사망과 중복 골인을 거절한다.</summary>
    [Server]
    public bool FinishRace()
    {
        if (!CanMove || _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        status = PlayerStatus.Finished;
        ApplyStatus();
        return true;
    }

    /// <summary>골인 상태가 동기화되면 캐릭터 표시와 개인 조작을 갱신한다.</summary>
    private void OnStatusChanged(PlayerStatus previous, PlayerStatus current)
    {
        ApplyStatus();
    }

    /// <summary>생존 상태가 같아도 공수 교대가 동기화되면 캐릭터 표시를 한 번 갱신한다.</summary>
    private void LateUpdate()
    {
        if (_previousVisible != IsVisible)
            ApplyStatus();
    }

    /// <summary>바라보는 방향이 바뀔 때만 서버에 알려 뒤에서 맞는 경우도 동일하게 판정한다.</summary>
    private void Update()
    {
        if (isLocalPlayer && CanMove && lastSentFacing != _movement.IsFacingRight)
        {
            lastSentFacing = _movement.IsFacingRight;
            CmdSetFacing(_movement.IsFacingRight);
        }
    }

    /// <summary>소유자가 보낸 현재 방향만 수락한다. 연출 중에는 이미 확정한 방향을 유지한다.</summary>
    [Command]
    private void CmdSetFacing(bool right)
    {
        if (CanMove)
            facingRight = right;
    }

    /// <summary>네트워크와 개인 UI는 유지하고 수비자·골인 캐릭터의 월드 표시와 충돌을 끈다.</summary>
    private void ApplyStatus()
    {
        bool visible = IsVisible;
        _previousVisible = visible;

        foreach (Renderer visual in _renderers)
            visual.enabled = visible;

        foreach (Canvas canvas in _canvases)
            canvas.enabled = visible && status == PlayerStatus.Alive;

        foreach (Collider2D collider in _colliders)
            collider.enabled = visible && status == PlayerStatus.Alive;

        if (!isLocalPlayer)
            return;

        _movement.MovementLocked = !CanMove;

        if (!visible || status == PlayerStatus.Dead)
        {
            _movement.ResetMovement();
            _body.simulated = false;
        }
    }

    /// <summary>서버에서 중복 사망과 골인·판 종료 후의 사망 요청을 막는다.</summary>
    [Server]
    public bool TryDie(Vector2? hitOrigin = null)
        => TryDeath(true, hitOrigin ?? (Vector2)transform.position);

    /// <summary>낙사는 피격 연출을 생략하고 기존처럼 다음 공격자로 즉시 교대한다.</summary>
    [Server]
    public bool TryFall() => TryDeath(false, transform.position);

    /// <summary>한 번의 사망만 승인하고 원인에 맞는 교대 방식을 레이스에 전달한다.</summary>
    [Server]
    private bool TryDeath(bool present, Vector2 hitOrigin)
    {
        if (!CanMove || _lastDeathStep == Time.fixedTimeAsDouble)
            return false;

        _lastDeathStep = Time.fixedTimeAsDouble;
        status = PlayerStatus.Dead;
        Race?.NotifyDeath(this, present, hitOrigin);
        Debug.Log($"플레이어 사망: netId={netId}");
        return true;
    }

    /// <summary>다음 라운드에서 개인 기록과 골인 상태를 비우고 출발 위치로 복구한다.</summary>
    [Server]
    public void ResetForRound(Vector2 position)
    {
        _lastDeathStep = double.NegativeInfinity;
        status = PlayerStatus.Alive;
        Respawn(position);
    }

    /// <summary>관찰자와 소유자의 위치를 함께 복구하고 짧은 피격 무적을 적용한다.</summary>
    [Server]
    public void Respawn(Vector2 position)
    {
        // 공수 경기의 사망 복귀는 교대 담당이 출발점에서 처리한다.
        if (status == PlayerStatus.Dead && Race != null && Race.Roles != null)
            return;

        Vector3 destination = new Vector3(position.x, position.y, transform.position.z);
        _networkTransform.ServerTeleport(destination, transform.rotation);
        RpcResetMovement(position);

        _invincibleUntil = NetworkTime.time + RespawnInvincibleTime;
        status = PlayerStatus.Alive;
        ApplyStatus();
    }

    /// <summary>소유자의 물리 위치와 점프·속도·입력 버퍼를 초기화한다.</summary>
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
