#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>선택한 B 시안을 실제 바닥 크기에 맞춰 배치한다. 기존 지형·장애물의 위치는 유지한다.</summary>
public static class DisappearingFloorSetup
{
    private const string SpritePath = "Assets/Image/DisappearingFloor_Lavender_v1.png";
    private const string SourcePath = "Temp/DisappearingFloor_Concept.png";
    private const string FloorPath = "Cannon Corridor/Long Flat Corridor";
    private const string SurfaceName = "Lavender Fading Surface";

    [MenuItem("Tools/맵/긴 통로에 회보라 사라지는 바닥 적용")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Play를 종료한 뒤 적용하세요.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new System.InvalidOperationException("SampleScene을 열고 적용하세요.");

        GameObject floor = GameObject.Find(FloorPath);
        if (floor == null || !floor.TryGetComponent(out BoxCollider2D shape))
            throw new System.InvalidOperationException("긴 통로의 BoxCollider2D를 찾을 수 없습니다.");
        RaceManager race = Object.FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        string[] zonePaths = { "SafeZones/Cannon Corridor Respawn Area", "SafeZones/Cannon Exit Respawn Area" };
        var zones = new Collider2D[zonePaths.Length];
        for (int i = 0; i < zonePaths.Length; i++)
        {
            GameObject zone = GameObject.Find(zonePaths[i]);
            if (zone == null || !zone.TryGetComponent(out zones[i]))
                throw new System.InvalidOperationException("부활 영역을 찾을 수 없습니다: " + zonePaths[i]);
        }
        if (race == null)
            throw new System.InvalidOperationException("공통 시작 시각을 제공할 RaceManager가 없습니다.");

        Sprite sprite = PrepareSprite();
        Undo.SetCurrentGroupName("회보라 사라지는 바닥 적용");
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
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/PastelLandscape_Unlit.mat");
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
        respawns.arraySize = zones.Length;
        for (int i = 0; i < zones.Length; i++)
            respawns.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
        settings.FindProperty("walkableDuration").floatValue = 8f;
        settings.FindProperty("unavailableDuration").floatValue = 3f;
        settings.FindProperty("reappearDuration").floatValue = 2f;
        settings.FindProperty("maximumOpacity").floatValue = 0.65f;
        settings.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = floor;
        Debug.Log("회보라 사라지는 바닥 적용: 8초 충돌·페이드 아웃 → 1초 완전 소멸 → 2초 페이드 인(충돌 없음).", floor);
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
