using UnityEngine;

/// <summary>얇은 바깥 테두리와 흰 띠를 가진 작은 원형 볼륨 손잡이.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class VolumeSliderHandle : UnityEngine.UI.MaskableGraphic
{
    /// <summary>고정된 원형 메시로 손잡이를 그리며 색상 변화는 기존 UI 전환에 맡긴다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
    {
        helper.Clear();
        Rect area = GetPixelAdjustedRect();
        float radius = Mathf.Min(area.width, area.height) * 0.5f;

        if (radius <= 0f)
            return;

        AddCircle(helper, area.center, radius, new Color(0.52f, 0.43f, 0.54f));
        AddCircle(helper, area.center, radius - 1f, Color.white);
        AddCircle(helper, area.center, radius - 2.5f, color);
    }

    /// <summary>중심과 바깥 정점을 삼각형으로 연결해 작은 원을 만든다.</summary>
    private static void AddCircle(UnityEngine.UI.VertexHelper helper, Vector2 center, float radius, Color tint)
    {
        if (radius <= 0f)
            return;

        const int segments = 32;
        int first = helper.currentVertCount;
        helper.AddVert(center, tint, Vector2.zero);

        for (int index = 0; index < segments; index++)
        {
            float angle = index * Mathf.PI * 2f / segments;
            helper.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }

        for (int index = 0; index < segments; index++)
            helper.AddTriangle(first, first + index + 1, first + (index + 1) % segments + 1);
    }
}
