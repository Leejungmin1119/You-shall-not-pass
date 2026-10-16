using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 개인 점수를 변경하고 모든 접속자에게 동기화한다.
/// </summary>
public sealed class PlayerScore : NetworkBehaviour
{
    [SerializeField, Min(1)] private int WinScore = 10;
    [SerializeField, Range(2, 10)] private int GoalScore = 2;
    [SyncVar(hook = nameof(OnScoreChanged)), SerializeField] private int score;
    public int Score => score;

    /// <summary>
    /// 서버에서 새 플레이어의 점수를 초기화한다.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        score = 0;
    }

    /// <summary>
    /// 서버에서 골인 점수를 추가하고 승리 점수로 제한한다.
    /// </summary>
    [Server]
    public void AddGoalScore()
    {
        int limit = Mathf.Max(1, WinScore);
        score = (int)System.Math.Min((long)score + Mathf.Clamp(GoalScore, 2, 10), limit);
        Debug.Log($"골인! 현재 점수: {score}");
        WinCheck();
    }

    [Server]
    public void ServerAddScore(int amount)
    {
        if (amount <= 0) return;
        var race = RaceSession.Instance;
        int limit = race != null ? race.Rules.winScore : Mathf.Max(1, WinScore);
        score = (int)System.Math.Min((long)score + amount, limit);
        race?.CheckScoreLimit();
    }

    private void OnScoreChanged(int previous, int current)
        => GetComponent<NetworkPlayerScore>()?.RefreshDisplay();

    /// <summary>
    /// Inspector에서 지정한 승리 점수에 도달했는지 서버에서 확인한다.
    /// </summary>
    [Server]
    private void WinCheck()
    {
        if (score >= Mathf.Max(1, WinScore))
            Debug.Log("승리하셨습니다.");
    }
}
