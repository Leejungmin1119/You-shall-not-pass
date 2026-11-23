using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>무대는 흑백, Danger Rendering Layer의 피해 오브젝트는 붉게 표시한다.</summary>
public sealed class TimeSkillGrayscale : MonoBehaviour
{
    private static readonly int GrayscaleId = Shader.PropertyToID("_StageGrayscale");
    private static readonly int DangerId = Shader.PropertyToID("_StageDanger");
    private static readonly int WaveId = Shader.PropertyToID("_StageWave");
    private static readonly int WaveStyleId = Shader.PropertyToID("_StageWaveStyle");
    private static readonly int WaveDistortionId = Shader.PropertyToID("_StageWaveDistortion");
    private static readonly int SpriteUVBoundsId = Shader.PropertyToID("_StageSpriteUVBounds");
    // Default 비트를 유지하고 표시 분류만 추가한다. 물리 Layer와는 무관하다.
    public const uint DangerRenderingLayer = 1u << 1;
    private static TimeSkillGrayscale current;
    private readonly Dictionary<(Material original, bool danger, Vector4 uvBounds), Material> replacements = new();
    private readonly Dictionary<Renderer, Material> originals = new();
    private RaceManager race;
    private Material litTemplate;
    private Material unlitTemplate;
    private Camera outputCamera;
    private Color originalBackground;
    private readonly List<Renderer> destroyedRenderers = new();
    private float nextCleanupTime;
    private double canceledThrough = double.NegativeInfinity;
    private double observedWaveStart = double.NegativeInfinity;
    private float waveReach;

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
        Shader.SetGlobalVector(WaveId, Vector4.zero);
        Shader.SetGlobalVector(WaveStyleId, Vector4.zero);
        Shader.SetGlobalVector(WaveDistortionId, Vector4.zero);
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
        Vector4 uvBounds = SpriteUVBounds(renderer);
        var key = (original, danger, uvBounds);
        if (!replacements.TryGetValue(key, out Material replacement))
        {
            replacement = new Material(template) { name = original.name + (danger ? " (Time Skill Danger)" : " (Time Skill)"), hideFlags = HideFlags.DontSave };
            replacement.CopyPropertiesFromMaterial(original);
            replacement.SetFloat(DangerId, danger ? 1f : 0f);
            replacement.SetVector(SpriteUVBoundsId, uvBounds);
            replacements.Add(key, replacement);
        }

        originals[renderer] = original;
        renderer.sharedMaterial = replacement;
    }

    /// <summary>굴절로 스프라이트 시트의 이웃 그림까지 읽지 않도록 실제 UV 사각형을 보관한다.</summary>
    private static Vector4 SpriteUVBounds(Renderer renderer)
    {
        if (renderer is not SpriteRenderer sprite || sprite.sprite == null)
            return new Vector4(0f, 0f, 1f, 1f);

        Vector2[] coordinates = sprite.sprite.uv;
        Vector2 minimum = Vector2.one;
        Vector2 maximum = Vector2.zero;
        foreach (Vector2 uv in coordinates)
        {
            minimum = Vector2.Min(minimum, uv);
            maximum = Vector2.Max(maximum, uv);
        }
        return new Vector4(minimum.x, minimum.y, maximum.x, maximum.y);
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
        float waveProgress = UpdateWave(strength);

        if (outputCamera != null)
        {
            float gray = originalBackground.grayscale;
            outputCamera.backgroundColor = Color.Lerp(originalBackground,
                new Color(gray, gray, gray, originalBackground.a), strength * waveProgress);
        }
    }

    /// <summary>시전자를 따라가는 중심과 공통 시각으로 원을 넓힌다. 실제 감속은 즉시 적용된다.</summary>
    private float UpdateWave(float strength)
    {
        if (strength <= 0f || race == null)
        {
            Shader.SetGlobalVector(WaveId, Vector4.zero);
            Shader.SetGlobalVector(WaveStyleId, Vector4.zero);
            Shader.SetGlobalVector(WaveDistortionId, Vector4.zero);
            return 0f;
        }

        Vector2 center = race.TimeSkillCenter;
        float softness = race.SkillWaveSoftness;
        if (observedWaveStart != race.TimeSkillStartedAt)
        {
            observedWaveStart = race.TimeSkillStartedAt;
            // 발동 순간 화면 모서리까지의 거리를 보관해 카메라가 움직여도 확산 속도를 유지한다.
            if (outputCamera != null && outputCamera.orthographic)
            {
                Vector2 distance = center - (Vector2)outputCamera.transform.position;
                float x = Mathf.Abs(distance.x) + outputCamera.orthographicSize * outputCamera.aspect;
                float y = Mathf.Abs(distance.y) + outputCamera.orthographicSize;
                waveReach = new Vector2(x, y).magnitude + softness;
            }
            else
                waveReach = 50f;
        }

        float progress = Mathf.Clamp01((float)(NetworkTime.time - race.TimeSkillStartedAt) / race.SkillWaveDuration);
        // 앞 65% 동안 반경 15%까지 감속해 거의 멈춘 뒤, 남은 시간에 빠르게 가속한다.
        // 두 구간의 연결 속도를 0으로 맞춰 반경이 튀거나 역방향으로 움직이지 않게 한다.
        float expansion;
        if (progress < 0.65f)
        {
            float remaining = 1f - progress / 0.65f;
            expansion = 0.15f * (1f - remaining * remaining * remaining);
        }
        else
        {
            float acceleration = (progress - 0.65f) / 0.35f;
            expansion = 0.15f + 0.85f * acceleration * acceleration * acceleration;
        }
        Shader.SetGlobalVector(WaveId, new Vector4(center.x, center.y, waveReach * expansion, progress < 1f ? 1f : 0f));
        Shader.SetGlobalVector(WaveStyleId, new Vector4(softness, 0f, 0f, 0f));
        Shader.SetGlobalVector(WaveDistortionId, new Vector4(race.SkillWaveDistortion,
            race.SkillWaveDistortionWidth, race.SkillWaveWavelength, 1f - expansion));
        return progress;
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
        Shader.SetGlobalVector(WaveId, Vector4.zero);
        Shader.SetGlobalVector(WaveStyleId, Vector4.zero);
        Shader.SetGlobalVector(WaveDistortionId, Vector4.zero);

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
        Shader.SetGlobalVector(WaveId, Vector4.zero);
        Shader.SetGlobalVector(WaveStyleId, Vector4.zero);
        Shader.SetGlobalVector(WaveDistortionId, Vector4.zero);

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
