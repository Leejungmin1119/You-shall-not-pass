using UnityEngine;

/// <summary>하단 중앙의 3칸에 자기 인벤토리 아이콘만 표시한다.</summary>
public sealed class PlayerInventoryUI : MonoBehaviour
{
    [SerializeField] private ConveyorController conveyor;
    [SerializeField] private UnityEngine.UI.Image[] icons = new UnityEngine.UI.Image[0];

    private PlayerInventory inventory;

    /// <summary>소유자의 인벤토리를 연결하고 기존 아이템도 표시한다.</summary>
    public void Bind(PlayerInventory playerInventory)
    {
        Unbind();
        inventory = playerInventory;
        inventory.SlotChanged += RefreshSlot;

        for (int index = 0; index < icons.Length; index++)
            RefreshSlot(index);
    }

    /// <summary>변경된 칸만 기존 아이템 데이터의 아이콘으로 갱신한다.</summary>
    private void RefreshSlot(int index)
    {
        int itemId = index < inventory.Items.Count ? inventory.Items[index] : 0;
        ItemData data = conveyor.GetItemData(itemId);
        icons[index].sprite = data != null ? data.Icon : null;
        icons[index].enabled = data != null && data.Icon != null;
    }

    /// <summary>기존 구독을 해제하고 화면의 아이콘을 비운다.</summary>
    public void Unbind()
    {
        if (inventory != null)
            inventory.SlotChanged -= RefreshSlot;

        inventory = null;

        foreach (UnityEngine.UI.Image icon in icons)
        {
            if (icon != null)
                icon.enabled = false;
        }
    }

    /// <summary>씬 종료 시 인벤토리에 UI 구독이 남지 않도록 정리한다.</summary>
    private void OnDestroy()
    {
        Unbind();
    }
}
