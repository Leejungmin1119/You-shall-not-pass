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
    private PlayerState playerState;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        playerHealth = GetComponent<NetworkPlayerHealth>();
        playerState = GetComponent<PlayerState>();
        body.simulated = false;
        platformMovement = GetComponent<PlayerMovement>();
        if (platformMovement != null) platformMovement.enabled = false;
    }

    /// <summary>
    /// 자기 플레이어만 이동을 활성화하고 게임 씬의 카메라 추적 대상을 연결한다.
    /// </summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        bool inLobby = (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            || (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby);
        body.simulated = !inLobby;
        if (platformMovement != null) platformMovement.enabled = !inLobby;
        if (inLobby)
            transform.position = new Vector3(((int)(netId % 4) - 1.5f) * 1.8f, 0f, 0f);
        else if (playerState != null && playerState.IsRoleManaged)
            ApplyAttackerRole(playerState.IsAttacker);
        else
            BindLocalCamera();
    }

    /// <summary>
    /// 역할 교대가 있는 게임에서 공격자만 표시하고 자기 공격자의 이동을 활성화한다.
    /// </summary>
    public void ApplyAttackerRole(bool isAttacker)
    {
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite != null) sprite.enabled = isAttacker;
        bool canMove = isLocalPlayer && isAttacker;
        if (body.simulated && !canMove)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        body.simulated = canMove;
        if (platformMovement != null)
        {
            platformMovement.MovementLocked = !canMove;
            platformMovement.enabled = canMove;
        }
    }

    /// <summary>
    /// 씬에 배치된 추적 카메라가 네트워크로 생성된 자기 플레이어를 따라가게 한다.
    /// </summary>
    private void BindLocalCamera()
    {
        MapViewController mapView = FindFirstObjectByType<MapViewController>();
        if (mapView != null)
            mapView.SetTrackingTarget(transform);
    }

    // 로비에서는 플랫폼 물리 대신 평면 이동을 사용한다.
    // 기존 ClientToServer NetworkTransform이 위치를 다른 접속자에게 전달한다.
    private void MoveInLobby()
    {
        if (NetworkManager.singleton is CapstoneRoomManager manager && manager.IsEditingText) return;
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
        if ((NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            || (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby))
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

        if (playerState != null && playerState.IsRoleManaged && !playerState.IsAttacker)
            return;

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
