using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>호스트가 레이스 상태, 기한, 도착 순위와 결과를 결정한다.</summary>
[RequireComponent(typeof(NetworkIdentity), typeof(RaceTrapService))]
public sealed class RaceSession : NetworkBehaviour
{
    public static RaceSession Instance { get; private set; }
    [SerializeField] private RaceNetworkRules rules;
    [SerializeField] private Transform startPoint;
    [SerializeField] private BoxCollider2D fieldBounds;
    [SyncVar] private RacePhase phase;
    [SyncVar] private int round;
    [SyncVar] private double phaseDeadline;
    [SyncVar] private double matchDeadline;
    [SyncVar] private RaceEndReason endReason;
    public readonly SyncList<uint> Winners = new SyncList<uint>();
    private readonly List<PlayerState> players = new List<PlayerState>(4);
    private readonly List<uint> finishOrder = new List<uint>(4);
    private readonly Dictionary<uint, Vector2> starts = new Dictionary<uint, Vector2>();
    public RaceNetworkRules Rules => rules;
    public Bounds FieldBounds => fieldBounds != null ? fieldBounds.bounds : default;
    public RacePhase Phase => phase;
    public int Round => round;
    public double PhaseDeadline => phaseDeadline;
    public double MatchDeadline => matchDeadline;
    public RaceEndReason EndReason => endReason;
    public bool IsRacing => phase == RacePhase.Racing || phase == RacePhase.FinishCountdown;
    public double MatchSecondsLeft => System.Math.Max(0, matchDeadline - NetworkTime.time);
    public double PhaseSecondsLeft => System.Math.Max(0, phaseDeadline - NetworkTime.time);
    public IReadOnlyList<PlayerState> Players => players;
    public RaceTrapService Traps => GetComponent<RaceTrapService>();

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Register(PlayerState player)
    {
        if (!players.Contains(player)) players.Add(player);
    }

    public void Unregister(PlayerState player)
    {
        if (isServer && phase != RacePhase.Waiting && phase != RacePhase.MatchEnd)
            AbortForDisconnect();
        players.Remove(player);
    }

    private void Update()
    {
        if (!isServer || rules == null || NetworkServer.isLoadingScene) return;
        if (phase == RacePhase.Waiting)
        {
            if (NetworkServer.connections.Count == 0) return;
            foreach (var connection in NetworkServer.connections.Values)
                if (!connection.isReady || connection.identity == null ||
                    !connection.identity.TryGetComponent(out PlayerState player) || !players.Contains(player))
                    return;
            players.Sort((a, b) => a.netId.CompareTo(b.netId));
            for (int i = 0; i < players.Count; i++)
                starts[players[i].netId] = startPoint != null
                    ? (Vector2)startPoint.position + Vector2.right * (i * 1.5f)
                    : (Vector2)players[i].transform.position;
            matchDeadline = NetworkTime.time + rules.matchSeconds;
            BeginRound();
            return;
        }
        if (phase == RacePhase.MatchEnd) return;
        if (NetworkTime.time >= matchDeadline) { EndMatch(RaceEndReason.TimeLimit); return; }
        if (phase == RacePhase.RoundReady && NetworkTime.time >= phaseDeadline)
        {
            phase = RacePhase.Racing;
            phaseDeadline = 0;
        }
        else if (phase == RacePhase.FinishCountdown && NetworkTime.time >= phaseDeadline)
            EndRound();
        else if (phase == RacePhase.RoundEnd && NetworkTime.time >= phaseDeadline)
            BeginRound();
    }

    [Server]
    private void BeginRound()
    {
        round++;
        finishOrder.Clear();
        phase = RacePhase.RoundReady;
        phaseDeadline = NetworkTime.time + rules.readySeconds;
        Traps.ServerClear();
        FindFirstObjectByType<ConveyorController>()?.ServerClear();
        foreach (var player in players)
        {
            player.GetComponent<NetworkRaceInventory>()?.ServerCancelSelection();
            player.Respawn(starts[player.netId]);
        }
    }

