#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using Mirror.RemoteCalls;
using UnityEngine;

/// <summary>명시적 개발용 실행 인자에서만 실제 메시지 전송 횟수를 측정한다. GC 측정용 도구가 아니다.</summary>
public sealed class NetworkTrafficBenchmark : MonoBehaviour
{
    [Serializable] private sealed class Control
    {
        public int sequence;
        public string action;
        public uint ownerId;
        public float duration;
    }
    [Serializable] private sealed class Report
    {
        public int sequence;
        public string action, error;
        public bool success;
        public uint ownerId, instanceId;
        public double elapsed, firstMessageDelay, largestMessageGap;
        public float aimX, aimY;
        public long messages, serializedBytes, deliveries, deliveryBytes;
        public long reliableMessages, unreliableMessages, expiredMessages;
    }
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private string directory, id;
    private int lastSequence;
    private float nextControlPoll;
    private Report current;
    private double startedAt, lastMessageAt;
    private float duration;
    private bool preparing, movingAim;
    private PlayerHeldItem aimHolder;
    private bool holderWasEnabled;
    private Action<Vector2> sendAim;
    private Camera aimCamera;
    private Missile fixtureMissile;
    private RoundTrap fixtureRound;
    private ItemData fixtureData;
    private static uint nextFixtureId = 0xF0000000;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains("-networkTrafficBenchmark")) return;
        int directoryIndex = Array.IndexOf(args, "-lobbyProbeDirectory");
        int idIndex = Array.IndexOf(args, "-lobbyProbeId");
        if (directoryIndex < 0 || idIndex < 0 || directoryIndex + 1 >= args.Length || idIndex + 1 >= args.Length) return;
        var collector = new GameObject("Network traffic benchmark").AddComponent<NetworkTrafficBenchmark>();
        collector.directory = args[directoryIndex + 1];
        collector.id = args[idIndex + 1];
        Directory.CreateDirectory(collector.directory);
        DontDestroyOnLoad(collector.gameObject);
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(directory)) return;
        if (current != null)
        {
            double elapsed = Time.unscaledTimeAsDouble - startedAt;
            if (sendAim != null)
            {
                float x = movingAim ? 0.6f + Mathf.Sin((float)elapsed * 2f) * 0.08f : 0.6f;
                sendAim(aimCamera.ViewportToScreenPoint(new Vector3(x, 0.65f)));
            }
            if (elapsed >= duration) FinishMeasurement();
            return;
        }
        if (preparing || Time.unscaledTime < nextControlPoll) return;
        nextControlPoll = Time.unscaledTime + 0.1f;
        string path = Path.Combine(directory, id + ".traffic-command.json");
        if (!File.Exists(path)) return;
        try
        {
            Control command = JsonUtility.FromJson<Control>(File.ReadAllText(path));
            if (command == null || command.sequence <= lastSequence) return;
            lastSequence = command.sequence;
            if (command.action == "prepareAim") PrepareAim(command);
            else if (command.action == "prepareRound" || command.action == "prepareMovingRound")
                StartCoroutine(PrepareRound(command));
            else if (command.action == "corruptAim" || command.action == "inspectAim")
            {
                if (fixtureMissile == null) throw new InvalidOperationException("Prepare an aim fixture first.");
                if (command.action == "corruptAim") fixtureMissile.SetAim(new Vector2(-10000f, -10000f));
                var value = NewReport(command);
                value.aimX = fixtureMissile.AimPosition.x;
                value.aimY = fixtureMissile.AimPosition.y;
                Write(value);
            }
            else if (command.action == "cleanup") { ClearFixtures(); Write(NewReport(command)); }
            else BeginMeasurement(command);
        }
        catch (Exception error)
        {
            Write(new Report { sequence = lastSequence, action = "error", error = error.Message });
        }
    }

    private static Report NewReport(Control command)
        => new Report { sequence = command.sequence, action = command.action, ownerId = command.ownerId, success = true };

    private PlayerState ServerOwner(uint ownerId)
    {
        if (!NetworkServer.active || !NetworkServer.spawned.TryGetValue(ownerId, out NetworkIdentity identity) ||
            !identity.TryGetComponent(out PlayerState player) || !player.CanUseTraps)
            throw new InvalidOperationException("The fixture needs a current server-side defender.");
        return player;
    }

    private void PrepareAim(Control command)
    {
        ClearFixtures();
        PlayerState owner = ServerOwner(command.ownerId);
        var catalog = FindFirstObjectByType<ConveyorController>();
        ItemData data = catalog.GetItemData(5);
        if (!(data?.Prefab is Missile)) throw new InvalidOperationException("Missile item 5 is missing.");
        Vector3 point = catalog.OutputCamera.ViewportToWorldPoint(new Vector3(0.6f, 0.65f, -catalog.OutputCamera.transform.position.z));
        fixtureMissile = (Missile)Instantiate(data.Prefab, point, Quaternion.identity);
        fixtureMissile.Initialize(data, new ItemSpawnMessage { ItemId = data.Id, InstanceId = ++nextFixtureId,
            OwnerNetId = owner.netId, Position = point, SpawnTime = NetworkTime.localTime }, true, 0f);
        fixtureMissile.enabled = false;
        Report value = NewReport(command); value.instanceId = fixtureMissile.InstanceId;
        Write(value);
    }

    private IEnumerator PrepareRound(Control command)
    {
        preparing = true;
        Report value = NewReport(command);
        try
        {
            ClearFixtures();
            PlayerState owner = ServerOwner(command.ownerId);
            var catalog = FindFirstObjectByType<ConveyorController>();
            var network = FindFirstObjectByType<SpearNetwork>();
            ItemData data = catalog.GetItemData(7);
            if (!(data?.Prefab is RoundTrap)) throw new InvalidOperationException("Rolling boulder item 7 is missing.");
            var area = ((BoxCollider2D)typeof(SpearNetwork).GetField("useBounds", PrivateInstance).GetValue(network)).bounds;
            var race = FindFirstObjectByType<RaceManager>();
            PlayerState attacker = race.Participants.First(player => race.Roles.IsAttacker(player));
            float x = attacker.transform.position.x + (command.action == "prepareMovingRound" ? 8f : 4f);
            x = Mathf.Clamp(x, area.min.x + 1f, area.max.x - 1f);
            RaycastHit2D ground = Physics2D.Raycast(new Vector2(x, area.max.y + 5f), Vector2.down,
                area.size.y + 10f, network.GroundLayer);
            if (!ground) throw new InvalidOperationException("No ground under the boulder fixture.");
            float radius = data.Prefab.GetComponent<CircleCollider2D>().radius * Mathf.Abs(data.Prefab.transform.lossyScale.x);
            float y = ground.point.y + radius + 0.01f;
            if (command.action == "prepareMovingRound") y = Mathf.Min(y + 8f, area.max.y - radius);
            fixtureData = Instantiate(data);
            fixtureData.Lifetime = 8f;
            fixtureData.Placement = ItemPlacement.AtCursor;
            if (!network.TryUse(fixtureData, owner.netIdentity, new Vector2(x, y), area.max.y))
                throw new InvalidOperationException("Server rejected the boulder fixture position.");
            fixtureRound = FindObjectsByType<RoundTrap>(FindObjectsSortMode.None).First(item => item.Data == fixtureData);
            value.instanceId = fixtureRound.InstanceId;
        }
        catch (Exception error) { value.success = false; value.error = error.Message; }
        if (value.success && command.action == "prepareRound") yield return new WaitForSecondsRealtime(1f);
        preparing = false;
        Write(value);
    }

    private void BeginMeasurement(Control command)
    {
        bool aim = command.action == "aimStationary" || command.action == "aimMoving";
        bool round = command.action == "roundStationary" || command.action == "roundMoving";
        if (!aim && !round) throw new InvalidOperationException("Unknown traffic action.");
        if (round && fixtureRound == null) throw new InvalidOperationException("Prepare a boulder fixture first.");
        if (aim)
        {
            aimHolder = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerHeldItem>() : null;
            if (aimHolder == null || !aimHolder.isLocalPlayer) throw new InvalidOperationException("The aim benchmark needs the owned player.");
            holderWasEnabled = aimHolder.enabled;
            aimHolder.enabled = false;
            aimCamera = FindFirstObjectByType<ConveyorController>().OutputCamera;
            sendAim = (Action<Vector2>)Delegate.CreateDelegate(typeof(Action<Vector2>), aimHolder,
                typeof(PlayerHeldItem).GetMethod("UpdateMissileAim", PrivateInstance));
            typeof(PlayerHeldItem).GetField("nextAimSendTime", PrivateInstance).SetValue(aimHolder, 0f);
            typeof(PlayerHeldItem).GetField("hasSentAim", PrivateInstance)?.SetValue(aimHolder, false);
            movingAim = command.action == "aimMoving";
        }
        current = NewReport(command);
        if (round) current.instanceId = fixtureRound.InstanceId;
        startedAt = Time.unscaledTimeAsDouble;
        lastMessageAt = startedAt;
        duration = command.duration > 0f ? command.duration : command.action == "roundMoving" ? 1f : 4f;
        NetworkDiagnostics.OutMessageEvent += RecordOutgoing;
    }

    private void RecordOutgoing(NetworkDiagnostics.MessageInfo info)
    {
        if (current == null) return;
        bool matching = info.message is CommandMessage command && sendAim != null &&
            RemoteProcedureCalls.GetDelegate(command.functionHash)?.Method.Name.Contains("CmdUpdateMissileAim") == true;
        if (info.message is ItemStateMessage state && state.InstanceId == current.instanceId)
        {
            matching = true;
            if (state.Expired) current.expiredMessages++;
        }
        if (!matching) return;
        double now = Time.unscaledTimeAsDouble;
        if (current.messages == 0) current.firstMessageDelay = now - startedAt;
        else current.largestMessageGap = Math.Max(current.largestMessageGap, now - lastMessageAt);
        lastMessageAt = now;
        current.messages++;
        current.serializedBytes += info.bytes;
        current.deliveries += info.count;
        current.deliveryBytes += (long)info.bytes * info.count;
        if (info.channel == Channels.Reliable) current.reliableMessages++;
        else current.unreliableMessages++;
    }

    private void FinishMeasurement()
    {
        NetworkDiagnostics.OutMessageEvent -= RecordOutgoing;
        if (aimHolder != null) aimHolder.enabled = holderWasEnabled;
        aimHolder = null; sendAim = null; aimCamera = null;
        current.elapsed = Time.unscaledTimeAsDouble - startedAt;
        Report completed = current; current = null;
        Write(completed);
    }

    private void ClearFixtures()
    {
        if (fixtureMissile != null) Destroy(fixtureMissile.gameObject);
        if (fixtureRound != null && NetworkServer.active)
            typeof(RoundTrap).GetMethod("Expire", PrivateInstance).Invoke(fixtureRound, null);
        fixtureMissile = null; fixtureRound = null;
        if (fixtureData != null) Destroy(fixtureData);
        fixtureData = null;
    }

    private void Write(Report value)
        => File.WriteAllText(Path.Combine(directory, id + ".traffic.json"), JsonUtility.ToJson(value, true));

    private void OnDestroy()
    {
        NetworkDiagnostics.OutMessageEvent -= RecordOutgoing;
        if (aimHolder != null) aimHolder.enabled = holderWasEnabled;
        ClearFixtures();
    }
}
#endif
