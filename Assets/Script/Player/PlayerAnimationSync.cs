using System;
using Mirror;
using UnityEngine;

public enum PlayerAnimationMode : byte
{
    Idle,
    IdleBoil,
    Walk,
    Run,
    Rising,
    Apex,
    Falling,
    WallSlide
}

public enum PlayerAirPose : byte
{
    Standing,
    Moving,
    Running
}

/// <summary>그림의 상태와 재생 비율만 전달한다. 개별 스프라이트 프레임은 전송하지 않는다.</summary>
[Serializable]
public struct PlayerAnimationState : IEquatable<PlayerAnimationState>
{
    public const byte PlaybackSteps = 20;

    public PlayerAnimationMode Mode;
    public PlayerAirPose AirPose;
    public bool FlipX;
    public byte PlaybackStep;

    public float PlaybackRatio => PlaybackStep / (float)PlaybackSteps;

    /// <summary>소유자가 결정한 표시 상태를 하나의 값으로 묶는다.</summary>
    public PlayerAnimationState(PlayerAnimationMode mode, PlayerAirPose airPose, bool flipX,
        byte playbackStep = PlaybackSteps)
    {
        Mode = mode;
        AirPose = airPose;
        FlipX = flipX;
        PlaybackStep = playbackStep;
    }

    /// <summary>자세 전환과 재생 속도 조정을 구분한다.</summary>
    public bool HasSamePose(PlayerAnimationState other)
        => Mode == other.Mode && AirPose == other.AirPose && FlipX == other.FlipX;

    /// <summary>같은 상태를 반복 전송하거나 SyncVar를 불필요하게 갱신하지 않는다.</summary>
    public bool Equals(PlayerAnimationState other)
        => HasSamePose(other) && PlaybackStep == other.PlaybackStep;

    /// <summary>박싱된 상태도 동일한 비교 규칙을 사용한다.</summary>
    public override bool Equals(object obj) => obj is PlayerAnimationState other && Equals(other);

    /// <summary>상태 필드를 겹치지 않는 비트에 배치해 해시 값을 만든다.</summary>
    public override int GetHashCode()
        => (int)Mode | ((int)AirPose << 4) | (PlaybackStep << 8) | (FlipX ? 1 << 16 : 0);
}

/// <summary>소유자의 애니메이션 결정을 서버를 통해 관찰자에게 전달한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(PlayerIdleBoil), typeof(PlayerState))]
public sealed class PlayerAnimationSync : NetworkBehaviour
{
    [SyncVar] private PlayerAnimationState currentState = new PlayerAnimationState(
        PlayerAnimationMode.Idle, PlayerAirPose.Standing, false);

    private PlayerState player;
    private PlayerAnimationState lastSent;
    private bool hasSent;
    private float nextPlaybackSendTime;
    private const float PlaybackSendInterval = 0.1f;

    public PlayerAnimationState CurrentState => currentState;

    /// <summary>애니메이션 요청을 확인할 플레이어 상태를 보관한다.</summary>
    private void Awake() => player = GetComponent<PlayerState>();

    /// <summary>새 소유권에서 첫 상태는 반드시 전송한다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        hasSent = false;
        nextPlaybackSendTime = 0f;
    }

    /// <summary>자세 변경은 즉시 보내고, 재생 비율만 변하면 최대 초당 10회로 제한한다.</summary>
    public void Publish(PlayerAnimationState next)
    {
        if (!isClient || !isLocalPlayer || (hasSent && lastSent.Equals(next)))
            return;

        if (hasSent && lastSent.HasSamePose(next) && Time.unscaledTime < nextPlaybackSendTime)
            return;

        lastSent = next;
        hasSent = true;
        nextPlaybackSendTime = Time.unscaledTime + PlaybackSendInterval;

        CmdSetAnimation(next);
    }

    /// <summary>소유자의 표시 상태만 수락하며, 조작 불가 상태에서는 이동 자세를 허용하지 않는다.</summary>
    [Command]
    private void CmdSetAnimation(PlayerAnimationState next)
    {
        if (next.Mode > PlayerAnimationMode.WallSlide || next.AirPose > PlayerAirPose.Running ||
            next.PlaybackStep > PlayerAnimationState.PlaybackSteps)
            return;

        if (!player.CanMove && next.Mode != PlayerAnimationMode.Idle &&
            next.Mode != PlayerAnimationMode.IdleBoil)
            next = new PlayerAnimationState(PlayerAnimationMode.Idle, PlayerAirPose.Standing, next.FlipX);

        currentState = next;
    }
}
