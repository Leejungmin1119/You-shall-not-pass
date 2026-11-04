using System;
using Mirror;
using UnityEngine;

/// <summary>서버가 확정해 전원에게 전달하는 아이템 생성 정보.</summary>
public struct ItemSpawnMessage : NetworkMessage
{
    public int ItemId;
    public uint InstanceId;
    public uint OwnerNetId;
    public Vector2 Position;
    public bool HasGround;
    public float GroundY;
    public float FallSpeed;
    public double SpawnTime;
}

public struct ItemHitMessage : NetworkMessage
{
    public uint InstanceId;
    public uint OwnerNetId;
    public uint HitNetId;
}

/// <summary>서버가 확정한 이동 또는 폭발 표시 정보.</summary>
public struct ItemStateMessage : NetworkMessage
{
    public uint InstanceId;
    public Vector2 Position;
    public float Angle;
    public double ServerTime;
    public bool Exploded;
    public bool GroundExplosion;
    public bool Expired;
}

/// <summary>
/// 아이템 프리팹 생성과 서버 적중 결과를 전파한다. 구매와 보유 상태는 플레이어가 관리한다.
/// 기존 씬 연결을 유지하기 위해 컴포넌트 이름은 SpearNetwork를 유지한다.
/// </summary>
public class SpearNetwork : MonoBehaviour
{
    [SerializeField] private BoxCollider2D useBounds;
    public float UseCooldown = 0.5f;
    public float MaxCatchUpSeconds = 0.3f;
    public static event Action<ItemHitMessage, bool> HitReceived;
    private SpearDropper dropper;
    private ConveyorController conveyor;
    private FallZone fallZone;
    private bool clientRegistered;
    private uint nextInstanceId;
    public LayerMask GroundLayer => dropper != null ? dropper.GroundLayer : 0;

    /// <summary>씬에 연결된 생성기, 상품 목록과 부활 위치 담당을 보관한다.</summary>
    private void Awake()
    {
        dropper = FindFirstObjectByType<SpearDropper>();
        conveyor = FindFirstObjectByType<ConveyorController>(FindObjectsInactive.Include);
        fallZone = FindFirstObjectByType<FallZone>(FindObjectsInactive.Include);
    }

    /// <summary>아이템 종류와 무관한 서버 적중 처리를 구독한다.</summary>
    private void OnEnable() { ItemBase.PlayerHit += OnItemHit; ItemBase.StateChanged += OnItemState; }

    /// <summary>비활성화 시 정적 이벤트 구독을 해제한다.</summary>
    private void OnDisable() { ItemBase.PlayerHit -= OnItemHit; ItemBase.StateChanged -= OnItemState; }

    /// <summary>Mirror가 접속 종료 시 지우는 클라이언트 메시지 처리기를 다시 등록한다.</summary>
    private void Update()
    {
        if (NetworkClient.active && !clientRegistered)
        {
            NetworkClient.RegisterHandler<ItemSpawnMessage>(OnSpawnMessage);
            NetworkClient.RegisterHandler<ItemHitMessage>(OnHitMessage);
            NetworkClient.RegisterHandler<ItemStateMessage>(OnStateMessage);
            clientRegistered = true;
        }
        else if (!NetworkClient.active) clientRegistered = false;
    }

