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

    /// <summary>실제로 유지되는 방 관리자만 통신과 로비 서비스를 초기화한다.</summary>
    public override void Awake()
    {
        base.Awake();

        // 로비 복귀 시 생성된 중복 관리자는 기본 클래스에서 삭제된다.
        // 여기서 계속 초기화하면 유지 중인 Transport.active를 덮어쓰게 된다.
        if (singleton != this)
            return;

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

    /// <summary>개발자 1인 시험 중인지. 접속을 받지 않는 호스트 혼자 게임 씬을 돌린다.</summary>
    public bool IsSoloTest { get; private set; }
    private bool soloLaunchPending;

    /// <summary>
    /// 개발자 시험용. 인원·준비 조건 없이 혼자 게임 씬으로 넘어간다.
    /// 포트를 열지 않아 다른 기기가 들어올 수 없고, 시험 뒤 에디터에 포트가 잡혀 남지도 않는다.
    /// </summary>
    public void BeginSoloTest()
    {
        if (NetworkClient.active || NetworkServer.active || Lan.Searching) return;
        if (lanTransport != null) SelectTransport(lanTransport);
        NetworkServer.listen = false;
        IsSoloTest = true;
        StartHost();
        soloLaunchPending = NetworkServer.active;
        if (!soloLaunchPending)
        {
            IsSoloTest = false;
            NetworkServer.listen = true;
        }
    }

    public override void OnRoomStopHost()
    {
        Lan?.Cancel();
        IsSoloTest = false;
        soloLaunchPending = false;
    }

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

    /// <summary>기존 방과 참가자 연결을 유지한 채 전원을 로비로 돌려보낸다.</summary>
    [Server]
    public void ReturnToLobby()
    {
        if (IsLobby || NetworkServer.isLoadingScene)
            return;

        // 1인 시험은 돌아갈 방이 없으므로 호스트를 끄고 첫 로비 화면으로 돌아간다.
        if (IsSoloTest)
        {
            StopHost();
            return;
        }

        // NetworkRoomManager가 기존 로비 플레이어를 복구하고 준비 상태를 해제한다.
        ServerChangeScene(RoomScene);
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
        profile.SetPlayerColor(CapstoneLobbyUI.GetCrewColor(lobby.netId));
        Debug.Log($"Server: copied player name '{lobby.PlayerName}' to game player.");
        return true;
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient connection)
    {
        var race = FindFirstObjectByType<RaceManager>();
        if (race != null && race.Phase != RacePhase.Waiting)
            race.EndMatch(RaceEndReason.PlayerDisconnected);
    }

    public override void OnRoomClientSceneChanged()
    {
        if (Utils.IsSceneActive(GameplayScene))
            GetComponent<CapstoneLobbyUI>()?.ShowNotice(IsSoloTest ? "SOLO TEST" : "GAME START!",
                IsSoloTest ? "Developer mode. No one else can join." : "You shall not pass. Good luck!", true);
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
        // 호스트의 로비 플레이어가 생긴 뒤에 넘어가야 게임 씬에서 게임 플레이어로 교체된다.
        if (soloLaunchPending && IsLobby && !NetworkServer.isLoadingScene &&
            NetworkServer.localConnection?.identity != null)
        {
            soloLaunchPending = false;
            ServerChangeScene(GameplayScene);
            return;
        }
        if (!NetworkServer.active || !NetworkClient.active || IsEditingText || Keyboard.current == null) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame) TryStartGame();
    }
}
