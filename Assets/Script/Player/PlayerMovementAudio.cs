using UnityEngine;

/// <summary>
/// 화면에 표시 중인 자세를 읽어 걷기·달리기 발소리와 착지음을 낸다.
/// 자세는 이미 모든 참가자에게 동기화되므로 소리를 따로 전송하지 않고 각 화면에서 직접 판정한다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30)] // PlayerIdleBoil이 이번 프레임의 그림을 정한 뒤에 읽는다.
[RequireComponent(typeof(PlayerIdleBoil), typeof(PlayerState))]
public sealed class PlayerMovementAudio : MonoBehaviour
{
    [Header("발소리")]
    [Tooltip("최고 속도 대비 이 비율 이상이면 달리기 소리를 냅니다. 잭오랜턴의 달리기 그림 기준과 같습니다.")]
    [SerializeField, Range(0.1f, 1f)] private float runSoundSpeedRatio = 0.75f;
    [Tooltip("발소리 사이의 최소 간격(초). 걷기·달리기 전환 순간의 겹침을 막습니다.")]
    [SerializeField, Min(0f)] private float stepCooldown = 0.09f;

    [Header("착지")]
    [Tooltip("이 시간(초)보다 짧게 뜬 경우는 착지로 치지 않습니다.")]
    [SerializeField, Min(0f)] private float minAirSeconds = 0.06f;
    [Tooltip("착지 직후 첫 발소리까지의 간격(초).")]
    [SerializeField, Min(0f)] private float stepDelayAfterLanding = 0.12f;
    [Tooltip("가장 약한 착지의 음량 비율. 최고 낙하 속도에서 1이 됩니다.")]
    [SerializeField, Range(0f, 1f)] private float softLandingVolume = 0.65f;
    [SerializeField, Min(0f)] private float softLandingSpeed = 1f;

    [Header("반복 완화")]
    [SerializeField, Range(0f, 0.2f)] private float pitchVariation = 0.03f;
    [SerializeField, Range(0f, 0.5f)] private float volumeVariation = 0.06f;

    // 정상 이동으로는 나올 수 없는 속도. 위치 복구(부활·교대)를 낙하로 세지 않기 위한 기준이다.
    private const float TeleportSpeed = 40f;
    // 제자리에서 자세만 바뀐 경우(부활 직후의 이전 공중 자세)를 착지로 세지 않는다.
    private const float MinLandingSpeed = 0.5f;

    private PlayerIdleBoil visual;
    private PlayerState state;
    private PlayerMovement movement;

    private bool initialized;
    private bool wasAirborne;
    private float airSeconds;
    private float fallSpeed;
    private float previousY;
    private float nextStepTime;
    private int lastHalf = -1;
    private PlayerAnimationMode lastMode;

    /// <summary>자세 표시 담당과 생존·역할 상태를 보관한다.</summary>
    private void Awake()
    {
        visual = GetComponent<PlayerIdleBoil>();
        state = GetComponent<PlayerState>();
        movement = GetComponent<PlayerMovement>();
    }

    /// <summary>움직일 수 있는 캐릭터만 판정한다. 수비자·사망·출발 대기 중에는 기록을 비운다.</summary>
    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        if (delta <= 0f)
            return;

        if (!state.CanMove)
        {
            ResetContacts();
            return;
        }

        float y = transform.position.y;
        float downSpeed = (previousY - y) / delta;
        previousY = y;

        if (!initialized || Mathf.Abs(downSpeed) > TeleportSpeed)
        {
            initialized = true;
            wasAirborne = visual.IsAirborne;
            airSeconds = 0f;
            fallSpeed = 0f;
            lastHalf = -1;
            return;
        }

        UpdateLanding(delta, downSpeed);
        UpdateSteps();
    }

    /// <summary>공중 자세가 끝난 순간에 한 번, 떨어진 속도에 맞는 크기로 낸다.</summary>
    private void UpdateLanding(float delta, float downSpeed)
    {
        bool airborne = visual.IsAirborne;

        if (airborne)
        {
            airSeconds += delta;
            fallSpeed = Mathf.Max(fallSpeed, downSpeed);
        }
        else
        {
            if (wasAirborne && airSeconds >= minAirSeconds && fallSpeed >= MinLandingSpeed)
            {
                float hardSpeed = movement != null && movement.moveStats != null
                    ? movement.moveStats.MaxFallSpeed : 9f;
                float strength = Mathf.InverseLerp(softLandingSpeed,
                    Mathf.Max(softLandingSpeed + 0.01f, hardSpeed), fallSpeed);
                Play(SoundType.Land, Mathf.Lerp(softLandingVolume, 1f, strength));
                nextStepTime = Time.time + stepDelayAfterLanding;
            }

            airSeconds = 0f;
            fallSpeed = 0f;
        }

        wasAirborne = airborne;
    }

    /// <summary>걸음 그림의 첫 장면과 가운데 장면이 발이 땅에 닿는 순간이다. 주기의 절반이 바뀔 때마다 한 번 낸다.</summary>
    private void UpdateSteps()
    {
        PlayerAnimationState shown = visual.CurrentAnimation;
        int count = visual.CurrentFrameCount;

        if (!visual.IsWalking || count < 2)
        {
            lastHalf = -1;
            return;
        }

        // 걷기와 달리기 그림이 바뀌면 재생 위치가 처음으로 돌아가므로 새 걸음으로 본다.
        if (shown.Mode != lastMode)
        {
            lastMode = shown.Mode;
            lastHalf = -1;
        }

        int half = visual.CurrentFrame * 2 / count;
        if (half == lastHalf)
            return;

        lastHalf = half;
        if (Time.time < nextStepTime)
            return;

        // 인형·기사는 달리기 그림이 따로 없어 재생 속도 비율로 달리기를 가린다.
        bool running = shown.Mode == PlayerAnimationMode.Run ||
            shown.PlaybackRatio >= runSoundSpeedRatio;
        Play(running ? SoundType.RunStep : SoundType.WalkStep, 1f);
        nextStepTime = Time.time + stepCooldown;
    }

    /// <summary>같은 음원이 이어져도 기계적으로 들리지 않게 음높이와 크기를 조금씩 달리한다.</summary>
    private void Play(SoundType type, float volumeScale)
    {
        float pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        float volume = volumeScale * Random.Range(1f - volumeVariation, 1f);
        SoundPlayer.Play(type, transform.position, volume, pitch);
    }

    /// <summary>다시 움직이기 시작할 때 이전 위치와 자세로 착지나 발소리가 나지 않게 한다.</summary>
    private void ResetContacts()
    {
        initialized = false;
        wasAirborne = false;
        airSeconds = 0f;
        fallSpeed = 0f;
        lastHalf = -1;
    }

    /// <summary>비활성화 후 다시 켜져도 이전 기록을 이어 쓰지 않는다.</summary>
    private void OnDisable()
    {
        ResetContacts();
        nextStepTime = 0f;
    }
}
