using UnityEngine;

/// <summary>기존 BoxCollider2D의 월드 경계에 잔디·흙 그림을 맞춘다. 충돌체는 수정하지 않는다.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
public sealed class WorldTerrainAppearance : MonoBehaviour
{
    [SerializeField] private Sprite grassSprite;
    [SerializeField] private Sprite soilSprite;
    [SerializeField] private Material terrainMaterial;
    [SerializeField, Min(0.05f)] private float grassSectionHeight = 2f;
    [SerializeField] private SpriteRenderer soilVisual;
    [SerializeField] private SpriteRenderer grassVisual;
    private BoxCollider2D shape;
    private Bounds previousBounds;
    private Matrix4x4 previousTransform;
    private float previousCapHeight;
    private bool placed;

    public void Configure(Sprite grass, Sprite soil, Material material)
    {
        grassSprite = grass;
        soilSprite = soil;
        terrainMaterial = material;
        placed = false;
        if (soilVisual == null) soilVisual = CreateVisual("Soil Visual", -11);
        if (grassVisual == null) grassVisual = CreateVisual("Grass Surface", -10);
        Refresh();
    }

    private SpriteRenderer CreateVisual(string name, int order)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        child.layer = gameObject.layer;
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = order;
        return renderer;
    }

    private void OnEnable() => Refresh();
    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        if (soilVisual == null || grassVisual == null || grassSprite == null || soilSprite == null)
            return;
        if (shape == null) shape = GetComponent<BoxCollider2D>();
        Bounds bounds = shape.bounds;
        if (bounds.size.x <= 0f || bounds.size.y <= 0f) return;
        float capHeight = Mathf.Min(grassSectionHeight, bounds.size.y);
        Matrix4x4 parentTransform = transform.localToWorldMatrix;
        if (placed && previousBounds == bounds && previousTransform == parentTransform &&
            previousCapHeight == capHeight && soilVisual.sprite == soilSprite && grassVisual.sprite == grassSprite &&
            soilVisual.sharedMaterial == terrainMaterial && grassVisual.sharedMaterial == terrainMaterial)
            return;
        Place(soilVisual, soilSprite, bounds.center, bounds.size);
        Place(grassVisual, grassSprite,
            new Vector3(bounds.center.x, bounds.max.y - capHeight * 0.5f, bounds.center.z),
            new Vector2(bounds.size.x, capHeight));
        previousBounds = bounds;
        previousTransform = parentTransform;
        previousCapHeight = capHeight;
        placed = true;
    }

    private void Place(SpriteRenderer renderer, Sprite sprite, Vector3 center, Vector2 size)
    {
        if (renderer.sprite != sprite) renderer.sprite = sprite;
        if (renderer.sharedMaterial != terrainMaterial) renderer.sharedMaterial = terrainMaterial;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        Transform visual = renderer.transform;
        // 회전된 벽도 위쪽에 잔디가 오도록 월드 기준으로 표시한다.
        visual.SetPositionAndRotation(center, Quaternion.identity);
        visual.localScale = Vector3.one;
        Vector3 scale = visual.lossyScale;
        visual.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), 1f);
        renderer.size = size;
    }
}
