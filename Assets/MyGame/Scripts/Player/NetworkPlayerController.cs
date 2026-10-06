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
        if (inLobby)
            transform.position = new Vector3(((int)(netId % 4) - 1.5f) * 1.8f, 0f, 0f);
    }

    // 로비에서는 플랫폼 물리 대신 평면 이동을 사용한다.
    // 기존 ClientToServer NetworkTransform이 위치를 다른 접속자에게 전달한다.
    private void MoveInLobby()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        Vector2 input = new Vector2(
            (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
            (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
        Vector3 position = transform.position + (Vector3)(input.normalized * 3f * Time.fixedDeltaTime);
        position.x = Mathf.Clamp(position.x, -5f, 5f);
        position.y = Mathf.Clamp(position.y, -2f, 2f);
        position.z = 0;
        transform.position = position;
    }

    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
        {
            MoveInLobby();
            return;
        }
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
