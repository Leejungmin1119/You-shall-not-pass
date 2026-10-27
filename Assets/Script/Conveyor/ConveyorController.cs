using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 공유할 아이템의 식별 정보와 이동 시간을 보관한다.
/// </summary>
public struct ConveyorEntry
{
    public uint Id;
    public int ItemId;
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
    [SerializeField] private ItemData[] itemCatalog;
    [SerializeField] private Camera outputCamera;
    [SerializeField] private UnityEngine.UI.Image heldIcon;
    [Tooltip("모든 화면에서 동일하게 사용하는 아이템 이동 시간입니다.")]
    [SerializeField, Min(0.01f)] private float travelDuration = 8f;

    // 생성과 제거만 동기화한다. 늦게 접속한 클라이언트도 현재 목록을 받는다.
    private readonly SyncList<ConveyorEntry> items = new SyncList<ConveyorEntry>();
    private readonly Dictionary<uint, ConveyorItem> views = new Dictionary<uint, ConveyorItem>();
    private float _spawnTimer;
    private uint nextItemId;
    private RaceManager race;
    private RectTransform uiTransform;
    private Vector2 initialAnchoredPosition;
    public Camera OutputCamera => outputCamera;
    public UnityEngine.UI.Image HeldIcon => heldIcon;

    /// <summary>네트워크 생성 위치가 적용되기 전에 씬의 앵커 기준 UI 위치를 보관한다.</summary>
    private void Awake()
    {
        uiTransform = transform as RectTransform;

        if (uiTransform != null)
            initialAnchoredPosition = uiTransform.anchoredPosition;
    }

    /// <summary>
    /// 살아 있고 빈손이며 요청 대기 중이 아닌 자기 플레이어만 구매할 수 있다.
    /// </summary>
    public bool CanLocalPlayerCollect
    {
        get
        {
            if (!isClient || NetworkClient.localPlayer == null)
                return false;
            return NetworkClient.localPlayer.TryGetComponent(out PlayerHeldItem player) && player.CanRequestPurchase;
        }
    }

