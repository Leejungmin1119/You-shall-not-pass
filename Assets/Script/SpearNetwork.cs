using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// 클라이언트 → 호스트: 커서 위치에 창을 쓰고 싶다.
public struct SpearUseRequest : NetworkMessage
{
    public Vector2 CursorWorldPosition;
}

// 호스트 → 전원: 이 창이 생겼다. 움직임은 정해져 있어서 위치는 다시 보내지 않는다.
public struct SpearSpawnMessage : NetworkMessage
{
    public uint SpearId;
    public int OwnerId;
    public float X;
    public float StartY;
}

// 호스트 → 전원: 이 창이 이 캐릭터를 맞혔다.
public struct SpearHitMessage : NetworkMessage
{
    public uint SpearId;
    public int OwnerId;
    public uint HitNetId;
}

/// <summary>
/// 창의 네트워크 흐름. 요청 → 호스트 확인 → 전원에게 생성 → 호스트 판정 → 전원에게 결과.
/// 창은 NetworkIdentity 없이 메시지만으로 동기화한다(서버 담당의 플레이어 프리팹과 무관하게 동작).
/// </summary>
public class SpearNetwork : MonoBehaviour
{
    // 방어자가 연속으로 쓰지 못하게 하는 대기 시간. 기획서 4장 [제안] 값.
    public float UseCooldown = 0.5f;

    // 왕복 지연 측정값이 비정상적으로 클 때 창을 그만큼 앞당기지 않도록 두는 상한.
    public float MaxCatchUpSeconds = 0.3f;

    // 이 PC가 맞은 것인지(isMine)와 함께 결과를 알린다. 조작 잠금·점수·연출은 여기에 붙인다.
    public static event Action<SpearHitMessage, bool> HitReceived;

    private SpearDropper _dropper;
    private rolechangeManager _roleManager;
    private bool _serverHandlersRegistered;
    private bool _clientHandlersRegistered;
    private uint _nextSpearId;
    private readonly Dictionary<int, double> _lastUseTime = new Dictionary<int, double>();

    public static void SendUseRequest(Vector2 cursorWorldPosition)
    {
        NetworkClient.Send(new SpearUseRequest { CursorWorldPosition = cursorWorldPosition });
    }

    private void Awake()
    {
        _dropper = FindFirstObjectByType<SpearDropper>();
        if (_dropper == null)
        {
            Debug.LogWarning("[창] SpearDropper가 씬에 없어 네트워크 창을 만들 수 없습니다.");
        }
    }

    private void OnEnable()
    {
        Spear.AttackerHit += OnSpearHit;
    }

    private void OnDisable()
    {
        Spear.AttackerHit -= OnSpearHit;
    }

    private void Update()
    {
        // 핸들러는 서버/클라이언트가 멈출 때 Mirror가 비운다. 켜질 때마다 다시 등록하도록 상태를 따라간다.
        if (NetworkServer.active && !_serverHandlersRegistered)
        {
            NetworkServer.RegisterHandler<SpearUseRequest>(OnUseRequest);
            _serverHandlersRegistered = true;
            _lastUseTime.Clear();
            _nextSpearId = 0;
        }
        else if (!NetworkServer.active)
        {
            _serverHandlersRegistered = false;
        }

        if (NetworkClient.active && !_clientHandlersRegistered)
        {
            NetworkClient.RegisterHandler<SpearSpawnMessage>(OnSpawnMessage);
            NetworkClient.RegisterHandler<SpearHitMessage>(OnHitMessage);
            _clientHandlersRegistered = true;
        }
        else if (!NetworkClient.active)
        {
            _clientHandlersRegistered = false;
        }
    }

    // ── 호스트 ────────────────────────────────────────────────

