using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum TurnCardPhase : byte { None, Dealing, Choosing, Burning, Revealing, Complete, ResultHold, Dismissing }

/// <summary>숫자는 공개 시점까지 0으로 보내고 선택한 사람만 먼저 공유한다.</summary>
public struct TurnCardState
{
    public uint OwnerId;
    public byte Number;
}

/// <summary>서버가 카드 선택을 확정하고 숫자가 큰 순서로 공격 기회를 순환한다.</summary>
public sealed class rolechangeManager : NetworkBehaviour
{
    public const int CardCount = 10;
    public const float DealDuration = 0.6f;
    public const float SelectionDuration = 10f;
    public const float BurnDuration = 0.7f;
    public const float RevealInterval = 0.7f;
    public const float WinnerDuration = 1f;
    public const float ResultHoldDuration = 1.5f;
    public const float DismissDuration = 0.65f;

    [SyncVar] private uint attackerNetId;
    [SyncVar] private TurnCardPhase cardPhase;
    [SyncVar] private double cardPhaseStartedAt;

    public readonly SyncList<TurnCardState> Cards = new SyncList<TurnCardState>();
    public readonly SyncList<uint> TurnOrder = new SyncList<uint>();

    // 실제 숫자는 서버 메모리에만 보관하고 공개할 때 한 장씩 동기화한다.
    private readonly byte[] hiddenNumbers = new byte[CardCount];
    private readonly List<uint> drawPlayers = new List<uint>(4);
    private readonly List<int> revealOrder = new List<int>(4);
    private int revealedCount;

    public uint AttackerNetId => attackerNetId;
    public TurnCardPhase CardPhase => cardPhase;
    public double CardPhaseStartedAt => cardPhaseStartedAt;
    public bool IsDrawing => cardPhase != TurnCardPhase.None && cardPhase != TurnCardPhase.Complete;

    /// <summary>동기화된 공격자 번호로 이동·피격·구매 권한을 판단한다.</summary>
    public bool IsAttacker(PlayerState player)
    {
        return player != null && attackerNetId != 0 && player.netId == attackerNetId;
    }

    /// <summary>새 경기에서 한 번만 1~10을 섞고 뒷면 10장을 배부한다.</summary>
    [Server]
    public void BeginCardDraw(IReadOnlyList<PlayerState> players)
    {
        attackerNetId = 0;
        Cards.Clear();
        TurnOrder.Clear();
        drawPlayers.Clear();
        revealOrder.Clear();
        revealedCount = 0;

        foreach (PlayerState player in players)
            drawPlayers.Add(player.netId);

        for (int index = 0; index < CardCount; index++)
            hiddenNumbers[index] = (byte)(index + 1);

        for (int index = CardCount - 1; index > 0; index--)
        {
            int other = Random.Range(0, index + 1);
            byte number = hiddenNumbers[index];
            hiddenNumbers[index] = hiddenNumbers[other];
            hiddenNumbers[other] = number;
        }

        for (int index = 0; index < CardCount; index++)
            Cards.Add(default);

        SetCardPhase(TurnCardPhase.Dealing, NetworkTime.time);
    }

    /// <summary>참가자의 첫 선택만 승인한다. 같은 카드 경합은 서버 도착 순서로 처리한다.</summary>
    [Server]
    public bool TryChooseCard(uint playerId, int index)
    {
        if (cardPhase != TurnCardPhase.Choosing ||
            NetworkTime.time >= cardPhaseStartedAt + SelectionDuration ||
            !drawPlayers.Contains(playerId) || index < 0 || index >= Cards.Count ||
            Cards[index].OwnerId != 0 || FindChosenCard(playerId) >= 0)
            return false;

        Cards[index] = new TurnCardState { OwnerId = playerId };
        return true;
    }

    /// <summary>특정 참가자가 이미 선택한 카드의 위치를 찾는다.</summary>
    public int FindChosenCard(uint playerId)
    {
        if (playerId == 0)
            return -1;

        for (int index = 0; index < Cards.Count; index++)
            if (Cards[index].OwnerId == playerId)
                return index;

        return -1;
    }

