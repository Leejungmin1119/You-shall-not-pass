using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>서버가 최초 공격자를 무작위로 뽑고 참가 순서대로 공격 기회를 순환한다.</summary>
public sealed class rolechangeManager : NetworkBehaviour
{
    [SyncVar] private uint attackerNetId;

    public uint AttackerNetId => attackerNetId;

    /// <summary>동기화된 공격자 번호로 이동·피격·구매 권한을 판단한다.</summary>
    public bool IsAttacker(PlayerState player)
    {
        return player != null && attackerNetId != 0 && player.netId == attackerNetId;
    }

    /// <summary>참가자 생성이 끝난 뒤 경기당 한 번만 최초 공격자를 선택한다.</summary>
    [Server]
    public void ChooseFirst(IReadOnlyList<PlayerState> players)
    {
        attackerNetId = players.Count > 0 ? players[Random.Range(0, players.Count)].netId : 0;
    }

    /// <summary>현재 공격자의 다음 참가자를 고르며 마지막이면 첫 참가자로 돌아간다.</summary>
    [Server]
    public void Advance(IReadOnlyList<PlayerState> players)
    {
        for (int index = 0; index < players.Count; index++)
        {
            if (players[index].netId != attackerNetId)
                continue;

            SelectAt(players, index + 1);
            return;
        }

        SelectAt(players, 0);
    }

    /// <summary>공격자가 퇴장하면 제거된 자리의 다음 참가자에게 기회를 넘긴다.</summary>
    [Server]
    public void SelectAt(IReadOnlyList<PlayerState> players, int index)
    {
        attackerNetId = players.Count > 0 ? players[index % players.Count].netId : 0;
    }
}
