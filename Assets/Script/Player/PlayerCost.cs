using Mirror;
using UnityEngine;

/// <summary>
/// 서버에서 개인 코스트를 지급하고 구매 요청을 처리한다.
/// </summary>
public class PlayerCost : NetworkBehaviour
{
    [Header("코스트 설정")]
    [SerializeField, Min(0)] private int startingCost = 100;
    [SerializeField, Min(0)] private int maxCost = 300;
    [SerializeField, Min(0.01f)] private float MoneyTimer = 5f;
    [SerializeField, Min(1)] private int GetCost = 10;

    [SyncVar] private int currentCost;
    private float autoGainTimer;
    private bool canAutoGain;
    public int CurrentCost => currentCost;

    /// <summary>
    /// 서버에서 초기 코스트를 설정하고 지급 설정을 확인한다.
    /// </summary>
    public override void OnStartServer()
    {
        if (RaceSession.Instance != null)
        {
            var rules = RaceSession.Instance.Rules;
            startingCost = rules.startCost;
            maxCost = rules.costMax;
            MoneyTimer = rules.costTickSeconds;
            GetCost = rules.costTickAmount;
        }
        currentCost = Mathf.Clamp(startingCost, 0, Mathf.Max(0, maxCost));
        autoGainTimer = 0f;

        canAutoGain = maxCost >= 0 && GetCost > 0 && MoneyTimer > 0f &&
            !float.IsNaN(MoneyTimer) && !float.IsInfinity(MoneyTimer);

        if (!canAutoGain)
            Debug.LogError("코스트 상한, 지급 간격과 지급량을 확인하세요. 자동 지급을 중단합니다.", this);
    }

    /// <summary>
    /// 로비를 제외한 게임에서 서버의 게임 시간에 따라 코스트를 지급한다.
    /// </summary>
    private void Update()
    {
        if (RaceSession.Instance != null && !RaceSession.Instance.IsRacing) return;
        if (!isServer || !canAutoGain)
            return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            return;

        autoGainTimer += Time.deltaTime;
        while (autoGainTimer >= MoneyTimer)
        {
            autoGainTimer -= MoneyTimer;
            AddCost(GetCost);
        }
    }

    /// <summary>
    /// 서버에서 보유 상한을 넘지 않도록 코스트를 지급한다.
    /// </summary>
    [Server]
    private void AddCost(int amount)
    {
        if (amount <= 0 || currentCost >= maxCost)
            return;
        currentCost += Mathf.Min(amount, maxCost - currentCost);
    }

    /// <summary>
    /// 서버에서 잔액을 확인하고 충분하면 지정한 코스트를 차감한다.
    /// </summary>
    [Server]
    public bool TrySpendCost(int amount)
    {
        if (amount <= 0 || currentCost < amount)
            return false;
        currentCost -= amount;
        return true;
    }

    /// <summary>
    /// 소유한 플레이어에서 선택한 아이템 ID만 서버에 전달한다.
    /// </summary>
    public bool RequestCollect(uint itemId)
    {
        if (!isLocalPlayer || !isClient)
            return false;
        if (RaceSession.Instance != null)
            return GetComponent<NetworkRaceInventory>()?.RequestBuy(itemId, false) == true;
        CmdTryCollect(itemId);
        return true;
    }

    /// <summary>
    /// 서버의 컨베이어에 구매를 요청하고 요청자에게만 결과를 돌려준다.
    /// </summary>
    [Command]
    private void CmdTryCollect(uint itemId)
    {
        if (RaceSession.Instance != null) return;
        ConveyorController conveyor = FindFirstObjectByType<ConveyorController>();
        bool succeeded = conveyor != null && conveyor.TryCollect(itemId, this);
        TargetCollectResult(itemId, succeeded);
    }

    /// <summary>
    /// 구매자의 요청 대기를 해제하고 구매 결과를 표시한다.
    /// </summary>
    [TargetRpc]
    private void TargetCollectResult(uint itemId, bool succeeded)
    {
        ConveyorController conveyor = FindFirstObjectByType<ConveyorController>();
        if (conveyor != null)
            conveyor.CompleteCollectRequest(itemId);
        Debug.Log(succeeded ? "습득 완료." : "구매 실패: 아이템이 없거나 코스트가 부족합니다.");
    }
}
