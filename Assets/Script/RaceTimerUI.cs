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
    private Vector3 countdownScale;
    private Quaternion countdownRotation;

    /// <summary>카운트다운만 크고 선명하게 꾸미고 원래 배치 방향을 기억한다.</summary>
    private void Awake()
    {
        if (countdownText == null)
            return;

        countdownScale = countdownText.rectTransform.localScale;
        countdownRotation = countdownText.rectTransform.localRotation;

        countdownText.fontSize = 120f;
        countdownText.color = Color.white;
        countdownText.outlineColor = Color.black;
        countdownText.outlineWidth = 0.2f;
        // Mobile SDF는 외곽선 수치와 함께 전용 셰이더 기능도 켜야 한다.
        // fontMaterial은 TMP가 관리하는 개별 인스턴스라 다른 글자에는 영향을 주지 않는다.
        countdownText.fontMaterial.EnableKeyword("OUTLINE_ON");
        countdownText.raycastTarget = false;
    }

    /// <summary>중간에 비활성화되어도 회전하거나 커진 상태를 남기지 않는다.</summary>
    private void OnDisable()
    {
        if (countdownText == null)
            return;

        countdownText.text = string.Empty;
        countdownText.rectTransform.localScale = countdownScale;
        countdownText.rectTransform.localRotation = countdownRotation;
        countdownText.alpha = 1f;
    }

    /// <summary>교대와 무관한 경기 전체 남은 시간을 초가 바뀔 때만 갱신한다.</summary>
    private void Update()
    {
        if (race == null)
            return;

        UpdateStartCountdown();

        int seconds;

        if (race.IsPreparing)
            seconds = race.MatchEndsAt > 0 ? RemainingSeconds(race.MatchEndsAt) : Mathf.CeilToInt(race.RoundDuration);
        else if (race.Phase == RacePhase.Ended)
            seconds = 0;
        else
            seconds = Mathf.Max(0, Mathf.CeilToInt((float)race.RemainingTime));

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
        double elapsed = System.Math.Max(0d, NetworkTime.time - race.PhaseStartedAt);
        double digitElapsed = elapsed;
        float duration = 0.8f;

        if (race.Phase == RacePhase.StartCountdown)
        {
            if (elapsed < race.CountdownThreeDuration)
            {
                text = "3";
                duration = race.CountdownThreeDuration;
            }
            else if (elapsed < race.CountdownThreeDuration + race.CountdownTwoDuration)
            {
                text = "2";
                digitElapsed -= race.CountdownThreeDuration;
                duration = race.CountdownTwoDuration;
            }
            else
            {
                text = "1";
                digitElapsed -= race.CountdownThreeDuration + race.CountdownTwoDuration;
                duration = race.StartCountdownDuration - race.CountdownThreeDuration - race.CountdownTwoDuration;
            }
        }
        else if (race.Phase == RacePhase.Racing && elapsed < 0.8)
        {
            text = "Start!";
        }

        if (countdownText.text != text)
            countdownText.text = text;

        if (text.Length > 0)
            AnimateCountdown(Mathf.Clamp01((float)(digitElapsed / Mathf.Max(0.01f, duration))));
    }

    /// <summary>빠르게 확대하고 살짝 되돌아온 뒤, 끝부분에서 회전하며 작아진다.</summary>
    private void AnimateCountdown(float progress)
    {
        float scale;
        float rotation = 0f;
        float alpha = 1f;

        // 각 숫자의 시간 중 앞 18%는 튀어나오고, 다음 12%는 제 크기로 돌아온다.
        if (progress < 0.18f)
        {
            float t = progress / 0.18f;
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            scale = Mathf.LerpUnclamped(0.3f, 1.15f, easeOut);
        }
        else if (progress < 0.3f)
        {
            float t = Mathf.SmoothStep(0f, 1f, (progress - 0.18f) / 0.12f);
            scale = Mathf.Lerp(1.15f, 1f, t);
        }
        else if (progress < 0.8f)
        {
            scale = 1f;
        }
        else
        {
            // 마지막 20%에서 시계 방향으로 돌아가며 사라져 다음 숫자와 구분한다.
            float t = Mathf.SmoothStep(0f, 1f, (progress - 0.8f) / 0.2f);
            scale = 1f - t;
            rotation = -90f * t;
            alpha = 1f - t;
        }

        countdownText.rectTransform.localScale = countdownScale * scale;
        countdownText.rectTransform.localRotation = countdownRotation * Quaternion.Euler(0f, 0f, rotation);
        countdownText.alpha = alpha;
    }

    /// <summary>서버의 종료 시각까지 남은 초를 올림해 표시한다.</summary>
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
