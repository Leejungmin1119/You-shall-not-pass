using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 첫 공격자를 무작위로 지정하고 낙사 시 로비 번호 순서로 교대한다.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public sealed class rolechangeManager : NetworkBehaviour
{
    [SerializeField] private Transform firstSpawnPoint;
    [SyncVar(hook = nameof(OnAttackerChanged))] private uint attackerNetId;
    private readonly List<PlayerState> _players = new List<PlayerState>(4);
    private bool _started;
    private MapViewController _mapView;
    private FallZone _fallZone;

    /// <summary>
    /// 역할 교대 후 추적 대상을 변경할 카메라 관리자를 보관한다.
    /// </summary>
    private void Awake()
    {
        _mapView = FindFirstObjectByType<MapViewController>();
    }

    /// <summary>
    /// 서버와 클라이언트에서 생성된 플레이어를 중복 없이 등록한다.
    /// </summary>
    public void RegisterPlayer(PlayerState player)
    {
        if (!_players.Contains(player)) _players.Add(player);
        ApplyRoles();
    }

    /// <summary>
    /// 연결 종료 또는 씬 종료 시 플레이어를 목록에서 제거한다.
    /// </summary>
    public void UnregisterPlayer(PlayerState player)
    {
        _players.Remove(player);
    }

    /// <summary>
    /// 모든 접속자의 게임 플레이어가 생성되면 첫 공격자를 지정한다.
    /// </summary>
    private void Update()
    {
        if (!isServer || _started || NetworkServer.isLoadingScene ||
            NetworkServer.connections.Count == 0)
            return;

        foreach (var connection in NetworkServer.connections.Values)
        {
            if (connection.identity == null ||
                !connection.identity.TryGetComponent(out PlayerState player) ||
                !_players.Contains(player))
                return;
        }

        StartFirstTurn();
    }

    /// <summary>
    /// 서버에서 첫 공격자를 한 번 추첨하고 시작 위치에 배치한다.
    /// </summary>
    [Server]
    private void StartFirstTurn()
    {
        // 교대 순서는 유지하고 시작할 플레이어만 무작위로 선택한다.
        _players.Sort((a, b) => GetPlayerOrder(a).CompareTo(GetPlayerOrder(b)));
        int firstIndex = Random.Range(0, _players.Count);
        PlayerState firstPlayer = _players[firstIndex];
        _started = true;
        Vector2 position = firstSpawnPoint != null
            ? (Vector2)firstSpawnPoint.position : (Vector2)firstPlayer.transform.position;
        firstPlayer.Respawn(position);
        attackerNetId = firstPlayer.netId;
        ApplyRoles();
        Debug.Log($"첫 공격자: 플레이어 {firstIndex + 1}", this);
    }

    /// <summary>
    /// 로비의 플레이어 번호를 사용하며 로비 없이 테스트하면 네트워크 생성 순서를 사용한다.
    /// </summary>
    private long GetPlayerOrder(PlayerState player)
    {
        if (NetworkManager.singleton is NetworkRoomManager room)
        {
            foreach (NetworkRoomPlayer lobbyPlayer in room.roomSlots)
            {
                if (lobbyPlayer != null && lobbyPlayer.connectionToClient == player.connectionToClient)
                    return lobbyPlayer.index;
            }
        }
        return player.netId;
    }

    /// <summary>
    /// 요청자가 현재 공격자인지 확인한다. 클라이언트에서도 동기화된 ID로 확인한다.
    /// </summary>
    public bool IsCurrentAttacker(NetworkIdentity player)
    {
        return player != null && attackerNetId != 0 && player.netId == attackerNetId;
    }

    /// <summary>
    /// 현재 공격자의 낙사를 한 번 처리하고 다음 공격자를 탐색된 위치에 배치한다.
    /// </summary>
    [Server]
    public bool TryChangeAfterFall(NetworkIdentity fallenPlayer, Vector2 position)
    {
        if (!_started || !IsCurrentAttacker(fallenPlayer)) return false;
        int index = _players.FindIndex(p => p != null && p.netIdentity == fallenPlayer);
        if (index < 0 || !_players[index].TryDie()) return false;

        // 마지막 플레이어의 다음 순서는 첫 플레이어다. 혼자 테스트하면 자신으로 돌아온다.
        PlayerState next = _players[(index + 1) % _players.Count];
        next.Respawn(position);
        attackerNetId = next.netId;
        ApplyRoles();
        Debug.Log($"낙사 공수 교대: 플레이어 {index + 1} → 플레이어 {(index + 1) % _players.Count + 1}", this);
        return true;
    }

    /// <summary>
    /// 현재 공격자가 함정에 맞은 것을 한 번 처리한다. 함정 주인에게 점수를 주고
    /// 다음 공격자를 맞은 위치 뒤쪽(낙사와 같은 SafeZone 규칙)에 배치한다.
    /// 교대가 끝난 뒤 도착한 다른 적중은 공격자가 바뀌어 있으므로 무시된다(동시 적중 1건만 인정).
    /// </summary>
    [Server]
    public bool TryChangeAfterHit(NetworkIdentity hitPlayer, Vector2 hitPosition, NetworkIdentity hitter)
    {
        if (!_started || !IsCurrentAttacker(hitPlayer)) return false;
        // 자기 함정이나 주인을 알 수 없는 함정은 점수와 교대를 만들지 않는다.
        if (hitter == null || hitter == hitPlayer) return false;
        int index = _players.FindIndex(p => p != null && p.netIdentity == hitPlayer);
        if (index < 0) return false;

        if (_fallZone == null) _fallZone = FindFirstObjectByType<FallZone>();
        if (_fallZone == null || !_fallZone.TryGetRespawnPosition(hitPosition, out Vector2 position))
        {
            Debug.LogError("적중 후 부활 위치를 찾지 못했습니다. FallZone의 SafeZone과 예비 부활 위치를 확인하세요.", this);
            return false;
        }

        if (!_players[index].TryDie()) return false;

        if (hitter.TryGetComponent(out PlayerScore hitterScore))
            hitterScore.AddHitScore();

        PlayerState next = _players[(index + 1) % _players.Count];
        next.Respawn(position);
        attackerNetId = next.netId;
        ApplyRoles();
        Debug.Log($"적중 공수 교대: 플레이어 {index + 1} → 플레이어 {(index + 1) % _players.Count + 1}, " +
                  $"적중자 netId={hitter.netId}, 맞은 위치 {hitPosition}, 부활 위치 {position}", this);
        return true;
    }

    /// <summary>
    /// 서버에서 변경된 공격자 ID를 받은 클라이언트에 역할을 적용한다.
    /// </summary>
    private void OnAttackerChanged(uint previous, uint current)
    {
        ApplyRoles();
    }

    /// <summary>
    /// 공격자의 표시와 소유자 입력을 활성화하고 모든 화면의 카메라 추적 대상을 변경한다.
    /// </summary>
    private void ApplyRoles()
    {
        foreach (PlayerState player in _players)
        {
            if (player == null) continue;
            bool isAttacker = IsCurrentAttacker(player.netIdentity);
            player.GetComponent<NetworkPlayerController>()?.ApplyAttackerRole(isAttacker);
            if (isAttacker && _mapView != null)
                _mapView.SetTrackingTarget(player.transform);
        }
    }
}
