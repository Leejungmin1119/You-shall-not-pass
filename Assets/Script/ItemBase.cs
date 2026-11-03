using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// 생성된 함정의 소유자와 적중 기록을 보관한다. 이동 방식은 각 함정이 구현한다.
/// </summary>
public abstract class ItemBase : MonoBehaviour
{
    public static event Action<ItemBase, PlayerState> PlayerHit;
    public static event Action<ItemStateMessage> StateChanged;
    private static readonly Dictionary<uint, ItemBase> instances = new Dictionary<uint, ItemBase>();
    private readonly HashSet<uint> hitPlayers = new HashSet<uint>();
    public static int ActiveCount => instances.Count;
    public uint InstanceId { get; private set; }
    public uint OwnerNetId { get; private set; }
    public ItemData Data { get; private set; }
    protected bool IsJudge { get; private set; }

    /// <summary>
    /// 서버가 확정한 소유자와 설정을 생성 정보 메시지에서 읽어 적용한다.
    /// </summary>
    public virtual void Initialize(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        Data = data;
        InstanceId = spawn.InstanceId;
        OwnerNetId = spawn.OwnerNetId;
        IsJudge = isJudge;
        hitPlayers.Clear();
        instances[InstanceId] = this;
    }

    /// <summary>라운드 종료 시 등록된 함정을 즉시 멈추고 파괴한다.</summary>
    public static void ClearAll()
    {
        foreach (ItemBase item in instances.Values)
        {
            if (item == null)
                continue;

            item.gameObject.SetActive(false);
            Destroy(item.gameObject);
        }

        instances.Clear();
    }

    /// <summary>수신한 위치 또는 폭발 상태를 해당 아이템에 적용한다.</summary>
    public static void ReceiveState(ItemStateMessage message)
    {
        if (instances.TryGetValue(message.InstanceId, out ItemBase item) && item != null)
            item.ApplyState(message);
    }

    /// <summary>아이템별 원격 상태 처리를 정의한다. 창은 별도 상태 메시지가 없다.</summary>
    public virtual void ApplyState(ItemStateMessage message) { }

    /// <summary>서버가 결정한 이동 또는 폭발 상태를 전송 담당에 전달한다.</summary>
    protected void PublishState(bool exploded, bool groundExplosion = false, bool expired = false, Vector2? position = null, float? angle = null)
    {
        if (!IsJudge) return;
        StateChanged?.Invoke(new ItemStateMessage {
            InstanceId = InstanceId, Position = position ?? (Vector2)transform.position,
            Angle = angle ?? transform.eulerAngles.z, ServerTime = NetworkTime.localTime, Exploded = exploded, GroundExplosion = groundExplosion, Expired = expired
        });
    }

    /// <summary>보유 중 착지 미리보기를 만든다. 낙하하지 않는 종류는 표시하지 않는다.</summary>
    public virtual SpriteRenderer CreatePlacementPreview(Vector2 position) { return null; }

    /// <summary>원격 플레이어의 비활성 물리도 포함해 이동 구간의 가장 가까운 접촉을 찾는다.</summary>
    protected bool FindPlayerContact(Vector2 from, Vector2 to, float radius, out PlayerState target, out Vector2 contact)
    {
        target = null;
        contact = to;
        float nearest = float.PositiveInfinity;
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !identity.TryGetComponent(out PlayerState player) || !CanHit(player)) continue;
            Collider2D collider = player.GetComponent<Collider2D>();
            if (collider == null) continue;
            Bounds body = collider.bounds;
            body.Expand(new Vector3(radius * 2f, radius * 2f, 0f));
            float entry = 0f;
            bool touching = body.Contains(new Vector3(from.x, from.y, body.center.z)) ||
                (distance > 0f && body.IntersectRay(new Ray(new Vector3(from.x, from.y, body.center.z), delta / distance), out entry) && entry <= distance);
            if (!touching || entry >= nearest) continue;
            nearest = entry;
            target = player;
        }
        if (target != null && distance > 0f) contact = from + delta.normalized * nearest;
        return target != null;
    }

    /// <summary>파괴된 아이템을 메시지 조회 목록에서 제거한다.</summary>
    protected virtual void OnDestroy()
    {
        if (instances.TryGetValue(InstanceId, out ItemBase item) && item == this) instances.Remove(InstanceId);
    }

    /// <summary>
    /// 서버에서 아직 이 함정에 맞지 않은 생존 플레이어만 판정 대상으로 삼는다.
    /// </summary>
    protected bool CanHit(PlayerState player)
    {
        return IsJudge && player != null && player.CanReceiveHit && !hitPlayers.Contains(player.netId);
    }

    /// <summary>
    /// 접촉 사실을 공통 서버 처리에 전달한다. 점수와 부활은 여기서 변경하지 않는다.
    /// </summary>
    protected void ReportHit(PlayerState player)
    {
        PlayerHit?.Invoke(this, player);
    }

    /// <summary>
    /// 서버가 적중을 승인한 뒤 기록하여 동일 함정의 중복 득점을 막는다.
    /// </summary>
    public void ConfirmHit(PlayerState player)
    {
        hitPlayers.Add(player.netId);
    }
}
