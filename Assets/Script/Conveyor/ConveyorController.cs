using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 공유할 아이템의 식별 정보와 이동 시간을 보관한다.
/// </summary>
public struct ConveyorEntry
{
    public uint Id;
    public int Price;
    public double SpawnTime;
    public float Lifetime;
    public bool Purchased;
}

/// <summary>
/// 서버의 공용 아이템 목록과 각 클라이언트의 컨베이어 표시를 관리한다.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class ConveyorController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private ConveyorItem itemPrefab;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform spawnPoint;
    [Header("Settings")]
    [SerializeField, Min(0.01f)] private float spawnInterval = 2f;
    [SerializeField, Min(0.01f)] private float moveSpeed = 80f;
    [SerializeField, Min(1)] private int itemPrice = 10;
    [Tooltip("모든 화면에서 동일하게 사용하는 아이템 이동 시간입니다.")]
    [SerializeField, Min(0.01f)] private float travelDuration = 8f;

    // 생성과 제거만 동기화한다. 늦게 접속한 클라이언트도 현재 목록을 받는다.
    private readonly SyncList<ConveyorEntry> items = new SyncList<ConveyorEntry>();
    private readonly Dictionary<uint, ConveyorItem> views = new Dictionary<uint, ConveyorItem>();
    private float _spawnTimer;
    private uint nextItemId;

    /// <summary>
    /// 역할과 코스트가 연결된 자기 수비자만 컨베이어를 클릭할 수 있다.
    /// </summary>
    public bool CanLocalPlayerCollect
    {
        get
        {
            if (!isClient || NetworkClient.localPlayer == null)
                return false;
            PlayerState player = NetworkClient.localPlayer.GetComponent<PlayerState>();
            return player != null && player.IsRoleManaged && !player.IsAttacker &&
                player.GetComponent<PlayerCost>() != null;
        }
    }

    /// <summary>
    /// 서버의 생성 상태를 초기화한다. 첫 아이템은 게임 시작 후 생성한다.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        nextItemId = 0;
        _spawnTimer = spawnInterval;
    }

    /// <summary>
    /// 목록 변경을 구독하고 접속 전에 존재하던 아이템도 화면에 생성한다.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (itemPrefab == null || viewport == null || spawnPoint == null)
        {
            Debug.LogError("컨베이어의 Item Prefab, Viewport, Spawn Point를 연결하세요.", this);
            return;
        }
        items.OnAdd += ShowItem;
        items.OnRemove += RemoveItem;
        items.OnClear += ClearViews;
        for (int index = 0; index < items.Count; index++)
            ShowItem(index);
    }

    /// <summary>
    /// 접속 종료 시 목록 구독과 생성한 UI를 정리한다.
    /// </summary>
    public override void OnStopClient()
    {
        items.OnAdd -= ShowItem;
        items.OnRemove -= RemoveItem;
        items.OnClear -= ClearViews;
        ClearViews();
        base.OnStopClient();
    }

    /// <summary>
    /// 서버에서 만료 아이템을 제거하고 설정한 주기로 새 아이템을 등록한다.
    /// </summary>
    private void Update()
    {
        if (!isServer || spawnInterval <= 0f || travelDuration <= 0f)
            return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            return;

        for (int index = items.Count - 1; index >= 0; index--)
        {
            if (NetworkTime.time >= items[index].SpawnTime + items[index].Lifetime)
                items.RemoveAt(index);
        }
        _spawnTimer += Time.deltaTime;
        while (_spawnTimer >= spawnInterval)
        {
            _spawnTimer -= spawnInterval;
            SpawnItem();
        }
    }

    /// <summary>
    /// 서버에서 고유 ID를 부여해 아이템을 목록에 추가한다. UI는 클라이언트가 만든다.
    /// </summary>
    [Server]
    private void SpawnItem()
    {
        items.Add(new ConveyorEntry
        {
            Id = ++nextItemId,
            Price = Mathf.Max(1, itemPrice),
            SpawnTime = NetworkTime.time,
            Lifetime = travelDuration
        });
    }

    /// <summary>
    /// 서버에서 수비자 여부, 아이템과 잔액을 확인하고 구매자 코스트만 차감한다.
    /// </summary>
    [Server]
    public bool TryCollect(uint itemId, PlayerCost buyer)
    {
        if (buyer == null || !buyer.isServer || buyer.connectionToClient == null)
            return false;
        PlayerState player = buyer.GetComponent<PlayerState>();
        if (player == null || !player.IsRoleManaged || player.IsAttacker)
            return false;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            return false;

        for (int index = 0; index < items.Count; index++)
        {
            ConveyorEntry item = items[index];
            if (item.Id != itemId)
                continue;
            if (NetworkTime.time >= item.SpawnTime + item.Lifetime ||
                !buyer.TrySpendCost(item.Price))
                return false;

            // 제거 이유를 함께 보내 구매는 투명화, 만료는 삭제로 표시한다.
            item.Purchased = true;
            items[index] = item;
            items.RemoveAt(index);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 공용 목록에 추가된 아이템을 지정 위치에 UI로 생성한다.
    /// </summary>
    private void ShowItem(int index)
    {
        ConveyorEntry entry = items[index];
        if (views.ContainsKey(entry.Id))
            return;
        ConveyorItem item = Instantiate(itemPrefab, viewport, false);
        RectTransform rect = (RectTransform)item.transform;
        rect.anchoredPosition = spawnPoint.anchoredPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
        item.Initialize(viewport, moveSpeed);
        item.BindNetworkItem(this, entry);
        views.Add(entry.Id, item);
    }

    /// <summary>
    /// 서버의 제거 이유에 따라 모든 화면에서 구매 연출 또는 만료 삭제를 실행한다.
    /// </summary>
    private void RemoveItem(int index, ConveyorEntry entry)
    {
        if (!views.TryGetValue(entry.Id, out ConveyorItem item))
            return;
        if (item != null)
        {
            if (entry.Purchased)
            {
                item.BeginCollect();
                // 연출 중인 UI도 접속 종료 시 정리할 수 있도록 파괴될 때까지 보관한다.
                return;
            }
            else
                Destroy(item.gameObject);
        }
        views.Remove(entry.Id);
    }

    /// <summary>
    /// 삭제된 UI의 참조를 해제한다. 구매 연출이 끝난 아이템도 여기에 해당한다.
    /// </summary>
    public void ForgetItem(uint itemId)
    {
        views.Remove(itemId);
    }

    /// <summary>
    /// 자기 플레이어의 코스트 컴포넌트를 통해 서버에 구매를 요청한다.
    /// </summary>
    public bool RequestCollect(uint itemId)
    {
        if (!CanLocalPlayerCollect)
            return false;
        PlayerCost cost = NetworkClient.localPlayer.GetComponent<PlayerCost>();
        if (cost == null)
        {
            Debug.LogError("네트워크 플레이어 프리팹에 PlayerCost가 필요합니다.", this);
            return false;
        }
        return cost.RequestCollect(itemId);
    }

    /// <summary>
    /// 구매 응답을 받은 아이템의 중복 요청 차단을 해제한다.
    /// </summary>
    public void CompleteCollectRequest(uint itemId)
    {
        if (views.TryGetValue(itemId, out ConveyorItem item) && item != null)
            item.CompleteCollectRequest();
    }

    /// <summary>
    /// 목록 초기화나 접속 종료 시 관리 중인 UI를 삭제한다.
    /// </summary>
    private void ClearViews()
    {
        foreach (ConveyorItem item in views.Values)
        {
            if (item != null)
                Destroy(item.gameObject);
        }
        views.Clear();
    }
}
