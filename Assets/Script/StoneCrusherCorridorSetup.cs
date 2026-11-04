#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>사라지는 바닥 구간 끝에 쿵쿵이 전용 구간을 잇고 골인 지점을 새 끝으로 옮긴다.</summary>
public static class StoneCrusherCorridorSetup
{
    private const string CorridorName = "Stone Crusher Corridor";
    private const string FloorName = "Crusher Floor";
    private const string RespawnName = "Crusher Entry Respawn Area";
    private const string CrusherName = "MapStoneCrusher";
    private const string CrusherSpritePath = "Assets/Image/Trap/Trap_StoneCrusher_Doodle_v1.png";
    private const float FloorWidth = 30f;
    private const float CrusherOffsetX = 12f;
    // 처음 배치된 쿵쿵이(바닥 윗면 -3.5, 본체 중심 3.5)와 같은 높이 차이를 쓴다.
    private const float CrusherHeightAboveFloor = 7f;
    private const float EntryRespawnWidth = 7f;

    [MenuItem("Tools/맵/쿵쿵이 구간 추가")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Play를 종료한 뒤 적용하세요.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new System.InvalidOperationException("SampleScene을 열고 적용하세요.");

        GameObject precedingFloor = RequireObject("Disappearing Floor Corridor/Exit Landing");
        if (!precedingFloor.TryGetComponent(out BoxCollider2D preceding))
            throw new System.InvalidOperationException("사라지는 바닥 구간의 도착 바닥에 BoxCollider2D가 없습니다.");
        RaceManager race = Object.FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        if (race == null)
            throw new System.InvalidOperationException("쿵쿵이가 참조할 RaceManager가 없습니다.");
        Transform safeZones = RequireObject("SafeZones").transform;
        GameObject goal = RequireObject("GoalZone");
        GameObject goalView = RequireObject("RaceStartPresentation/GoalView");
        BoxCollider2D mapBounds = RequireObject("MapViewBounds").GetComponent<BoxCollider2D>();
        BoxCollider2D fallBounds = RequireObject("FallZone").GetComponent<BoxCollider2D>();
        if (mapBounds == null || fallBounds == null)
            throw new System.InvalidOperationException("카메라 또는 낙사 영역의 BoxCollider2D가 없습니다.");
        Sprite grass = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/GrassSoil_GrassTile_v1.png");
        Sprite soil = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/GrassSoil_DirtTile_v1.png");
        Sprite crusherSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CrusherSpritePath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/PastelLandscape_Unlit.mat");
        if (grass == null || soil == null || crusherSprite == null || material == null)
            throw new System.InvalidOperationException("바닥·쿵쿵이 그림 또는 머티리얼이 없습니다.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("쿵쿵이 구간 추가");

        // 앞 구간의 길이를 바꾸지 않고 도착 바닥 끝점부터 같은 높이로 잇는다.
        float startX = preceding.bounds.max.x;
        float centerY = preceding.bounds.center.y;
        float endX = startX + FloorWidth;
        Transform corridor = FindOrCreate(null, CorridorName).transform;
        BoxCollider2D floor = PlaceFloor(corridor, FloorName, startX, FloorWidth, centerY);
        Physics2D.SyncTransforms();
        ConfigureSolidFloor(floor, grass, soil, material);
        float floorTop = floor.bounds.max.y;

        // 쿵쿵이가 떨어지는 자리에는 부활하지 않도록 입구 쪽에만 부활 영역을 둔다.
        PlaceRespawn(safeZones, RespawnName, startX + 0.2f, EntryRespawnWidth, floorTop);
        PlaceCrusher(race, crusherSprite, material, new Vector3(startX + CrusherOffsetX, floorTop + CrusherHeightAboveFloor, 0f));

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
        Selection.activeGameObject = corridor.gameObject;
        Debug.Log($"쿵쿵이 구간 배치: 바닥 {startX:F1}~{endX:F1}, 쿵쿵이 x={startX + CrusherOffsetX:F1}, 골인 x={endX - 2f:F1}.", corridor);
    }

    /// <summary>
    /// 씬에 이미 쿵쿵이가 있으면 그 오브젝트를 옮기고, 없으면 처음 배치된 쿵쿵이와 같은 설정으로 만든다.
    /// 병합 순서와 관계없이 다시 실행해도 쿵쿵이는 한 개만 새 구간에 남는다.
    /// </summary>
    private static void PlaceCrusher(RaceManager race, Sprite sprite, Material material, Vector3 position)
    {
        StoneCrusherTrap[] existing = Object.FindObjectsByType<StoneCrusherTrap>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        GameObject crusher;
        if (existing.Length > 0)
        {
            crusher = existing[0].gameObject;
            if (existing.Length > 1)
                Debug.LogWarning($"쿵쿵이가 {existing.Length}개 있습니다. '{crusher.name}'만 새 구간으로 옮겼습니다.", crusher);
        }
        else
        {
            crusher = new GameObject(CrusherName);
            Undo.RegisterCreatedObjectUndo(crusher, "쿵쿵이 생성");
            crusher.layer = LayerMask.NameToLayer("Ground");

            var visual = Undo.AddComponent<SpriteRenderer>(crusher);
            visual.sprite = sprite;
            visual.sharedMaterial = material;
            visual.sortingOrder = 5;

            // RequireComponent로 NetworkIdentity·BoxCollider2D·Rigidbody2D가 함께 붙는다.
            var trap = Undo.AddComponent<StoneCrusherTrap>(crusher);
            var shape = crusher.GetComponent<BoxCollider2D>();
            shape.size = new Vector2(3.6f, 3.6f);
            var body = crusher.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            var settings = new SerializedObject(trap);
            settings.FindProperty("race").objectReferenceValue = race;
            settings.FindProperty("groundLayer").intValue = LayerMask.GetMask("Ground");
            settings.ApplyModifiedProperties();
        }

        Undo.RecordObject(crusher.transform, "쿵쿵이를 새 구간으로 이동");
        crusher.transform.SetPositionAndRotation(position, Quaternion.identity);
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
        Undo.RegisterCreatedObjectUndo(result, "쿵쿵이 구간 오브젝트 생성");
        if (parent != null) result.transform.SetParent(parent, false);
        return result;
    }

    private static BoxCollider2D PlaceFloor(Transform parent, string name, float left, float width, float centerY)
    {
        GameObject floor = FindOrCreate(parent, name);
        Undo.RecordObjects(new Object[] { floor, floor.transform }, "쿵쿵이 구간 바닥 배치");
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.tag = "Ground";
        floor.transform.SetPositionAndRotation(new Vector3(left + width * 0.5f, centerY, 0f), Quaternion.identity);
        floor.transform.localScale = Vector3.one;
        BoxCollider2D shape = floor.GetComponent<BoxCollider2D>();
        if (shape == null) shape = Undo.AddComponent<BoxCollider2D>(floor);
        Undo.RecordObject(shape, "바닥 충돌 크기");
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
        Undo.RecordObject(appearance, "쿵쿵이 구간 바닥 그림 연결");
        appearance.enabled = true;
        appearance.Configure(grass, soil, material);
    }

    private static void PlaceRespawn(Transform parent, string name, float left, float width, float floorTop)
    {
        GameObject zone = FindOrCreate(parent, name);
        Undo.RecordObjects(new Object[] { zone, zone.transform }, "쿵쿵이 구간 부활 영역");
        zone.layer = LayerMask.NameToLayer("SafeZone");
        zone.transform.SetPositionAndRotation(new Vector3(left + width * 0.5f, floorTop + 1.45f, 0f), Quaternion.identity);
        zone.transform.localScale = Vector3.one;
        BoxCollider2D shape = zone.GetComponent<BoxCollider2D>();
        if (shape == null) shape = Undo.AddComponent<BoxCollider2D>(zone);
        Undo.RecordObject(shape, "부활 영역 크기");
        shape.size = new Vector2(width, 3.1f);
        shape.offset = Vector2.zero;
        shape.isTrigger = true;
        shape.enabled = true;
    }

    private static void MoveX(Transform target, float x)
    {
        Undo.RecordObject(target, "새 구간 끝으로 골인 이동");
        Vector3 position = target.position;
        position.x = x;
        target.position = position;
    }

    private static void ExtendRight(BoxCollider2D shape, float right)
    {
        // offset도 함께 옮겨 기존 왼쪽 경계를 보존한다. 다시 실행해도 추가 확장되지 않는다.
        float extraWidth = right - shape.bounds.max.x;
        if (extraWidth <= 0.001f) return;
        Undo.RecordObject(shape, "새 구간까지 맵 영역 확장");
        float scaleX = shape.transform.lossyScale.x;
        shape.size += new Vector2(extraWidth / Mathf.Abs(scaleX), 0f);
        shape.offset += new Vector2(extraWidth / (2f * scaleX), 0f);
    }
}
#endif
