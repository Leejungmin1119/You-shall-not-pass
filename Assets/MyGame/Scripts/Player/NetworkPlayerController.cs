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

    /// <summary>
    /// 이동에 필요한 컴포넌트를 보관하고 소유권이 확인되기 전에는 물리를 끈다.
    /// </summary>
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
        if (platformMovement != null)
        {
            platformMovement.MovementLocked = false;
            platformMovement.enabled = !inLobby;
        }
        if (inLobby)
            transform.position = new Vector3(((int)(netId % 4) - 1.5f) * 1.8f, 0f, 0f);
        else
            BindLocalCamera();
    }

    /// <summary>
    /// 공수 경기에서는 공격자만 불투명하게 표시한다.
    /// 원격 캐릭터의 물리는 끈 상태로 두고 NetworkTransform이 위치를 적용한다.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            sprite.enabled = playerState == null || playerState.IsVisible;
            Color color = sprite.color;
            bool roleGame = playerState != null && playerState.Race != null &&
                playerState.Race.Roles != null;
            color.a = roleGame || isLocalPlayer ? 1f : 0.5f;
            sprite.color = color;
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

    /// <summary>
    /// 로비에서 평면 이동을 처리한다. 변경된 위치는 NetworkTransform이 전송한다.
    /// </summary>
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

    /// <summary>
    /// 소유자의 로비 이동과 사망 잠금을 처리하고 게임 이동은 PlayerMovement에 맡긴다.
    /// </summary>
    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;
        if ((NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby)
            || (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby))
        {
            MoveInLobby();
            return;
        }
        // 골인·대기·판 종료 상태는 소유자의 물리와 이동도 함께 멈춘다.
        bool canMove = playerState == null || playerState.CanMove;
        body.simulated = canMove;

        if (platformMovement != null)
            platformMovement.MovementLocked = !canMove;

        if (!canMove)
        {
            body.linearVelocity = Vector2.zero;
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
