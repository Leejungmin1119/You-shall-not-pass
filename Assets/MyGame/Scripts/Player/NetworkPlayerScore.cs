    using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerScore : NetworkBehaviour
{
    [Header("화면 표시")]
    [SerializeField] private TMP_Text nameText;

    [SyncVar(hook = nameof(OnScoreChanged))]
    private int score;

    private PlayerMovement platformMovement;

    private void Awake()
    {
        platformMovement = GetComponent<PlayerMovement>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdatePlayerText();
        Debug.Log($"플레이어 생성: netId = {netId}");
    }

    private void Update()
    {
        if (RaceSession.Instance != null) return;
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby) return;
        if (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby) return;

        bool scoreKeyPressed = platformMovement != null
            ? Keyboard.current.pKey.wasPressedThisFrame
            : Keyboard.current.spaceKey.wasPressedThisFrame;

        if (scoreKeyPressed)
        {
            CmdAddScore();
        }
    }

    [Command]
    private void CmdAddScore()
    {
        if (RaceSession.Instance != null) return;
        score++;
        Debug.Log($"서버 점수 증가: netId = {netId}, score = {score}");
    }

    private void OnScoreChanged(int oldScore, int newScore)
    {
        UpdatePlayerText();
    }

    public void RefreshDisplay() => UpdatePlayerText();

    private void UpdatePlayerText()
    {
        if (nameText == null) return;
        var profile = GetComponent<NetworkPlayerProfile>();
        string displayName = profile != null ? profile.PlayerName : $"Player {netId}";
        nameText.richText = false;
        int displayedScore = RaceSession.Instance != null ? GetComponent<PlayerScore>().Score : score;
        nameText.text = $"{displayName}\nScore: {displayedScore}";
    }
}
