using UnityEngine;

/// <summary>판의 둥근 테두리 바깥에 남은 사각형 바탕을 그리지 않는 메뉴 이미지.</summary>
public sealed class GameMenuBoardImage : UnityEngine.UI.RawImage
{
    /// <summary>이미지는 유지하고 바깥 모서리만 둥근 메시로 잘라 표시한다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
    {
        helper.Clear();
        Rect area = GetPixelAdjustedRect();

        if (area.width <= 0f || area.height <= 0f)
            return;

        const int segments = 12;
        const int count = 4 * (segments + 1);
        float radiusX = area.width * 0.065f;
        float radiusY = area.height * 0.05f;
        AddPoint(helper, area.center, area);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(
                corner == 0 || corner == 3 ? area.xMax - radiusX : area.xMin + radiusX,
                corner < 2 ? area.yMax - radiusY : area.yMin + radiusY);

            for (int step = 0; step <= segments; step++)
            {
                float angle = (corner * 90f + step * 90f / segments) * Mathf.Deg2Rad;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                AddPoint(helper, point, area);
            }
        }

        for (int index = 0; index < count; index++)
            helper.AddTriangle(0, index + 1, (index + 1) % count + 1);
    }

    /// <summary>잘라낸 경계에서도 원본 이미지의 같은 위치를 읽도록 UV를 맞춘다.</summary>
    private void AddPoint(UnityEngine.UI.VertexHelper helper, Vector2 point, Rect area)
    {
        Vector2 uv = new Vector2(
            uvRect.x + (point.x - area.xMin) / area.width * uvRect.width,
            uvRect.y + (point.y - area.yMin) / area.height * uvRect.height);
        helper.AddVert(point, color, uv);
    }
}
