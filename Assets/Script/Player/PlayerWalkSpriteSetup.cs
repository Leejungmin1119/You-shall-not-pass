#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 걷기 시트(3×2칸, 512px)를 프레임마다 기준점을 맞춰 잘라 플레이어 프리팹에 연결한다.
///
/// 시트는 칸마다 캐릭터 위치가 크게 다르고(인형 머리 중심 x 327→189px), 윗줄과 아랫줄의 발끝 높이도 14~25px 다르다.
/// 칸을 균등하게 자르면 걸을 때 좌우로 미끄러지고 위아래로 튄다. 그래서 프레임마다
///  - 가로 기준: 머리 중심(걷는 동안 가장 덜 움직이는 부분)
///  - 세로 기준: 발끝(지면)
/// 을 대기 프레임과 같은 위치에 오도록 기준점(pivot)을 정한다. 대기↔걷기 전환 때도 캐릭터가 튀지 않는다.
/// 인형 걷기 그림은 대기보다 약 10% 작게 그려져 있어서, 머리 폭 비율로 픽셀 밀도(PPU)를 맞춰 화면 크기를 같게 한다.
/// 다시 눌러도 같은 결과가 나오고, 이미 있는 스프라이트의 ID를 재사용해 프리팹 참조가 끊기지 않는다.
/// </summary>
public static class PlayerWalkSpriteSetup
{
    private const string IdleSheetPath = "Assets/Image/DollKnight_IdleBoil_Sheet_v1.png";
    private const string PrefabPath = "Assets/MyGame/Prefabs/NetworkPlatformPlayer.prefab";
    private const int CellSize = 512;
    private const int Columns = 3;
    private const int Rows = 2;

    // 반투명 테두리까지 자르되, 지면·머리 측정은 거의 불투명한 픽셀만 본다.
    private const byte BoundsAlpha = 8;
    private const byte SolidAlpha = 128;
    private const float HeadRegionRatio = 0.4f;
    private const int Padding = 2;

    private struct Character
    {
        public string Prefix;
        public string WalkSheetPath;
        public string IdleReferenceSprite;
        public string WalkField;
    }

    private static readonly Character[] Characters =
    {
        new Character { Prefix = "Doll", WalkSheetPath = "Assets/Image/Doll_Walk_Sheet_v1.png", IdleReferenceSprite = "Doll_Idle_1", WalkField = "dollWalkFrames" },
        new Character { Prefix = "Knight", WalkSheetPath = "Assets/Image/Knight_Walk_Sheet_v1.png", IdleReferenceSprite = "Knight_Idle_1", WalkField = "knightWalkFrames" },
    };

    private struct Measure
    {
        public RectInt Bounds;   // 반투명 포함 외곽
        public int FeetY;        // 가장 낮은 불투명 행(아래 기준)
        public float HeadCenterX;
        public int HeadWidth;
    }

    [MenuItem("Tools/캐릭터/걷기 시트 기준점 맞춰 자르고 프리팹에 연결")]
    public static void Run()
    {
        Texture2D idlePixels = LoadPixels(IdleSheetPath);
        Sprite[] idleSprites = AssetDatabase.LoadAllAssetsAtPath(IdleSheetPath).OfType<Sprite>().ToArray();
        TextureImporter idleImporter = (TextureImporter)AssetImporter.GetAtPath(IdleSheetPath);

        var walkFrames = new Dictionary<string, Sprite[]>();
        foreach (Character character in Characters)
        {
            Sprite idle = idleSprites.FirstOrDefault(s => s.name == character.IdleReferenceSprite);
            if (idle == null)
            {
                Debug.LogError($"[걷기] 기준 대기 스프라이트 {character.IdleReferenceSprite}가 없습니다.");
                return;
            }

            // 대기 프레임에서 "기준점으로부터 발끝·머리 중심이 얼마나 떨어져 있는가"를 월드 단위로 잰다.
            RectInt idleRect = new RectInt((int)idle.rect.x, (int)idle.rect.y, (int)idle.rect.width, (int)idle.rect.height);
            Measure idleMeasure = MeasureRegion(idlePixels, idleRect);
            float idlePpu = idle.pixelsPerUnit;
            Vector2 idlePivot = idle.rect.position + idle.pivot;
            float feetBelowPivot = (idlePivot.y - idleMeasure.FeetY) / idlePpu;
            float headRightOfPivot = (idleMeasure.HeadCenterX - idlePivot.x) / idlePpu;

            Texture2D walkPixels = LoadPixels(character.WalkSheetPath);
            var measures = new List<Measure>();
            for (int index = 0; index < Columns * Rows; index++)
            {
                int column = index % Columns;
                int rowFromTop = index / Columns;
                var cell = new RectInt(column * CellSize, walkPixels.height - (rowFromTop + 1) * CellSize, CellSize, CellSize);
                measures.Add(MeasureRegion(walkPixels, cell));
            }

            float walkPpu = idlePpu * (float)measures.Average(m => m.HeadWidth) / idleMeasure.HeadWidth;

            var rects = new List<SpriteRect>();
            for (int index = 0; index < measures.Count; index++)
            {
                Measure m = measures[index];
                var pivotPixels = new Vector2(m.HeadCenterX - headRightOfPivot * walkPpu,
                                              m.FeetY + feetBelowPivot * walkPpu);
                Rect rect = new Rect(m.Bounds.x, m.Bounds.y, m.Bounds.width, m.Bounds.height);
                rects.Add(new SpriteRect
                {
                    name = $"{character.Prefix}_Walk_{index + 1}",
                    rect = rect,
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2((pivotPixels.x - rect.x) / rect.width, (pivotPixels.y - rect.y) / rect.height),
                });
            }

            ApplyImport(character.WalkSheetPath, rects, walkPpu, idleImporter);
            Debug.Log($"[걷기] {character.WalkSheetPath}: 6프레임, PPU {walkPpu:F1} (대기 {idlePpu}), " +
                      $"발끝 기준점 아래 {feetBelowPivot:F3}, 머리 중심 기준점 오른쪽 {headRightOfPivot:F3}");

            walkFrames[character.WalkField] = AssetDatabase.LoadAllAssetsAtPath(character.WalkSheetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.name)
                .ToArray();
        }

        AssignToPrefab(walkFrames);
    }

