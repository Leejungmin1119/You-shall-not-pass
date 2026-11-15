using Mirror;
using UnityEngine;

/// <summary>소유자의 알림과 서버가 보유한 위치를 함께 확인해 골인을 판정한다.</summary>
[RequireComponent(typeof(BoxCollider2D), typeof(NetworkIdentity))]
public sealed class GoalZone : NetworkBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerState>();
        if (player != null && player.isLocalPlayer && player.Race != null)
            CmdRequestGoalContact(player.Race.RoundNumber);
    }

    [Command(requiresAuthority = false)]
    private void CmdRequestGoalContact(int round, NetworkConnectionToClient sender = null)
        => ServerTryContact(sender, round);

    [Server]
    public bool ServerTryContact(NetworkConnectionToClient sender, int round)
    {
        if (NetworkServer.isLoadingScene || !isActiveAndEnabled || sender?.identity == null ||
            !sender.identity.TryGetComponent(out PlayerState player) || player.Race == null ||
            round != player.Race.RoundNumber) return false;
        var zone = GetComponent<BoxCollider2D>();
        var body = player.GetComponent<Collider2D>();
        if (!zone.enabled || body == null) return false;
        Bounds area = zone.bounds;
        area.Expand(0.5f);
        return area.Intersects(body.bounds) && player.Race.TryFinish(player);
    }

    private void Update()
    {
        if (!isServer) return;
        var players = PlayerState.ServerPlayers;
        for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
        {
            PlayerState player = players[playerIndex];
            if (player != null && player.Race != null)
                ServerTryContact(player.connectionToClient, player.Race.RoundNumber);
        }
    }
}
