using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

public enum RaceEndReason : byte { None, ScoreLimit, TimeLimit, PlayerDisconnected }

public enum RacePhase
{
    Waiting = 0,
    Racing = 1,
    FinishCountdown = 2,
    Ended = 3,
    MapPreview = 4,
    StartCountdown = 5
}

/// <summary>서버에서 레이스 순위, 현재 제한시간과 다음 라운드 시작을 관리한다.</summary>
[RequireComponent(typeof(NetworkIdentity))]
public sealed class RaceManager : NetworkBehaviour
{
    [FormerlySerializedAs("matchDuration")]
    [SerializeField, Min(1f)] private float roundDuration = 900f;
    [SerializeField, Min(1f)] private float finishDuration = 20f;
    [SerializeField] private ConveyorController conveyor;

    [Header("라운드 시작 연출")]
    [SerializeField, Min(0.1f)] private float mapPreviewDuration = 6f;
    [SerializeField, Min(0f)] private float cameraReturnDuration = 1.5f;
    [SerializeField, Min(0.1f)] private float countdownThreeDuration = 1f;
    [SerializeField, Min(0.1f)] private float countdownTwoDuration = 1f;
    [SerializeField, Min(0.1f)] private float countdownOneDuration = 1f;

    [SyncVar] private RacePhase phase;
    [SyncVar] private double phaseStartedAt;
    [SyncVar] private double endsAt;
    [SyncVar] private int roundNumber;
    [SyncVar] private double matchEndsAt;
    [SyncVar] private RaceEndReason endReason;
    public readonly SyncList<uint> Winners = new SyncList<uint>();
    private double returnToLobbyAt;
    public IReadOnlyList<PlayerState> Participants => isServer ? participants :
        NetworkClient.spawned.Values.Select(identity => identity.GetComponent<PlayerState>())
            .Where(player => player != null).ToArray();
    public double MatchEndsAt => matchEndsAt;
    public RaceEndReason EndReason => endReason;

    private readonly List<PlayerState> participants = new List<PlayerState>(4);
    private readonly Dictionary<PlayerState, Vector2> startPositions = new Dictionary<PlayerState, Vector2>();
    private int finishedCount;

    public RacePhase Phase => phase;
    public double PhaseStartedAt => phaseStartedAt;
    public float MapPreviewDuration => mapPreviewDuration;
    public float CountdownThreeDuration => countdownThreeDuration;
    public float CountdownTwoDuration => countdownTwoDuration;
    public float StartCountdownDuration => countdownThreeDuration + countdownTwoDuration + countdownOneDuration;
    public bool IsPreparing => phase == RacePhase.Waiting || phase == RacePhase.MapPreview ||
        phase == RacePhase.StartCountdown;
    public double EndsAt => endsAt;
    public float RoundDuration => roundDuration;
    public int RoundNumber => roundNumber;
    public bool IsRunning => (phase == RacePhase.Racing || phase == RacePhase.FinishCountdown) &&
        NetworkTime.time < endsAt && (matchEndsAt <= 0 || NetworkTime.time < matchEndsAt);

    /// <summary>게임 씬에 생성된 참가자와 처음 배치된 출발 위치를 보관한다.</summary>
    [Server]
    public void RegisterPlayer(PlayerState player)
    {
        if (startPositions.ContainsKey(player))
            return;

        participants.Add(player);
        startPositions.Add(player, player.transform.position);
    }

    /// <summary>접속이 종료된 참가자를 제거하고 완료 인원도 함께 정리한다.</summary>
    [Server]
    public void UnregisterPlayer(PlayerState player)
    {
        if (!participants.Contains(player)) return;
        if (phase != RacePhase.Waiting && phase != RacePhase.Ended)
            EndMatch(RaceEndReason.PlayerDisconnected);
        participants.Remove(player);

        startPositions.Remove(player);

        if (player.Status == PlayerStatus.Finished)
            finishedCount--;
    }

    /// <summary>참가자 생성 완료를 기다리고, 진행 중에는 서버 시각으로 종료를 판정한다.</summary>
    private void Update()
    {
        if (!isServer || NetworkServer.isLoadingScene)
            return;

        if (phase == RacePhase.Waiting)
        {
            if (ArePlayersReady())
                PrepareRound();

            return;
        }

        if (phase == RacePhase.Ended)
        {
            if (NetworkTime.time >= returnToLobbyAt && NetworkManager.singleton is CapstoneRoomManager room)
            {
                returnToLobbyAt = double.PositiveInfinity;
                room.ReturnToLobby();
            }
            return;
        }
        if (matchEndsAt > 0 && NetworkTime.time >= matchEndsAt)
        {
            EndMatch(RaceEndReason.TimeLimit);
            return;
        }

        if (phase == RacePhase.MapPreview)
        {
            if (NetworkTime.time >= phaseStartedAt + mapPreviewDuration + cameraReturnDuration)
                BeginStartCountdown();

            return;
        }

        if (phase == RacePhase.StartCountdown)
        {
            if (NetworkTime.time >= phaseStartedAt + StartCountdownDuration)
                StartRace();

            return;
        }

        if (phase == RacePhase.Racing && NetworkTime.time >= endsAt)
        {
            EndMatch(RaceEndReason.TimeLimit);
            return;
        }

        if (phase == RacePhase.FinishCountdown &&
            (NetworkTime.time >= endsAt || finishedCount >= participants.Count))
            EndRound();
    }

