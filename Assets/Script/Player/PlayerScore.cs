using System;
using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 개인 점수를 변경하고 모든 접속자에게 동기화한다.
/// </summary>
public sealed class PlayerScore : NetworkBehaviour
{
    [SerializeField, Min(1)] private int WinScore = 10;
    [SerializeField, Range(2, 10)] private int GoalScore = 2;
    [SerializeField, Min(1)] private int HitScore = 1;
    [SyncVar(hook = nameof(OnScoreChanged)), SerializeField] private int score;
    public int Score => score;

    /// <summary>
    /// 각 화면에 새 점수가 도착했을 때 알린다. 이름표와 HUD 점수판이 구독한다.
    /// 호스트에서는 서버가 값을 바꾸는 순간 함께 호출된다.
    /// </summary>
    public event Action<int> ScoreChanged;

    private void OnScoreChanged(int oldScore, int newScore)
    {
        ScoreChanged?.Invoke(newScore);
    }

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

    /// <summary>
    /// 서버에서 함정 적중 점수를 추가하고 승리 점수로 제한한다.
    /// </summary>
    [Server]
    public void AddHitScore()
    {
        int limit = Mathf.Max(1, WinScore);
        score = (int)System.Math.Min((long)score + Mathf.Max(1, HitScore), limit);
        Debug.Log($"적중! 현재 점수: {score}");
        WinCheck();
    }

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
