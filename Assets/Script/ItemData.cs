using UnityEngine;

public enum ItemPlacement { Drop, AtCursor }

/// <summary>
/// 판매와 사용에 공유하는 고정 설정. 보유 상태와 남은 수명은 저장하지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "Game/Item Data")]
public sealed class ItemData : ScriptableObject
{
    [Min(1)] public int Id = 1;
    public string DisplayName;
    public Sprite Icon;
    [Tooltip("손에 들었을 때 커서 아이콘의 표시 배율입니다.")]
    [Min(0.1f)] public float HeldIconScale = 1f;
    public ItemBase Prefab;
    [Min(1)] public int Price = 20;
    [Min(0)] public int SpawnWeight = 15;
    public ItemPlacement Placement = ItemPlacement.Drop;
    [Range(1, 3)] public int Count = 1;
    [Min(0f)] public float Spacing = 1f;
    [Min(0.05f)] public float FallTime = 0.6f;
    [Min(0.05f)] public float Lifetime = 2f;
}