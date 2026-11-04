using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class CapstoneRoomPlayer : NetworkRoomPlayer
{
    [SyncVar] private string playerName;
    [SyncVar] private int seatIndex = -1;
    [SyncVar] private double countdownEndsAt;
    public int SeatIndex => seatIndex;
    public double CountdownEndsAt => countdownEndsAt;

    // 고정 좌석은 SyncVar 하나로 모든 화면에서 동일하게 배치한다. 위치 입력은 전송하지 않는다.
    private void Awake()
    {
        var movementSync = GetComponent<NetworkTransformBase>();
        if (movementSync != null) movementSync.enabled = false;
    }

    internal void ServerAssignSeat(int seat)
    {
        seatIndex = seat;
        countdownEndsAt = 0d;
        transform.position = CapstoneRoomManager.GetLobbySeatPosition(seat);
    }

    [Server]
    internal void ServerSetCountdown(double deadline) => countdownEndsAt = deadline;

    private void LateUpdate()
    {
        if (seatIndex >= 0 && NetworkManager.singleton is CapstoneRoomManager room && room.IsLobby)
            transform.position = CapstoneRoomManager.GetLobbySeatPosition(seatIndex);
    }

    public string PlayerName => playerName;
    public bool IsReady => readyToBegin;

    public override void OnStartServer()
    {
        base.OnStartServer();
        playerName = $"Player {netId}";
    }

    private void Update()
    {
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (!(NetworkManager.singleton is CapstoneRoomManager manager) || !manager.IsLobby || manager.IsEditingText) return;
        if (Keyboard.current.fKey.wasPressedThisFrame) ToggleReady();
    }

    public void ToggleReady()
    {
        if (isLocalPlayer) CmdChangeReadyState(!readyToBegin);
    }

    public void RequestName(string requestedName)
    {
        if (!isLocalPlayer) return;
        // 준비 해제와 이름 요청을 같은 신뢰성 채널에 순서대로 보낸다.
        // 다른 어셈블리의 SyncVar를 직접 수정하지 않는다.
        CmdChangeReadyState(false);
        CmdSetPlayerName(requestedName);
    }

    [Command]
    private void CmdSetPlayerName(string requestedName)
    {
        if (!(NetworkManager.singleton is CapstoneRoomManager manager) || !manager.IsLobby) return;
        if (readyToBegin)
        {
            TargetNameResult(connectionToClient, false, "Cancel ready before changing your name.");
            return;
        }
        string name = (requestedName ?? "").Trim();
        // 공백 이름, 제어 문자, TMP 리치 텍스트 태그를 허용하지 않는다.
        var cleaned = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsControl(c) || c == '<' || c == '>') continue;
            if (cleaned.Length >= 16) break;
            cleaned.Append(c);
        }
        name = cleaned.ToString().Trim();
        if (name.Length == 0)
        {
            TargetNameResult(connectionToClient, false, "Please enter a name (1-16 characters).");
            return;
        }
        playerName = name;
        TargetNameResult(connectionToClient, true, "Name saved. Ready up when you are done.");
    }

    [TargetRpc]
    private void TargetNameResult(NetworkConnectionToClient target, bool success, string message)
    {
        NetworkManager.singleton?.GetComponent<CapstoneLobbyUI>()?.ShowNotice(
            success ? "NAME SAVED" : "CHECK NAME", message, success);
    }

    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = true;
    }

    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
        foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
    }
}
