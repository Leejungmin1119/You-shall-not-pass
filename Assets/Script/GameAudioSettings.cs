using UnityEngine;

public enum GameAudioChannel { Master, Music, Effects }

/// <summary>자기 PC의 마스터·배경음·효과음 볼륨을 보관하고 씬 전환 후에도 유지한다.</summary>
public static class GameAudioSettings
{
    private const string MasterKey = "Audio.MasterVolume";
    private const string MusicKey = "Audio.MusicVolume";
    private const string EffectsKey = "Audio.EffectsVolume";
    private static bool loaded;
    private static bool dirty;
    private static float master = 1f;
    private static float music = 1f;
    private static float effects = 1f;

    public static float MusicVolume => GetVolume(GameAudioChannel.Music);
    public static float EffectsVolume => GetVolume(GameAudioChannel.Effects);

    /// <summary>도메인 재로드를 생략해도 실행 시작 시 저장값을 다시 읽는다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        loaded = false;
        dirty = false;
    }

    /// <summary>사운드 재생기가 만들어지기 전에 저장된 볼륨을 적용한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Load()
    {
        if (loaded)
            return;

        master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));
        effects = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f));
        loaded = true;
        AudioListener.volume = master;
    }

    /// <summary>슬라이더와 재생기가 사용하는 0~1 범위의 현재 음량을 반환한다.</summary>
    public static float GetVolume(GameAudioChannel channel)
    {
        Load();

        switch (channel)
        {
            case GameAudioChannel.Master: return master;
            case GameAudioChannel.Music: return music;
            default: return effects;
        }
    }

    /// <summary>드래그 중에는 즉시 반영하고 저장소의 메모리 값만 갱신한다.</summary>
    public static void SetVolume(GameAudioChannel channel, float value)
    {
        Load();
        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(GetVolume(channel), value))
            return;

        switch (channel)
        {
            case GameAudioChannel.Master:
                master = value;
                AudioListener.volume = value;
                PlayerPrefs.SetFloat(MasterKey, value);
                break;
            case GameAudioChannel.Music:
                music = value;
                PlayerPrefs.SetFloat(MusicKey, value);
                break;
            case GameAudioChannel.Effects:
                effects = value;
                PlayerPrefs.SetFloat(EffectsKey, value);
                break;
        }

        dirty = true;
    }

    /// <summary>설정을 닫거나 게임을 떠날 때만 디스크에 저장해 드래그 중 저장 비용을 피한다.</summary>
    public static void Save()
    {
        if (!dirty)
            return;

        PlayerPrefs.Save();
        dirty = false;
    }
}
