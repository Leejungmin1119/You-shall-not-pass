using Mirror;
using UnityEngine;

public class NetworkPlayerProfile : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] private string playerName;
    [SyncVar(hook = nameof(OnColorChanged))] private Color playerColor = Color.white;
    // -1이면 로비를 거치지 않은 시험 씬이므로 프리팹에 지정된 캐릭터를 그대로 쓴다.
    [SyncVar(hook = nameof(OnCharacterChanged))] private int characterIndex = -1;
    private PlayerInfoHUD hud;
    public string PlayerName => string.IsNullOrWhiteSpace(playerName) ? $"Player {netId}" : playerName;
    public Color PlayerColor => playerColor;
    public event System.Action<bool> TurnCardChoiceResponded;
    public bool IsTurnCardReady { get; private set; }

    /// <summary>자기 게임 플레이어의 생성이 끝난 뒤 카드 선택을 받을 준비가 되었음을 알린다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (TryGetComponent<PlayerState>(out _))
            CmdReadyForTurnCards();
    }

    /// <summary>모든 소유자가 게임 플레이어를 받은 뒤에만 선택 제한시간을 시작하도록 한다.</summary>
    [Command]
    private void CmdReadyForTurnCards()
    {
        if (TryGetComponent(out PlayerState player) && player.Race != null)
            IsTurnCardReady = true;
    }

    /// <summary>소유자 본인의 카드 선택을 서버에 요청한다. 참가 여부와 중복 선택은 서버가 확인한다.</summary>
    [Command]
    public void CmdChooseTurnCard(byte index)
    {
        PlayerState player = GetComponent<PlayerState>();

        bool accepted = player != null && player.Race != null && player.Race.Phase == RacePhase.CardSelection &&
            player.Race.Roles.TryChooseCard(netId, index);
        TargetTurnCardChoice(accepted);
    }

    /// <summary>카드 경합 결과는 요청한 소유자에게만 돌려준다.</summary>
    [TargetRpc]
    private void TargetTurnCardChoice(bool accepted)
    {
        TurnCardChoiceResponded?.Invoke(accepted);
    }

    /// <summary>서버에서 게임에 표시할 이름을 지정한다.</summary>
    [Server]
    public void SetPlayerName(string newName) => playerName = newName;

    /// <summary>로비에서 사용하던 식별 색상을 게임의 모든 화면에 전달한다.</summary>
    [Server]
    public void SetPlayerColor(Color color)
    {
        playerColor = new Color(color.r, color.g, color.b, 1f);
    }

    /// <summary>로비에서 고른 캐릭터를 모든 화면에 전달한다.</summary>
    [Server]
    public void SetCharacter(PlayerIdleBoil.CharacterStyle style)
    {
        characterIndex = (int)style;
        ApplyCharacter();
    }

    private void OnCharacterChanged(int previous, int current) => ApplyCharacter();

    private void ApplyCharacter()
    {
        if (characterIndex < 0 || characterIndex >= PlayerIdleBoil.CharacterCount) return;
        GetComponent<PlayerIdleBoil>()?.SetCharacter((PlayerIdleBoil.CharacterStyle)characterIndex);
    }

    /// <summary>기존 이름표를 갱신하고 게임 HUD의 플레이어 칸에 등록한다.</summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyCharacter();
        GetComponent<NetworkPlayerScore>()?.RefreshDisplay();

        hud = FindFirstObjectByType<PlayerInfoHUD>(FindObjectsInactive.Include);
        if (hud != null)
            hud.RegisterPlayer(this);

        Debug.Log($"Game player: {PlayerName}, netId={netId}");
    }

    /// <summary>퇴장하거나 게임 씬이 종료되면 HUD 연결을 해제한다.</summary>
    public override void OnStopClient()
    {
        if (hud != null)
            hud.UnregisterPlayer(this);

        hud = null;
        base.OnStopClient();
    }

    /// <summary>늦게 도착하거나 변경된 이름을 이름표와 HUD에 함께 반영한다.</summary>
    private void OnNameChanged(string oldName, string newName)
    {
        GetComponent<NetworkPlayerScore>()?.RefreshDisplay();

        if (hud != null)
            hud.RefreshName(this);
    }

    /// <summary>색상이 동기화되면 HUD의 이름 색상도 갱신한다.</summary>
    private void OnColorChanged(Color previous, Color current)
    {
        GetComponent<NetworkPlayerScore>()?.RefreshDisplay();

        if (hud != null)
            hud.RefreshName(this);
    }
}
