using UnityEngine;

/// <summary>시작 보호 구역을 반투명 파란 원으로 표시한다. 실제 사용 제한은 서버가 판단한다.</summary>
public sealed class StartProtectionZone : MonoBehaviour
{
    private const int Segments = 64;
    private RaceManager race;
    private GameObject visual;
    private Mesh mesh;
    private Material material;

    /// <summary>원을 한 번만 만들고, 매 프레임에는 위치와 표시 여부만 갱신한다.</summary>
    private void Awake()
    {
        race = GetComponent<RaceManager>();
        visual = new GameObject("StartProtectionArea");
        visual.transform.SetParent(transform, false);
        visual.SetActive(false);

        material = new Material(Shader.Find("Sprites/Default"));
        mesh = CreateCircle();
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer fill = visual.AddComponent<MeshRenderer>();
        fill.sharedMaterial = material;
        fill.sortingOrder = 100;

        LineRenderer border = visual.AddComponent<LineRenderer>();
        border.sharedMaterial = material;
        border.useWorldSpace = false;
        border.loop = true;
        border.widthMultiplier = 0.05f;
        border.startColor = border.endColor = new Color(0.15f, 0.65f, 1f, 0.85f);
        border.sortingOrder = 101;
        border.positionCount = Segments;

        for (int index = 0; index < Segments; index++)
            border.SetPosition(index, GetEdge(index));
    }

    /// <summary>서버와 같은 종료 시각을 사용해 출발 지점에 파란 범위를 고정 표시한다.</summary>
    private void LateUpdate()
    {
        bool visible = race.IsStartProtectionActive;

        if (visual.activeSelf != visible)
            visual.SetActive(visible);

        if (visible)
            visual.transform.position = new Vector3(race.StartProtectionCenter.x, race.StartProtectionCenter.y, 0f);
    }

    /// <summary>중심과 원 둘레를 삼각형으로 연결해 반투명한 파란 면을 만든다.</summary>
    private Mesh CreateCircle()
    {
        var vertices = new Vector3[Segments + 1];
        var colors = new Color[vertices.Length];
        var triangles = new int[Segments * 3];

        for (int index = 0; index < vertices.Length; index++)
            colors[index] = new Color(0.15f, 0.65f, 1f, 0.2f);

        for (int index = 0; index < Segments; index++)
        {
            vertices[index + 1] = GetEdge(index);
            triangles[index * 3] = 0;
            triangles[index * 3 + 1] = (index + 1) % Segments + 1;
            triangles[index * 3 + 2] = index + 1;
        }

        var circle = new Mesh { name = "StartProtectionCircle", vertices = vertices, colors = colors, triangles = triangles };
        circle.RecalculateBounds();
        return circle;
    }

    /// <summary>사용 제한 판정과 같은 반경으로 원 둘레의 좌표를 구한다.</summary>
    private static Vector3 GetEdge(int index)
    {
        float angle = index * Mathf.PI * 2f / Segments;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * RaceManager.StartProtectionRadius;
    }

    /// <summary>씬 종료 시 실행 중에 만든 메시와 재질을 해제한다.</summary>
    private void OnDestroy()
    {
        if (visual != null)
            Destroy(visual);

        if (mesh != null)
            Destroy(mesh);

        if (material != null)
            Destroy(material);
    }
}
