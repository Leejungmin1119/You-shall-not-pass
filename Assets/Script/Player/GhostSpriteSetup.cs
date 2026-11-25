#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 유령 시트(512px 칸)를 잘라 플레이어 프리팹에 연결한다.
///
/// 유령 시트는 Codex가 시트 안에서 얼굴 위치를 맞춰 그렸으므로, 시트마다 기준점 하나를 모든 칸에 같게 둔다
/// (걷기의 둥실거림처럼 칸 안의 움직임은 그대로 보인다). 시트끼리는 다음을 맞춘다.
///  - 크기: 대기 1번의 몸 높이를 잭오랜턴 대기 1번의 화면 높이에 맞춘 PPU 하나를 모든 시트에 쓴다
///    (네 시트 모두 두 눈 간격이 60~63px로 같은 배율이다).
///  - 가로: 머리 오른쪽 끝(얼굴 앞)이 시트마다 같은 자리. 대기는 머리 중심을 잭오랜턴 머리 중심에 맞춘다.
///  - 세로: 지상 시트는 가장 낮은 꼬리 끝을 잭오랜턴 발끝 높이(충돌체 바닥)에, 공중 시트는 정수리를 대기와 같은 높이에.
/// 다시 눌러도 같은 결과가 나오고, 이미 있는 스프라이트의 ID를 재사용해 프리팹 참조가 끊기지 않는다.
/// </summary>
public static class GhostSpriteSetup
{
    private const string JackIdlePath = "Assets/Image/JackOLantern_Idle.png";
    private const string JackIdleSprite = "JackOLantern_Idle_1";
    private const int CellSize = 512;
    private const byte BoundsAlpha = 8;
    private const byte SolidAlpha = 128;
    private const float HeadRegionRatio = 0.4f;
    private const int Padding = 2;

    private struct Sheet
    {
        public string Path;
        public string Prefix;
        public int Columns;
        public int Rows;
        public string Field;
        public bool Grounded;
    }

    // 첫 시트(대기)가 다른 시트의 기준이다.
    private static readonly Sheet[] Sheets =
    {
        new Sheet { Path = "Assets/Image/Ghost_Idle.png", Prefix = "Ghost_Idle", Columns = 4, Rows = 2, Field = "ghostIdleFrames", Grounded = true },
        new Sheet { Path = "Assets/Image/Ghost_Walk.png", Prefix = "Ghost_Walk", Columns = 3, Rows = 2, Field = "ghostWalkFrames", Grounded = true },
        new Sheet { Path = "Assets/Image/Ghost_Run.png", Prefix = "Ghost_Run", Columns = 4, Rows = 2, Field = "ghostRunFrames", Grounded = true },
        new Sheet { Path = "Assets/Image/Ghost_StandingJump.png", Prefix = "Ghost_StandingJump", Columns = 3, Rows = 2, Field = "ghostAirFrames", Grounded = false },
    };

    private struct Measure
    {
        public RectInt Bounds; // 반투명 포함 외곽(시트 좌표)
        public int Bottom;     // 가장 낮은 불투명 행(칸 좌표)
        public int Top;        // 가장 높은 불투명 행(칸 좌표)
        public float HeadCenterX;
        public int HeadRight;  // 머리 영역 오른쪽 끝(칸 좌표, 끝 픽셀 다음 칸)
    }

