using System;
using System.Collections;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

public class CapstoneRoomManager : NetworkRoomManager
{
    public const int LobbySeatCount = 4;
    public const double LobbyCountdownDuration = 5d;
    [SerializeField, Range(2, LobbySeatCount)] private int minimumLobbyPlayers = 2;
    private readonly System.Collections.Generic.Dictionary<int, int> seatsByConnection =
        new System.Collections.Generic.Dictionary<int, int>();
    private double serverCountdownEndsAt;
    public int RequiredLobbyPlayers => Mathf.Clamp(minimumLobbyPlayers, 2, LobbySeatCount);
    public double LobbyCountdownEndsAt
    {
        get
        {
            if (NetworkServer.active) return serverCountdownEndsAt;
            var player = NetworkClient.localPlayer != null
                ? NetworkClient.localPlayer.GetComponent<CapstoneRoomPlayer>() : null;
            return player != null ? player.CountdownEndsAt : 0d;
        }
    }
    public bool IsLobbyCountdownActive => IsLobby && LobbyCountdownEndsAt > 0d;

    public static Vector3 GetLobbySeatPosition(int seat)
        => new Vector3((Mathf.Clamp(seat, 0, LobbySeatCount - 1) - 1.5f) * 2.8f, 0f, 0f);

    private string pendingConnectionError;
    public string LastConnectionError { get; private set; } = "";
    private bool leavingIntentionally;
    private double connectionDeadline;
    private Coroutine relayStartup;
    private UnityWebRequest relayHealthRequest;
    public bool IsCheckingRelay => relayStartup != null;
    public bool IsRelayRoom => transport is PasswordRelayTransport;
    public bool IsOpeningRelayRoom => NetworkServer.active && IsRelayRoom && !relayTransport.RoomOpen;
    public string DefaultRelayEndpoint => relayTransport?.DefaultEndpoint ?? "";
    private void BeginConnectionAttempt()
    {
        leavingIntentionally = false;
        connectionDeadline = 0;
        LastConnectionError = "";
        pendingConnectionError = null;
        CapstoneLobbyUI.PendingConnectionError = null;
    }

    public void ReportConnectionFailure(string message)
    {
        LastConnectionError = message;
        pendingConnectionError = message;
    }

    public void LeaveRoom()
    {
        leavingIntentionally = true;
        connectionDeadline = 0;
        CancelRelayStartup();
        Lan?.Cancel();
        if (NetworkServer.active && NetworkClient.active) StopHost();
        else if (NetworkClient.active) StopClient();
        else if (NetworkServer.active) StopServer();
    }

