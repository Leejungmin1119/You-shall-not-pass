using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class CapstoneRoomManager : NetworkRoomManager
{
    public bool IsLobby => Utils.IsSceneActive(RoomScene);
    public bool IsEditingText => GetComponent<CapstoneLobbyUI>()?.IsEditingText == true;

    public bool CanStartGame()
    {
        if (!NetworkServer.active || !IsLobby || NetworkServer.isLoadingScene ||
            NetworkServer.connections.Count < minPlayers) return false;
        foreach (var connection in NetworkServer.connections.Values)
        {
            var player = connection.identity != null
                ? connection.identity.GetComponent<CapstoneRoomPlayer>() : null;
            if (player == null || !player.readyToBegin) return false;
        }
        return true;
    }

    // 준비 완료 시 자동 전환 대신 Host의 시작 버튼을 기다린다.
    public override void OnRoomServerPlayersReady() { }

    [Server]
    public void TryStartGame()
    {
        if (!IsLobby || NetworkServer.isLoadingScene) return;
        if (!CanStartGame())
        {
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("READY UP!",
                $"At least {minPlayers} players must connect and everyone must be ready.", false);
            return;
        }
        ServerChangeScene(GameplayScene);
    }

    public override bool OnRoomServerSceneLoadedForPlayer(
        NetworkConnectionToClient conn, GameObject roomPlayer, GameObject gamePlayer)
    {
        var lobby = roomPlayer.GetComponent<CapstoneRoomPlayer>();
        var profile = gamePlayer.GetComponent<NetworkPlayerProfile>();
        if (lobby == null || profile == null)
        {
            Debug.LogError("Room player or game profile component missing.");
            return false;
        }
        profile.SetPlayerName(lobby.PlayerName);
        Debug.Log($"Server: copied player name '{lobby.PlayerName}' to game player.");
        return true;
    }

    public override void OnRoomClientSceneChanged()
    {
        if (Utils.IsSceneActive(GameplayScene))
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("GAME START!", "You shall not pass. Good luck!", true);
    }

    public override void Update()
    {
        base.Update();
        if (!NetworkServer.active || !NetworkClient.active || IsEditingText || Keyboard.current == null) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame) TryStartGame();
    }
}
