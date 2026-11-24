using UnityEngine;
using UnityEngine.UI;

/// <summary>공개한 카드의 앞면을 그을림 없는 크림색 종이와 보라색 테두리로 그린다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TurnCardFace : MaskableGraphic
{
    private Color selectionColor = Color.white;

    /// <summary>확정된 소유자의 색상으로 가장 바깥 선택 테두리만 갱신한다.</summary>
    public void SetSelectionColor(Color value)
    {
        if (selectionColor == value)
            return;

        selectionColor = value;
        SetVerticesDirty();
    }

    /// <summary>이미지나 소각 재질을 사용하지 않고 깨끗한 카드 앞면을 만든다.</summary>
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect area = rectTransform.rect;
        AddRoundedRectangle(helper, area, 18f, selectionColor);
        AddRoundedRectangle(helper, Inset(area, 4f), 15f, new Color(0.08f, 0.035f, 0.12f));
        AddRoundedRectangle(helper, Inset(area, 7f), 13f, new Color(0.56f, 0.36f, 0.68f));
        AddRoundedRectangle(helper, Inset(area, 15f), 9f, new Color(1f, 237f / 255f, 181f / 255f));
    }

    /// <summary>중심은 유지하면서 안쪽 테두리의 사각형을 구한다.</summary>
    private static Rect Inset(Rect rect, float amount)
    {
        return new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);
    }

    /// <summary>둥근 네 모서리를 가진 색면을 삼각형으로 연결한다.</summary>
    private static void AddRoundedRectangle(VertexHelper helper, Rect rect, float radius, Color tint)
    {
        const int cornerSegments = 6;
        const int count = 4 * (cornerSegments + 1);
        int first = helper.currentVertCount;
        helper.AddVert(rect.center, tint, Vector2.zero);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                corner < 2 ? rect.yMax - radius : rect.yMin + radius);

            for (int step = 0; step <= cornerSegments; step++)
            {
                float angle = (corner * 90f + step * 90f / cornerSegments) * Mathf.Deg2Rad;
                helper.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            }
        }

        for (int index = 0; index < count; index++)
            helper.AddTriangle(first, first + index + 1, first + (index + 1) % count + 1);
    }
}