    /// <summary>
    /// 서버에서 위치와 프리팹을 확인한 뒤 묶음 전체를 같은 프레임에 생성한다.
    /// </summary>
    public bool TryUse(ItemData data, NetworkIdentity owner, Vector2 cursor, float startY)
    {
        if (!NetworkServer.active || data == null || data.Prefab == null || owner == null ||
            dropper == null || conveyor == null || useBounds == null ||
            !IsFinite(cursor.x) || !IsFinite(cursor.y) || !IsFinite(startY) ||
            data.Count < 1 || data.Count > 3 || data.FallTime <= 0f || data.Lifetime <= 0f)
            return false;
        if (!owner.TryGetComponent(out PlayerState actor) || !actor.CanUseTraps || owner.connectionToClient == null)
            return false;
        foreach (var identity in NetworkServer.spawned.Values)
            if (identity != null && identity.TryGetComponent(out PlayerState player) && player.IsInvincible &&
                Vector2.Distance(cursor, player.transform.position) < 1.5f) return false;
        Bounds area = useBounds.bounds;
        if (cursor.x < area.min.x || cursor.x > area.max.x || cursor.y < area.min.y || cursor.y > area.max.y ||
            (data.Placement == ItemPlacement.Drop && (startY <= cursor.y || startY > area.max.y + area.size.x)))
            return false;
        var batch = new ItemSpawnMessage[data.Count];
        for (int index = 0; index < batch.Length; index++)
        {
            float x = cursor.x + (index - (data.Count - 1) * 0.5f) * data.Spacing;
            if (x < area.min.x || x > area.max.x) return false;
            Vector2 position = data.Placement == ItemPlacement.Drop ? new Vector2(x, startY) : new Vector2(x, cursor.y);
            RaycastHit2D ground = data.Placement == ItemPlacement.Drop
                ? Physics2D.Raycast(position, Vector2.down, Mathf.Max(1f, position.y - area.min.y + 1f), dropper.GroundLayer)
                : default;
            float distance = ground ? position.y - ground.point.y : Mathf.Max(1f, position.y - area.min.y);
            if (data.Placement == ItemPlacement.Drop && distance <= 0.01f) return false;
            batch[index] = new ItemSpawnMessage {
                ItemId = data.Id, OwnerNetId = owner.netId, Position = position,
                HasGround = ground, GroundY = ground ? ground.point.y : 0f,
                FallSpeed = distance / data.FallTime, SpawnTime = NetworkTime.localTime
            };
        }
        foreach (ItemSpawnMessage prepared in batch)
        {
            ItemSpawnMessage spawn = prepared;
            spawn.InstanceId = ++nextInstanceId;
            dropper.Spawn(data, spawn, true, 0f);
            NetworkServer.SendToAll(spawn);
        }
        return true;
    }

    /// <summary>호스트는 이미 생성했으므로 원격 화면에서만 표시용 아이템을 만든다.</summary>
    private void OnSpawnMessage(ItemSpawnMessage message)
    {
        if (NetworkServer.active || dropper == null || conveyor == null) return;
        ItemData data = conveyor.GetItemData(message.ItemId);
        if (data == null || data.Prefab == null) return;
        float age = Mathf.Clamp((float)(NetworkTime.predictedTime - message.SpawnTime), 0f, MaxCatchUpSeconds);
        dropper.Spawn(data, message, false, age);
    }

    /// <summary>승인된 적중을 기록하고 사망·교대를 요청하며 기존 소유자 적중 점수를 지급한다.</summary>
    private void OnItemHit(ItemBase item, PlayerState player)
    {
        if (!NetworkServer.active || !player.CanReceiveHit)
            return;

        Vector2 position = player.transform.position;
        bool roleGame = player.Race != null && player.Race.Roles != null;

        if (!roleGame && (fallZone == null || !fallZone.TryGetRespawnPosition(position, out position)))
            return;

        if (!player.TryDie())
            return;

        item.ConfirmHit(player);

        if (!roleGame)
            player.Respawn(position);
        if (item.OwnerNetId != player.netId &&
            NetworkServer.spawned.TryGetValue(item.OwnerNetId, out NetworkIdentity owner) &&
            owner.TryGetComponent(out PlayerScore score)) score.AddHitScore();
        NetworkServer.SendToAll(new ItemHitMessage {
            InstanceId = item.InstanceId, OwnerNetId = item.OwnerNetId, HitNetId = player.netId
        });
    }

    /// <summary>서버가 확정한 적중을 화면 연출에 전달한다.</summary>
    private void OnHitMessage(ItemHitMessage message)
    {
        bool mine = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == message.HitNetId;
        HitReceived?.Invoke(message, mine);
    }

    /// <summary>폭발과 소멸은 순서를 보장하고 연속 이동은 최신 위치를 우선하여 전파한다.</summary>
    private void OnItemState(ItemStateMessage message)
    {
        if (NetworkServer.active)
            NetworkServer.SendToAll(message, (message.Exploded || message.Expired) ? Channels.Reliable : Channels.Unreliable);
    }

    /// <summary>호스트의 서버 판정을 중복 실행하지 않고 원격 표시만 갱신한다.</summary>
    private void OnStateMessage(ItemStateMessage message)
    {
        if (!NetworkServer.active) ItemBase.ReceiveState(message);
    }

    /// <summary>발사자가 보낸 조준점이 유한한 값이고 사용 가능 영역 안인지 확인한다.</summary>
    public bool IsValidAim(Vector2 position)
    {
        if (useBounds == null || !IsFinite(position.x) || !IsFinite(position.y)) return false;
        Bounds area = useBounds.bounds;
        return position.x >= area.min.x && position.x <= area.max.x &&
            position.y >= area.min.y && position.y <= area.max.y;
    }

    /// <summary>외부 입력 좌표에 NaN이나 무한대가 포함되었는지 확인한다.</summary>
    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
}
