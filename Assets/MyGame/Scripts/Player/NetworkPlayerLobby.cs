using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerLobby : NetworkBehaviour
{
    [SerializeField] private TMP_Text readyText;
    [SyncVar(hook = nameof(OnReadyChanged))] private bool isReady;
    public bool IsReady => isReady;

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateReadyText();
    }

    private void Update()
    {
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && !manager.IsLobby) return;
        if (Keyboard.current.fKey.wasPressedThisFrame) CmdToggleReady();
    }

    public void ToggleReady()
    {
        if (isLocalPlayer) CmdToggleReady();
    }

    [Command]
    private void CmdToggleReady()
    {
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            isReady = !isReady;
    }

    private void OnReadyChanged(bool oldReady, bool newReady) => UpdateReadyText();

    private void UpdateReadyText()
    {
        if (readyText != null) readyText.text = isReady ? "Ready" : "Not Ready";
    }
}