    [Server]
    public bool TryFinish(PlayerState player)
    {
        if (!IsRacing || !players.Contains(player) || player.Status != PlayerStatus.Alive ||
            finishOrder.Contains(player.netId) || NetworkTime.time >= matchDeadline ||
            (phase == RacePhase.FinishCountdown && NetworkTime.time >= phaseDeadline)) return false;
        finishOrder.Add(player.netId);
        player.ServerMarkFinished();
        int rank = finishOrder.Count - 1;
        player.GetComponent<PlayerScore>().ServerAddScore(rank < rules.finishScores.Length ? rules.finishScores[rank] : 0);
        if (phase == RacePhase.MatchEnd) return true;
        if (finishOrder.Count == players.Count) EndRound();
        else if (phase == RacePhase.Racing)
        {
            phase = RacePhase.FinishCountdown;
            phaseDeadline = NetworkTime.time + rules.finishSeconds;
        }
        return true;
    }

    [Server]
    private void EndRound()
    {
        phase = RacePhase.RoundEnd;
        phaseDeadline = NetworkTime.time + rules.roundEndSeconds;
        Traps.ServerClear();
        FindFirstObjectByType<ConveyorController>()?.ServerClear();
    }

    [Server]
    public void CheckScoreLimit()
    {
        if (IsRacing && players.Any(p => p != null && p.GetComponent<PlayerScore>().Score >= rules.winScore))
            EndMatch(RaceEndReason.ScoreLimit);
    }

    [Server]
    private void EndMatch(RaceEndReason reason)
    {
        if (phase == RacePhase.MatchEnd) return;
        endReason = reason;
        phase = RacePhase.MatchEnd;
        phaseDeadline = 0;
        Winners.Clear();
        if (reason != RaceEndReason.PlayerDisconnected && players.Count > 0)
        {
            int best = players.Where(p => p != null).Max(p => p.GetComponent<PlayerScore>().Score);
            foreach (var player in players)
                if (player != null && player.GetComponent<PlayerScore>().Score == best) Winners.Add(player.netId);
        }
        Traps.ServerClear();
        FindFirstObjectByType<ConveyorController>()?.ServerClear();
    }

    [Server]
    public void AbortForDisconnect() => EndMatch(RaceEndReason.PlayerDisconnected);

    public RaceRequestResult ValidateAction(PlayerState player, int requestedRound)
    {
        if (requestedRound != round) return RaceRequestResult.StaleRound;
        if (!IsRacing || rules == null || player == null || !players.Contains(player) ||
            player.Status == PlayerStatus.Dead || NetworkTime.time >= matchDeadline ||
            (phase == RacePhase.FinishCountdown && NetworkTime.time >= phaseDeadline))
            return RaceRequestResult.NotRacing;
        return RaceRequestResult.Success;
    }

    public RaceRequestResult ValidatePosition(Vector2 position)
    {
        if (!RaceNetworkRules.IsFinite(position) || fieldBounds == null)
            return RaceRequestResult.InvalidPosition;
        Bounds bounds = fieldBounds.bounds;
        if (position.x < bounds.min.x || position.x > bounds.max.x ||
            position.y < bounds.min.y || position.y > bounds.max.y)
            return RaceRequestResult.InvalidPosition;
        foreach (var player in players)
            if (player != null && player.IsInvincible &&
                Vector2.Distance(position, player.transform.position) < rules.protectedRadius)
                return RaceRequestResult.ProtectedArea;
        return RaceRequestResult.Success;
    }

    public Vector2 FindHitRespawn(PlayerState player)
    {
        Vector2 candidate = (Vector2)player.transform.position - Vector2.right * rules.hitBackDistance;
        foreach (var zone in FindObjectsByType<FallZone>(FindObjectsSortMode.None))
            if (zone.TryFindRespawnPosition(player.transform.position, out Vector2 safe)) return safe;
        return starts.TryGetValue(player.netId, out var start) ? start : candidate;
    }
}