    private void OnUseRequest(NetworkConnectionToClient connection, SpearUseRequest request)
    {
        if (_dropper == null)
        {
            return;
        }

        Vector2 cursor = request.CursorWorldPosition;
        if (!IsFinite(cursor.x) || !IsFinite(cursor.y))
        {
            Debug.LogWarning($"[창] 요청 거부(잘못된 좌표): 연결 {connection.connectionId}");
            return;
        }

        // 공격자는 함정을 쓸 수 없다(기획서 3장: 공격자인 동안 인벤토리 사용 불가).
        if (connection.identity != null &&
            connection.identity.TryGetComponent(out PlayerState requester) &&
            requester.IsRoleManaged && requester.IsAttacker)
        {
            Debug.Log($"[창] 요청 거부(공격자): 연결 {connection.connectionId}");
            return;
        }

        double now = NetworkTime.localTime;
        if (_lastUseTime.TryGetValue(connection.connectionId, out double lastTime) &&
            now - lastTime < UseCooldown)
        {
            Debug.Log($"[창] 요청 거부(재사용 대기): 연결 {connection.connectionId}");
            return;
        }

        _lastUseTime[connection.connectionId] = now;

        NetworkServer.SendToAll(new SpearSpawnMessage
        {
            SpearId = ++_nextSpearId,
            OwnerId = connection.connectionId,
            X = cursor.x,
            StartY = _dropper.SpawnHeight,
        });
    }

    private void OnSpearHit(Spear spear, PlayerMovement target)
    {
        if (!NetworkServer.active)
        {
            return;
        }

        NetworkIdentity identity = target.GetComponent<NetworkIdentity>();
        if (identity == null)
        {
            return;
        }

        // 역할 교대가 있는 게임 씬이면 교대가 받아들여진 적중만 알린다.
        // 이미 교대된 뒤 도착한 적중(동시 적중)이나 자기 함정은 여기서 걸러진다.
        if (_roleManager == null)
        {
            _roleManager = FindFirstObjectByType<rolechangeManager>();
        }

        if (_roleManager != null)
        {
            NetworkConnectionToClient ownerConnection;
            NetworkServer.connections.TryGetValue(spear.OwnerId, out ownerConnection);
            NetworkIdentity hitter = ownerConnection != null ? ownerConnection.identity : null;

            if (!_roleManager.TryChangeAfterHit(identity, target.transform.position, hitter))
            {
                Debug.Log($"[창] 적중 무시: 창 {spear.SpearId}, 맞은 netId {identity.netId}");
                return;
            }
        }

        NetworkServer.SendToAll(new SpearHitMessage
        {
            SpearId = spear.SpearId,
            OwnerId = spear.OwnerId,
            HitNetId = identity.netId,
        });
    }

    // ── 모든 PC ───────────────────────────────────────────────

    private void OnSpawnMessage(SpearSpawnMessage message)
    {
        if (_dropper == null)
        {
            return;
        }

        // 호스트 창은 판정 기준이라 그대로 두고, 다른 PC는 도착까지 걸린 시간(왕복의 절반)만큼 앞당긴다.
        // NetworkTime.time은 클라이언트에서 서버보다 늦은 보간용 시간축이라 시작 시각을 빼는 방식은 쓰지 않는다.
        bool isHost = NetworkServer.active;
        float catchUp = isHost ? 0f : Mathf.Min((float)(NetworkTime.rtt * 0.5), MaxCatchUpSeconds);

        _dropper.Spawn(message.X, message.StartY, message.SpearId, message.OwnerId, isHost, catchUp);
    }

    private void OnHitMessage(SpearHitMessage message)
    {
        bool isMine = NetworkClient.spawned.TryGetValue(message.HitNetId, out NetworkIdentity identity) &&
                      identity.isLocalPlayer;

        Debug.Log($"[창] 판정 수신: 창 {message.SpearId} (주인 연결 {message.OwnerId}) → " +
                  (isMine ? "내가 맞음" : $"플레이어 {message.HitNetId}가 맞음"));

        // 교대되면 필드의 창을 모두 없앤다(기획서 3장 RoleSwap [제안]).
        // 남겨 두면 새 공격자가 남은 창에 바로 맞을 수 있다.
        foreach (Spear spear in FindObjectsByType<Spear>(FindObjectsSortMode.None))
        {
            Destroy(spear.gameObject);
        }

        HitReceived?.Invoke(message, isMine);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
