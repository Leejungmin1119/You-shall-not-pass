using UnityEngine;
using UnityEngine.UI;

/// <summary>소각 조각과 최고 카드의 반짝임을 하나의 UI 메시로 표시한다.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TurnCardEffects : MaskableGraphic
{
    private TurnCardSelectionUI screen;
    private bool wasAnimating;

    /// <summary>텍스처 없이 그리는 입자는 클릭을 가로채지 않게 한다.</summary>
    public void Initialize(TurnCardSelectionUI owner)
    {
        screen = owner;
        raycastTarget = false;
    }

    /// <summary>표시 중인 짧은 연출만 갱신하며 오브젝트나 재질을 반복 생성하지 않는다.</summary>
    private void Update()
    {
        bool animating = false;

        if (screen != null && screen.Roles != null)
        {
            TurnCardPhase phase = screen.Roles.CardPhase;
            animating = phase == TurnCardPhase.Burning ||
                (screen.HighestCard >= 0 && (phase == TurnCardPhase.Revealing ||
                    phase == TurnCardPhase.ResultHold || phase == TurnCardPhase.Dismissing));
        }

        // 효과가 끝나는 프레임에 한 번 비우고, 선택 대기 중에는 메시 갱신을 멈춘다.
        if (animating || wasAnimating)
            SetVerticesDirty();

        wasAnimating = animating;
    }

    /// <summary>동기화된 연출 시각과 카드 위치로 불씨·재 또는 별빛의 정점을 만든다.</summary>
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();

        if (screen == null || screen.Roles == null)
            return;

        if (screen.Roles.CardPhase == TurnCardPhase.Burning)
        {
            float time = screen.PhaseElapsed / rolechangeManager.BurnDuration;

            for (int index = 0; index < screen.Roles.Cards.Count; index++)
            {
                if (screen.Roles.Cards[index].OwnerId != 0)
                    continue;

                for (int particle = 0; particle < 12; particle++)
                {
                    float seed = index * 23f + particle * 7.13f;
                    float age = time - Random01(seed) * 0.35f;

                    if (age <= 0f || age >= 0.8f)
                        continue;

                    Vector2 offset = new Vector2((Random01(seed + 1) - 0.5f) * 180f,
                        (Random01(seed + 2) - 0.5f) * 230f);
                    offset += new Vector2(Mathf.Sin(seed) * age * 65f, age * 170f);
                    Color tint = Color.Lerp(new Color(1f, 0.6f, 0.12f), new Color(0.3f, 0.27f, 0.35f), age * 1.6f);
                    tint.a = Mathf.Sin(age / 0.8f * Mathf.PI);
                    AddStar(helper, screen.CardPosition(index) + offset, Mathf.Lerp(7f, 2f, age), tint, 0.65f);
                }
            }
        }
        else if ((screen.Roles.CardPhase == TurnCardPhase.Revealing || screen.Roles.CardPhase == TurnCardPhase.ResultHold ||
            screen.Roles.CardPhase == TurnCardPhase.Dismissing) && screen.HighestCard >= 0)
        {
            float time = screen.HighestElapsed;
            Vector2 center = screen.CardPosition(screen.HighestCard);

            for (int index = 0; index < 16; index++)
            {
                float seed = index * 13.7f;
                float pulse = Mathf.Max(0f, Mathf.Sin(time * 11f - index * 1.7f));
                float angle = index * Mathf.PI * 2f / 16f;
                Vector2 offset = new Vector2(Mathf.Cos(angle) * (155f + Random01(seed) * 65f),
                    Mathf.Sin(angle) * (170f + Random01(seed + 1) * 60f));
                Color tint = Color.Lerp(new Color(1f, 0.75f, 0.24f), Color.white, Random01(seed + 2));
                tint.a = pulse * Mathf.Clamp01(time * 6f);
                AddStar(helper, center + offset, 5f + pulse * 13f, tint, 0.18f);
            }
        }
    }

    /// <summary>모든 화면에서 같은 입자 배치가 나오도록 고정된 난수를 구한다.</summary>
    private static float Random01(float seed)
    {
        return Mathf.Repeat(Mathf.Sin(seed * 17.13f) * 43758.54f, 1f);
    }

    /// <summary>가늘게 뻗은 네 방향의 빛을 삼각형 8개로 그린다.</summary>
    private static void AddStar(VertexHelper helper, Vector2 center, float radius, Color color, float innerRatio)
    {
        int first = helper.currentVertCount;
        helper.AddVert(center, color, Vector2.zero);

        for (int index = 0; index < 8; index++)
        {
            float angle = index * Mathf.PI / 4f;
            float distance = radius * (index % 2 == 0 ? 1f : innerRatio);
            helper.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, color, Vector2.zero);
        }

        for (int index = 0; index < 8; index++)
            helper.AddTriangle(first, first + index + 1, first + (index + 1) % 8 + 1);
    }
}
