using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum PurchaseDestination
{
    Inventory,
    Hand
}

/// <summary>서버가 개인 아이템 3칸을 관리하고 소유자에게만 내용을 동기화한다.</summary>
public sealed class PlayerInventory : NetworkBehaviour
{
    public const int Capacity = 3;

    private readonly SyncList<int> items = new SyncList<int>();
    private PlayerInventoryUI view;

    public IReadOnlyList<int> Items => items;
    public event Action<int> SlotChanged;

    /// <summary>서버에서 빈 슬롯 3개를 준비한다. 0은 비어 있는 칸이다.</summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        for (int index = 0; index < Capacity; index++)
            items.Add(0);
    }

    /// <summary>동기화된 슬롯 변경을 화면에 전달하도록 구독한다.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        items.OnSet += OnSlotChanged;
    }

    /// <summary>자기 인벤토리만 하단 UI에 연결하고 초기 내용도 바로 표시한다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        view = FindFirstObjectByType<PlayerInventoryUI>(FindObjectsInactive.Include);

        if (view != null)
            view.Bind(this);
    }

    /// <summary>네트워크 목록이 갱신된 슬롯 번호만 UI에 알린다.</summary>
    private void OnSlotChanged(int index, int previous)
    {
        SlotChanged?.Invoke(index);
    }

    /// <summary>앞에서부터 빈 칸을 찾고, 가득 찼으면 -1을 반환한다.</summary>
    public int FindEmptySlot()
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index] == 0)
                return index;
        }

        return -1;
    }

    /// <summary>구매 담당이 빈 칸과 상품을 검증한 뒤, 같은 서버 처리 안에서 저장한다.</summary>
    [Server]
    internal void StoreItem(int slot, int itemId)
    {
        items[slot] = itemId;
    }

    /// <summary>라운드 종료 시 슬롯 수는 유지하고 아이템만 모두 비운다.</summary>
    [Server]
    public void ClearInventory()
    {
        for (int index = 0; index < items.Count; index++)
            items[index] = 0;
    }

    /// <summary>연결 종료 시 슬롯 변경 구독과 자기 UI 연결을 해제한다.</summary>
    public override void OnStopClient()
    {
        items.OnSet -= OnSlotChanged;

        if (view != null)
            view.Unbind();

        base.OnStopClient();
    }
}
