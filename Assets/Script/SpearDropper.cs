using UnityEngine;

/// <summary>
/// 서버가 확정한 생성 정보를 프리팹에 적용한다. 입력이나 구매는 처리하지 않는다.
/// </summary>
public class SpearDropper : MonoBehaviour
{
    public LayerMask GroundLayer;

    /// <summary>
    /// 아이템별 프리팹을 생성하고 공통 초기화를 호출한다.
    /// </summary>
    public ItemBase Spawn(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        ItemBase item = Instantiate(data.Prefab, spawn.Position, Quaternion.identity);
        item.name = $"{data.DisplayName}_{spawn.InstanceId}";
        item.Initialize(data, spawn, isJudge, initialAge);
        return item;
    }
}