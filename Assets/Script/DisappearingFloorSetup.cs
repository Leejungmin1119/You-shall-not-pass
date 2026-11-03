#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>기존 대포 바닥을 복구하고 맵 끝에 별도의 사라지는 길과 양쪽 대기 바닥을 배치한다.</summary>
public static class DisappearingFloorSetup
{
    private const string SpritePath = "Assets/Image/DisappearingFloor_Lavender_v1.png";
    private const string SourcePath = "Temp/DisappearingFloor_Concept.png";
    private const string CorridorName = "Disappearing Floor Corridor";
    private const string SurfaceName = "Lavender Fading Surface";

    [MenuItem("Tools/맵/사라지는 바닥 전용 길 추가")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Play를 종료한 뒤 적용하세요.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new System.InvalidOperationException("SampleScene을 열고 적용하세요.");

        GameObject cannonFloor = GameObject.Find("Cannon Corridor/Long Flat Corridor");
        GameObject firebarFloor = GameObject.Find("Rotating Fireball Corridor/Firebar Long Flat Floor");
        if (cannonFloor == null || firebarFloor == null ||
            !firebarFloor.TryGetComponent(out BoxCollider2D precedingFloor))
            throw new System.InvalidOperationException("대포 바닥 또는 회전 불 구간의 마지막 바닥을 찾을 수 없습니다.");
        RaceManager race = Object.FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        if (race == null)
            throw new System.InvalidOperationException("공통 시작 시각을 제공할 RaceManager가 없습니다.");
        Transform safeZones = RequireObject("SafeZones").transform;
        GameObject goal = RequireObject("GoalZone");
        GameObject goalView = RequireObject("RaceStartPresentation/GoalView");
        BoxCollider2D mapBounds = RequireObject("MapViewBounds").GetComponent<BoxCollider2D>();
        BoxCollider2D fallBounds = RequireObject("FallZone").GetComponent<BoxCollider2D>();
        if (mapBounds == null || fallBounds == null)
            throw new System.InvalidOperationException("카메라 또는 낙사 영역의 BoxCollider2D가 없습니다.");
        Sprite grass = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/GrassSoil_GrassTile_v1.png");
        Sprite soil = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/GrassSoil_DirtTile_v1.png");
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/PastelLandscape_Unlit.mat");
        if (grass == null || soil == null || material == null)
            throw new System.InvalidOperationException("대기 바닥에 사용할 잔디·흙 그림 또는 머티리얼이 없습니다.");

        Sprite sprite = PrepareSprite();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("사라지는 바닥 전용 길 추가");
        RestoreCannonFloor(cannonFloor);

        // 대포·회전 불 구간의 길이를 바꾸지 않고 마지막 끝점부터 별도 구간을 잇는다.
        float startX = precedingFloor.bounds.max.x;
        float centerY = precedingFloor.bounds.center.y;
        const float entryWidth = 6f, bridgeWidth = 50f, exitWidth = 8f;
        float bridgeStart = startX + entryWidth;
        float bridgeEnd = bridgeStart + bridgeWidth;
        float endX = bridgeEnd + exitWidth;
        Transform corridor = FindOrCreate(null, CorridorName).transform;
        BoxCollider2D entry = PlaceFloor(corridor, "Entry Waiting Floor", startX, entryWidth, centerY);
        BoxCollider2D shape = PlaceFloor(corridor, "Fading Bridge", bridgeStart, bridgeWidth, centerY);
        BoxCollider2D exit = PlaceFloor(corridor, "Exit Landing", bridgeEnd, exitWidth, centerY);
        Physics2D.SyncTransforms();
        ConfigureSolidFloor(entry, grass, soil, material);
        ConfigureSolidFloor(exit, grass, soil, material);
        PlaceRespawn(safeZones, "Fading Floor Entry Waiting", entry.bounds);
        PlaceRespawn(safeZones, "Fading Floor Exit Landing", exit.bounds);
        ConfigureFadingFloor(shape, race, sprite, material);

        MoveX(goal.transform, endX - 2f);
        MoveX(goalView.transform, endX - 2f);
        ExtendRight(mapBounds, endX + 10f);
        ExtendRight(fallBounds, endX + 16f);
        BoxCollider2D safeBounds = safeZones.GetComponent<BoxCollider2D>();
        if (safeBounds != null) ExtendRight(safeBounds, endX);
        CameraBackdrop backdrop = Object.FindFirstObjectByType<CameraBackdrop>(FindObjectsInactive.Include);
        if (backdrop != null)
        {
            var backgroundSettings = new SerializedObject(backdrop);
            SerializedProperty areaProperty = backgroundSettings.FindProperty("landscapeArea");
            Rect area = areaProperty.rectValue;
            area.xMax = Mathf.Max(area.xMax, endX + 20f);
            areaProperty.rectValue = area;
            backgroundSettings.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = shape.gameObject;
        Debug.Log($"전용 길 배치: 대기 {startX:F1}~{bridgeStart:F1}, 사라지는 길 {bridgeStart:F1}~{bridgeEnd:F1}, 도착 {bridgeEnd:F1}~{endX:F1}. 대포 바닥 복구.", corridor);
    }

    private static void ConfigureFadingFloor(BoxCollider2D shape, RaceManager race, Sprite sprite, Material material)
    {
        GameObject floor = shape.gameObject;
        WorldTerrainAppearance appearance = floor.GetComponent<WorldTerrainAppearance>();
        if (appearance != null)
        {
            Undo.RecordObject(appearance, "기존 잔디·흙 표시 중지");
            appearance.enabled = false;
        }

        Transform surface = floor.transform.Find(SurfaceName);
        if (surface == null)
        {
            var child = new GameObject(SurfaceName);
            Undo.RegisterCreatedObjectUndo(child, "사라지는 바닥 그림 생성");
            child.transform.SetParent(floor.transform, false);
            child.layer = floor.layer;
            surface = child.transform;
        }
        SpriteRenderer visual = surface.GetComponent<SpriteRenderer>();
        if (visual == null) visual = Undo.AddComponent<SpriteRenderer>(surface.gameObject);
        Undo.RecordObjects(new Object[] { surface, visual }, "B 시안 크기와 투명도 적용");
        surface.localPosition = shape.offset;
        surface.localRotation = Quaternion.identity;
        surface.localScale = new Vector3(shape.size.x / sprite.bounds.size.x, shape.size.y / sprite.bounds.size.y, 1f);
        visual.sprite = sprite;
        visual.drawMode = SpriteDrawMode.Simple;
        visual.sortingOrder = -10;
        visual.color = new Color(1f, 1f, 1f, 0.65f);
        if (material != null) visual.sharedMaterial = material;
        visual.enabled = true;

        foreach (SpriteRenderer oldVisual in floor.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (oldVisual == visual) continue;
            Undo.RecordObject(oldVisual, "기존 바닥 그림 중지");
            oldVisual.enabled = false;
        }

        DisappearingFloor cycle = floor.GetComponent<DisappearingFloor>();
        if (cycle == null) cycle = Undo.AddComponent<DisappearingFloor>(floor);
        var settings = new SerializedObject(cycle);
        settings.FindProperty("visual").objectReferenceValue = visual;
        settings.FindProperty("race").objectReferenceValue = race;
        SerializedProperty respawns = settings.FindProperty("respawnAreas");
        // 사라지는 길에는 SafeZone을 두지 않고 양 끝의 영구 바닥에서만 재시작한다.
        respawns.arraySize = 0;
        settings.FindProperty("walkableDuration").floatValue = 8f;
        settings.FindProperty("unavailableDuration").floatValue = 3f;
        settings.FindProperty("reappearDuration").floatValue = 2f;
        settings.FindProperty("maximumOpacity").floatValue = 0.65f;
        settings.ApplyModifiedProperties();
    }

    private static GameObject RequireObject(string path)
    {
        GameObject result = GameObject.Find(path);
        if (result == null) throw new System.InvalidOperationException("오브젝트를 찾을 수 없습니다: " + path);
        return result;
    }

    private static GameObject FindOrCreate(Transform parent, string name)
    {
        Transform existing = parent != null ? parent.Find(name) : null;
        GameObject result = existing != null ? existing.gameObject : parent == null ? GameObject.Find(name) : null;
        if (result != null) return result;
        result = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(result, "전용 길 오브젝트 생성");
        if (parent != null) result.transform.SetParent(parent, false);
        return result;
    }

    private static BoxCollider2D PlaceFloor(Transform parent, string name, float left, float width, float centerY)
    {
        GameObject floor = FindOrCreate(parent, name);
        Undo.RecordObjects(new Object[] { floor, floor.transform }, "전용 길 크기 배치");
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.tag = "Ground";
        floor.transform.SetPositionAndRotation(new Vector3(left + width * 0.5f, centerY, 0f), Quaternion.identity);
        floor.transform.localScale = Vector3.one;
        BoxCollider2D shape = floor.GetComponent<BoxCollider2D>();
        if (shape == null) shape = Undo.AddComponent<BoxCollider2D>(floor);
        Undo.RecordObject(shape, "발판 충돌 크기");
        shape.size = new Vector2(width, 2f);
        shape.offset = Vector2.zero;
        shape.isTrigger = false;
        shape.enabled = true;
        return shape;
    }

    private static void ConfigureSolidFloor(BoxCollider2D shape, Sprite grass, Sprite soil, Material material)
    {
        WorldTerrainAppearance appearance = shape.GetComponent<WorldTerrainAppearance>();
        if (appearance == null) appearance = Undo.AddComponent<WorldTerrainAppearance>(shape.gameObject);
        Undo.RecordObject(appearance, "대기 바닥 그림 연결");
        appearance.enabled = true;
        appearance.Configure(grass, soil, material);
    }

    private static void PlaceRespawn(Transform parent, string name, Bounds floorBounds)
    {
        GameObject zone = FindOrCreate(parent, name);
        Undo.RecordObjects(new Object[] { zone, zone.transform }, "안전한 대기 바닥 부활 영역");
        zone.layer = LayerMask.NameToLayer("SafeZone");
        zone.transform.SetPositionAndRotation(new Vector3(floorBounds.center.x, floorBounds.max.y + 1.45f, 0f), Quaternion.identity);
        zone.transform.localScale = Vector3.one;
        BoxCollider2D shape = zone.GetComponent<BoxCollider2D>();
        if (shape == null) shape = Undo.AddComponent<BoxCollider2D>(zone);
        Undo.RecordObject(shape, "안전 부활 영역 크기");
        shape.size = new Vector2(floorBounds.size.x - 0.4f, 3.1f);
        shape.offset = Vector2.zero;
        shape.isTrigger = true;
        shape.enabled = true;
    }

    private static void RestoreCannonFloor(GameObject floor)
    {
        DisappearingFloor oldCycle = floor.GetComponent<DisappearingFloor>();
        if (oldCycle != null) Undo.DestroyObjectImmediate(oldCycle);
        Transform oldSurface = floor.transform.Find(SurfaceName);
        if (oldSurface != null) Undo.DestroyObjectImmediate(oldSurface.gameObject);
        WorldTerrainAppearance appearance = floor.GetComponent<WorldTerrainAppearance>();
        if (appearance != null)
        {
            Undo.RecordObject(appearance, "대포 통로 원래 그림 복구");
            appearance.enabled = true;
        }
        foreach (SpriteRenderer oldVisual in floor.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Undo.RecordObject(oldVisual, "대포 통로 그림 켜기");
            oldVisual.enabled = true;
        }
        BoxCollider2D shape = floor.GetComponent<BoxCollider2D>();
        Undo.RecordObject(shape, "대포 통로 충돌 복구");
        shape.enabled = true;
        foreach (string path in new[] { "SafeZones/Cannon Corridor Respawn Area", "SafeZones/Cannon Exit Respawn Area" })
        {
            Collider2D zone = RequireObject(path).GetComponent<Collider2D>();
            Undo.RecordObject(zone, "대포 통로 부활 영역 복구");
            zone.enabled = true;
        }
    }

    private static void MoveX(Transform target, float x)
    {
        Undo.RecordObject(target, "새 길 끝으로 골인 시점 이동");
        Vector3 position = target.position;
        position.x = x;
        target.position = position;
    }

    private static void ExtendRight(BoxCollider2D shape, float right)
    {
        // offset도 함께 옮겨 기존 왼쪽 경계를 보존한다. 다시 실행해도 추가 확장되지 않는다.
        float extraWidth = right - shape.bounds.max.x;
        if (extraWidth <= 0.001f) return;
        Undo.RecordObject(shape, "새 길까지 맵 영역 확장");
        float scaleX = shape.transform.lossyScale.x;
        shape.size += new Vector2(extraWidth / Mathf.Abs(scaleX), 0f);
        shape.offset += new Vector2(extraWidth / (2f * scaleX), 0f);
    }

    private static Sprite PrepareSprite()
    {
        if (!File.Exists(SpritePath))
        {
            if (!File.Exists(SourcePath))
                throw new FileNotFoundException("B 시안 원본이 없습니다.", SourcePath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var selected = new Texture2D(1974, 156, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(SourcePath)))
                    throw new System.InvalidOperationException("시안 PNG를 읽을 수 없습니다.");
                if (source.width != 2172 || source.height != 724)
                    throw new System.InvalidOperationException("시안 크기가 달라 B 영역을 자를 수 없습니다.");
                // 승인된 비교판의 가운데 B만 추출한다. A/C와 글자는 게임 에셋에 포함하지 않는다.
                selected.SetPixels(source.GetPixels(145, 277, 1974, 156));
                selected.Apply();
                File.WriteAllBytes(SpritePath, ImageConversion.EncodeToPNG(selected));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(selected);
            }
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null) throw new System.InvalidOperationException("바닥 Sprite를 불러오지 못했습니다.");
        return sprite;
    }
}
#endif
