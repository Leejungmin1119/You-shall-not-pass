using UnityEngine;

/// <summary>
/// 플래이어 코스튬 관리.
/// </summary>
public class PlayerCost : MonoBehaviour
{
    [Header("코스트 설정")]
    [SerializeField, Min(0)] private int startingCost = 100;
    [SerializeField, Min(0)] private int maxCost = 300;
    [SerializeField, Min(0.01f)] private float MoneyTimer = 5f;
    [SerializeField, Min(1)] private int GetCost = 10;

    private int currentCost;
    private float autoGainTimer;
    public int CurrentCost => currentCost;

    /// <summary>
    /// 코스튬 초기화
    /// </summary>
    public void Awake()
    {
        int safeMaxCost = Mathf.Max(0, maxCost);
        currentCost = Mathf.Clamp(startingCost, 0, safeMaxCost);
        autoGainTimer = 0f;

        if (maxCost < 0)
        {
            Debug.LogError("PlayerCost의 보유 상한은 0 이상이어야 합니다. 자동 획득을 중단합니다.", this);
        }

        if (MoneyTimer <= 0f || float.IsNaN(MoneyTimer) || float.IsInfinity(MoneyTimer))
        {
            Debug.LogError("PlayerCost의 획득 간격은 유한한 양수여야 합니다. 자동 획득을 중단합니다.", this);
        }

        if (GetCost <= 0)
        {
            Debug.LogError("PlayerCost의 획득량은 0보다 커야 합니다. 자동 획득을 중단합니다.", this);
        }
    }

    /// <summary>
    /// 서버에서 게임 시간 기준 지급 주기를 누적하고 지난 주기만큼 코스트를 지급한다.
    /// </summary>
    private void Update()
    {
        //예외처리
        if (maxCost < 0 ||
            MoneyTimer <= 0f ||
            float.IsNaN(MoneyTimer) ||
            float.IsInfinity(MoneyTimer) ||
            GetCost <= 0)
        {
            return;
        }

        autoGainTimer += Time.deltaTime;

        while (autoGainTimer >= MoneyTimer)
        {
            autoGainTimer = 0f;
            AddCost(GetCost);
        }
    }

    /// <summary>
    /// 서버에서 보유 상한을 넘지 않도록 지정한 양의 코스트를 지급한다.
    /// </summary>
    private void AddCost(int amount)
    {
        if (amount <= 0 || currentCost >= maxCost)
        {
            return;
        }

        currentCost = currentCost >= maxCost - amount
            ? maxCost
            : currentCost + amount;

        Debug.Log($"현제 코스트는 {currentCost}");
    }

    /// <summary>
    /// 코스트 확인 후 차감
    /// </summary>
    public bool TrySpendCost(int amount)
    {
        if (amount <= 0 || currentCost < amount)
        {
            return false;
        }

        currentCost -= amount;
        return true;
    }
}
