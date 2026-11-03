using UnityEngine;
using Mirror;

/// <summary>
/// 자기 플레이어의 낙사 접촉을 전달하고 서버에서 부활 위치를 탐색한다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D), typeof(NetworkIdentity))]
public sealed class FallZone : NetworkBehaviour
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
    private bool _reportedMissingRespawn;

    /// <summary>
    /// SafeZone 레이어를 대상으로 트리거도 탐지하도록 필터를 준비한다.
    /// </summary>
    private void Awake()
    {
        _areaFilter.SetLayerMask(safeZoneLayer);
        _areaFilter.useTriggers = true;
    }

    /// <summary>
    /// 자기 플레이어가 낙사 영역에 진입하면 서버에 해당 영역을 전달한다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        NetworkIdentity player = other.GetComponentInParent<NetworkIdentity>();
        if (player != null && player.isLocalPlayer)
            CmdRequestFall();
    }

    /// <summary>
    /// 서버에서 요청자의 위치로 부활 지점을 찾고 같은 플레이어를 복귀시킨다.
    /// </summary>
    [Command(requiresAuthority = false)]
    private void CmdRequestFall(NetworkConnectionToClient sender = null)
    {
        ServerTryContact(sender);
    }

    [Server]
    public bool ServerTryContact(NetworkConnectionToClient sender)
    {
        if (NetworkServer.isLoadingScene || !isActiveAndEnabled || sender?.identity == null ||
            !sender.identity.TryGetComponent(out PlayerState player)) return false;
        var zone = GetComponent<BoxCollider2D>();
        var body = player.GetComponent<Collider2D>();
        if (!zone.enabled || body == null) return false;
        Bounds area = zone.bounds;
        area.Expand(0.5f);
        if (!area.Intersects(body.bounds)) return false;
        if (TryGetRespawnPosition(player.transform.position, out Vector2 position))
        {
            if (!player.TryDie()) return false;
            player.Respawn(position);
            return true;
        }
        if (!_reportedMissingRespawn)
        {
            _reportedMissingRespawn = true;
            Debug.LogError("부활 가능한 SafeZone과 예비 부활 위치가 없습니다.", this);
        }
        return false;
    }

    private void Update()
    {
        if (!isServer) return;
        foreach (var identity in NetworkServer.spawned.Values)
            if (identity != null && identity.GetComponent<PlayerState>() != null)
                ServerTryContact(identity.connectionToClient);
    }

    /// <summary>
    /// 지정 위치 뒤쪽의 부활 위치를 찾는다. 함정 적중도 같은 SafeZone 규칙을 사용한다.
    /// 탐색에 실패하면 예비 부활 위치를 쓰고, 그것도 없으면 false를 반환한다.
    /// </summary>
    [Server]
    public bool TryGetRespawnPosition(Vector2 from, out Vector2 position)
    {
        if (TryFindRespawnPosition(from, out position))
            return true;
        if (respawnPoint != null)
        {
            position = respawnPoint.position;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 고정된 Y에서 뒤쪽 후보를 확인하고, SafeZone이 있으면 Ray 시작 위치를 반환한다.
    /// </summary>
    private bool TryFindRespawnPosition(Vector2 fallPosition, out Vector2 position)
    {
        position = default;
        if (safeZoneBounds == null)
            return false;

        // 첫 후보는 사망 위치에서 지정 거리만큼 왼쪽으로 떨어진 위치다.
        float firstX = fallPosition.x - backDistance;

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
