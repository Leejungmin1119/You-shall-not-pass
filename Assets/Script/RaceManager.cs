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
    StartCountdown = 5,
    DeathPresentation = 6
}

/// <summary>서버에서 공수 경기의 제한시간, 교대 초기화와 경기 종료를 관리한다.</summary>
[RequireComponent(typeof(NetworkIdentity), typeof(rolechangeManager))]
public sealed class RaceManager : NetworkBehaviour
{
    [FormerlySerializedAs("matchDuration")]
    [SerializeField, Min(1f)] private float roundDuration = 900f;
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
    private PlayerState pendingDeath;
    private bool pendingDeathPresentation;
    private Vector2 pendingHitOrigin;
    private bool endAfterDeath;
    private RaceEndReason deferredEndReason;
    private DeathSequence deathSequence;
    private RaceProgress progress;
    [SyncVar] private uint dyingPlayerId;
    [SyncVar] private double deathStartedAt;
    [SyncVar] private double deathRemaining;
    [SyncVar] private double simulationDelay;
    [SyncVar] private bool deathForward;
    [SyncVar] private bool deathFacingRight;
    [SyncVar] private Vector2 deathFeet;
    [SyncVar] private float deathWidth;
    [SyncVar] private float deathGroundY;

    public bool IsDeathPresentation => phase == RacePhase.DeathPresentation;
    public bool IsSimulating => IsRunning || IsDeathPresentation;
    public uint DyingPlayerId => dyingPlayerId;
    public double DeathStartedAt => deathStartedAt;
    public bool DeathForward => deathForward;
    public bool DeathFacingRight => deathFacingRight;
    public Vector2 DeathFeet => deathFeet;
    public float DeathWidth => deathWidth;
    public float DeathGroundY => deathGroundY;
    public double RemainingTime => IsDeathPresentation ? deathRemaining :
        System.Math.Max(0d, (matchEndsAt > 0 ? matchEndsAt : endsAt) - NetworkTime.time);
    // 통신 시계는 건드리지 않고 함정의 이동 시계만 히트스톱·슬로모션에 맞춘다.
    public double SimulationTime => NetworkTime.time - simulationDelay - (IsDeathPresentation ?
        DeathElapsed - DeathSequence.WorldElapsed(DeathElapsed) : 0d);
    public double DeathElapsed => System.Math.Max(0d, NetworkTime.time - deathStartedAt);

    public rolechangeManager Roles { get; private set; }

    /// <summary>동일한 네트워크 오브젝트의 역할 관리자를 보관한다.</summary>
    private void Awake()
    {
        Roles = GetComponent<rolechangeManager>();
        deathSequence = GetComponent<DeathSequence>();
        progress = GetComponent<RaceProgress>();
    }

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
    public bool IsRunning => phase == RacePhase.Racing &&
        NetworkTime.time < endsAt && (matchEndsAt <= 0 || NetworkTime.time < matchEndsAt);

    /// <summary>게임 씬에 생성된 참가자와 처음 배치된 출발 위치를 보관한다.</summary>
    [Server]
    public void RegisterPlayer(PlayerState player)
    {
        if (startPositions.ContainsKey(player))
            return;

        participants.Add(player);
        participants.Sort((left, right) => left.netId.CompareTo(right.netId));
        startPositions.Add(player, player.transform.position);
    }

