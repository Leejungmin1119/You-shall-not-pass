using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>경로상의 거리를 계산하고, 서버에서 사망 시 확정한 최고기록만 공유한다.</summary>
public sealed class RaceProgress : NetworkBehaviour
{
    [SerializeField] private RaceManager race;
    [Tooltip("출발점부터 골인점까지 이동 순서대로 배치합니다.")]
    [SerializeField] private Transform[] waypoints = new Transform[0];

    private readonly SyncDictionary<uint, float> bestRecords = new SyncDictionary<uint, float>();
    private Vector2[] points;
    private float[] distances;
    private float routeLength;
    private uint attemptPlayerId;
    private int attemptRound = -1;
    private float attemptBest;
    private bool attemptRecorded;
    private uint trackedPlayerId;
    private int trackedRound = -1;
    private Vector2 previousPosition;
    private float currentProgress;

    public IReadOnlyDictionary<uint, float> BestRecords => bestRecords;
    public float RouteLength => routeLength;
    public RaceManager Race => race;

    /// <summary>변하지 않는 경로 좌표와 누적 거리를 한 번 계산한다.</summary>
    private void Awake()
    {
        routeLength = 0f;
        points = new Vector2[waypoints.Length];
        distances = new float[waypoints.Length];

        for (int index = 0; index < waypoints.Length; index++)
        {
            if (waypoints[index] == null)
            {
                Debug.LogError("진행도 경로의 중간 지점이 비어 있습니다.", this);
                enabled = false;
                return;
            }

            points[index] = waypoints[index].position;

            if (index > 0)
                routeLength += Vector2.Distance(points[index - 1], points[index]);

            distances[index] = routeLength;
        }

        if (race == null || routeLength <= 0f)
        {
            Debug.LogError("진행도 UI에 경기와 출발·골인 경로를 연결하세요.", this);
            enabled = false;
        }
    }

    /// <summary>위치를 가장 가까운 경로 선분에 투영하고 전체 경로 길이에 대한 비율을 구한다.</summary>
    public float GetProgress(Vector2 position)
    {
        return ProjectProgress(position, 0f, routeLength);
    }

    /// <summary>실제 이동 거리의 두 배 이내에서 이전 경로 위치를 이어 찾는다. 꺾인 길의 지름길도 허용한다.</summary>
    public float GetProgress(Vector2 position, Vector2 fromPosition, float fromProgress)
    {
        float travel = Vector2.Distance(fromPosition, position) * 2f;
        float distance = Mathf.Clamp01(fromProgress) * routeLength;

        return ProjectProgress(position, Mathf.Max(0f, distance - travel),
            Mathf.Min(routeLength, distance + travel));
    }

    /// <summary>차례가 바뀔 때만 처음부터 찾고, 같은 공격자는 직전 위치에서 연속해서 추적한다.</summary>
    public float GetCurrentProgress(PlayerState player)
    {
        Vector2 position = player.transform.position;

        if (trackedPlayerId != player.netId || trackedRound != race.RoundNumber)
        {
            trackedPlayerId = player.netId;
            trackedRound = race.RoundNumber;
            currentProgress = GetProgress(position);
        }
        else
        {
            currentProgress = GetProgress(position, previousPosition, currentProgress);
        }

        previousPosition = position;
        return currentProgress;
    }

