#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;

/// <summary>Explicit opt-in microbenchmarks. Fixture objects exist only inside a synchronous measurement call.</summary>
public sealed class NetworkPerformanceProbe : MonoBehaviour
{
    [Serializable] private sealed class Control
    {
        public int sequence, extraObjects, iterations = 10000;
        public string action;
    }
    [Serializable] private sealed class Result
    {
        public int sequence, iterations, extraObjects, spawnedObjects, players, hits;
        public string action, error;
        public long allocatedBytes;
        public double milliseconds;
        public bool success, allocationCounterAvailable;
    }
    private string directory, id;
    private int lastSequence;
    private float nextRead;
    private bool allocationCounterAvailable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-networkCpuBenchmark") < 0) return;
        int d = Array.IndexOf(args, "-lobbyProbeDirectory"), i = Array.IndexOf(args, "-lobbyProbeId");
        if (d < 0 || i < 0 || d + 1 >= args.Length || i + 1 >= args.Length || !Path.IsPathRooted(args[d + 1])) return;
        var probe = new GameObject("Network CPU benchmark").AddComponent<NetworkPerformanceProbe>();
        probe.directory = args[d + 1];
        probe.id = args[i + 1];
        probe.allocationCounterAvailable = CheckAllocationCounter();
        DontDestroyOnLoad(probe.gameObject);
    }

    private static bool CheckAllocationCounter()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sample = new byte[4096];
        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(sample);
        return after > before;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRead || string.IsNullOrEmpty(directory)) return;
        nextRead = Time.unscaledTime + 0.1f;
        string path = Path.Combine(directory, id + ".perf-command.json");
        if (!File.Exists(path)) return;
        Control control;
        try { control = JsonUtility.FromJson<Control>(File.ReadAllText(path)); }
        catch (IOException) { return; }
        catch (ArgumentException) { return; }
        if (control == null || control.sequence <= lastSequence) return;
        var result = new Result { sequence = control.sequence, action = control.action,
            allocationCounterAvailable = allocationCounterAvailable,
            iterations = Mathf.Clamp(control.iterations, 1, 100000), extraObjects = Mathf.Clamp(control.extraObjects, 0, 1024) };
        try
        {
            if (control.action == "roster") MeasureRoster(result);
            else if (control.action == "collision") MeasureCollision(result);
            else throw new ArgumentException("Unknown benchmark action");
            result.success = true;
        }
        catch (Exception exception) { result.error = exception.ToString(); }
        lastSequence = control.sequence;
        File.WriteAllText(Path.Combine(directory, id + ".perf.json"), JsonUtility.ToJson(result, true));
    }

    private void MeasureRoster(Result result)
    {
        var ui = NetworkManager.singleton.GetComponent<CapstoneLobbyUI>();
        var method = typeof(CapstoneLobbyUI).GetMethod("CollectRoomPlayers", BindingFlags.Instance | BindingFlags.NonPublic);
        var collect = (Func<IReadOnlyList<CapstoneRoomPlayer>>)Delegate.CreateDelegate(typeof(Func<IReadOnlyList<CapstoneRoomPlayer>>), ui, method);
        for (int i = 0; i < 20; i++) collect();
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < result.iterations; i++) result.hits += collect().Count;
        watch.Stop();
        result.allocatedBytes = allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
        result.milliseconds = watch.Elapsed.TotalMilliseconds;
        result.players = collect().Count;
        result.spawnedObjects = NetworkClient.spawned.Count;
    }

    private void MeasureCollision(Result result)
    {
        if (!NetworkServer.active) throw new InvalidOperationException("Collision benchmark requires the server");
        var race = FindFirstObjectByType<RaceManager>();
        PlayerState attacker = null;
        if (race != null)
            for (int i = 0; i < race.Participants.Count; i++)
                if (race.Roles.IsAttacker(race.Participants[i])) attacker = race.Participants[i];
        if (attacker == null || !attacker.CanReceiveHit) throw new InvalidOperationException("Wait for a vulnerable racing attacker");
        var objects = new List<GameObject>();
        var keys = new List<uint>();
        GameObject itemObject = null;
        ItemData data = null;
        try
        {
            for (int i = 0; i < result.extraObjects; i++)
            {
                var go = new GameObject("Inert benchmark identity");
                objects.Add(go);
                go.SetActive(false);
                var identity = go.AddComponent<NetworkIdentity>();
                uint key = 0xF0000000u + (uint)i;
                NetworkServer.spawned.Add(key, identity);
                keys.Add(key);
            }
            itemObject = new GameObject("Contact benchmark");
            var item = itemObject.AddComponent<NetworkCollisionBenchmarkItem>();
            data = ScriptableObject.CreateInstance<ItemData>();
            item.Initialize(data, new ItemSpawnMessage { InstanceId = 0xEFFFFFFFu }, true, 0f);
            Vector2 center = attacker.GetComponent<Collider2D>().bounds.center;
            Vector2 from = center - Vector2.right * 2f, to = center + Vector2.right * 2f;
            for (int i = 0; i < 20; i++) item.Contact(from, to);
            result.spawnedObjects = NetworkServer.spawned.Count;
            result.players = race.Participants.Count;
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < result.iterations; i++)
                if (item.Contact(from, to)) result.hits++;
            watch.Stop();
            result.allocatedBytes = allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
            result.milliseconds = watch.Elapsed.TotalMilliseconds;
            if (result.hits != result.iterations) throw new InvalidOperationException("Benchmark contact result changed");
        }
        finally
        {
            foreach (uint key in keys) NetworkServer.spawned.Remove(key);
            foreach (GameObject go in objects) Destroy(go);
            if (itemObject != null) Destroy(itemObject);
            if (data != null) Destroy(data);
        }
    }
}

public sealed class NetworkCollisionBenchmarkItem : ItemBase
{
    public bool Contact(Vector2 from, Vector2 to)
        => FindPlayerContact(from, to, 0.1f, out _, out _);
}
#endif