    [MenuItem("Tools/캐릭터/유령 시트 자르고 프리팹에 연결")]
    public static void Run()
    {
        Sprite jack = AssetDatabase.LoadAllAssetsAtPath(JackIdlePath).OfType<Sprite>()
            .FirstOrDefault(s => s.name == JackIdleSprite);
        if (jack == null)
        {
            Debug.LogError($"[유령] 기준 스프라이트 {JackIdleSprite}가 없습니다.");
            return;
        }

        var jackRect = new RectInt((int)jack.rect.x, (int)jack.rect.y, (int)jack.rect.width, (int)jack.rect.height);
        Measure jackMeasure = MeasureRegion(PlayerWalkSpriteSetup.LoadPixels(JackIdlePath), jackRect);
        Vector2 jackPivot = jack.rect.position + jack.pivot - jackRect.position;
        float feetBelowPivot = (jackPivot.y - jackMeasure.Bottom) / jack.pixelsPerUnit;
        float headCenterRightOfPivot = (jackMeasure.HeadCenterX - jackPivot.x) / jack.pixelsPerUnit;
        float jackHeight = (jackMeasure.Top + 1 - jackMeasure.Bottom) / jack.pixelsPerUnit;

        var measures = new List<Measure>[Sheets.Length];
        for (int s = 0; s < Sheets.Length; s++)
        {
            Texture2D pixels = PlayerWalkSpriteSetup.LoadPixels(Sheets[s].Path);
            measures[s] = new List<Measure>();
            for (int index = 0; index < Sheets[s].Columns * Sheets[s].Rows; index++)
                measures[s].Add(MeasureRegion(pixels, Cell(pixels, Sheets[s], index)));
        }

        Measure idle = measures[0][0];
        float ppu = (idle.Top + 1 - idle.Bottom) / jackHeight;
        var idlePivot = new Vector2(Median(measures[0].Select(m => m.HeadCenterX)) - headCenterRightOfPivot * ppu,
                                    measures[0].Min(m => m.Bottom) + feetBelowPivot * ppu);
        float faceRightOfPivot = Median(measures[0].Select(m => (float)m.HeadRight)) - idlePivot.x;
        float topAbovePivot = Median(measures[0].Select(m => (float)m.Top)) - idlePivot.y;

        TextureImporter reference = (TextureImporter)AssetImporter.GetAtPath(JackIdlePath);
        var frames = new Dictionary<string, Sprite[]>();
        for (int s = 0; s < Sheets.Length; s++)
        {
            Sheet sheet = Sheets[s];
            List<Measure> list = measures[s];
            var pivot = new Vector2(Median(list.Select(m => (float)m.HeadRight)) - faceRightOfPivot,
                sheet.Grounded ? list.Min(m => m.Bottom) + feetBelowPivot * ppu
                               : Median(list.Select(m => (float)m.Top)) - topAbovePivot);

            Texture2D pixels = PlayerWalkSpriteSetup.LoadPixels(sheet.Path);
            var rects = new List<SpriteRect>();
            for (int index = 0; index < list.Count; index++)
            {
                RectInt cell = Cell(pixels, sheet, index);
                RectInt b = list[index].Bounds;
                var rect = new Rect(b.x, b.y, b.width, b.height);
                Vector2 pivotPixels = cell.position + pivot;
                rects.Add(new SpriteRect
                {
                    name = $"{sheet.Prefix}_{index + 1}",
                    rect = rect,
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2((pivotPixels.x - rect.x) / rect.width, (pivotPixels.y - rect.y) / rect.height),
                });
            }

            PlayerWalkSpriteSetup.ApplyImport(sheet.Path, rects, ppu, reference);
            Debug.Log($"[유령] {sheet.Path}: {rects.Count}프레임, PPU {ppu:F2}, 칸 안 기준점 ({pivot.x:F1}, {pivot.y:F1})");

            frames[sheet.Field] = AssetDatabase.LoadAllAssetsAtPath(sheet.Path)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();
        }

        PlayerWalkSpriteSetup.AssignToPrefab(frames);
    }

    /// <summary>위 왼쪽 칸부터 읽는 순서의 칸 영역(텍스처 좌표는 아래가 0).</summary>
    private static RectInt Cell(Texture2D texture, Sheet sheet, int index)
        => new RectInt(index % sheet.Columns * CellSize, texture.height - (index / sheet.Columns + 1) * CellSize, CellSize, CellSize);

    private static float Median(IEnumerable<float> values)
    {
        float[] sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
    }

    /// <summary>외곽은 시트 좌표, 나머지 값은 영역 안 좌표로 돌려준다.</summary>
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

        // 머리: 불투명 영역의 위쪽 40%.
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
            Bottom = solidMinY - region.y,
            Top = solidMaxY - region.y,
            HeadCenterX = (headMinX + headMaxX + 1) * 0.5f - region.x,
            HeadRight = headMaxX + 1 - region.x,
        };
    }
}
#endif
