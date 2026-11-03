using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerEffects : NetworkBehaviour
{
    [Header("효과")]
    [SerializeField] private float flashDuration = 0.5f;

    private SpriteRenderer playerRenderer;
    private Color defaultColor;
    private Coroutine flashRoutine;

    private void Awake()
    {
        playerRenderer = GetComponentInChildren<SpriteRenderer>();

        if (playerRenderer != null)
        {
            defaultColor = playerRenderer.color;
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // 단색 사각형 시절에는 내 캐릭터를 초록으로 칠해 구분했지만, 캐릭터 그림에 색이 곱해져 원래 색이 사라진다.
        // 지금은 다른 플레이어가 반투명으로 보여(NetworkPlayerController) 칠하지 않고 그림 원래 색을 쓴다.

        Debug.Log($"내 플레이어입니다: netId = {netId}");
    }

    private void Update()
    {
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby) return;
        if (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby) return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            CmdRequestGlobalEffect();
        }

        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            CmdRequestPrivateEffect();
        }
    }

    [Command]
    private void CmdRequestGlobalEffect()
    {
        Debug.Log($"서버: 전체 효과 요청, netId = {netId}");
        RpcPlayGlobalEffect();
    }

    [ClientRpc]
    private void RpcPlayGlobalEffect()
    {
        PlayFlash(Color.red);
    }

    [Command]
    private void CmdRequestPrivateEffect()
    {
        Debug.Log($"서버: 개인 효과 요청, netId = {netId}");
        TargetPlayPrivateEffect(connectionToClient);
    }

    [TargetRpc]
    private void TargetPlayPrivateEffect(NetworkConnectionToClient target)
    {
        PlayFlash(Color.blue);
    }

    private void PlayFlash(Color flashColor)
    {
        if (playerRenderer == null) return;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashColor(flashColor));
    }

    private IEnumerator FlashColor(Color flashColor)
    {
        playerRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        playerRenderer.color = defaultColor;
        flashRoutine = null;
    }

    private void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        if (playerRenderer != null)
        {
            playerRenderer.color = defaultColor;
        }
    }
}
