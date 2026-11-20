using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>무대는 흑백, Danger Rendering Layer의 피해 오브젝트는 붉게 표시한다.</summary>
public sealed class TimeSkillGrayscale : MonoBehaviour
{
    private static readonly int GrayscaleId = Shader.PropertyToID("_StageGrayscale");
    private static readonly int DangerId = Shader.PropertyToID("_StageDanger");
    // Default 비트를 유지하고 표시 분류만 추가한다. 물리 Layer와는 무관하다.
    public const uint DangerRenderingLayer = 1u << 1;
    private static TimeSkillGrayscale current;
    private readonly Dictionary<(Material original, bool danger), Material> replacements = new();
    private readonly Dictionary<Renderer, Material> originals = new();
    private RaceManager race;
    private Material litTemplate;
    private Material unlitTemplate;
    private Camera outputCamera;
    private Color originalBackground;
    private readonly List<Renderer> destroyedRenderers = new();
    private float nextCleanupTime;
    private double canceledThrough = double.NegativeInfinity;

    /// <summary>해제된 스킬 시각을 기억하고 같은 스킬의 늦은 동기화가 흑백을 다시 켜지 못하게 한다.</summary>
    public static void Cancel(double startedAt)
    {
        if (current == null)
            return;

        current.canceledThrough = System.Math.Max(current.canceledThrough, startedAt);

        if (current.race == null || current.race.TimeSkillStartedAt <= current.canceledThrough)
            current.ApplyStrength(0f);
    }

    /// <summary>원격 접속·새 경기에서도 이전 화면의 전역 셰이더 값이 남지 않게 초기화한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGlobals()
    {
        current = null;
        Shader.SetGlobalFloat(GrayscaleId, 0f);
    }

    /// <summary>씬의 표시 대상을 한 번 수집하고 원래 재질별로 대체 재질을 공유한다.</summary>
    public void Configure(RaceManager owner, Material lit, Material unlit)
    {
        if (race != null)
            return;

        if (lit == null || unlit == null)
        {
            Debug.LogError("시간 스킬의 흑백 재질 연결을 확인하세요.", this);
            return;
        }

        race = owner;
        litTemplate = lit;
        unlitTemplate = unlit;
        current = this;
        outputCamera = Camera.main;

        if (outputCamera != null)
            originalBackground = outputCamera.backgroundColor;

        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            RegisterRenderer(renderer);
    }

    /// <summary>스킬 중 만들어지는 함정·투사체·착지 예고에도 생성 즉시 같은 표시를 적용한다.</summary>
    public static void Register(GameObject target)
    {
        if (current == null)
            return;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            current.RegisterRenderer(renderer);
    }

    /// <summary>박힌 창·피해가 끝난 폭발처럼 표시만 남은 경우 위험 강조를 해제한다.</summary>
    public static void SetDanger(Renderer renderer, bool dangerous)
    {
        if (renderer == null)
            return;

        uint mask = dangerous ? renderer.renderingLayerMask | DangerRenderingLayer
            : renderer.renderingLayerMask & ~DangerRenderingLayer;
        if (mask == renderer.renderingLayerMask)
            return;

        renderer.renderingLayerMask = mask;
        current?.RegisterRenderer(renderer);
    }

    /// <summary>플레이어와 UI는 제외하고 프로젝트에서 사용하는 기본 스프라이트 재질만 교체한다.</summary>
    private void RegisterRenderer(Renderer renderer)
    {
        if (renderer == null ||
            !(renderer is SpriteRenderer || renderer is TilemapRenderer) ||
            renderer.GetComponentInParent<PlayerState>() != null ||
            renderer.GetComponentInParent<Canvas>() != null)
            return;

        Material original = originals.TryGetValue(renderer, out Material saved) ? saved : renderer.sharedMaterial;

        if (original == null)
            return;

        string shader = original.shader.name;
        Material template;

        if (shader == "Universal Render Pipeline/2D/Sprite-Lit-Default")
            template = litTemplate;
        else if (shader == "Universal Render Pipeline/2D/Sprite-Unlit-Default" || shader == "Sprites/Default")
            template = unlitTemplate;
        else
            return;

        bool danger = (renderer.renderingLayerMask & DangerRenderingLayer) != 0;
        var key = (original, danger);
        if (!replacements.TryGetValue(key, out Material replacement))
        {
            replacement = new Material(template) { name = original.name + (danger ? " (Time Skill Danger)" : " (Time Skill)"), hideFlags = HideFlags.DontSave };
            replacement.CopyPropertiesFromMaterial(original);
            replacement.SetFloat(DangerId, danger ? 1f : 0f);
            replacements.Add(key, replacement);
        }

        originals[renderer] = original;
        renderer.sharedMaterial = replacement;
    }

    /// <summary>네트워크 시각의 강도만 갱신한다. 매 프레임 전체 검색이나 재질 생성을 하지 않는다.</summary>
    private void LateUpdate()
    {
        float strength = race != null && race.IsRunning && race.TimeSkillStartedAt > canceledThrough
            ? race.TimeSkillGrayscale : 0f;
        ApplyStrength(strength);

        if (Time.unscaledTime >= nextCleanupTime)
        {
            nextCleanupTime = Time.unscaledTime + 1f;
            RemoveDestroyedRenderers();
        }
    }

    /// <summary>셰이더 강도와 카메라 배경색을 같은 호출에서 갱신한다.</summary>
    private void ApplyStrength(float strength)
    {
        Shader.SetGlobalFloat(GrayscaleId, strength);

        if (outputCamera != null)
        {
            float gray = originalBackground.grayscale;
            outputCamera.backgroundColor = Color.Lerp(originalBackground,
                new Color(gray, gray, gray, originalBackground.a), strength);
        }
    }

    /// <summary>수명이 끝난 함정의 참조가 경기 내내 쌓이지 않도록 주기적으로 제거한다.</summary>
    private void RemoveDestroyedRenderers()
    {
        destroyedRenderers.Clear();

        foreach (var entry in originals)
        {
            if (entry.Key == null)
                destroyedRenderers.Add(entry.Key);
        }

        foreach (Renderer renderer in destroyedRenderers)
            originals.Remove(renderer);
    }

    /// <summary>비활성화 중에는 흑백 강도와 카메라 배경색을 즉시 원상 복구한다.</summary>
    private void OnDisable()
    {
        if (current != this)
            return;

        Shader.SetGlobalFloat(GrayscaleId, 0f);

        if (outputCamera != null)
            outputCamera.backgroundColor = originalBackground;
    }

    /// <summary>씬 종료 시 원래 표시를 복구하고 공유하던 실행 중 재질을 해제한다.</summary>
    private void OnDestroy()
    {
        if (current != this)
            return;

        current = null;
        Shader.SetGlobalFloat(GrayscaleId, 0f);

        if (outputCamera != null)
            outputCamera.backgroundColor = originalBackground;

        foreach (var entry in originals)
        {
            if (entry.Key != null && replacements.ContainsValue(entry.Key.sharedMaterial))
                entry.Key.sharedMaterial = entry.Value;
        }

        foreach (Material material in replacements.Values)
            Destroy(material);
    }
}
