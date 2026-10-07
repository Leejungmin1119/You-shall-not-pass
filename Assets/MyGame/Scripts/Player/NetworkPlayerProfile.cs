using Mirror;
using UnityEngine;

public class NetworkPlayerProfile : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] private string playerName;
    public string PlayerName => string.IsNullOrWhiteSpace(playerName) ? $"Player {netId}" : playerName;

    [Server]
    public void SetPlayerName(string newName) => playerName = newName;

    public override void OnStartClient()
    {
        base.OnStartClient();
        GetComponent<NetworkPlayerScore>()?.RefreshDisplay();
        Debug.Log($"Game player: {PlayerName}, netId={netId}");
    }

    private void OnNameChanged(string oldName, string newName)
        => GetComponent<NetworkPlayerScore>()?.RefreshDisplay();
}
