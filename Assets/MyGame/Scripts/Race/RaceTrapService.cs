using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct RaceTrapSnapshot
{
    public uint id;
    public uint ownerNetId;
    public int ownerConnectionId;
    public int itemId;
    public int round;
    public Vector2 position;
    public double spawnTime;
    public double expiresAt;
}

/// <summary>다른 함정 담당은 서버 판정과 클라이언트 표시를 이 어댑터로 연결한다.</summary>
public interface IRaceTrapAdapter
{
    bool Supports(int itemId);
    GameObject Create(RaceTrapSnapshot snapshot, bool judgeOnServer);
}

/// <summary>활성 함정 스냅샷, 소유자, 수명과 적중 결과의 공통 네트워크 계층.</summary>
public sealed class RaceTrapService : NetworkBehaviour
{
    private readonly SyncDictionary<uint, RaceTrapSnapshot> traps = new SyncDictionary<uint, RaceTrapSnapshot>();
    private readonly Dictionary<uint, GameObject> views = new Dictionary<uint, GameObject>();
    private readonly Dictionary<uint, HashSet<uint>> hitVictims = new Dictionary<uint, HashSet<uint>>();
    private readonly List<uint> expired = new List<uint>();
    private SpearDropper dropper;
    private uint nextId;
    private bool subscribed;
    public IReadOnlyDictionary<uint, RaceTrapSnapshot> Traps => traps;
    public event Action<uint, uint, uint> HitReceived;
    public event Action<RaceTrapSnapshot> TrapUpdated;

    public override void OnStartServer() => Subscribe();
    public override void OnStartClient()
    {
        Subscribe();
        foreach (uint id in traps.Keys) Show(id);
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        dropper = FindFirstObjectByType<SpearDropper>();
        traps.OnAdd += Show;
        traps.OnRemove += Remove;
        traps.OnSet += UpdateView;
        traps.OnClear += ClearViews;
        Spear.AttackerHit += OnSpearHit;
    }

    private void OnDestroy()
    {
        traps.OnAdd -= Show;
        traps.OnRemove -= Remove;
        traps.OnSet -= UpdateView;
        traps.OnClear -= ClearViews;
        Spear.AttackerHit -= OnSpearHit;
        ClearViews();
    }

    public bool Supports(RaceItemDefinition item)
    {
        if (item == null || !item.enabled) return false;
        if (item.spearCount > 0 && (dropper != null || FindFirstObjectByType<SpearDropper>() != null)) return true;
        foreach (var component in GetComponents<MonoBehaviour>())
            if (component is IRaceTrapAdapter adapter && adapter.Supports(item.id)) return true;
        return false;
    }

    [Server]
    public bool ServerTrySpawn(RaceItemDefinition item, NetworkIdentity owner, Vector2 position)
    {
        if (!Supports(item) || owner == null || owner.connectionToClient == null) return false;
        var race = RaceSession.Instance;
        var snapshot = new RaceTrapSnapshot
        {
            id = ++nextId, itemId = item.id, ownerNetId = owner.netId,
            ownerConnectionId = owner.connectionToClient.connectionId,
            round = race.Round, position = position, spawnTime = NetworkTime.time,
            expiresAt = NetworkTime.time + item.lifetime
        };
        GameObject view = Create(snapshot);
        if (view == null) return false;
        views.Add(snapshot.id, view);
        hitVictims.Add(snapshot.id, new HashSet<uint>());
        traps.Add(snapshot.id, snapshot);
        return true;
    }