    /// <summary>
    /// 서버의 생성 상태를 초기화한다. 첫 아이템은 게임 시작 후 생성한다.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        nextItemId = 0;
        _spawnTimer = spawnInterval;
        if (!ValidateCatalog()) enabled = false;
    }

    /// <summary>아이템 ID, 가격, 프리팹과 아이콘을 확인해 잘못된 상품 생성을 막는다.</summary>
    private bool ValidateCatalog()
    {
        var ids = new HashSet<int>();
        if (itemCatalog == null || itemCatalog.Length == 0)
        {
            Debug.LogError("컨베이어에 판매할 아이템 데이터를 연결하세요.", this);
            return false;
        }
        int totalWeight = 0;
        foreach (ItemData data in itemCatalog)
        {
            if (data == null || data.Id <= 0 || !ids.Add(data.Id) || data.Price <= 0 ||
                data.SpawnWeight < 0 || data.Prefab == null || data.Icon == null)
            {
                Debug.LogError("상품 ID 중복 또는 아이템 데이터의 누락된 설정을 확인하세요.", this);
                return false;
            }
            totalWeight += data.SpawnWeight;
        }
        if (totalWeight <= 0)
        {
            Debug.LogError("등장 가중치가 양수인 상품이 하나 이상 필요합니다.", this);
            return false;
        }
        return true;
    }

    /// <summary>판매 UI와 소유자 화면에서 서버와 같은 종류의 데이터를 찾는다.</summary>
    public ItemData GetItemData(int itemId)
    {
        if (itemCatalog == null) return null;
        foreach (ItemData data in itemCatalog)
            if (data != null && data.Id == itemId) return data;
        return null;
    }

    /// <summary>필드 사용 입력이 판매 영역 위에서 발생했는지 확인한다.</summary>
    public bool IsPointerOverViewport(Vector2 screenPosition)
    {
        return viewport != null && RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPosition, null);
    }

    /// <summary>
    /// 목록 변경을 구독하고 접속 전에 존재하던 아이템도 화면에 생성한다.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        // Mirror가 보낸 호스트의 로컬 좌표 대신 각 Canvas의 앵커 기준 배치를 사용한다.
        if (uiTransform != null)
            uiTransform.anchoredPosition = initialAnchoredPosition;

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

        if (race != null && !race.IsRunning)
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

    /// <summary>이전 라운드 상품을 지우고 새 상품 한 개부터 컨베이어를 다시 시작한다.</summary>
    [Server]
    public void ResetForRound()
    {
        ClearItems();
        _spawnTimer = 0;
        SpawnItem();
    }

    /// <summary>상품 ID는 재사용하지 않고 목록과 생성 대기시간만 비운다.</summary>
    [Server]
    public void ClearItems()
    {
        items.Clear();
        _spawnTimer = 0;
    }

    /// <summary>
    /// 서버에서 고유 ID를 부여해 아이템을 목록에 추가한다. UI는 클라이언트가 만든다.
    /// </summary>
    [Server]
    private void SpawnItem()
    {
        ItemData data = ChooseRandomItem();
        items.Add(new ConveyorEntry
        {
            Id = ++nextItemId,
            ItemId = data.Id,
            Price = data.Price,
            SpawnTime = NetworkTime.time,
            Lifetime = travelDuration
        });
    }

    /// <summary>서버에서 각 상품의 등장 가중치에 따라 하나를 선택한다.</summary>
    private ItemData ChooseRandomItem()
    {
        int total = 0;
        foreach (ItemData data in itemCatalog) total += data.SpawnWeight;
        int roll = Random.Range(0, total);
        foreach (ItemData data in itemCatalog)
        {
            roll -= data.SpawnWeight;
            if (roll < 0) return data;
        }
        return itemCatalog[itemCatalog.Length - 1];
    }

    /// <summary>
    /// 서버에서 생존 여부, 아이템과 잔액을 확인하고 구매자 코스트만 차감한다.
    /// </summary>
    [Server]
    public bool TryCollect(uint itemId, PlayerHeldItem buyer,
        PurchaseDestination destination = PurchaseDestination.Hand)
    {
        if (destination != PurchaseDestination.Inventory && destination != PurchaseDestination.Hand)
            return false;

        if (buyer == null || !buyer.isServer || buyer.connectionToClient == null)
            return false;
        if (!buyer.CanHoldItem || !buyer.TryGetComponent(out PlayerCost cost))
            return false;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            return false;

        // 즉시 구매도 취소할 때 돌아갈 빈 슬롯을 먼저 확보한다.
        if (!buyer.TryGetComponent(out PlayerInventory inventory))
            return false;

        int emptySlot = inventory.FindEmptySlot();
        if (emptySlot < 0)
            return false;

        for (int index = 0; index < items.Count; index++)
        {
            ConveyorEntry item = items[index];
            if (item.Id != itemId)
                continue;
            ItemData data = GetItemData(item.ItemId);
            if (NetworkTime.time >= item.SpawnTime + item.Lifetime ||
                data == null || !cost.TrySpendCost(item.Price))
                return false;

            // 이 구간은 서버의 한 호출 안에서 실행되므로 구매가 중간에 끼어들지 않는다.
            inventory.StoreItem(emptySlot, data.Id);

            if (destination == PurchaseDestination.Hand)
                inventory.TrySelectSlot(emptySlot, buyer, data);

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
    /// 자기 플레이어의 보유 담당을 통해 서버에 구매를 요청한다.
    /// </summary>
    public bool RequestCollect(uint itemId, PurchaseDestination destination = PurchaseDestination.Hand)
    {
        if (!CanLocalPlayerCollect)
            return false;
        return NetworkClient.localPlayer.GetComponent<PlayerHeldItem>().RequestPurchase(itemId, destination);
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
