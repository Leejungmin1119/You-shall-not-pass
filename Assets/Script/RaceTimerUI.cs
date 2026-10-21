using Mirror;
using TMPro;
using UnityEngine;

/// <summary>TIMER 제목 아래에 현재 레이스의 남은 시간 하나만 표시한다.</summary>
public sealed class RaceTimerUI : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text countdownText;

    private int lastSeconds = -1;

    /// <summary>일반 진행과 골인 마감에 같은 시간 표시를 사용하며 초가 바뀔 때만 갱신한다.</summary>
    private void Update()
    {
        if (race == null)
            return;

        UpdateStartCountdown();

        int seconds;

        if (race.IsPreparing)
            seconds = Mathf.CeilToInt(race.RoundDuration);
        else if (race.Phase == RacePhase.Ended)
            seconds = 0;
        else
            seconds = RemainingSeconds(race.EndsAt);

        if (lastSeconds == seconds)
            return;

        lastSeconds = seconds;
        timeText.text = FormatTime(seconds);
    }

    /// <summary>서버 시각을 기준으로 중앙의 3·2·1과 짧은 출발 문구를 표시한다.</summary>
    private void UpdateStartCountdown()
    {
        if (countdownText == null)
            return;

        string text = string.Empty;
        double elapsed = NetworkTime.time - race.PhaseStartedAt;

        if (race.Phase == RacePhase.StartCountdown)
        {
            if (elapsed < race.CountdownThreeDuration)
                text = "3";
            else if (elapsed < race.CountdownThreeDuration + race.CountdownTwoDuration)
                text = "2";
            else
                text = "1";
        }
        else if (race.Phase == RacePhase.Racing && elapsed < 0.8)
        {
            text = "Start!";
        }

        if (countdownText.text != text)
            countdownText.text = text;
    }

    /// <summary>서버 시각과의 차이를 올림하여 시작 직후 20초부터 표시한다.</summary>
    private static int RemainingSeconds(double endsAt)
    {
        return Mathf.Max(0, Mathf.CeilToInt((float)(endsAt - NetworkTime.time)));
    }

    /// <summary>남은 시간을 두 자리 분과 초로 표시한다.</summary>
    private static string FormatTime(int seconds)
    {
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