    // ── 공중(도약·정점·낙하) ─────────────────────────────────────────

    private const int AirCellCount = 3;
    // 정수리에서 머리 폭의 35% 아래 행. 이 깊이에서는 어느 공중 자세도 팔이 머리에 닿지 않는다(실측).
    private const float HeadProbeDepthRatio = 0.35f;

    private struct AirCharacter
    {
        public string Prefix;
        public string SheetPath;
        public string IdleReferenceSprite;
        public string Field;
    }

    private static readonly AirCharacter[] AirCharacters =
    {
        new AirCharacter { Prefix = "Doll", SheetPath = "Assets/Image/Doll_Jump_Sheet_v1.png", IdleReferenceSprite = "Doll_Idle_1", Field = "dollAirFrames" },
        new AirCharacter { Prefix = "Knight", SheetPath = "Assets/Image/Knight_Jump_Sheet_v1.png", IdleReferenceSprite = "Knight_Idle_1", Field = "knightAirFrames" },
    };

    private static readonly string[] AirFrameNames = { "Rise", "Apex", "Fall" };

    /// <summary>
    /// 공중 시트(가로 3칸: 도약·정점·낙하)를 잘라 프리팹에 연결한다.
    /// 공중 자세는 발 위치가 제각각이라 발끝 대신 머리를 기준으로 맞춘다.
    ///  - 크기: 팔이 머리와 떨어진 정점 프레임의 머리 폭으로 시트 전체 PPU를 정한다
    ///    (프레임마다 재면 머리 옆으로 든 팔·투구 장식이 섞여 값이 흔들렸다).
    ///  - 기준점: 정수리 높이와, 정수리에서 머리 폭 35% 아래 행의 머리 중심이 대기 프레임과 같은 자리에 오게 한다.
    /// 대기·걷기에서 점프로 바뀌어도 머리가 제자리에 있다.
    /// </summary>
    [MenuItem("Tools/캐릭터/공중(도약·정점·낙하) 시트 기준점 맞춰 자르고 프리팹에 연결")]
    public static void RunAir()
    {
        Texture2D idlePixels = LoadPixels(IdleSheetPath);
        Sprite[] idleSprites = AssetDatabase.LoadAllAssetsAtPath(IdleSheetPath).OfType<Sprite>().ToArray();
        TextureImporter idleImporter = (TextureImporter)AssetImporter.GetAtPath(IdleSheetPath);

        var airFrames = new Dictionary<string, Sprite[]>();
        foreach (AirCharacter character in AirCharacters)
        {
            Sprite idle = idleSprites.FirstOrDefault(s => s.name == character.IdleReferenceSprite);
            if (idle == null)
            {
                Debug.LogError($"[공중] 기준 대기 스프라이트 {character.IdleReferenceSprite}가 없습니다.");
                return;
            }

            RectInt idleRect = new RectInt((int)idle.rect.x, (int)idle.rect.y, (int)idle.rect.width, (int)idle.rect.height);
            float idlePpu = idle.pixelsPerUnit;
            Vector2 idlePivot = idle.rect.position + idle.pivot;
            int idleHeadWidth = WidestUpperRow(idlePixels, idleRect);
            HeadProbe idleHead = ProbeHead(idlePixels, idleRect, Mathf.RoundToInt(idleHeadWidth * HeadProbeDepthRatio));
            float headTopAbovePivot = (idleHead.TopY - idlePivot.y) / idlePpu;
            float headCenterRightOfPivot = (idleHead.CenterX - idlePivot.x) / idlePpu;

            Texture2D sheet = LoadPixels(character.SheetPath);
            int cellWidth = sheet.width / AirCellCount;
            var cells = new RectInt[AirCellCount];
            for (int i = 0; i < AirCellCount; i++)
                cells[i] = new RectInt(i * cellWidth, 0, cellWidth, sheet.height);

            int apexHeadWidth = WidestUpperRow(sheet, cells[1]);
            float airPpu = idlePpu * apexHeadWidth / idleHeadWidth;
            int probeDepth = Mathf.RoundToInt(apexHeadWidth * HeadProbeDepthRatio);

            var rects = new List<SpriteRect>();
            for (int i = 0; i < AirCellCount; i++)
            {
                Measure m = MeasureRegion(sheet, cells[i]);
                HeadProbe head = ProbeHead(sheet, cells[i], probeDepth);
                var pivotPixels = new Vector2(head.CenterX - headCenterRightOfPivot * airPpu,
                                              head.TopY - headTopAbovePivot * airPpu);
                Rect rect = new Rect(m.Bounds.x, m.Bounds.y, m.Bounds.width, m.Bounds.height);
                rects.Add(new SpriteRect
                {
                    name = $"{character.Prefix}_Air_{i + 1}_{AirFrameNames[i]}",
                    rect = rect,
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2((pivotPixels.x - rect.x) / rect.width, (pivotPixels.y - rect.y) / rect.height),
                });
            }

            ApplyImport(character.SheetPath, rects, airPpu, idleImporter);
            Debug.Log($"[공중] {character.SheetPath}: 3프레임, PPU {airPpu:F1} (대기 {idlePpu}), " +
                      $"정수리 기준점 위 {headTopAbovePivot:F3}, 머리 중심 기준점 오른쪽 {headCenterRightOfPivot:F3}");

            airFrames[character.Field] = AssetDatabase.LoadAllAssetsAtPath(character.SheetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.name)
                .ToArray();
        }

        AssignToPrefab(airFrames);
    }

