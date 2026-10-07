using UnityEngine;

/// <summary>
/// 낙사 시 뒤쪽 SafeZone을 확인하고, 지정한 높이에서 즉시 부활을 요청한다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public sealed class FallZone : MonoBehaviour
{
    [Header("부활 영역")]
    [SerializeField] private Transform respawnPoint; // 탐색 실패 시 사용할 예비 위치
    [Tooltip("자식 SafeZone 콜라이더들을 담은 부모입니다.")]
    [SerializeField] private BoxCollider2D safeZoneBounds;
    [SerializeField] private LayerMask safeZoneLayer; // 자식 영역만 포함하고 부모는 제외

    [Header("부활 위치와 탐색")]
    [SerializeField] private float respawnY = 0f;
    [SerializeField, Min(0.1f)] private float rayDistance = 5f;
    [SerializeField, Min(0f)] private float backDistance = 10f;
    [SerializeField, Min(0.1f)] private float searchInterval = 1f;
    [SerializeField, Min(1)] private int maxSearchAttempts = 30;

    // 낙사할 때마다 배열을 생성하지 않고 첫 번째 탐색 결과를 재사용한다.
    private readonly RaycastHit2D[] _areaHit = new RaycastHit2D[1];
    private ContactFilter2D _areaFilter;

    /// <summary>
    /// SafeZone 레이어를 대상으로 트리거도 탐지하도록 필터를 준비한다.
    /// </summary>
    private void Awake()
    {
        _areaFilter.SetLayerMask(safeZoneLayer);
        _areaFilter.useTriggers = true;
    }

    /// <summary>
    /// 낙사한 플레이어에게 탐색한 위치 또는 예비 위치로 즉시 부활을 요청한다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어의 자식 콜라이더가 진입한 경우에도 같은 플레이어를 찾는다.
        PlayerState player = other.GetComponentInParent<PlayerState>();
        if (player == null)
            return;

        if (TryFindRespawnPosition(player, out Vector2 position))
            player.TryDie(position);
        else if (respawnPoint != null)
            player.TryDie((Vector2)respawnPoint.position);
        else
            Debug.LogError("부활 가능한 SafeZone과 예비 부활 위치가 없습니다.", this);
    }

    /// <summary>
    /// 고정된 Y에서 뒤쪽 후보를 확인하고, SafeZone이 있으면 Ray 시작 위치를 반환한다.
    /// </summary>
    private bool TryFindRespawnPosition(PlayerState player, out Vector2 position)
    {
        position = default;
        if (safeZoneBounds == null)
            return false;

        // 첫 후보는 사망 위치에서 지정 거리만큼 왼쪽으로 떨어진 위치다.
        float firstX = player.transform.position.x - backDistance;

        for (int attempt = 0; attempt < maxSearchAttempts; attempt++)
        {
            // 실패할 때마다 X만 더 왼쪽으로 옮긴다. Y는 Inspector 값을 유지한다.
            Vector2 candidate = new Vector2(firstX - searchInterval * attempt, respawnY);

            int count = Physics2D.Raycast(candidate, Vector2.down,
                _areaFilter, _areaHit, rayDistance);

            if (count == 0)
                continue;

            // 지정한 부모 아래의 자식 영역만 인정하며 부모 콜라이더 자체는 제외한다.
            Collider2D hit = _areaHit[0].collider;
            if (hit == safeZoneBounds || !hit.transform.IsChildOf(safeZoneBounds.transform))
                continue;

            // 충돌 지점이 아니라 Ray를 쏜 위치에서 부활한다. 지면/발/경사는 계산하지 않는다.
            position = candidate;
            return true;
        }

        return false;
    }
}
