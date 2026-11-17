using Mirror;
using UnityEngine;

/// <summary>스테이지 배경음악을 반복 재생한다. 시간 스킬 중에는 피아노를 빼고 낮은 박동음만 남긴다.</summary>
[DisallowMultipleComponent]
public sealed class BgmPlayer : MonoBehaviour
{
    private const double ScheduleLeadTime = 0.15d;

    [Header("음원 (길이와 샘플레이트가 같은 반복 음원)")]
    [SerializeField] private AudioClip piano;
    [SerializeField] private AudioClip slowThump;

    [Header("음량")]
    [SerializeField, Range(0f, 1f)] private float pianoVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float slowThumpVolume = 0.3f;

    [Header("시간 스킬 전환 시간 (실제 시간, 초)")]
    [SerializeField, Min(0.01f)] private float pianoFadeOut = 0.7f;
    [SerializeField, Min(0.01f)] private float pianoFadeIn = 0.3f;
    [SerializeField, Min(0.01f)] private float thumpFadeIn = 0.12f;
    [SerializeField, Min(0.01f)] private float thumpFadeOut = 0.5f;

    [SerializeField] private RaceManager race;

    private AudioSource pianoSource;
    private AudioSource thumpSource;
    private float pianoLevel = 1f;
    private float thumpLevel;
    private bool started;

    /// <summary>두 음원을 미리 읽고 재생기를 준비한다. 화면이 없는 전용 서버에서는 재생하지 않는다.</summary>
    private void Awake()
    {
        if (Application.isBatchMode || piano == null || slowThump == null)
        {
            if (!Application.isBatchMode)
                Debug.LogError("BGM 음원 연결을 확인하세요.", this);

            enabled = false;
            return;
        }

        if (piano.samples != slowThump.samples || piano.frequency != slowThump.frequency)
            Debug.LogWarning("BGM 두 음원의 길이나 샘플레이트가 달라 반복할수록 박자가 어긋납니다.", this);

        if (race == null)
            race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);

        pianoSource = CreateSource(piano);
        thumpSource = CreateSource(slowThump);
        ApplyVolume();
    }

    /// <summary>거리 감쇠가 없는 반복 재생기를 만든다. 음량은 매 프레임 다시 정한다.</summary>
    private AudioSource CreateSource(AudioClip clip)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.clip = clip;

        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();

        return source;
    }

    /// <summary>두 음원을 같은 오디오 시각에 시작해 박자를 맞추고, 시간 스킬 강도에 따라 음량만 바꾼다.</summary>
    private void Update()
    {
        if (!started)
        {
            // 접속 없이 서버만 실행 중이면 소리를 내지 않는다.
            if (NetworkServer.active && !NetworkClient.active)
                return;

            if (piano.loadState != AudioDataLoadState.Loaded || slowThump.loadState != AudioDataLoadState.Loaded)
                return;

            double startAt = AudioSettings.dspTime + ScheduleLeadTime;
            pianoSource.PlayScheduled(startAt);
            thumpSource.PlayScheduled(startAt);
            started = true;
        }

        // 감속 중에는 1, 복귀 중에는 속도 곡선을 따라 0으로 돌아온다.
        float slow = race != null ? race.TimeSkillGrayscale : 0f;
        float delta = Time.unscaledDeltaTime;
        float pianoTarget = 1f - slow;

        pianoLevel = Mathf.MoveTowards(pianoLevel, pianoTarget,
            delta / (pianoTarget < pianoLevel ? pianoFadeOut : pianoFadeIn));
        thumpLevel = Mathf.MoveTowards(thumpLevel, slow,
            delta / (slow > thumpLevel ? thumpFadeIn : thumpFadeOut));
        ApplyVolume();
    }

    /// <summary>재생을 끊지 않고 음량만 바꿔 두 음원의 박자를 유지한다.</summary>
    private void ApplyVolume()
    {
        pianoSource.volume = pianoVolume * pianoLevel;
        thumpSource.volume = slowThumpVolume * thumpLevel;
    }

    /// <summary>비활성화와 씬 종료 시 두 음원을 함께 멈춘다. 다시 켜지면 처음부터 박자를 맞춰 시작한다.</summary>
    private void OnDisable()
    {
        if (pianoSource != null)
            pianoSource.Stop();

        if (thumpSource != null)
            thumpSource.Stop();

        started = false;
        pianoLevel = 1f;
        thumpLevel = 0f;
    }
}