    private GameObject Create(RaceTrapSnapshot snapshot)
    {
        foreach (var component in GetComponents<MonoBehaviour>())
            if (component is IRaceTrapAdapter adapter && adapter.Supports(snapshot.itemId))
                return adapter.Create(snapshot, isServer);
        var item = RaceSession.Instance.Rules.FindItem(snapshot.itemId);
        if (item == null || item.spearCount <= 0 || dropper == null) return null;
        var root = new GameObject($"RaceTrap_{snapshot.id}");
        float catchUp = isServer ? 0 : Mathf.Max(0, (float)(NetworkTime.time - snapshot.spawnTime));
        for (int i = 0; i < item.spearCount; i++)
        {
            float offset = (i - (item.spearCount - 1) * 0.5f) * 0.6f;
            var spear = dropper.Spawn(snapshot.position.x + offset, dropper.SpawnHeight,
                snapshot.id, snapshot.ownerConnectionId, isServer, catchUp);
            spear.transform.SetParent(root.transform, true);
        }
        return root;
    }

    private void Show(uint id)
    {
        if (views.ContainsKey(id)) return;
        var snapshot = traps[id];
        if (snapshot.expiresAt <= NetworkTime.time) return;
        var view = Create(snapshot);
        if (view != null) views[id] = view;
    }

    private void Remove(uint id, RaceTrapSnapshot previous)
    {
        if (views.TryGetValue(id, out var view) && view != null) Destroy(view);
        views.Remove(id);
        hitVictims.Remove(id);
    }

    private void ClearViews()
    {
        foreach (var view in views.Values) if (view != null) Destroy(view);
        views.Clear();
        hitVictims.Clear();
    }

    [Server]
    public void ServerClear() => traps.Clear();

    private void Update()
    {
        if (!isServer) return;
        expired.Clear();
        foreach (var pair in traps)
            if (NetworkTime.time >= pair.Value.expiresAt || !views.TryGetValue(pair.Key, out var view) ||
                view == null)
                expired.Add(pair.Key);
        foreach (uint id in expired) traps.Remove(id);
    }

    /// <summary>미사일 등 이동형 어댑터가 호스트에서 위치를 갱신한다.</summary>
    [Server]
    public bool ServerMove(uint id, Vector2 position)
    {
        if (!RaceNetworkRules.IsFinite(position) || !traps.TryGetValue(id, out var snapshot)) return false;
        snapshot.position = position;
        traps[id] = snapshot;
        return true;
    }

    [Server]
    public bool ServerDespawn(uint id) => traps.Remove(id);

    private void UpdateView(uint id, RaceTrapSnapshot previous)
    {
        var snapshot = traps[id];
        if (views.TryGetValue(id, out var view) && view != null)
            view.transform.position = snapshot.position;
        TrapUpdated?.Invoke(snapshot);
    }

    private void OnSpearHit(Spear spear, PlayerMovement target)
    {
        if (isServer && target.TryGetComponent(out PlayerState victim)) ServerTryHit(spear.SpearId, victim);
    }

    /// <summary>클라이언트는 호출할 수 없다. 함정의 서버 물리 판정에서만 호출한다.</summary>
    [Server]
    public bool ServerTryHit(uint id, PlayerState victim)
    {
        var race = RaceSession.Instance;
        if (race == null || !race.IsRacing || !traps.TryGetValue(id, out var trap) ||
            trap.round != race.Round || trap.expiresAt <= NetworkTime.time ||
            victim == null || race.ValidateAction(victim, trap.round) != RaceRequestResult.Success ||
            !victim.IsDamageable || !hitVictims.TryGetValue(id, out var victims) ||
            victims.Contains(victim.netId)) return false;
        victims.Add(victim.netId);
        victim.Respawn(race.FindHitRespawn(victim));
        RpcHit(id, trap.ownerNetId, victim.netId);
        if (trap.ownerNetId != victim.netId && NetworkServer.spawned.TryGetValue(trap.ownerNetId, out var owner))
            owner.GetComponent<PlayerScore>()?.ServerAddScore(race.Rules.hitScore);
        return true;
    }

    [ClientRpc]
    private void RpcHit(uint trapId, uint owner, uint victim) => HitReceived?.Invoke(trapId, owner, victim);
}