    /// <summary>서버 시각으로 선택 마감, 소각, 낮은 숫자부터의 공개를 진행한다.</summary>
    private void Update()
    {
        if (!isServer)
            return;

        double elapsed = NetworkTime.time - cardPhaseStartedAt;

        if (cardPhase == TurnCardPhase.Dealing && elapsed >= DealDuration)
            SetCardPhase(TurnCardPhase.Choosing, cardPhaseStartedAt + DealDuration);
        else if (cardPhase == TurnCardPhase.Choosing)
        {
            bool allChosen = true;

            foreach (uint playerId in drawPlayers)
                allChosen &= FindChosenCard(playerId) >= 0;

            if (allChosen || elapsed >= SelectionDuration)
                FinishSelection();
        }
        else if (cardPhase == TurnCardPhase.Burning && elapsed >= BurnDuration)
        {
            SetCardPhase(TurnCardPhase.Revealing, cardPhaseStartedAt + BurnDuration);
            RevealNextCard();
        }
        else if (cardPhase == TurnCardPhase.Revealing)
        {
            while (revealedCount < revealOrder.Count && elapsed >= revealedCount * RevealInterval)
                RevealNextCard();

            if (elapsed >= Mathf.Max(0, revealOrder.Count - 1) * RevealInterval + WinnerDuration)
                SetCardPhase(TurnCardPhase.ResultHold, NetworkTime.time);
        }
        else if (cardPhase == TurnCardPhase.ResultHold && elapsed >= ResultHoldDuration)
            SetCardPhase(TurnCardPhase.Dismissing, NetworkTime.time);
        else if (cardPhase == TurnCardPhase.Dismissing && elapsed >= DismissDuration)
        {
            TurnOrder.Clear();

            for (int index = revealOrder.Count - 1; index >= 0; index--)
                TurnOrder.Add(Cards[revealOrder[index]].OwnerId);

            attackerNetId = TurnOrder.Count > 0 ? TurnOrder[0] : 0;
            SetCardPhase(TurnCardPhase.Complete, NetworkTime.time);
        }
    }

    /// <summary>시간 내에 선택하지 않은 참가자에게 남은 카드를 배정하고 공개 순서를 확정한다.</summary>
    [Server]
    private void FinishSelection()
    {
        foreach (uint playerId in drawPlayers)
        {
            if (FindChosenCard(playerId) >= 0)
                continue;

            int start = Random.Range(0, CardCount);

            for (int offset = 0; offset < CardCount; offset++)
            {
                int index = (start + offset) % CardCount;

                if (Cards[index].OwnerId != 0)
                    continue;

                Cards[index] = new TurnCardState { OwnerId = playerId };
                break;
            }
        }

        revealOrder.Clear();

        for (int index = 0; index < Cards.Count; index++)
            if (Cards[index].OwnerId != 0)
                revealOrder.Add(index);

        revealOrder.Sort((left, right) => hiddenNumbers[left].CompareTo(hiddenNumbers[right]));
        SetCardPhase(TurnCardPhase.Burning, NetworkTime.time);
    }

    /// <summary>선택된 카드 한 장의 숫자만 공개해 다음 카드의 숫자는 계속 숨긴다.</summary>
    [Server]
    private void RevealNextCard()
    {
        int index = revealOrder[revealedCount++];
        TurnCardState card = Cards[index];
        card.Number = hiddenNumbers[index];
        Cards[index] = card;
    }

    /// <summary>각 연출 단계와 시작 시각을 모든 화면에 같은 값으로 보낸다.</summary>
    [Server]
    private void SetCardPhase(TurnCardPhase value, double startedAt)
    {
        cardPhaseStartedAt = startedAt;
        cardPhase = value;
    }

    /// <summary>기존 호출부에서도 카드로 정한 첫 공격자를 사용한다.</summary>
    [Server]
    public void ChooseFirst(IReadOnlyList<PlayerState> players)
    {
        attackerNetId = TurnOrder.Count > 0 ? TurnOrder[0] :
            (players.Count > 0 ? players[0].netId : 0);
    }

    /// <summary>카드 순서의 다음 참가자를 선택하며 마지막 다음에는 처음으로 돌아간다.</summary>
    [Server]
    public void Advance(IReadOnlyList<PlayerState> players)
    {
        if (TurnOrder.Count > 0)
        {
            int current = TurnOrder.IndexOf(attackerNetId);

            for (int offset = 1; offset <= TurnOrder.Count; offset++)
            {
                uint next = TurnOrder[(current + offset + TurnOrder.Count) % TurnOrder.Count];

                foreach (PlayerState player in players)
                    if (player.netId == next)
                    {
                        attackerNetId = next;
                        return;
                    }
            }
        }

        SelectAt(players, 0);
    }

    /// <summary>독립 시험 씬에서 퇴장한 경우 남은 참가자 중 다음 사람을 선택한다.</summary>
    [Server]
    public void SelectAt(IReadOnlyList<PlayerState> players, int index)
    {
        attackerNetId = players.Count > 0 ? players[index % players.Count].netId : 0;
    }
}
