using System;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>내용과 선택 슬롯은 소유자에게만, 개수는 PlayerState를 통해 전원에게 보낸다.</summary>
public sealed class NetworkRaceInventory : NetworkBehaviour
{
    public readonly SyncList<int> Slots = new SyncList<int>();
    [SyncVar] private int heldSlot = -1;
    private double nextBuyTime;
    private double nextUseTime;
    public int HeldSlot => heldSlot;
    public event Action<uint, RaceRequestResult> RequestCompleted;
    private uint requestId;

    private void Awake() => syncMode = SyncMode.Owner;

    public override void OnStartServer()
    {
        var race = RaceSession.Instance;
        int count = race != null ? race.Rules.inventorySlots : 3;
        for (int i = 0; i < count; i++) Slots.Add(0);
    }

    private void Update()
    {
        if (!isLocalPlayer || RaceSession.Instance == null || Keyboard.current == null) return;
        var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if (selected != null && (selected.GetComponent<TMPro.TMP_InputField>() != null ||
            selected.GetComponent<UnityEngine.UI.InputField>() != null)) return;
        var keyboard = Keyboard.current;
        if (keyboard.digit1Key.wasPressedThisFrame) RequestSelect(0);
        if (keyboard.digit2Key.wasPressedThisFrame) RequestSelect(1);
        if (keyboard.digit3Key.wasPressedThisFrame) RequestSelect(2);
    }

    public bool RequestBuy(uint itemId, bool equip)
    {
        if (!isLocalPlayer || RaceSession.Instance == null) return false;
        CmdBuy(++requestId, RaceSession.Instance.Round, itemId, equip);
        return true;
    }

    public void RequestSelect(int slot)
    {
        if (isLocalPlayer && RaceSession.Instance != null)
            CmdSelect(++requestId, RaceSession.Instance.Round, slot);
    }

    public void RequestUse(Vector2 worldPosition)
    {
        if (isLocalPlayer && RaceSession.Instance != null)
            CmdUse(++requestId, RaceSession.Instance.Round, worldPosition);
    }

    [Command]
    private void CmdBuy(uint id, int round, uint itemId, bool equip)
    {
        var conveyor = FindFirstObjectByType<ConveyorController>();
        RaceRequestResult result = conveyor != null
            ? conveyor.ServerBuy(itemId, this, round, equip) : RaceRequestResult.Unavailable;
        TargetBuyResult(id, itemId, result);
    }

    [Server]
    public RaceRequestResult ValidatePurchase(int round, out int slot)
    {
        slot = -1;
        var race = RaceSession.Instance;
        if (race == null) return RaceRequestResult.NotRacing;
        var result = race.ValidateAction(GetComponent<PlayerState>(), round);
        if (result != RaceRequestResult.Success) return result;
        if (heldSlot >= 0) return RaceRequestResult.HoldingItem;
        if (NetworkTime.time < nextBuyTime) return RaceRequestResult.Cooldown;
        slot = Slots.IndexOf(0);
        return slot >= 0 ? RaceRequestResult.Success : RaceRequestResult.InventoryFull;
    }

    [Server]
    public void ServerStore(int slot, int itemId, bool equip)
    {
        Slots[slot] = itemId;
        if (equip) heldSlot = slot;
        nextBuyTime = NetworkTime.time + RaceSession.Instance.Rules.buyCooldown;
        UpdatePublicCount();
    }

    [Command]
    private void CmdSelect(uint id, int round, int slot)
        => TargetResult(id, ServerSelect(round, slot));

    [Server]
    public RaceRequestResult ServerSelect(int round, int slot)
    {
        var race = RaceSession.Instance;
        if (race == null) return RaceRequestResult.NotRacing;
        var result = race.ValidateAction(GetComponent<PlayerState>(), round);
        if (result != RaceRequestResult.Success) return result;
        if (slot < 0 || slot >= Slots.Count) return RaceRequestResult.InvalidSlot;
        if (heldSlot == slot) { heldSlot = -1; return RaceRequestResult.Success; }
        if (heldSlot >= 0) return RaceRequestResult.HoldingItem;
        if (Slots[slot] == 0) return RaceRequestResult.EmptySlot;
        heldSlot = slot;
        return RaceRequestResult.Success;
    }

    [Command]
    private void CmdUse(uint id, int round, Vector2 position)
        => TargetResult(id, ServerUse(round, position));

    [Server]
    public RaceRequestResult ServerUse(int round, Vector2 position)
    {
        var race = RaceSession.Instance;
        if (race == null) return RaceRequestResult.NotRacing;
        var result = race.ValidateAction(GetComponent<PlayerState>(), round);
        if (result != RaceRequestResult.Success) return result;
        if (heldSlot < 0 || heldSlot >= Slots.Count || Slots[heldSlot] == 0) return RaceRequestResult.EmptySlot;
        if (NetworkTime.time < nextUseTime) return RaceRequestResult.Cooldown;
        result = race.ValidatePosition(position);
        if (result != RaceRequestResult.Success) return result;
        var item = race.Rules.FindItem(Slots[heldSlot]);
        if (item == null || !race.Traps.ServerTrySpawn(item, netIdentity, position))
            return RaceRequestResult.UnsupportedItem;
        Slots[heldSlot] = 0;
        heldSlot = -1;
        nextUseTime = NetworkTime.time + race.Rules.useCooldown;
        UpdatePublicCount();
        return RaceRequestResult.Success;
    }

    [Server]
    public void ServerCancelSelection() => heldSlot = -1;

    [Server]
    private void UpdatePublicCount()
    {
        int count = 0;
        foreach (int item in Slots) if (item != 0) count++;
        GetComponent<PlayerState>().ServerSetInventoryCount(count);
    }

    [TargetRpc]
    private void TargetBuyResult(uint id, uint itemId, RaceRequestResult result)
    {
        FindFirstObjectByType<ConveyorController>()?.CompleteCollectRequest(itemId);
        RequestCompleted?.Invoke(id, result);
        Debug.Log($"[Race] Buy {id}: {result}");
    }

    [TargetRpc]
    private void TargetResult(uint id, RaceRequestResult result)
    {
        RequestCompleted?.Invoke(id, result);
        if (result != RaceRequestResult.Success) Debug.Log($"[Race] Request {id}: {result}");
    }
}
