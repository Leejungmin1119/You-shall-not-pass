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
        if (player != null && player.isLocalPlayer && player.Race != null)
            CmdRequestGoalContact(player.Race.RoundNumber);
    }

    /// <summary>
    /// 서버에 현재 라운드의 골인을 요청한다. 이전 라운드에서 늦게 도착한 요청은 거절한다.
    /// </summary>
    [Command(requiresAuthority = false)]
    private void CmdRequestGoalContact(int roundNumber, NetworkConnectionToClient sender = null)
    {
        if (sender?.identity == null)
            return;

        if (!sender.identity.TryGetComponent(out PlayerState player) || player.Race == null ||
            player.Race.RoundNumber != roundNumber)
            return;

        player.Race.TryFinish(player);
    }

}
