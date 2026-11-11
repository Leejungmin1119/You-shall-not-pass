using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>서버의 사망 시각으로 히트스톱, 쓰러짐, 별 회전과 로컬 재생 속도를 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RaceManager))]
public sealed class DeathSequence : MonoBehaviour
{
    public const double TotalDuration = 6d;
    public const double SlowDuration = 3d;
    public const double HitStopDuration = 0.1d;

    [Header("12프레임 몸 동작")]
    [SerializeField] private Sprite[] dollBackward = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] dollForward = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] knightBackward = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] knightForward = Array.Empty<Sprite>();
    [SerializeField] private Sprite starSprite;

    private RaceManager race;
    private SpriteRenderer source;
    private SpriteRenderer corpse;
    private readonly SpriteRenderer[] stars = new SpriteRenderer[3];
    private Sprite[] frames;
    private bool active;
    private bool previousForceOff;
    private bool previousAudioPause;
    private float previousTimeScale;
    private double sequenceStartedAt = double.NegativeInfinity;
    private float visualScale;
    private MapViewController mapView;
    private AudioSource scream;
    private bool screamStarted;
    private readonly List<(CanvasGroup group, float alpha, bool interactable, bool blocksRaycasts)> hiddenUI = new();

    public int CurrentFrame { get; private set; }
    public bool IsShowing => active && corpse != null;
    public static bool IsUIHidden { get; private set; }

    /// <summary>같은 오브젝트의 레이스만 참조한다. 연출용 렌더러는 최초 피격 때 한 번 만든다.</summary>
    private void Awake() => race = GetComponent<RaceManager>();

    /// <summary>네트워크 시계는 유지하고 화면과 물리의 재생 속도만 변경한다.</summary>
    private void Update()
    {
        if (!race.IsDeathPresentation)
        {
            if (active)
                Restore();
            return;
        }

        if (!active)
        {
            active = true;
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;

            if (race.isClient)
                HideUI();
        }

        double elapsed = race.DeathElapsed;
        Time.timeScale = previousTimeScale * WorldSpeed(elapsed);

        if (race.isClient)
        {
            AudioListener.pause = previousAudioPause || elapsed < HitStopDuration;
            if (sequenceStartedAt != race.DeathStartedAt)
                BeginVisual();

            UpdateScream(elapsed);
        }
    }

    /// <summary>히트스톱 직후 비명을 한 번 시작하며 화면의 슬로모션과 무관하게 기본 속도로 재생한다.</summary>
    private void UpdateScream(double elapsed)
    {
        if (!screamStarted && elapsed >= HitStopDuration)
        {
            screamStarted = true;
            scream = SoundPlayer.PlayTrackedOnce(SoundType.DeathScream, race.DeathFeet);
        }

    }

    /// <summary>초기화 지연에도 플레이어 생성이 끝난 뒤 같은 연출을 현재 시점부터 표시한다.</summary>
    private void BeginVisual()
    {
        if (!NetworkClient.spawned.TryGetValue(race.DyingPlayerId, out NetworkIdentity identity))
            return;

        source = identity.GetComponent<SpriteRenderer>();
        var animator = identity.GetComponent<PlayerIdleBoil>();
        bool knight = animator != null && animator.Character == PlayerIdleBoil.CharacterStyle.Knight;
        frames = knight ? (race.DeathForward ? knightForward : knightBackward) :
            (race.DeathForward ? dollForward : dollBackward);

        if (frames.Length != 12 || frames[0] == null || starSprite == null)
        {
            Debug.LogError("사망 연출의 몸 12프레임과 별 Sprite 연결을 확인하세요.", this);
            sequenceStartedAt = race.DeathStartedAt;
            return;
        }

        if (corpse == null)
        {
            corpse = CreateRenderer("Death Body");
            for (int index = 0; index < stars.Length; index++)
                stars[index] = CreateRenderer($"Death Star {index + 1}");
        }

        previousForceOff = source.forceRenderingOff;
        source.forceRenderingOff = true;
        visualScale = source.bounds.size.y / Mathf.Max(0.01f, frames[0].bounds.size.y);
        corpse.transform.localScale = Vector3.one * visualScale;
        corpse.sharedMaterial = source.sharedMaterial;
        corpse.sortingLayerID = source.sortingLayerID;
        corpse.sortingOrder = source.sortingOrder + 1;
        corpse.color = Color.white;
        corpse.flipX = !race.DeathFacingRight;
        corpse.enabled = true;

        foreach (SpriteRenderer star in stars)
        {
            star.sprite = starSprite;
            star.sharedMaterial = source.sharedMaterial;
            star.sortingLayerID = source.sortingLayerID;
            star.sortingOrder = corpse.sortingOrder + 1;
            float scale = 0.17f / Mathf.Max(0.01f, starSprite.bounds.size.x);
            star.transform.localScale = Vector3.one * scale;
        }

        sequenceStartedAt = race.DeathStartedAt;
        mapView = FindFirstObjectByType<MapViewController>();
        if (mapView != null)
            mapView.SetDeathTarget(corpse.transform);
        if (race.DeathElapsed < 1d)
            SoundPlayer.Play(SoundType.PlayerHit, race.DeathFeet);
    }

    /// <summary>몸은 한 번만 쓰러진다. 바닥 접촉 뒤에는 같은 프레임을 유지하고 별만 연속 회전한다.</summary>
    private void LateUpdate()
    {
        if (!active || corpse == null || source == null || frames == null || frames.Length != 12)
            return;

        double elapsed = race.DeathElapsed;
        float progress = Mathf.Clamp01((float)(WorldElapsed(Math.Min(elapsed, SlowDuration)) /
            WorldElapsed(SlowDuration)));
        CurrentFrame = Mathf.Min(11, Mathf.FloorToInt(progress * 12f));
        corpse.sprite = frames[CurrentFrame];
        float direction = (race.DeathFacingRight ? 1f : -1f) * (race.DeathForward ? 1f : -1f);
        float slide = Mathf.SmoothStep(0f, 1f, progress);
        corpse.transform.position = new Vector3(race.DeathFeet.x + direction * race.DeathWidth * slide,
            Mathf.Lerp(race.DeathFeet.y, race.DeathGroundY, slide), source.transform.position.z);

        Bounds bounds = corpse.bounds;
        bool headOnRight = race.DeathForward == race.DeathFacingRight;
        float headFraction = Mathf.Lerp(0.5f, headOnRight ? 0.76f : 0.24f, progress);
        Vector2 center = new Vector2(Mathf.Lerp(bounds.min.x, bounds.max.x, headFraction), bounds.max.y + 0.24f);
        double angle = WorldElapsed(elapsed) * Math.PI * 2d / 1.44d;

        for (int index = 0; index < stars.Length; index++)
        {
            stars[index].enabled = CurrentFrame >= 5;
            double phase = angle + index * Math.PI * 2d / 3d;
            // 월드 Y축은 위쪽이 양수이므로 음의 sin을 사용해 시계방향으로 돌린다.
            stars[index].transform.position = new Vector3(center.x + 0.38f * (float)Math.Cos(phase),
                center.y - 0.12f * (float)Math.Sin(phase), source.transform.position.z);
        }
    }

    /// <summary>처음 0.1초 정지한 뒤 0.2배에서 시작해 3초 시점에 정상 속도로 부드럽게 복귀한다.</summary>
    public static float WorldSpeed(double elapsed)
    {
        if (elapsed < HitStopDuration) return 0f;
        if (elapsed >= SlowDuration) return 1f;
        float t = (float)((elapsed - HitStopDuration) / (SlowDuration - HitStopDuration));
        return 0.2f + 0.8f * t * t;
    }

    /// <summary>속도 곡선을 적분해 느린 구간과 정상 구간의 위치·별 각도를 연속으로 이어 준다.</summary>
    public static double WorldElapsed(double elapsed)
    {
        double span = SlowDuration - HitStopDuration;
        double t = Math.Max(0d, Math.Min(elapsed - HitStopDuration, span));
        return 0.2d * t + 0.8d * t * t * t / (3d * span * span) + Math.Max(0d, elapsed - SlowDuration);
    }

    /// <summary>같은 씬의 자식으로 재사용할 표시 오브젝트를 만들며 충돌체는 추가하지 않는다.</summary>
    private SpriteRenderer CreateRenderer(string label)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        return child.AddComponent<SpriteRenderer>();
    }

    /// <summary>최상위 Canvas의 표시와 클릭만 막는다. UI 갱신과 네트워크 오브젝트는 계속 동작한다.</summary>
    private void HideUI()
    {
        IsUIHidden = true;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas)
                continue;

            CanvasGroup group = canvas.GetComponent<CanvasGroup>();

            if (group == null)
                group = canvas.gameObject.AddComponent<CanvasGroup>();

            hiddenUI.Add((group, group.alpha, group.interactable, group.blocksRaycasts));
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    /// <summary>원래 표시 설정으로 되돌린다. 자식의 공격자·수비자별 UI 설정은 그대로 유지한다.</summary>
    private void RestoreUI()
    {
        foreach (var saved in hiddenUI)
        {
            if (saved.group == null)
                continue;

            saved.group.alpha = saved.alpha;
            saved.group.interactable = saved.interactable;
            saved.group.blocksRaycasts = saved.blocksRaycasts;
        }

        hiddenUI.Clear();
        IsUIHidden = false;
    }

    /// <summary>교대·경기 종료·접속 해제 시 시간, 소리와 원래 그림을 반드시 복구한다.</summary>
    private void Restore()
    {
        SoundPlayer.Stop(scream);
        scream = null;
        screamStarted = false;

        RestoreUI();

        if (mapView != null)
            mapView.SetDeathTarget(null);
        if (source != null)
            source.forceRenderingOff = previousForceOff;
        if (corpse != null)
            corpse.enabled = false;
        foreach (SpriteRenderer star in stars)
            if (star != null) star.enabled = false;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = previousAudioPause;
        source = null;
        active = false;
        sequenceStartedAt = double.NegativeInfinity;
    }

    /// <summary>씬을 닫는 도중에도 전역 재생 속도와 음소거 상태를 남기지 않는다.</summary>
    private void OnDisable()
    {
        if (active) Restore();
    }
}
