using Mirror;
using UnityEngine;

/// <summary>
/// 자기 플레이어의 골인 영역 진입을 서버에 전달한다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D), typeof(NetworkIdentity))]
public sealed class GoalZone : NetworkBehaviour
{
    /// <summary>
    /// 자기 플레이어가 진입하면 서버에 골인 처리를 요청한다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerState player = other.GetComponentInParent<PlayerState>();
        if (player != null && player.isLocalPlayer)
            CmdRequestGoalContact();
    }

    private void Update()
    {
        var race = RaceSession.Instance;
        if (!isServer || race == null || !race.IsRacing) return;
        // 접촉 알림이 이동 스냅샷보다 먼저 도착해도 뒤따르는 서버 좌표로 판정한다.
        foreach (var player in race.Players)
            if (player != null) ServerTryContact(player.connectionToClient);
    }

    /// <summary>
    /// 자기 플레이어의 골인 영역에 진입했다고 서버에 요청한다.
    /// </summary>
    [Command(requiresAuthority = false)]
    private void CmdRequestGoalContact(NetworkConnectionToClient sender = null)
    {
        ServerTryContact(sender);
    }

    [Server]
    public bool ServerTryContact(NetworkConnectionToClient sender)
    {
        if (!isActiveAndEnabled || NetworkServer.isLoadingScene || sender?.identity == null) return false;
        if (!sender.identity.TryGetComponent(out PlayerState player)) return false;
        // 소유자의 접촉 알림을 호스트가 보유한 위치로 다시 검증한다.
        var zone = GetComponent<BoxCollider2D>();
        var bodyCollider = player.GetComponent<Collider2D>();
        if (zone == null || bodyCollider == null) return false;
        Bounds area = zone.bounds;
        area.Expand(0.5f);
        if (!area.Intersects(bodyCollider.bounds)) return false;
        return player.TryCompleteGoal();
    }
}
