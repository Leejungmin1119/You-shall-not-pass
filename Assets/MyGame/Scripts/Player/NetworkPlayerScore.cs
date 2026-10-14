using Mirror;
using TMPro;
using UnityEngine;

/// <summary>
/// 머리 위 이름표에 이름과 점수를 표시한다.
/// 점수는 게임 규칙을 가진 PlayerScore 하나로 통일해서, 여기서는 읽기만 한다.
/// </summary>
public class NetworkPlayerScore : NetworkBehaviour
{
    [Header("화면 표시")]
    [SerializeField] private TMP_Text nameText;

    // 연습용 NetworkPlayer 프리팹에는 PlayerScore가 없어서 null일 수 있다. 그때는 이름만 표시한다.
    private PlayerScore gameScore;

    private void Awake()
    {
        gameScore = GetComponent<PlayerScore>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (gameScore != null) gameScore.ScoreChanged += OnScoreChanged;
        UpdatePlayerText();
        Debug.Log($"플레이어 생성: netId = {netId}");
    }

    public override void OnStopClient()
    {
        if (gameScore != null) gameScore.ScoreChanged -= OnScoreChanged;
        base.OnStopClient();
    }

    private void OnScoreChanged(int newScore)
    {
        UpdatePlayerText();
    }

    public void RefreshDisplay() => UpdatePlayerText();

    private void UpdatePlayerText()
    {
        if (nameText == null) return;
        var profile = GetComponent<NetworkPlayerProfile>();
        string displayName = profile != null ? profile.PlayerName : $"Player {netId}";
        nameText.richText = false;
        nameText.text = gameScore != null ? $"{displayName}\nScore: {gameScore.Score}" : displayName;
    }
}
