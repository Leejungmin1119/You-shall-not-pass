using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum SoundType { Jump, CannonFire, StoneCrusherImpact, PlayerHit }

/// <summary>타입에 등록된 음원을 재생한다. 네트워크 전파와 동작 판정은 호출한 쪽이 담당한다.</summary>
[DisallowMultipleComponent]
public sealed class SoundPlayer : MonoBehaviour
{
    [Serializable]
    private sealed class SoundSetting
    {
        public SoundType Type;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 0.5f;
        public bool UseDistance;
        [Min(0f)] public float FullVolumeDistance = 5f;
        [Min(0.1f)] public float SilentDistance = 30f;
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public SoundSetting Setting;
        public Vector2 Position;
    }

    [Header("타입별 사운드")]
    [SerializeField] private SoundSetting[] sounds = Array.Empty<SoundSetting>();

    [Header("공통 재생 설정")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;
    [SerializeField, Range(4, 32)] private int voiceCount = 16;
    [SerializeField] private RaceManager race;
    [SerializeField] private Camera fallbackCamera;

    private static SoundPlayer current;
    private readonly Dictionary<SoundType, SoundSetting> lookup = new Dictionary<SoundType, SoundSetting>();
    private Voice[] voices;

    /// <summary>Domain Reload를 끄고 재실행해도 이전 씬의 참조를 남기지 않는다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => current = null;

    /// <summary>음원 조회표와 고정 개수의 재생기를 한 번 준비한다.</summary>
    private void Awake()
    {
        if (race == null)
            race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);

        if (fallbackCamera == null)
            fallbackCamera = Camera.main;

        foreach (SoundSetting setting in sounds)
        {
            if (setting == null)
                continue;

            if (lookup.ContainsKey(setting.Type))
                Debug.LogWarning($"중복 사운드 타입 설정: {setting.Type}", this);

            lookup[setting.Type] = setting;

            if (setting.Clip != null && setting.Clip.loadState == AudioDataLoadState.Unloaded)
                setting.Clip.LoadAudioData();
        }

        voices = new Voice[voiceCount];

        for (int index = 0; index < voices.Length; index++)
        {
            var child = new GameObject($"Sound Voice {index + 1}");
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            voices[index] = new Voice { Source = source };
        }
    }

    /// <summary>현재 게임 씬의 재생 담당을 등록한다. 씬을 넘어 유지하지 않는다.</summary>
    private void OnEnable()
    {
        if (current != null && current != this)
        {
            Debug.LogError("같은 씬에 SoundPlayer를 중복 배치할 수 없습니다.", this);
            enabled = false;
            return;
        }

        current = this;
    }

    /// <summary>호출부가 타입과 소리 발생 위치만 전달하는 공용 진입점이다.</summary>
    public static bool Play(SoundType type, Vector2 position)
    {
        if (current == null || (NetworkServer.active && !NetworkClient.active))
            return false;

        return current.PlayLocal(type, position);
    }

    /// <summary>비어 있는 재생기를 사용한다. 상한에 도달하면 재생 중인 소리를 끊지 않고 새 소리를 생략한다.</summary>
    private bool PlayLocal(SoundType type, Vector2 position)
    {
        if (AudioListener.pause && type != SoundType.PlayerHit)
            return false;

        if (!lookup.TryGetValue(type, out SoundSetting setting) || setting.Clip == null)
            return false;

        foreach (Voice voice in voices)
        {
            if (voice.Source.isPlaying)
                continue;

            voice.Setting = setting;
            voice.Position = position;
            voice.Source.clip = setting.Clip;
            voice.Source.ignoreListenerPause = type == SoundType.PlayerHit;
            voice.Source.volume = VolumeAt(voice, ListenerPosition());
            voice.Source.Play();
            return true;
        }

        return false;
    }

    /// <summary>재생 중인 소리만 거리 음량을 갱신한다. 새 오브젝트나 네트워크 메시지는 만들지 않는다.</summary>
    private void LateUpdate()
    {
        Vector2 listener = ListenerPosition();

        foreach (Voice voice in voices)
            if (voice.Source.isPlaying)
                voice.Source.volume = VolumeAt(voice, listener);
    }

    /// <summary>수비자도 관전 중인 공격자의 위치에서 듣는다. Tab 전체 보기는 청취 위치를 바꾸지 않는다.</summary>
    private Vector2 ListenerPosition()
    {
        if (race != null && race.Roles != null &&
            NetworkClient.spawned.TryGetValue(race.Roles.AttackerNetId, out NetworkIdentity attacker))
            return attacker.transform.position;

        return fallbackCamera != null ? (Vector2)fallbackCamera.transform.position : Vector2.zero;
    }

    /// <summary>XY 평면 거리로 계산해 카메라 Z축의 영향을 없애고 최대 거리에서는 무음으로 만든다.</summary>
    private float VolumeAt(Voice voice, Vector2 listener)
    {
        SoundSetting setting = voice.Setting;
        float gain = 1f;

        if (setting.UseDistance)
        {
            float near = Mathf.Max(0f, setting.FullVolumeDistance);
            float far = Mathf.Max(near + 0.1f, setting.SilentDistance);
            float distance = Vector2.Distance(listener, voice.Position);
            gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, distance));
        }

        return masterVolume * setting.Volume * gain;
    }

    /// <summary>씬 전환과 비활성화 시 재생을 끝내고 공용 참조를 정리한다.</summary>
    private void OnDisable()
    {
        if (current == this)
            current = null;

        if (voices != null)
            foreach (Voice voice in voices)
                if (voice.Source != null)
                    voice.Source.Stop();
    }
}
