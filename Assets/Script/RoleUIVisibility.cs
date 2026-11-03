using Mirror;
using UnityEngine;

/// <summary>네트워크 오브젝트는 켜 둔 채 공격자의 구매·골드·인벤토리 UI만 숨긴다.</summary>
public sealed class RoleUIVisibility : MonoBehaviour
{
    [SerializeField] private CanvasGroup[] defenderPanels;
    private PlayerState localPlayer;
    private bool? previousVisible;

    /// <summary>로컬 플레이어의 역할이 바뀔 때만 UI 표시와 클릭 허용 상태를 갱신한다.</summary>
    private void LateUpdate()
    {
        if (localPlayer == null && NetworkClient.localPlayer != null)
            localPlayer = NetworkClient.localPlayer.GetComponent<PlayerState>();

        bool visible = localPlayer != null && !localPlayer.IsAttacker;

        if (previousVisible == visible)
            return;

        previousVisible = visible;

        foreach (CanvasGroup panel in defenderPanels)
        {
            if (panel == null)
                continue;

            panel.alpha = visible ? 1f : 0f;
            panel.interactable = visible;
            panel.blocksRaycasts = visible;
        }
    }
}
