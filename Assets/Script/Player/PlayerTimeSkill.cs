using Mirror;
using UnityEngine;

/// <summary>공격자의 스킬 입력과 사망할 때 초기화되는 사용 기록을 서버에서 관리한다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerState))]
public sealed class PlayerTimeSkill : NetworkBehaviour
{
    [Header("스테이지 흑백 표시")]
    [SerializeField] private Material grayscaleLit;
    [SerializeField] private Material grayscaleUnlit;
    [SyncVar] private bool hasUsedSkill;
    private PlayerState player;

    public bool HasUsedSkill => hasUsedSkill;
    public bool CanUse => !hasUsedSkill && player != null && player.IsAttacker && player.CanMove;

    /// <summary>동일 플레이어의 역할·생존 상태를 참조한다.</summary>
    private void Awake()
    {
        player = GetComponent<PlayerState>();
    }

    /// <summary>로컬 화면에 한 번만 표시 담당을 연결한다. 원격 플레이어는 별도로 실행하지 않는다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (player.Race == null)
            return;

        var effect = player.Race.GetComponent<TimeSkillGrayscale>();

        if (effect == null)
            effect = player.Race.gameObject.AddComponent<TimeSkillGrayscale>();

        effect.Configure(player.Race, grayscaleLit, grayscaleUnlit);
    }

    /// <summary>새 경기에서 생성된 플레이어의 스킬을 사용 가능한 상태로 준비한다.</summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        ResetUsage();
    }

    /// <summary>사망이 승인되면 사용 기록을 초기화해 다음 공격 차례에 다시 사용할 수 있게 한다.</summary>
    [Server]
    public void ResetUsage()
    {
        hasUsedSkill = false;
    }

    /// <summary>소유자의 입력만 서버로 전달한다. UI 입력 맵에서는 스킬 입력이 발생하지 않는다.</summary>
    private void Update()
    {
        if (isLocalPlayer && CanUse && InputManager.TimeSkillWasPressed)
            CmdUseTimeSkill();
    }

    /// <summary>서버에서 횟수·역할·경기 상태를 재확인하고 승인된 경우에만 한 번 소모한다.</summary>
    [Command]
    public void CmdUseTimeSkill()
    {
        if (!CanUse || player.Race == null || !player.Race.TryStartTimeSkill(player))
            return;

        hasUsedSkill = true;
    }
}
