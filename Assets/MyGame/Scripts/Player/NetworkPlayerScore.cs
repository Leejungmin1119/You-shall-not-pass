using Mirror;
using TMPro;
using UnityEngine;

/// <summary>
/// 기존 프리팹 연결을 유지하면서 머리 위에는 이름만 플레이어 색상으로 표시한다.
/// </summary>
public class NetworkPlayerScore : NetworkBehaviour
{
    [Header("화면 표시")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField, Min(0f)] private float nameGap = 0.2f;

    private PlayerState playerState;

    /// <summary>캐릭터 가로 중앙을 맞추고 머리 바로 위에 이름표를 배치한다.</summary>
    private void Awake()
    {
        playerState = GetComponent<PlayerState>();

        if (nameText == null || !TryGetComponent(out CapsuleCollider2D body))
            return;

        Vector3 position = nameText.transform.localPosition;
        position.x = body.offset.x;
        position.y = body.offset.y + body.size.y * 0.5f + nameGap;
        nameText.transform.localPosition = position;
        nameText.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>접속 직후 동기화된 이름과 색상을 표시한다.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdatePlayerText();
        Debug.Log($"플레이어 생성: netId = {netId}");
    }

    /// <summary>숨겨진 수비자나 사망한 캐릭터 위에 이름만 남지 않게 한다.</summary>
    private void LateUpdate()
    {
        if (nameText != null && playerState != null)
            nameText.enabled = playerState.IsVisible && playerState.Status == PlayerStatus.Alive;
    }

    /// <summary>이름 또는 선택 색상이 동기화되었을 때 표시를 갱신한다.</summary>
    public void RefreshDisplay() => UpdatePlayerText();

    /// <summary>점수·체력을 섞지 않고 이름만 선택한 플레이어 색상으로 표시한다.</summary>
    private void UpdatePlayerText()
    {
        if (nameText == null) return;
        var profile = GetComponent<NetworkPlayerProfile>();
        string displayName = profile != null ? profile.PlayerName : $"Player {netId}";
        nameText.richText = false;
        nameText.text = displayName;
        nameText.color = profile != null ? profile.PlayerColor : Color.white;
    }
}
