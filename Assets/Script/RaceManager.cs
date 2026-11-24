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

    public const float StartProtectionRadius = 3f;
    private const double StartProtectionDuration = 2d;
    [SyncVar] private Vector2 startProtectionCenter;
    [SyncVar] private double startProtectionEndsAt;

    public Vector2 StartProtectionCenter => startProtectionCenter;
    public bool IsStartProtectionActive => IsRunning && NetworkTime.time < startProtectionEndsAt;

    [Header("공격자 시간 감속 스킬")]
    [SerializeField, Range(0.01f, 1f)] private float skillTimeScale = 0.1f;
    [SerializeField, Min(0.1f)] private float skillDuration = 5f;
    [SerializeField, Min(0.1f)] private float skillRecoveryDuration = 1f;
    [Header("시간 스킬 확산 연출")]
    [Tooltip("이동하는 공격자를 중심으로 화면 전체로 효과가 퍼지는 시간입니다. 감속 판정은 즉시 적용됩니다.")]
    [SerializeField, Min(0.01f)] private float skillWaveDuration = 0.6f;
    [Tooltip("퍼지는 경계의 부드러운 폭입니다. 월드 유닛 기준입니다.")]
    [SerializeField, Min(0.01f)] private float skillWaveSoftness = 0.8f;
    [Tooltip("파동이 지나갈 때 그림이 휘어지는 세기입니다. 월드 유닛 기준이며 0이면 왜곡하지 않습니다.")]
    [SerializeField, Range(0f, 0.5f)] private float skillWaveDistortion = 0.16f;
    [Tooltip("굴절이 나타나는 파동 띠의 폭입니다. 월드 유닛 기준입니다.")]
    [SerializeField, Min(0.01f)] private float skillWaveDistortionWidth = 2.6f;
    [Tooltip("굴곡이 한 번 반복되는 길이입니다. 월드 유닛 기준입니다.")]
    [SerializeField, Min(0.01f)] private float skillWaveWavelength = 1.5f;
    [SyncVar] private double skillStartedAt = -1d;
    [SyncVar] private double skillSimulationDelay;
    [SyncVar] private uint skillOwnerId;
    [SyncVar] private Vector2 skillOrigin;

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
    public double SimulationTime => NetworkTime.time - simulationDelay - skillSimulationDelay - SkillLostTime(NetworkTime.time) - (IsDeathPresentation ?
        DeathElapsed - DeathSequence.WorldElapsed(DeathElapsed) : 0d);
    public double DeathElapsed => System.Math.Max(0d, NetworkTime.time - deathStartedAt);

    public bool IsTimeSkillActive => skillStartedAt >= 0d &&
        NetworkTime.time < skillStartedAt + skillDuration + skillRecoveryDuration;

    // 공격자 물리·입력과 경기 시계에는 이 배율을 적용하지 않는다.
    public float StageTimeScale => GetStageTimeScale(NetworkTime.time);
    internal double TimeSkillStartedAt => skillStartedAt;
    /// <summary>각 화면에 표시되는 시전자 위치를 따라가며, 생성 전에는 동기화된 발동 위치를 사용한다.</summary>
    internal Vector2 TimeSkillCenter
    {
        get
        {
            if (isClient && NetworkClient.spawned.TryGetValue(skillOwnerId, out NetworkIdentity clientOwner) && clientOwner != null)
                return clientOwner.transform.position;
            if (isServer && NetworkServer.spawned.TryGetValue(skillOwnerId, out NetworkIdentity serverOwner) && serverOwner != null)
                return serverOwner.transform.position;
            return skillOrigin;
        }
    }
    internal float SkillWaveDuration => Mathf.Max(0.01f, skillWaveDuration);
    internal float SkillWaveSoftness => Mathf.Max(0.01f, skillWaveSoftness);
    internal float SkillWaveDistortion => Mathf.Clamp(skillWaveDistortion, 0f, 0.5f);
    internal float SkillWaveDistortionWidth => Mathf.Max(0.01f, skillWaveDistortionWidth);
    internal float SkillWaveWavelength => Mathf.Max(0.01f, skillWaveWavelength);

    /// <summary>감속 중에는 완전 흑백, 복귀 중에는 속도와 같은 곡선으로 색상을 돌려준다.</summary>
    public float TimeSkillGrayscale => IsTimeSkillActive
        ? Mathf.Clamp01((1f - StageTimeScale) / Mathf.Max(0.0001f, 1f - skillTimeScale))
        : 0f;

    /// <summary>5초 감속 이후 SmoothStep 곡선으로 정상 속도까지 복귀한다.</summary>
    private float GetStageTimeScale(double now)
    {
        if (skillStartedAt < 0d)
            return 1f;

        float elapsed = (float)System.Math.Max(0d, now - skillStartedAt);
        float recovery = Mathf.Clamp01((elapsed - skillDuration) / skillRecoveryDuration);
        return Mathf.Lerp(skillTimeScale, 1f, Mathf.SmoothStep(0f, 1f, recovery));
    }

    /// <summary>감속으로 지연된 시간을 적분해 복귀·취소 순간에도 함정 위치가 튀지 않게 한다.</summary>
    private double SkillLostTime(double now)
    {
        if (skillStartedAt < 0d)
            return 0d;

        double elapsed = System.Math.Max(0d, now - skillStartedAt);
        double hold = System.Math.Min(elapsed, skillDuration);
        double u = System.Math.Clamp((elapsed - skillDuration) / skillRecoveryDuration, 0d, 1d);
        double recovery = skillRecoveryDuration * (u - u * u * u + 0.5d * u * u * u * u);
        return (1d - skillTimeScale) * (hold + recovery);
    }

    /// <summary>현재 생존 공격자의 요청만 승인한다. 개인 사용 횟수는 플레이어가 보관한다.</summary>
    [Server]
    public bool TryStartTimeSkill(PlayerState player)
    {
        if (!IsRunning || pendingDeath != null || player == null || !player.CanMove ||
            !Roles.IsAttacker(player) || IsTimeSkillActive)
            return false;

        StopTimeSkill();
        skillOwnerId = player.netId;
        skillOrigin = player.transform.position;
        skillStartedAt = NetworkTime.time;
        return true;
    }

    /// <summary>누적된 함정 시각은 유지하면서 즉시 정상 배율로 복구한다.</summary>
    [Server]
    private void StopTimeSkill()
    {
        double canceledStart = skillStartedAt;
        skillSimulationDelay += SkillLostTime(NetworkTime.time);
        skillStartedAt = -1d;
        skillOwnerId = 0;

        if (canceledStart >= 0d)
        {
            global::TimeSkillGrayscale.Cancel(canceledStart);
            RpcResetTimeSkillGrayscale(canceledStart);
        }
    }

    /// <summary>사망·골인 때 모든 화면의 흑백을 즉시 해제하고 지연된 이전 스킬 상태의 재적용을 막는다.</summary>
    [ClientRpc]
    private void RpcResetTimeSkillGrayscale(double canceledStart)
    {
        global::TimeSkillGrayscale.Cancel(canceledStart);
    }

    public rolechangeManager Roles { get; private set; }

    /// <summary>동일한 네트워크 오브젝트의 역할 관리자를 보관한다.</summary>
    private void Awake()
    {
        Roles = GetComponent<rolechangeManager>();
        deathSequence = GetComponent<DeathSequence>();
        progress = GetComponent<RaceProgress>();
    }

    /// <summary>클라이언트 화면에 서버와 같은 범위의 시작 보호 구역을 표시한다.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!TryGetComponent<StartProtectionZone>(out _))
            gameObject.AddComponent<StartProtectionZone>();
    }

    /// <summary>현재 공격자의 출발 위치를 고정하고 실제 조작 시작부터 2초를 센다.</summary>
    [Server]
    private void BeginStartProtection()
    {
        startProtectionEndsAt = 0d;

        foreach (PlayerState player in participants)
        {
            if (!Roles.IsAttacker(player))
                continue;

            startProtectionCenter = startPositions[player];
            startProtectionEndsAt = NetworkTime.time + StartProtectionDuration;
            return;
        }
    }

    /// <summary>고정된 시작 보호 구역 안인지 서버와 표시 담당이 같은 기준으로 확인한다.</summary>
    public bool IsInsideStartProtection(Vector2 position)
    {
        return IsStartProtectionActive &&
            (position - startProtectionCenter).sqrMagnitude <= StartProtectionRadius * StartProtectionRadius;
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
        if (skillOwnerId == player.netId)
            StopTimeSkill();
        participants.RemoveAt(removedIndex);
        startPositions.Remove(player);
        progress?.RemovePlayer(player.netId);

        if (pendingDeath == player)
            pendingDeath = null;

        // 방 기반 경기는 CapstoneRoomManager의 이탈 종료 정책만 따른다.
        // 독립 시험 씬에서만 아래의 공격자 교대 예비 경로를 사용한다.
        if (NetworkManager.singleton is CapstoneRoomManager) return;
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

        if (skillStartedAt >= 0d && !IsTimeSkillActive)
            StopTimeSkill();

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

        BeginStartProtection();
        conveyor.ResetForRound();
    }

    /// <summary>현재 공격자의 골인에만 2점을 지급하고 다음 공격자로 교대한다.</summary>
    [Server]
    public bool TryFinish(PlayerState player)
    {
        if (!IsRunning || !Roles.IsAttacker(player) || !player.FinishRace())
            return false;

        RpcPlayGoalSound(player.transform.position);
        StopTimeSkill();
        progress?.ResetRecords();
        player.GetComponent<PlayerScore>().AddGoalScore(2);

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
            StopTimeSkill();
            if (player.TryGetComponent(out PlayerTimeSkill skill))
                skill.ResetUsage();

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
        StopTimeSkill();
        pendingDeath = null;
        roundNumber++;
        ClearRoundItems();

        foreach (PlayerState player in participants)
        {
            player.ResetForRound(startPositions[player]);
            player.GetComponent<PlayerCost>().ResetCost(Roles.IsAttacker(player));
        }

        BeginStartProtection();
        conveyor.ResetForRound();
    }

    /// <summary>판을 종료하고 함정을 정리한 뒤 기존 방의 로비로 복귀한다.</summary>
    [Server]
    public void EndMatch(RaceEndReason reason = RaceEndReason.ScoreLimit)
    {
        if (phase == RacePhase.Ended)
            return;

        StopTimeSkill();

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
