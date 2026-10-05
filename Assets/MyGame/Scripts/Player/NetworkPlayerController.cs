using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class NetworkPlayerController : NetworkBehaviour
{
    [Header("이동")]
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D body;
    private PlayerMovement platformMovement;
    private NetworkPlayerHealth playerHealth;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        playerHealth = GetComponent<NetworkPlayerHealth>();
        body.simulated = false;
        platformMovement = GetComponent<PlayerMovement>();
        if (platformMovement != null) platformMovement.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        bool inLobby = NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby;
        body.simulated = !inLobby;
        if (platformMovement != null) platformMovement.enabled = !inLobby;
    }

    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby) return;
        if (playerHealth != null && playerHealth.IsDead)
        {
            if (platformMovement != null) platformMovement.MovementLocked = true;
            body.linearVelocity = Vector2.zero;
            return;
        }

        if (platformMovement != null || Keyboard.current == null) return;
        var keyboard = Keyboard.current;
        float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                         - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                       - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);

        Vector2 direction =
            new Vector2(horizontal, vertical).normalized;

        body.linearVelocity = direction * moveSpeed;
    }
}
