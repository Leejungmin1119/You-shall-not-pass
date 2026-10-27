using UnityEngine;

/// <summary>하단 중앙에 자기 인벤토리와 잔액을 표시한다.</summary>
public sealed class PlayerInventoryUI : MonoBehaviour
{
    [SerializeField] private ConveyorController conveyor;
    [SerializeField] private UnityEngine.UI.Image[] icons = new UnityEngine.UI.Image[0];
    [SerializeField] private TMPro.TMP_Text costText;

    private PlayerInventory inventory;
    private PlayerCost cost;
    private UnityEngine.UI.Outline[] borders;
    private Color[] borderColors;

    /// <summary>기존 슬롯 테두리를 보관해 선택 변경 때만 색상을 갱신한다.</summary>
    private void Awake()
    {
        borders = new UnityEngine.UI.Outline[icons.Length];
        borderColors = new Color[icons.Length];

        for (int index = 0; index < icons.Length; index++)
        {
            if (icons[index] == null)
                continue;

            borders[index] = icons[index].transform.parent.GetComponent<UnityEngine.UI.Outline>();

            if (borders[index] != null)
                borderColors[index] = borders[index].effectColor;
        }
    }

    /// <summary>소유자의 인벤토리를 연결하고 기존 아이템도 표시한다.</summary>
    public void Bind(PlayerInventory playerInventory)
    {
        Unbind();
        inventory = playerInventory;
        inventory.SlotChanged += RefreshSlot;
        inventory.SelectionChanged += RefreshSelection;

        cost = playerInventory.GetComponent<PlayerCost>();

        if (cost != null)
        {
            cost.CostChanged += RefreshCost;
            RefreshCost(cost.CurrentCost);
        }

        for (int index = 0; index < icons.Length; index++)
            RefreshSlot(index);
    }

    /// <summary>자기 잔액이 바뀔 때만 하단의 코스트 숫자를 갱신한다.</summary>
    private void RefreshCost(int value)
    {
        if (costText != null)
            costText.SetText("{0}", value);
    }

    /// <summary>손에 든 아이템의 원래 슬롯을 강조하고 예약된 아이콘은 흐리게 표시한다.</summary>
    private void RefreshSelection()
    {
        for (int index = 0; index < icons.Length; index++)
        {
            bool selected = inventory != null && inventory.SelectedSlot == index;

            if (icons[index] != null)
                icons[index].color = new Color(1f, 1f, 1f, selected ? 0.35f : 1f);

            if (borders[index] != null)
                borders[index].effectColor = selected ? new Color(1f, 0.8f, 0.2f) : borderColors[index];
        }
    }

    /// <summary>변경된 칸만 기존 아이템 데이터의 아이콘으로 갱신한다.</summary>
    private void RefreshSlot(int index)
    {
        if (index < 0 || index >= icons.Length || icons[index] == null)
            return;

        int itemId = index < inventory.Items.Count ? inventory.Items[index] : 0;
        ItemData data = conveyor.GetItemData(itemId);
        icons[index].sprite = data != null ? data.Icon : null;
        icons[index].enabled = data != null && data.Icon != null;
        RefreshSelection();
    }

    /// <summary>기존 구독을 해제하고 화면의 아이콘을 비운다.</summary>
    public void Unbind()
    {
        if (cost != null)
            cost.CostChanged -= RefreshCost;

        cost = null;

        if (costText != null)
            costText.SetText("—");

        if (inventory != null)
        {
            inventory.SlotChanged -= RefreshSlot;
            inventory.SelectionChanged -= RefreshSelection;
        }

        inventory = null;
        RefreshSelection();

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
