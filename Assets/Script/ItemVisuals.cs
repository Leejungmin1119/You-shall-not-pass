using UnityEngine;

/// <summary>착지 예고와 폭발에 사용할 원형 표시를 공유한다.</summary>
public static class ItemVisuals
{
    private static Sprite circle;

    /// <summary>지름 1유닛의 원을 한 번 만들고 필요한 크기와 색으로 표시한다.</summary>
    public static SpriteRenderer CreateCircle(string name, Vector2 position, float radius, Color color, int order)
    {
        if (circle == null)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "ItemCircle", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(new Vector2(x - 31.5f, y - 31.5f).sqrMagnitude <= 1024f ? 255 : 0));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            circle.hideFlags = HideFlags.HideAndDontSave;
        }
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.localScale = Vector3.one * radius * 2f;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = circle;
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }
}
