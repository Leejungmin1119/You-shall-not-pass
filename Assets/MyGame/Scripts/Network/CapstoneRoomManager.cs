using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class CapstoneRoomManager : NetworkRoomManager
{
    private string pendingConnectionError;
    private PasswordRelayTransport relayTransport;
    private kcp2k.KcpTransport lanTransport;
    public LanRoomConnector Lan { get; private set; }
    public override void Awake()
    {
        base.Awake();
        showRoomGUI = false;
        relayTransport = GetComponent<PasswordRelayTransport>();
        lanTransport = GetComponent<kcp2k.KcpTransport>();
        if (relayTransport != null)
            relayTransport.Failed += message => pendingConnectionError = message;
        Lan = GetComponent<LanRoomConnector>() ?? gameObject.AddComponent<LanRoomConnector>();
        Lan.Initialize(this, lanTransport);
        SelectTransport(lanTransport);
    }

    public void BeginRoom(bool host, string password)
    {
        var relay = relayTransport;
        if (relay == null || !relay.ValidateSettings(password, out _))
        {
            string reason = "Online rooms are not available yet.";
            if (relay != null) relay.ValidateSettings(password, out reason);
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("ROOM UNAVAILABLE", reason, false);
            return;
        }
        Lan.Cancel();
        SelectTransport(relay);
        relay.RoomPassword = password;
        if (host) StartHost(); else StartClient();
    }

    private void SelectTransport(Transport selected)
    {
        transport = selected;
        Transport.active = selected;
    }

    public void BeginLanRoom(bool host)
    {
        if (NetworkClient.active || NetworkServer.active || Lan.Searching) return;
        if (lanTransport == null)
        {
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("WI-FI ROOM", "Local connection is not configured.", false);
            return;
        }
        SelectTransport(lanTransport);
        if (host)
        {
            StartHost();
            if (NetworkServer.active) Lan.Advertise();
        }
        else Lan.Search();
    }

    public override void OnRoomStopHost() => Lan?.Cancel();
    public override void OnRoomStopClient() => Lan?.Cancel();

    public bool IsLobby => Utils.IsSceneActive(RoomScene);
    public bool IsEditingText => GetComponent<CapstoneLobbyUI>()?.IsEditingText == true;

    public bool CanStartGame()
    {
        if (transport is PasswordRelayTransport relay && !relay.RoomOpen) return false;
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
        Lan.Cancel();
        (transport as PasswordRelayTransport)?.LockRoom();
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
        if (pendingConnectionError != null)
        {
            string message = pendingConnectionError;
            pendingConnectionError = null;
            CapstoneLobbyUI.PendingConnectionError = message;
            if (NetworkServer.active && NetworkClient.active) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("ROOM CONNECTION", message, false);
            return;
        }
        if (!NetworkServer.active || !NetworkClient.active || IsEditingText || Keyboard.current == null) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame) TryStartGame();
    }
}