    public override void OnRoomStartClient()
    {
        if (!NetworkServer.active && !IsRelayRoom)
            connectionDeadline = Time.unscaledTimeAsDouble + 12d;
    }
    public override void OnRoomClientConnect() => connectionDeadline = 0;
    public override void OnRoomClientDisconnect()
    {
        connectionDeadline = 0;
        if (!leavingIntentionally && !NetworkServer.active && string.IsNullOrEmpty(LastConnectionError))
            ReportConnectionFailure(IsRelayRoom ? "중계 방과 연결이 끊겼습니다." :
                "방장과 연결이 끊겼거나 방에 참가할 수 없습니다. 방 상태와 IP를 확인해 주세요.");
    }
    public override void OnClientError(TransportError error, string reason)
    {
        if (!leavingIntentionally && !IsRelayRoom)
            ReportConnectionFailure("연결 실패: 방장 IP, 같은 내부망 여부와 게임 방화벽 허용을 확인해 주세요.");
    }
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
        maxConnections = LobbySeatCount;
        minPlayers = RequiredLobbyPlayers;
        relayTransport = GetComponent<PasswordRelayTransport>();
        lanTransport = GetComponent<kcp2k.KcpTransport>();
        if (relayTransport != null)
            relayTransport.Failed += ReportRelayFailure;
        Lan = GetComponent<LanRoomConnector>() ?? gameObject.AddComponent<LanRoomConnector>();
        Lan.Initialize(this, lanTransport);
        SelectTransport(lanTransport);
    }

    public bool BeginRoom(bool host, string endpoint, string password)
    {
        if (NetworkClient.active || NetworkServer.active || Lan.Searching || IsCheckingRelay) return false;
        BeginConnectionAttempt();
        if (relayTransport == null)
        {
            ReportConnectionFailure("중계 서버 통신 설정이 없습니다.");
            return false;
        }
        if (!relayTransport.TryConfigure(endpoint, password, out Uri address, out string reason))
        {
            ReportConnectionFailure(reason);
            return false;
        }

        Lan.Cancel();
        relayStartup = StartCoroutine(CheckRelayAndConnect(host, address));
        return true;
    }

    private IEnumerator CheckRelayAndConnect(bool host, Uri address)
    {
        Uri health = new UriBuilder(address)
        {
            Scheme = address.Scheme == "wss" ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            Port = address.IsDefaultPort ? -1 : address.Port,
            Path = "/health"
        }.Uri;
        // Render 무료 서버는 유휴 상태에서 다시 시작하는 데 약 1분이 걸린다.
        double deadline = Time.unscaledTimeAsDouble + 75d;
        while (Time.unscaledTimeAsDouble < deadline)
        {
            relayHealthRequest = UnityWebRequest.Get(health);
            relayHealthRequest.timeout = Mathf.Max(1, Mathf.Min(10, Mathf.CeilToInt((float)(deadline - Time.unscaledTimeAsDouble))));
            relayHealthRequest.redirectLimit = 0;
            yield return relayHealthRequest.SendWebRequest();

            long status = relayHealthRequest.responseCode;
            bool ready = relayHealthRequest.result == UnityWebRequest.Result.Success &&
                relayHealthRequest.downloadHandler.text.Trim() == "ok";
            relayHealthRequest.Dispose();
            relayHealthRequest = null;
            if (ready)
            {
                relayStartup = null;
                SelectTransport(relayTransport);
                if (host) StartHost(); else StartClient(address);
                yield break;
            }
            if (status >= 300 && status < 500 && status != 408 && status != 429) break;
            yield return new WaitForSecondsRealtime(2f);
        }

        relayStartup = null;
        relayTransport.ClearPassword();
        ReportConnectionFailure("중계 서버가 응답하지 않습니다. 중계 주소와 서버 상태를 확인해 주세요.");
    }

    private void CancelRelayStartup()
    {
        if (relayStartup == null) return;
        StopCoroutine(relayStartup);
        relayStartup = null;
        relayHealthRequest?.Abort();
        relayHealthRequest?.Dispose();
        relayHealthRequest = null;
        relayTransport.ClearPassword();
    }

    public override void OnDestroy()
    {
        CancelRelayStartup();
        if (relayTransport != null) relayTransport.Failed -= ReportRelayFailure;
        base.OnDestroy();
    }

    private void ReportRelayFailure(string reason)
    {
        if (leavingIntentionally) return;
        ReportConnectionFailure(reason switch
        {
            "No room matches this password." => "이 비밀번호로 열린 방이 없습니다. 방장에게 비밀번호를 확인해 주세요.",
            "This password is already in use. Choose another." => "이미 사용 중인 방 비밀번호입니다. 다른 비밀번호를 입력해 주세요.",
            "The room is full." => "방이 가득 찼습니다.",
            "The game has already started." => "이미 게임이 시작되었습니다.",
            "The host left the room." => "방장이 방을 나갔습니다.",
            "Too many attempts. Wait a minute." => "접속 시도가 너무 많습니다. 1분 뒤에 다시 시도해 주세요.",
            "Server busy. Try again later." => "중계 서버가 혼잡합니다. 잠시 뒤 다시 시도해 주세요.",
            "Room connection timed out." => "중계 연결 시간이 초과되었습니다.",
            "Connection to the room was lost." => "중계 방과 연결이 끊겼습니다.",
            "중계 주소와 방 비밀번호를 확인해 주세요." => reason,
            _ => "중계 방에 연결할 수 없습니다. 중계 주소와 서버 상태를 확인해 주세요."
        });
    }

    private void SelectTransport(Transport selected)
    {
        transport = selected;
        Transport.active = selected;
    }

    public void BeginLanRoom(bool host)
    {
        if (NetworkClient.active || NetworkServer.active || Lan.Searching || IsCheckingRelay) return;
        if (lanTransport == null)
        {
            GetComponent<CapstoneLobbyUI>()?.ShowNotice("WI-FI ROOM", "Local connection is not configured.", false);
            return;
        }
        BeginConnectionAttempt();
        SelectTransport(lanTransport);
        if (host)
        {
            StartHost();
            if (NetworkServer.active && !Lan.Advertising) Lan.Advertise();
        }
        else Lan.Search();
    }

    public void BeginDirectRoom(string address)
    {
        if (NetworkClient.active || NetworkServer.active || Lan.Searching || IsCheckingRelay) return;
        BeginConnectionAttempt();
        if (lanTransport == null) { ReportConnectionFailure("로컬 통신 설정이 없습니다."); return; }
        SelectTransport(lanTransport);
        Lan.ConnectDirect(address);
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
        if (NetworkClient.active || NetworkServer.active || Lan.Searching || IsCheckingRelay) return;
        BeginConnectionAttempt();
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
        serverCountdownEndsAt = 0d;
        seatsByConnection.Clear();
        IsSoloTest = false;
        soloLaunchPending = false;
    }

    public override void OnRoomStopClient() => Lan?.Cancel();

    public bool IsLobby => Utils.IsSceneActive(RoomScene);
    public bool IsEditingText => GetComponent<CapstoneLobbyUI>()?.IsEditingText == true;

    public bool CanKickLobbyPlayer(CapstoneRoomPlayer player)
    {
        return NetworkServer.active && NetworkServer.localConnection != null &&
            IsLobby && !IsSoloTest && !NetworkServer.isLoadingScene &&
            player != null && !player.IsBeingRemoved && player.connectionToClient != null &&
            player.connectionToClient != NetworkServer.localConnection &&
            player.connectionToClient.identity == player.netIdentity;
    }

    /// <summary>요청자의 실제 연결이 방장인지 확인하고 현재 대기실 참가자만 내보낸다.</summary>
    [Server]
    public bool ServerTryKickPlayer(NetworkConnectionToClient requester, uint playerId)
    {
        if (requester == null || requester != NetworkServer.localConnection ||
            !NetworkServer.spawned.TryGetValue(playerId, out NetworkIdentity identity) ||
            !identity.TryGetComponent(out CapstoneRoomPlayer player) || !CanKickLobbyPlayer(player))
            return false;

        CancelLobbyCountdown();
        player.ServerNotifyRemoved();
        // 클라이언트가 안내를 받고 연결을 정리할 시간을 주며, 응답하지 않아도 서버가 종료한다.
        StartCoroutine(DisconnectRemovedPlayer(player.connectionToClient));
        return true;
    }

    private System.Collections.IEnumerator DisconnectRemovedPlayer(NetworkConnectionToClient connection)
    {
        yield return new WaitForSecondsRealtime(1f);
        if (NetworkServer.active && NetworkServer.connections.TryGetValue(connection.connectionId, out var current) &&
            current == connection)
            connection.Disconnect();
    }

    public bool CanStartGame()
    {
        if (transport is PasswordRelayTransport relay && !relay.RoomOpen) return false;
        if (!NetworkServer.active || !IsLobby || IsSoloTest || NetworkServer.isLoadingScene ||
            NetworkServer.connections.Count < RequiredLobbyPlayers ||
            NetworkServer.connections.Count > LobbySeatCount) return false;
        foreach (var connection in NetworkServer.connections.Values)
        {
            var player = connection.identity != null
                ? connection.identity.GetComponent<CapstoneRoomPlayer>() : null;
            if (!connection.isReady || player == null || player.IsBeingRemoved || !player.readyToBegin || player.SeatIndex < 0)
                return false;
        }
        return true;
    }

    /// <summary>좌석 번호는 Mirror의 진단 index와 별개로 유지하여 퇴장해도 다른 자리가 바뀌지 않는다.</summary>
    public override GameObject OnRoomServerCreateRoomPlayer(NetworkConnectionToClient connection)
    {
        CancelLobbyCountdown();
        if (!seatsByConnection.TryGetValue(connection.connectionId, out int seat))
        {
            seat = 0;
            while (seat < LobbySeatCount && seatsByConnection.ContainsValue(seat)) seat++;
            if (seat == LobbySeatCount)
                throw new System.InvalidOperationException("No free lobby seat for an accepted connection.");
            seatsByConnection[connection.connectionId] = seat;
        }
        GameObject instance = Instantiate(roomPlayerPrefab.gameObject, GetLobbySeatPosition(seat), Quaternion.identity);
        instance.GetComponent<CapstoneRoomPlayer>().ServerAssignSeat(seat);
        return instance;
    }

    public override void OnRoomServerConnect(NetworkConnectionToClient connection)
    {
        // 참가자 구성이 바뀌면 기존 마감 시각을 재사용하지 않는다.
        CancelLobbyCountdown();
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        CancelLobbyCountdown();
        if (IsLobby && !IsSoloTest && transport == lanTransport && NetworkServer.listen)
        {
            if (!Lan.Advertising) Lan.Advertise();
        }
        else Lan?.Cancel();
        if (IsLobby && transport is PasswordRelayTransport relay)
            relay.UnlockRoom();
    }

    public override void OnRoomStopServer()
    {
        if (IsRelayRoom) relayTransport.BeginRoomShutdown();
        serverCountdownEndsAt = 0d;
        seatsByConnection.Clear();
    }

    public override void ReadyStatusChanged()
    {
        base.ReadyStatusChanged();
        if (!CanStartGame()) CancelLobbyCountdown();
        else BeginLobbyCountdown();
    }

    public override void OnRoomServerPlayersReady() => BeginLobbyCountdown();
    public override void OnRoomServerPlayersNotReady() => CancelLobbyCountdown();

    private void BeginLobbyCountdown()
    {
        if (!CanStartGame() || serverCountdownEndsAt > 0d) return;
        SetLobbyCountdown(NetworkTime.time + LobbyCountdownDuration);
    }

    private void CancelLobbyCountdown()
    {
        if (serverCountdownEndsAt > 0d) SetLobbyCountdown(0d);
    }

    private void SetLobbyCountdown(double deadline)
    {
        serverCountdownEndsAt = deadline;
        foreach (NetworkRoomPlayer item in roomSlots)
            if (item is CapstoneRoomPlayer player) player.ServerSetCountdown(deadline);
    }

    /// <summary>시작 버튼이나 Enter로 5초를 생략할 수 없도록 서버에서 마감과 전원 준비를 다시 검사한다.</summary>
    [Server]
    public void TryStartGame()
    {
        if (serverCountdownEndsAt <= 0d || NetworkTime.time < serverCountdownEndsAt) return;
        if (!CanStartGame())
        {
            CancelLobbyCountdown();
            return;
        }
        CancelLobbyCountdown();
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
        // 1인 시험은 로비를 바로 지나가므로 방장 PC에서 미리 고른 캐릭터를 직접 쓴다.
        int preferred = CapstoneLobbyUI.PreferredCharacter;
        profile.SetCharacter(IsSoloTest && preferred >= 0
            ? (PlayerIdleBoil.CharacterStyle)preferred : lobby.Character);
        Debug.Log($"Server: copied player name '{lobby.PlayerName}' to game player.");
        return true;
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient connection)
    {
        CancelLobbyCountdown();
        bool wasParticipant = seatsByConnection.Remove(connection.connectionId);
        var race = FindFirstObjectByType<RaceManager>();
        // 현재 정책: 준비 연출을 포함해 경기 도중 이탈하면 판을 중단하고 남은 인원은 로비로 복귀한다.
        if (wasParticipant && race != null && !IsLobby)
            race.EndMatch(RaceEndReason.PlayerDisconnected);
    }

    /// <summary>시작 안내는 카드 선택 화면에서 표시하므로 별도의 중앙 알림을 띄우지 않는다.</summary>
    public override void OnRoomClientSceneChanged()
    {
    }

    public override void Update()
    {
        base.Update();
        if (connectionDeadline > 0 && !NetworkClient.isConnected && Time.unscaledTimeAsDouble >= connectionDeadline)
        {
            connectionDeadline = 0;
            ReportConnectionFailure("12초 안에 방에 연결하지 못했습니다. 방장 IP와 공유기·방화벽 설정을 확인해 주세요.");
        }
        if (pendingConnectionError != null)
        {
            string message = pendingConnectionError;
            pendingConnectionError = null;
            // 정리 과정의 disconnect 콜백이 원래 실패 이유를 덮어쓰지 않게 한다.
            leavingIntentionally = true;
            connectionDeadline = 0;
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
        if (!NetworkServer.active) return;
        if (!CanStartGame())
            CancelLobbyCountdown();
        else
        {
            BeginLobbyCountdown();
            if (NetworkTime.time >= serverCountdownEndsAt) TryStartGame();
        }
    }
}