    private struct HeadProbe
    {
        public int TopY;      // 정수리(가장 높은 불투명 행, 아래 기준 좌표)
        public float CenterX; // 정수리에서 depth 아래 행의 가장 긴 연속 구간 중심
    }

    private static HeadProbe ProbeHead(Texture2D texture, RectInt region, int depth)
    {
        Color32[] pixels = texture.GetPixels32();
        int width = texture.width;
        int top = int.MinValue;
        for (int y = region.yMax - 1; y >= region.y && top == int.MinValue; y--)
            for (int x = region.x; x < region.xMax; x++)
                if (pixels[y * width + x].a >= SolidAlpha) { top = y; break; }

        int row = Mathf.Max(region.y, top - depth);
        int bestStart = 0, bestLength = 0, runStart = -1;
        for (int x = region.x; x <= region.xMax; x++)
        {
            bool solid = x < region.xMax && pixels[row * width + x].a >= SolidAlpha;
            if (solid && runStart < 0) runStart = x;
            if (!solid && runStart >= 0)
            {
                if (x - runStart > bestLength) { bestLength = x - runStart; bestStart = runStart; }
                runStart = -1;
            }
        }

        return new HeadProbe { TopY = top, CenterX = bestStart + bestLength * 0.5f };
    }

    /// <summary>불투명 영역 위쪽 45% 안에서 가장 넓은 행의 폭(머리 폭).</summary>
    private static int WidestUpperRow(Texture2D texture, RectInt region)
    {
        Measure m = MeasureRegion(texture, region);
        Color32[] pixels = texture.GetPixels32();
        int width = texture.width;
        int top = m.Bounds.yMax - 1;
        int bottom = m.FeetY;
        int limit = top - Mathf.RoundToInt((top - bottom) * 0.45f);
        int best = 0;
        for (int y = top; y >= limit; y--)
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            for (int x = region.x; x < region.xMax; x++)
                if (pixels[y * width + x].a >= SolidAlpha) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            if (maxX >= minX) best = Mathf.Max(best, maxX - minX + 1);
        }
        return best;
    }

    private static Texture2D LoadPixels(string assetPath)
    {
        // 임포트 설정(Read/Write)을 바꾸지 않으려고 원본 PNG를 직접 읽는다.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(assetPath));
        return texture;
    }

    private static Measure MeasureRegion(Texture2D texture, RectInt region)
    {
        Color32[] pixels = texture.GetPixels32();
        int width = texture.width;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        int solidMinY = int.MaxValue, solidMaxY = int.MinValue;
        for (int y = region.y; y < region.yMax; y++)
        {
            for (int x = region.x; x < region.xMax; x++)
            {
                byte alpha = pixels[y * width + x].a;
                if (alpha >= BoundsAlpha)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                if (alpha >= SolidAlpha)
                {
                    solidMinY = Mathf.Min(solidMinY, y); solidMaxY = Mathf.Max(solidMaxY, y);
                }
            }
        }

        // 머리: 불투명 영역의 위쪽 40%. 가장 넓고 걷는 동안 가장 덜 움직인다.
        int headBottom = solidMaxY - Mathf.RoundToInt((solidMaxY - solidMinY) * HeadRegionRatio);
        int headMinX = int.MaxValue, headMaxX = int.MinValue;
        for (int y = headBottom; y <= solidMaxY; y++)
        {
            for (int x = region.x; x < region.xMax; x++)
            {
                if (pixels[y * width + x].a >= SolidAlpha)
                {
                    headMinX = Mathf.Min(headMinX, x); headMaxX = Mathf.Max(headMaxX, x);
                }
            }
        }

        var bounds = new RectInt(minX - Padding, minY - Padding, maxX - minX + 1 + Padding * 2, maxY - minY + 1 + Padding * 2);
        bounds.xMin = Mathf.Max(bounds.xMin, region.xMin);
        bounds.yMin = Mathf.Max(bounds.yMin, region.yMin);
        bounds.xMax = Mathf.Min(bounds.xMax, region.xMax);
        bounds.yMax = Mathf.Min(bounds.yMax, region.yMax);

        return new Measure
        {
            Bounds = bounds,
            FeetY = solidMinY,
            HeadCenterX = (headMinX + headMaxX + 1) * 0.5f,
            HeadWidth = headMaxX - headMinX + 1,
        };
    }

    private static void ApplyImport(string path, List<SpriteRect> rects, float pixelsPerUnit, TextureImporter reference)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = reference.filterMode;
        importer.textureCompression = reference.textureCompression;
        // 공중 시트는 가로 2172px라 대기 시트의 최대 크기(2048)를 그대로 쓰면 94%로 줄어 흐려진다.
        // 원본 크기가 들어가는 가장 작은 2의 거듭제곱으로 올린다.
        importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
        importer.maxTextureSize = Mathf.Max(reference.maxTextureSize,
            Mathf.NextPowerOfTwo(Mathf.Max(sourceWidth, sourceHeight)));
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        // 같은 이름의 스프라이트는 ID를 유지해서, 다시 실행해도 프리팹 참조가 그대로 남게 한다.
        Dictionary<string, GUID> existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        foreach (SpriteRect rect in rects)
            rect.spriteID = existing.TryGetValue(rect.name, out GUID id) ? id : GUID.Generate();

        provider.SetSpriteRects(rects.ToArray());
        var nameIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameIds?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    private static void AssignToPrefab(Dictionary<string, Sprite[]> walkFrames)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            PlayerIdleBoil animator = root.GetComponent<PlayerIdleBoil>();
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            if (animator == null || renderer == null)
            {
                Debug.LogError("[캐릭터] 플레이어 프리팹에 PlayerIdleBoil 또는 SpriteRenderer가 없습니다.");
                return;
            }

            var serialized = new SerializedObject(animator);
            foreach (KeyValuePair<string, Sprite[]> pair in walkFrames)
            {
                SerializedProperty list = serialized.FindProperty(pair.Key);
                list.arraySize = pair.Value.Length;
                for (int i = 0; i < pair.Value.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = pair.Value[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Sliced(고정 크기 1.4×2)는 프레임마다 크기가 다른 걷기 그림을 늘려 버린다.
            // Simple이면 대기 그림은 원래 크기(1.4×2.0) 그대로이고 걷기 그림도 비율이 유지된다.
            renderer.drawMode = SpriteDrawMode.Simple;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[캐릭터] {PrefabPath}: {string.Join(", ", walkFrames.Keys)} 연결, 그리기 모드 Simple");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
