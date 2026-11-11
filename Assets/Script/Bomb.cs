using Mirror;
using UnityEngine;

/// <summary>중력으로 떨어져 접촉 시 폭발한다. 지면 폭발만 범위 피해를 준다.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Bomb : ItemBase
{
    [SerializeField, Min(0.01f)] private float bodyRadius = 0.3f;
    [SerializeField, Min(0.01f)] private float explosionRadius = 1.5f;
    [SerializeField, Min(0.01f)] private float smokeDuration = 0.4f;
    [SerializeField] private float spinSpeed = 60f;
    private SpriteRenderer visual;
    private SpriteRenderer warning;
    private SpriteRenderer spark;
    private SpriteRenderer explosion;
    private Vector2 start;
    private float groundY;
    private float acceleration;
    private float age;
    private float explosionAge;
    private bool hasGround;
    private bool exploded;
    private bool groundExplosion;
    public bool HasExploded => exploded;
    public bool IsGroundExplosion => groundExplosion;

    /// <summary>약 1초의 가속 낙하와 최초 지면의 폭발 예고를 준비한다.</summary>
    public override void Initialize(ItemData data, ItemSpawnMessage spawn, bool isJudge, float initialAge)
    {
        base.Initialize(data, spawn, isJudge, initialAge);
        visual = GetComponent<SpriteRenderer>();
        start = spawn.Position;
        groundY = spawn.GroundY;
        hasGround = spawn.HasGround;
        float distance = hasGround ? Mathf.Max(0.01f, start.y - groundY - bodyRadius) : spawn.FallSpeed * data.FallTime;
        acceleration = 2f * distance / (data.FallTime * data.FallTime);
        if (hasGround) warning = CreatePlacementPreview(new Vector2(start.x, groundY));
        spark = ItemVisuals.CreateCircle("BombFuse", start, 0.045f, Color.yellow, visual.sortingOrder + 1);
        spark.transform.SetParent(transform, false);
        spark.transform.localPosition = new Vector3(0.1f, 0.42f, 0f);
        age = initialAge;
    }

    /// <summary>서버는 접촉을 검사하고 클라이언트는 같은 가속식으로 낙하를 표시한다.</summary>
    private void Update()
    {
        if (visual == null) return;
        if (exploded) { UpdateExplosion(); return; }
        age += Time.deltaTime;
        Vector2 previous = transform.position;
        float y = start.y - 0.5f * acceleration * age * age;
        bool landed = hasGround && y <= groundY + bodyRadius;
        if (landed) y = groundY + bodyRadius;
        Vector2 next = new Vector2(start.x, y);
        transform.position = next;
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        if (spark != null) spark.color = Color.Lerp(Color.yellow, Color.red, Mathf.PingPong(age * 8f, 1f));
        if (warning != null) warning.color = new Color(1f, 0.15f, 0.1f, Mathf.Lerp(0.15f, 0.6f, Mathf.Clamp01(age / Data.FallTime)));
        if (!IsJudge) return;
        // 프레임 사이 몸통 접촉을 먼저 판정하여 지면의 범위 폭발과 구분한다.
        if (FindPlayerContact(previous, next, bodyRadius, out PlayerState target, out Vector2 contact))
        {
            transform.position = contact;
            ReportHit(target);
            Explode(false);
        }
        else if (landed) Explode(true);
        else if (age >= Data.FallTime + 5f) Explode(false);
    }

    /// <summary>서버에서 폭발 형태와 위치를 확정한 뒤 모두에게 전달한다.</summary>
    private void Explode(bool onGround)
    {
        if (exploded) return;
        if (onGround) transform.position = new Vector2(start.x, groundY);
        BeginExplosion(onGround);
        RequestSound(SoundType.BombExplosion, transform.position);
        PublishState(true, onGround);
        if (onGround) CheckExplosionHits();
    }

    /// <summary>서버가 알려 준 폭발을 해당 위치에서 한 번만 재생한다.</summary>
    public override void ApplyState(ItemStateMessage message)
    {
        if (!message.Exploded || exploded) return;
        transform.position = message.Position;
        BeginExplosion(message.GroundExplosion);
    }

    /// <summary>몸통과 예고를 숨기고 실제 판정과 같은 크기의 폭발을 표시한다.</summary>
    private void BeginExplosion(bool onGround)
    {
        exploded = true;
        groundExplosion = onGround;
        visual.enabled = false;
        if (spark != null) Destroy(spark.gameObject);
        if (warning != null) Destroy(warning.gameObject);
        explosion = ItemVisuals.CreateCircle("BombExplosion", transform.position,
            onGround ? explosionRadius : bodyRadius, new Color(1f, 0.5f, 0.1f, 0.8f), visual.sortingOrder + 1);
    }

    /// <summary>지면 폭발의 피해를 0.2초 유지하고 약 0.4초 동안 연기를 지운다.</summary>
    private void UpdateExplosion()
    {
        explosionAge += Time.deltaTime;
        if (IsJudge && groundExplosion && explosionAge <= Data.Lifetime) CheckExplosionHits();
        float fade = Mathf.Clamp01(explosionAge / smokeDuration);
        if (explosion != null) explosion.color = Color.Lerp(new Color(1f, 0.5f, 0.1f, 0.8f), new Color(0.4f, 0.4f, 0.4f, 0f), fade);
        if (explosionAge >= Mathf.Max(smokeDuration, Data.Lifetime)) Destroy(gameObject);
    }

    /// <summary>원과 플레이어 몸체의 최단 거리로 범위 피해를 검사한다.</summary>
    private void CheckExplosionHits()
    {
        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            if (identity == null || !identity.TryGetComponent(out PlayerState player) || !CanHit(player)) continue;
            Collider2D body = player.GetComponent<Collider2D>();
            if (body != null && body.bounds.SqrDistance(transform.position) <= explosionRadius * explosionRadius)
                ReportHit(player);
        }
    }

    /// <summary>컨베이어 구매 후와 실제 낙하 중에 동일한 반경의 예고를 사용한다.</summary>
    public override SpriteRenderer CreatePlacementPreview(Vector2 position)
    {
        return ItemVisuals.CreateCircle("BombWarning", position, explosionRadius, new Color(1f, 0.15f, 0.1f, 0.2f), 9);
    }

    /// <summary>씬 종료 또는 수명 만료 시 별도로 만든 표시도 정리한다.</summary>
    protected override void OnDestroy()
    {
        if (warning != null) Destroy(warning.gameObject);
        if (explosion != null) Destroy(explosion.gameObject);
        base.OnDestroy();
    }
}
