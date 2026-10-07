using UnityEngine;

/// <summary>
/// 플레이어 개인의 점수를 보관한다.
/// </summary>
public sealed class PlayerScore : MonoBehaviour
{
    [SerializeField] private int WinScore = 10;
    [SerializeField, Range(2,10)] private int GoalScore;

    [SerializeField] private int score;

    public int Score => score;

    /// <summary>
    /// 골인 점수 2점을 추가하고 최대 10점으로 제한한다.
    /// </summary>
    public void AddGoalScore()
    {
        score = Mathf.Clamp(score + GoalScore, 0, WinScore);
        Debug.Log($"골인! 현재 점수: {score}");
        WinCheck();
    }

    private void WinCheck()
    {
        if(score >= 10)
        {
            Debug.Log("승리하셨습니다.");
        }
    }
}