    /// <summary>허용된 경로 거리와 겹치는 선분만 투영하여 가까운 다른 구간으로 건너뛰지 않게 한다.</summary>
    private float ProjectProgress(Vector2 position, float minDistance, float maxDistance)
    {
        if (routeLength <= 0f)
            return 0f;

        float nearestDistance = float.PositiveInfinity;
        float progressDistance = minDistance;

        for (int index = 1; index < points.Length; index++)
        {
            if (distances[index] < minDistance || distances[index - 1] > maxDistance)
                continue;

            Vector2 segment = points[index] - points[index - 1];
            float lengthSquared = segment.sqrMagnitude;

            if (lengthSquared <= Mathf.Epsilon)
                continue;

            float length = distances[index] - distances[index - 1];
            float minT = Mathf.Clamp01((minDistance - distances[index - 1]) / length);
            float maxT = Mathf.Clamp01((maxDistance - distances[index - 1]) / length);
            float t = Mathf.Clamp(Vector2.Dot(position - points[index - 1], segment) / lengthSquared,
                minT, maxT);
            Vector2 nearest = points[index - 1] + segment * t;
            float distance = (position - nearest).sqrMagnitude;

            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            progressDistance = distances[index - 1] + (distances[index] - distances[index - 1]) * t;
        }

        return Mathf.Clamp01(progressDistance / routeLength);
    }

    /// <summary>기존에 동기화된 공격자 번호로 대상을 찾는다. 별도 위치 메시지는 보내지 않는다.</summary>
    public bool TryGetAttacker(out PlayerState player)
    {
        player = null;

        if (race == null || race.Roles == null)
            return false;

        uint id = race.Roles.AttackerNetId;
        var spawned = isServer ? NetworkServer.spawned : NetworkClient.spawned;

        return spawned.TryGetValue(id, out NetworkIdentity identity) && identity.TryGetComponent(out player);
    }

    /// <summary>서버가 생존한 공격자의 시도별 최대 진행도를 기록한다. 이 값은 매 프레임 전송하지 않는다.</summary>
    private void LateUpdate()
    {
        if (!isServer || !race.IsRunning || !TryGetAttacker(out PlayerState player) ||
            player.Status != PlayerStatus.Alive)
            return;

        SampleAttempt(player);
    }

    /// <summary>차례가 바뀌면 시도 기록만 비우고, 되돌아가더라도 해당 시도의 최대 도달 거리는 보존한다.</summary>
    private void SampleAttempt(PlayerState player)
    {
        if (attemptPlayerId != player.netId || attemptRound != race.RoundNumber)
        {
            attemptPlayerId = player.netId;
            attemptRound = race.RoundNumber;
            attemptBest = 0f;
            attemptRecorded = false;
        }

        if (!attemptRecorded)
            attemptBest = Mathf.Max(attemptBest, GetCurrentProgress(player));
    }

    /// <summary>사망이 승인된 순간 한 번만 비교하며, 기존 최고기록을 넘었을 때만 깃발 기록을 동기화한다.</summary>
    [Server]
    public void RecordDeath(PlayerState player)
    {
        if (!enabled || player == null || !race.Roles.IsAttacker(player))
            return;

        SampleAttempt(player);

        if (attemptRecorded)
            return;

        attemptRecorded = true;
        bestRecords.TryGetValue(player.netId, out float previous);

        if (attemptBest > previous + 0.0001f)
            bestRecords[player.netId] = attemptBest;
    }

    /// <summary>새 경기·골인·시간 종료에서 모든 깃발과 진행 중인 시도 기록을 초기화한다.</summary>
    [Server]
    public void ResetRecords()
    {
        bestRecords.Clear();
        attemptPlayerId = 0;
        attemptRound = -1;
        attemptBest = 0f;
        attemptRecorded = false;
        trackedPlayerId = 0;
        trackedRound = -1;
    }

    /// <summary>퇴장한 플레이어의 기록을 제거한다.</summary>
    [Server]
    public void RemovePlayer(uint playerId)
    {
        bestRecords.Remove(playerId);
    }

    /// <summary>인스펙터에서 경로를 선택하면 중간 지점의 순서와 연결을 보여준다.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        for (int index = 0; index < waypoints.Length; index++)
        {
            if (waypoints[index] == null)
                continue;

            Gizmos.DrawWireSphere(waypoints[index].position, 0.35f);

            if (index > 0 && waypoints[index - 1] != null)
                Gizmos.DrawLine(waypoints[index - 1].position, waypoints[index].position);
        }
    }
}
