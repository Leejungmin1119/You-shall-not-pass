using UnityEngine;

/// <summary>
/// 컨베이어 UI의 아이템 생성 주기와 획득 요청을 관리한다.
/// </summary>
public class ConveyorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ConveyorItem itemPrefab;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform spawnPoint;

    [Header("Settings")]
    [SerializeField, Min(0.01f)] private float spawnInterval = 2f;
    [SerializeField, Min(0.01f)] private float moveSpeed = 80f;

    private float _spawnTimer;

    /// <summary>
    /// 필수 참조와 설정을 확인한 뒤 첫 번째 아이템을 즉시 생성한다.
    /// </summary>
    private void Start()
    {
        SpawnItem();
    }

    /// <summary>
    /// 생성 타이머를 갱신하고 설정한 간격마다 새 아이템을 생성한다.
    /// </summary>
    private void Update()
    {
        _spawnTimer += Time.deltaTime;

        while (_spawnTimer >= spawnInterval)
        {
            _spawnTimer -= spawnInterval;
            SpawnItem();
        }
    }

    /// <summary>
    /// 프리팹을 Viewport 자식으로 생성하고 SpawnPoint 위치에서 이동을 시작시킨다.
    /// </summary>
    private void SpawnItem()
    {
        ConveyorItem item = Instantiate(itemPrefab, viewport, false);
        RectTransform itemRectTransform = (RectTransform)item.transform;

        itemRectTransform.anchoredPosition = spawnPoint.anchoredPosition;
        itemRectTransform.localRotation = Quaternion.identity;
        itemRectTransform.localScale = Vector3.one;
        itemRectTransform.SetAsLastSibling();

        item.Initialize(viewport, moveSpeed);
    }
}
