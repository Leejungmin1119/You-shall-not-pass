using UnityEngine;

/// <summary>피격 판정 담당이 현재 위험 상태를 표시 담당에게 제공한다.</summary>
public interface ITimeSkillDangerSource
{
    bool IsDangerous { get; }
}

/// <summary>함정의 위험 상태만 공통 셰이더에 전달한다. 색상 계산은 셰이더가 수행한다.</summary>
[DisallowMultipleComponent]
public sealed class TimeSkillDangerHighlight : MonoBehaviour
{
    private static readonly int DangerId = Shader.PropertyToID("_SkillDanger");
    private Renderer target;
    private MaterialPropertyBlock properties;
    private ITimeSkillDangerSource source;
    private MonoBehaviour sourceObject;
    private bool hasSource;
    private bool? previousDanger;
    private Collider2D shape;

    /// <summary>한 렌더러에 한 번만 표시 담당을 연결하고 기존 피격 상태를 참조한다.</summary>
    public static void Bind(Renderer renderer, ITimeSkillDangerSource owner)
    {
        var highlight = renderer.GetComponent<TimeSkillDangerHighlight>();

        if (highlight == null)
            highlight = renderer.gameObject.AddComponent<TimeSkillDangerHighlight>();

        highlight.target = renderer;
        highlight.shape = renderer.GetComponent<Collider2D>();
        highlight.source = owner;
        highlight.sourceObject = owner as MonoBehaviour;
        highlight.hasSource = owner != null;
        highlight.previousDanger = null;
    }

    /// <summary>상태가 바뀐 경우에만 렌더러의 속성을 갱신하고 기존 속성은 보존한다.</summary>
    private void LateUpdate()
    {
        if (target == null)
            target = GetComponent<Renderer>();

        bool dangerous = !hasSource || sourceObject != null && source.IsDangerous;
        dangerous &= shape == null || shape.enabled;
        SetDanger(dangerous);
    }

    /// <summary>공유 재질을 복제하지 않고 이 렌더러의 위험 표시 값만 변경한다.</summary>
    private void SetDanger(bool dangerous)
    {
        if (target == null || previousDanger == dangerous)
            return;

        properties ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        properties.SetFloat(DangerId, dangerous ? 1f : 0f);
        target.SetPropertyBlock(properties);
        previousDanger = dangerous;
    }

    /// <summary>표시 담당을 끄면 위험 강조 값도 해제한다.</summary>
    private void OnDisable()
    {
        SetDanger(false);
        previousDanger = null;
    }
}
