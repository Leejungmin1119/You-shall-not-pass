using Mirror;
using UnityEngine;

/// <summary>성공한 점프만 공용 사운드에 연결하고, 소유자 외의 참가자에게 한 번 전달한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerState))]
public sealed class PlayerJumpAudio : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerState state;
    private bool subscribed;

    /// <summary>자기 플레이어의 동작 알림과 서버 상태 검사에 필요한 참조를 보관한다.</summary>
    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        state = GetComponent<PlayerState>();
    }

    /// <summary>이동을 실제로 계산하는 소유자만 점프 알림을 구독한다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Subscribe();
    }

    /// <summary>비활성화 후 다시 켜져도 알림이 중복 구독되지 않도록 한다.</summary>
    private void OnEnable()
    {
        if (isLocalPlayer)
            Subscribe();
    }

    /// <summary>일반 점프와 벽점프의 공통 성공 알림을 한 번만 연결한다.</summary>
    private void Subscribe()
    {
        if (subscribed)
            return;

        movement.JumpStarted += OnJumpStarted;
        subscribed = true;
    }

    /// <summary>소유자는 즉시 듣고, 나머지 참가자의 재생은 서버에 요청한다.</summary>
    private void OnJumpStarted()
    {
        if (!isLocalPlayer || !state.CanMove)
            return;

        SoundPlayer.Play(SoundType.Jump, transform.position);
        CmdPlayJumpSound();
    }

    /// <summary>클라이언트가 임의의 음원이나 위치를 지정하지 못하도록 점프음만 전달한다.</summary>
    [Command]
    private void CmdPlayJumpSound()
    {
        if (state.CanMove && !NetworkServer.isLoadingScene)
            RpcPlayJumpSound(transform.position);
    }

    /// <summary>이미 즉시 재생한 소유자를 제외해 호스트와 원격 화면에서 중복을 막는다.</summary>
    [ClientRpc(includeOwner = false)]
    private void RpcPlayJumpSound(Vector2 position)
    {
        SoundPlayer.Play(SoundType.Jump, position);
    }

    /// <summary>접속 종료 시 구독을 정리한다.</summary>
    public override void OnStopClient()
    {
        Unsubscribe();
        base.OnStopClient();
    }

    /// <summary>플레이어 비활성화 시 구독을 정리한다.</summary>
    private void OnDisable() => Unsubscribe();

    /// <summary>여러 종료 경로에서도 한 번만 구독을 해제한다.</summary>
    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        movement.JumpStarted -= OnJumpStarted;
        subscribed = false;
    }
}
