using UnityEngine;
using UnityEngine.UI;

/// <summary>카드의 바깥 테두리 모양은 유지하고 색상만 CanvasRenderer에서 변경한다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TurnCardBorder : MaskableGraphic
{
    private Color tint = Color.white;

    /// <summary>색상 변화로 카드 메시를 재생성하지 않고 렌더러의 색상만 바꾼다.</summary>
    public void SetTint(Color value)
    {
        if (tint == value)
            return;

        tint = value;
        canvasRenderer.SetColor(tint);
    }

    /// <summary>뒷면에서 앞면으로 전환할 때에도 현재 플레이어 색상을 유지한다.</summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        canvasRenderer.SetColor(tint);
    }

    /// <summary>크기가 바뀔 때만 둥근 바깥 테두리의 고정된 링을 만든다.</summary>
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        const int cornerSegments = 6;
        const int pointCount = 4 * (cornerSegments + 1);
        Rect outer = rectTransform.rect;
        Rect inner = new Rect(outer.x + 4f, outer.y + 4f, outer.width - 8f, outer.height - 8f);

        for (int index = 0; index < pointCount; index++)
        {
            int corner = index / (cornerSegments + 1);
            float angle = (corner * 90f + index % (cornerSegments + 1) * 90f / cornerSegments) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            helper.AddVert(CornerCenter(outer, corner, 18f) + direction * 18f, Color.white, Vector2.zero);
            helper.AddVert(CornerCenter(inner, corner, 15f) + direction * 15f, Color.white, Vector2.zero);
        }

        for (int index = 0; index < pointCount; index++)
        {
            int current = index * 2;
            int next = (index + 1) % pointCount * 2;
            helper.AddTriangle(current, next, current + 1);
            helper.AddTriangle(current + 1, next, next + 1);
        }
    }

    /// <summary>각 모서리 원호의 중심을 구한다.</summary>
    private static Vector2 CornerCenter(Rect rect, int corner, float radius)
    {
        return new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
            corner < 2 ? rect.yMax - radius : rect.yMin + radius);
    }
}
