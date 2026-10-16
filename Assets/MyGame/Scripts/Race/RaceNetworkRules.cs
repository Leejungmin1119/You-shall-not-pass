using System;
using UnityEngine;

public enum RacePhase : byte { Waiting, RoundReady, Racing, FinishCountdown, RoundEnd, MatchEnd }
public enum RaceEndReason : byte { None, ScoreLimit, TimeLimit, PlayerDisconnected }
public enum RaceRequestResult : byte
{
    Success, NotRacing, StaleRound, Unavailable, NotEnoughCost, InventoryFull,
    HoldingItem, InvalidSlot, EmptySlot, Cooldown, InvalidPosition, ProtectedArea, UnsupportedItem
}

[Serializable]
public sealed class RaceItemDefinition
{
    public int id;
    public int price;
    public int weight;
    public int spearCount;
    public float lifetime = 6f;
    public bool enabled = true;
}

/// <summary>기획서의 제안 수치를 에셋에서 조정한다. 미구현 함정은 구매 목록에서 제외한다.</summary>
[CreateAssetMenu(menuName = "Network/Race Rules")]
public sealed class RaceNetworkRules : ScriptableObject
{
    [Min(1)] public int winScore = 20;
    [Min(1)] public float matchSeconds = 900f;
    [Min(0.1f)] public float readySeconds = 7f;
    [Min(0.1f)] public float finishSeconds = 20f;
    [Min(0.1f)] public float roundEndSeconds = 0.25f;
    public int[] finishScores = { 4, 3, 2, 1 };
    [Min(0)] public int hitScore = 1;
    [Min(0)] public int startCost = 100;
    [Min(1)] public int costMax = 300;
    [Min(0.1f)] public float costTickSeconds = 5f;
    [Min(1)] public int costTickAmount = 10;
    [Range(1, 10)] public int inventorySlots = 3;
    [Min(0)] public float useCooldown = 0.5f;
    [Tooltip("미정 수치의 시험용 기본값. 팀 확정 후 에셋에서 조정합니다.")]
    [Min(0)] public float buyCooldown = 0.25f;
    [Min(0)] public float invincibleSeconds = 0.5f;
    [Tooltip("미정 수치의 시험용 기본값. 기존 낙사 탐색 설정을 함께 사용합니다.")]
    [Min(0)] public float hitBackDistance = 10f;
    [Min(0)] public float protectedRadius = 1.5f;
    [Min(0.1f)] public float conveyorSpawnSeconds = 2f;
    [Min(0.1f)] public float conveyorCrossSeconds = 8f;
    public RaceItemDefinition[] items =
    {
        new RaceItemDefinition { id = 1, price = 30, weight = 25, enabled = false },
        new RaceItemDefinition { id = 2, price = 20, weight = 15, spearCount = 1 },
        new RaceItemDefinition { id = 3, price = 35, weight = 10, spearCount = 2 },
        new RaceItemDefinition { id = 4, price = 50, weight = 5, spearCount = 3 },
        new RaceItemDefinition { id = 5, price = 50, weight = 15, enabled = false }
    };

    public RaceItemDefinition FindItem(int id)
        => Array.Find(items, item => item != null && item.id == id && item.enabled);

    public static bool IsFinite(Vector2 point)
        => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
           !float.IsNaN(point.y) && !float.IsInfinity(point.y);
}