    /// <summary>접속 중인 모든 클라이언트의 게임 플레이어 생성과 준비 완료를 확인한다.</summary>
    private bool ArePlayersReady()
    {
        if (participants.Count == 0)
            return false;

        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (!connection.isReady || connection.identity == null ||
                !connection.identity.TryGetComponent(out PlayerState player) ||
                !startPositions.ContainsKey(player))
                return false;
        }

        return true;
    }

    /// <summary>골인 기록과 코스트를 초기화하고 출발 위치에서 맵 소개를 시작한다.</summary>
    [Server]
    private void PrepareRound()
    {
        finishedCount = 0;
        endsAt = 0;
        roundNumber++;
        phaseStartedAt = NetworkTime.time;
        phase = RacePhase.MapPreview;

        foreach (PlayerState player in participants)
        {
            player.ResetForRound(startPositions[player]);
            player.GetComponent<PlayerCost>().ResetCost();
        }

        conveyor.ClearItems();
    }

    /// <summary>모든 화면에서 카메라가 복귀한 뒤 공통 시작 카운트다운으로 넘어간다.</summary>
    [Server]
    private void BeginStartCountdown()
    {
        phaseStartedAt = NetworkTime.time;
        phase = RacePhase.StartCountdown;
    }

    /// <summary>준비가 끝난 순간부터 조작과 컨베이어, 15분 제한시간을 시작한다.</summary>
    [Server]
    private void StartRace()
    {
        phaseStartedAt = NetworkTime.time;
        if (matchEndsAt <= 0) matchEndsAt = phaseStartedAt + roundDuration;
        endsAt = matchEndsAt;
        phase = RacePhase.Racing;

        conveyor.ResetForRound();
    }

    /// <summary>유효한 골인만 순서대로 접수하고 첫 도착에서만 20초 마감을 시작한다.</summary>
    [Server]
    public bool TryFinish(PlayerState player)
    {
        if (!IsRunning || player == null || !startPositions.ContainsKey(player) || !player.FinishRace())
            return false;

        finishedCount++;

        if (phase == RacePhase.Racing)
        {
            // 기존 남은 시간 대신 골인 마감 시간 하나만 사용한다.
            endsAt = System.Math.Min(matchEndsAt, NetworkTime.time + finishDuration);
            phase = RacePhase.FinishCountdown;
        }

        int points = Mathf.Max(0, 5 - finishedCount);
        player.GetComponent<PlayerScore>().AddGoalScore(points);

        // 점수 지급으로 판이 종료되었다면 다음 라운드를 열지 않는다.
        if (phase != RacePhase.Ended && finishedCount >= participants.Count)
            EndRound();

        return true;
    }

    /// <summary>미도착자에게 골인 점수를 지급하지 않고 함정과 보유 상태를 정리한다.</summary>
    [Server]
    private void EndRound()
    {
        ClearRoundItems();
        PrepareRound();
    }

    /// <summary>판을 종료하고 함정을 정리한 뒤 기존 방의 로비로 복귀한다.</summary>
    [Server]
    public void EndMatch(RaceEndReason reason = RaceEndReason.ScoreLimit)
    {
        if (phase == RacePhase.Ended)
            return;

        phase = RacePhase.Ended;
        endReason = reason;
        Winners.Clear();
        var remaining = participants.Where(p => p != null).ToArray();
        if (reason != RaceEndReason.PlayerDisconnected && remaining.Length > 0)
        {
            int best = remaining.Max(p => p.GetComponent<PlayerScore>().Score);
            foreach (var player in remaining)
                if (player.GetComponent<PlayerScore>().Score == best) Winners.Add(player.netId);
        }
        returnToLobbyAt = NetworkTime.time + 0.5;
        ClearRoundItems();
        conveyor.ClearItems();

        // 결과를 한 번 이상 복제한 뒤 기존 main의 로비 복귀 흐름을 실행한다.
        if (!(NetworkManager.singleton is CapstoneRoomManager))
            Debug.LogError("경기 종료 후 로비 복귀를 위해 CapstoneRoomManager가 필요합니다.", this);
    }

    /// <summary>서버의 함정을 즉시 멈추고 각 화면의 함정과 개인 보유 상태도 비운다.</summary>
    [Server]
    private void ClearRoundItems()
    {
        ItemBase.ClearAll();
        RpcClearFieldItems();

        foreach (PlayerState player in participants)
        {
            player.GetComponent<PlayerHeldItem>().ClearHeldItem();

            if (player.TryGetComponent(out PlayerInventory inventory))
                inventory.ClearInventory();
        }
    }

    /// <summary>원격 화면에서도 필드 함정과 별도 연출 오브젝트를 제거한다.</summary>
    [ClientRpc]
    private void RpcClearFieldItems()
    {
        if (!isServer)
            ItemBase.ClearAll();
    }
}
