using UnityEngine;

/// <summary>
/// 골인 영역에 진입한 플레이어에게 골인 처리를 요청한다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public sealed class GoalZone : MonoBehaviour
{
    /// <summary>
    /// 자식 콜라이더를 포함하여 진입한 플레이어를 찾고 골인을 요청한다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerState player = other.GetComponentInParent<PlayerState>();
        if (player != null)
            player.TryCompleteGoal();
    }
}
