using Mirror;
using UnityEngine;

/// <summary>기본 달리기를 설정 시간 유지하면 지상에서 반복음을 모든 참가자에게 공유한다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerState))]
public sealed class PlayerRunAudio : NetworkBehaviour
{
    [Header("달리기 사운드")]
    [Tooltip("달리기 시작부터 재생까지의 대기 시간(초). 0이면 즉시 재생합니다. 공중에서도 이동 시간은 누적됩니다.")]
    [SerializeField, Min(0f)] private float startDelay = 0.1f;

    [SyncVar] private bool running;

    private PlayerMovement movement;
    private PlayerState state;
    private float runDuration;
    private bool requestedRunning;
    private AudioSource loopSource;

    public bool IsRunSoundPlaying => loopSource != null && loopSource.isPlaying;

    /// <summary>실제 이동 판정과 서버의 생존·역할 상태를 참조한다.</summary>
    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        state = GetComponent<PlayerState>();
    }

    /// <summary>소유자가 달리기 시간을 누적하고 지상에서만 재생한다. 시작·정지 변경만 서버에 보낸다.</summary>
    private void LateUpdate()
    {
        if (isServer && !state.CanMove)
            running = false;

        if (!isClient)
            return;

        bool shouldPlay = running && state.CanMove;

        if (isLocalPlayer)
        {
            bool isRunning = state.CanMove && movement.IsRunningWithInput;
            float delay = Mathf.Max(0f, startDelay);
            runDuration = isRunning ? Mathf.Min(delay, runDuration + Time.deltaTime) : 0f;

            bool onGround = movement.Controller.IsGrounded() && !movement.IsJumping;
            shouldPlay = isRunning && onGround && runDuration >= delay;

            if (requestedRunning != shouldPlay)
            {
                requestedRunning = shouldPlay;
                CmdSetRunning(shouldPlay);
            }
        }

        SetPlayback(shouldPlay);
    }

    /// <summary>공격자의 유효한 요청만 공유한다. 위치·음원은 클라이언트가 임의로 지정하지 않는다.</summary>
    [Command]
    private void CmdSetRunning(bool play)
    {
        running = play && state.CanMove && !NetworkServer.isLoadingScene;
    }

    /// <summary>반복 재생은 한 번 시작하며, 조건 이탈 시 페이드 없이 바로 끊는다.</summary>
    private void SetPlayback(bool play)
    {
        if (play)
        {
            if (loopSource == null)
                loopSource = SoundPlayer.StartLoop(SoundType.Run, transform.position);

            return;
        }

        SoundPlayer.StopLoop(loopSource);
        loopSource = null;
    }

    /// <summary>접속 해제 시 사라진 플레이어의 반복음이 남지 않게 한다.</summary>
    public override void OnStopClient()
    {
        SetPlayback(false);
        base.OnStopClient();
    }

    /// <summary>비활성화·씬 종료 시 반복음과 대기 시간, 요청 상태를 초기화한다.</summary>
    private void OnDisable()
    {
        SetPlayback(false);
        runDuration = 0f;
        requestedRunning = false;
    }
}
