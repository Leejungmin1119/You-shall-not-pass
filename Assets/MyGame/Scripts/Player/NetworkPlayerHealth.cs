using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerHealth : NetworkBehaviour
{
    [SerializeField] private TMP_Text healthText;
    [SyncVar(hook = nameof(OnHealthChanged))] private int health = 100;
    public bool IsDead => health <= 0;

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateHealthText();
        ApplyDeathState();
    }

    private void Update()
    {
        if (RaceSession.Instance != null) return;
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby) return;
        if (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby) return;
        if (Keyboard.current.hKey.wasPressedThisFrame) CmdRequestPracticeDamage();
    }

    [Command]
    private void CmdRequestPracticeDamage() => TakeDamage(20);

    [Server]
    public void TakeDamage(int damage)
    {
        if (RaceSession.Instance != null) return;
        if (IsDead || damage <= 0) return;
        health = Mathf.Max(0, health - damage);
        if (IsDead) Die();
    }

    [Server]
    private void Die()
    {
        Debug.Log($"Server: Player {netId} died.");
    }

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        UpdateHealthText();
        ApplyDeathState();
    }

    private void ApplyDeathState()
    {
        // ClientToServer movement is still controlled by the owner.
        if (!isLocalPlayer) return;
        var movement = GetComponent<PlayerMovement>();
        if (IsDead && movement != null) movement.MovementLocked = true;
        if (IsDead) GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
    }

    private void UpdateHealthText()
    {
        if (healthText != null) healthText.text = $"HP: {health}";
    }
}
