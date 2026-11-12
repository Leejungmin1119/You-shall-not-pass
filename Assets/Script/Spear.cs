using Mirror;
using UnityEngine;

/// <summary>
/// 확정된 착지점까지 곧게 낙하하고, 꽂힌 뒤에는 피격 판정 없이 투명해진다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Spear : ItemBase
{
    [Tooltip("투명한 여백을 제외한 창의 로컬 판정 크기입니다. 아래쪽 끝을 기준으로 합니다.")]
    [SerializeField] private Vector2 hitSize = new Vector2(0.25f, 0.86f);
    private SpriteRenderer visual;
    private SpriteRenderer warning;
    private Vector2 startPosition;
    private float groundY;
    private bool hasGround;
    private float speed;
    private float age;
    private float stuckAge;
    private bool stuck;
    private bool hitSoundPlayed;
    public bool IsStuck => stuck;

    /// <summary>
    /// 이동 설정과 착지 예고를 준비하고 수신 지연만큼 표시를 앞당긴다.
    /// </summary>
    public override void Initialize(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        base.Initialize(data, spawn, isJudge, initialAge);
        visual = GetComponent<SpriteRenderer>();
        startPosition = spawn.Position;
        groundY = spawn.GroundY;
        hasGround = spawn.HasGround;
        speed = spawn.FallSpeed;
        hitSoundPlayed = false;
        if (hasGround)
            warning = CreateWarning(visual, new Vector2(startPosition.x, groundY));

        RequestSound(SoundType.ArrowStart, spawn.Position);
        Advance(initialAge);
    }

    /// <summary>
    /// 낙하와 수명 표시를 진행한다. 실제 적중은 서버에서만 확인한다.
    /// </summary>
    private void Update()
    {
        Advance(Time.deltaTime);
    }

    /// <summary>
    /// 프레임 사이 이동 구간도 검사하여 빠른 낙하가 캐릭터를 통과하지 않게 한다.
    /// </summary>
    private void Advance(float deltaTime)
    {
        if (visual == null) return;
        bool wasFalling = !stuck;
        float previousY = transform.position.y;
        age += deltaTime;
        float nextY = startPosition.y - speed * age;
        if (hasGround && nextY <= groundY)
        {
            nextY = groundY;

            if (!stuck)
                PlayHitSound(new Vector2(startPosition.x, groundY));

            stuck = true;
            stuckAge = Mathf.Max(0f, age - (startPosition.y - groundY) / speed);
            if (warning != null) Destroy(warning.gameObject);
        }
        transform.position = new Vector3(startPosition.x, nextY, transform.position.z);
        if (stuck)
        {
            Color color = visual.color;
            color.a = 1f - Mathf.Clamp01(stuckAge / Data.Lifetime);
            visual.color = color;
            if (stuckAge >= Data.Lifetime) { Destroy(gameObject); return; }
        }
        else if (age > Data.FallTime + 5f)
        {
            Destroy(gameObject);
            return;
        }
        // 착지한 프레임의 마지막 낙하 구간까지는 검사하되, 이후 박힌 화살에는 맞지 않는다.
        if (wasFalling)
            CheckPlayerHits(previousY);
    }

    /// <summary>서버가 승인한 플레이어 적중도 충돌음으로 처리한다. 이후 지면 접촉과 중복하지 않는다.</summary>
    public override void ConfirmHit(PlayerState player)
    {
        base.ConfirmHit(player);
        PlayHitSound(transform.position);
    }

    /// <summary>화살 하나의 최초 충돌에서만 한 번 재생을 요청한다.</summary>
    private void PlayHitSound(Vector2 position)
    {
        if (hitSoundPlayed)
            return;

        hitSoundPlayed = true;
        RequestSound(SoundType.ArrowHit, position);
    }

    /// <summary>
    /// 원격 캐릭터는 물리가 꺼져 있으므로 콜라이더 Bounds를 직접 비교한다.
    /// </summary>
    private void CheckPlayerHits(float previousY)
    {
        if (!IsJudge || !NetworkServer.active) return;
        // 64픽셀 이미지 전체에는 투명 여백이 있으므로 SpriteRenderer.bounds를 판정 폭으로 쓰지 않는다.
        Vector3 scale = transform.lossyScale;
        float halfWidth = hitSize.x * Mathf.Abs(scale.x) * 0.5f;
        float bottom = transform.position.y;
        float top = Mathf.Max(previousY, bottom) + hitSize.y * Mathf.Abs(scale.y);
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !identity.TryGetComponent(out PlayerState player) || !CanHit(player))
                continue;
            Collider2D collider = player.GetComponent<Collider2D>();
            if (collider == null) continue;
            Bounds body = collider.bounds;
            if (body.min.x < transform.position.x + halfWidth && body.max.x > transform.position.x - halfWidth &&
                body.min.y < top && body.max.y > bottom)
                ReportHit(player);
        }
    }

    /// <summary>보유 중에도 실제 창과 동일한 착지 예고를 만든다.</summary>
    public override SpriteRenderer CreatePlacementPreview(Vector2 position)
    {
        return CreateWarning(GetComponent<SpriteRenderer>(), position);
    }

    /// <summary>
    /// 착지할 위치에 창 모양의 붉은 반투명 예고를 만든다.
    /// </summary>
    public static SpriteRenderer CreateWarning(SpriteRenderer source, Vector2 position)
    {
        var marker = new GameObject("SpearWarning");
        marker.transform.position = position;
        marker.transform.localScale = source.transform.lossyScale;
        var renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = source.sprite;
        renderer.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder - 1;
        return renderer;
    }

    /// <summary>
    /// 씬 종료나 수명 만료 시 남아 있는 예고 표시도 정리한다.
    /// </summary>
    protected override void OnDestroy()
    {
        if (warning != null) Destroy(warning.gameObject);
        base.OnDestroy();
    }
}