    /// <summary>퇴장한 참가자를 제거하고 공격자였다면 다음 참가자에게 기회를 넘긴다.</summary>
    [Server]
    public void UnregisterPlayer(PlayerState player)
    {
        int removedIndex = participants.IndexOf(player);

        if (removedIndex < 0)
            return;

        bool wasAttacker = Roles.IsAttacker(player);
        participants.RemoveAt(removedIndex);
        startPositions.Remove(player);
        progress?.RemovePlayer(player.netId);

        if (pendingDeath == player)
            pendingDeath = null;

        if (wasAttacker && phase != RacePhase.Ended)
        {
            Roles.SelectAt(participants, removedIndex);

            if ((IsRunning || IsDeathPresentation) && participants.Count > 0)
            {
                if (IsDeathPresentation)
                    FinishDeathClock();
                phase = RacePhase.Racing;
                ResetTurn();
            }
            else if (IsPreparing)
            {
                // 시작 연출 중 퇴장해도 새 공격자에게 수비자 골드가 남지 않게 한다.
                foreach (PlayerState participant in participants)
                    participant.GetComponent<PlayerCost>().ResetCost(Roles.IsAttacker(participant));
            }
        }
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
        if (IsDeathPresentation)
        {
            if (DeathElapsed >= DeathSequence.TotalDuration)
            {
                FinishDeathClock();
                if (endAfterDeath)
                {
                    endAfterDeath = false;
                    EndMatch(deferredEndReason);
                }
                else
                {
                    phase = RacePhase.Racing;
                    CompleteTurn();
                }
            }
            return;
        }
        if (matchEndsAt > 0 && NetworkTime.time >= matchEndsAt && pendingDeath == null)
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

        if (phase == RacePhase.Racing && NetworkTime.time >= endsAt && pendingDeath == null)
        {
            EndMatch(RaceEndReason.TimeLimit);
            return;
        }

        if (pendingDeath != null)
        {
            PlayerState player = pendingDeath;
            pendingDeath = null;

            if (Roles.IsAttacker(player))
            {
                if (pendingDeathPresentation && deathSequence != null)
                    BeginDeathPresentation(player, pendingHitOrigin);
                else
                    CompleteTurn();
            }
        }
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
        pendingDeath = null;
        progress?.ResetRecords();
        Roles.ChooseFirst(participants);
        endsAt = 0;
        roundNumber++;
        phaseStartedAt = NetworkTime.time;
        phase = RacePhase.MapPreview;

        foreach (PlayerState player in participants)
        {
            player.ResetForRound(startPositions[player]);
            player.GetComponent<PlayerCost>().ResetCost(Roles.IsAttacker(player));
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

    /// <summary>현재 공격자의 골인에만 1점을 지급하고 다음 공격자로 교대한다.</summary>
    [Server]
    public bool TryFinish(PlayerState player)
    {
        if (!IsRunning || !Roles.IsAttacker(player) || !player.FinishRace())
            return false;

        RpcPlayGoalSound(player.transform.position);
        progress?.ResetRecords();
        player.GetComponent<PlayerScore>().AddGoalScore(1);

        if (phase != RacePhase.Ended)
            CompleteTurn();

        return true;
    }

    /// <summary>서버가 승인한 골인에서만 모든 참가자에게 폭죽 소리를 한 번 재생한다.</summary>
    [ClientRpc]
    private void RpcPlayGoalSound(Vector2 position)
    {
        SoundPlayer.Play(SoundType.GoalFireworks, position);
    }

    /// <summary>함정의 적중·점수 처리가 끝난 다음 Update에서 사망 교대를 수행한다.</summary>
    [Server]
    public void NotifyDeath(PlayerState player, bool present = true, Vector2 hitOrigin = default)
    {
        if (IsRunning && Roles.IsAttacker(player))
        {
            progress?.RecordDeath(player);
            pendingDeath = player;
            pendingDeathPresentation = present;
            pendingHitOrigin = hitOrigin;
        }
    }

    /// <summary>정면·후면을 확정하고 서버 시각으로 6초 사망 연출을 시작한다.</summary>
    [Server]
    private void BeginDeathPresentation(PlayerState player, Vector2 hitOrigin)
    {
        dyingPlayerId = player.netId;
        deathStartedAt = NetworkTime.time;
        deathRemaining = RemainingTime;
        deathFacingRight = player.FacingRight;
        float side = hitOrigin.x - player.transform.position.x;
        deathForward = Mathf.Abs(side) > 0.05f && side * (deathFacingRight ? 1f : -1f) < 0f;
        var renderer = player.GetComponent<SpriteRenderer>();
        var capsule = player.GetComponent<CapsuleCollider2D>();
        deathFeet = new Vector2(player.transform.position.x, renderer.bounds.min.y);
        deathWidth = capsule.size.x * Mathf.Abs(player.transform.lossyScale.x);
        float direction = (deathFacingRight ? 1f : -1f) * (deathForward ? 1f : -1f);
        Vector2 destination = deathFeet + Vector2.right * (direction * deathWidth);
        var stats = player.GetComponent<PlayerMovement>().moveStats;
        RaycastHit2D hit = Physics2D.Raycast(destination + Vector2.up * 0.1f,
            Vector2.down, 20f, stats.GroundLayer);
        deathGroundY = hit.collider != null && !hit.collider.isTrigger ? hit.point.y : deathFeet.y - 3f;
        phase = RacePhase.DeathPresentation;
    }

    /// <summary>실제 경과만큼 제한시간을 연장하고 함정의 느려진 시계를 연속해서 복구한다.</summary>
    [Server]
    private void FinishDeathClock()
    {
        double elapsed = DeathElapsed;
        simulationDelay += elapsed - DeathSequence.WorldElapsed(elapsed);
        matchEndsAt += elapsed;
        endsAt += elapsed;
        dyingPlayerId = 0;
    }

    /// <summary>다음 공격자를 선택한다. 경기 제한시간은 변경하지 않는다.</summary>
    [Server]
    private void CompleteTurn()
    {
        Roles.Advance(participants);
        ResetTurn();
    }

    /// <summary>모든 함정·보유 아이템·골드를 초기화하고 출발 위치에서 새 차례를 시작한다.</summary>
    [Server]
    private void ResetTurn()
    {
        pendingDeath = null;
        roundNumber++;
        ClearRoundItems();

        foreach (PlayerState player in participants)
        {
            player.ResetForRound(startPositions[player]);
            player.GetComponent<PlayerCost>().ResetCost(Roles.IsAttacker(player));
        }

        conveyor.ResetForRound();
    }

    /// <summary>판을 종료하고 함정을 정리한 뒤 기존 방의 로비로 복귀한다.</summary>
    [Server]
    public void EndMatch(RaceEndReason reason = RaceEndReason.ScoreLimit)
    {
        if (phase == RacePhase.Ended)
            return;

        // 결정타의 점수는 즉시 기록하되 사망 연출을 끝낸 뒤 로비로 돌아간다.
        if (phase == RacePhase.Racing && pendingDeath != null && pendingDeathPresentation && deathSequence != null)
        {
            endAfterDeath = true;
            deferredEndReason = reason;
            return;
        }

        phase = RacePhase.Ended;
        endReason = reason;

        if (reason == RaceEndReason.TimeLimit)
            progress?.ResetRecords();

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
