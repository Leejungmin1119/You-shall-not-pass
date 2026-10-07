using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerBombSpawner : NetworkBehaviour
{
    [Header("폭탄")]
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private float spawnDistance = 1.5f;
    [SerializeField] private float maxSpawnDistance = 3f;
    [SerializeField] private float bombCooldown = 2f;

    [SyncVar(hook = nameof(OnBombCountChanged))]
    private int remainingBombs = 3;

    private double nextBombAllowedTime;

    private void Update()
    {
        if (!isLocalPlayer || Keyboard.current == null) return;
        if (NetworkManager.singleton is PracticeNetworkManager manager && manager.IsLobby) return;
        if (NetworkManager.singleton is CapstoneRoomManager roomManager && roomManager.IsLobby) return;

        if (Keyboard.current.bKey.wasPressedThisFrame)
        {
            Vector2 requestedPosition =
                (Vector2)transform.position + Vector2.right * spawnDistance;

            CmdTrySpawnBomb(requestedPosition);
        }

        if (Keyboard.current.yKey.wasPressedThisFrame)
        {
            Vector2 invalidPosition =
                (Vector2)transform.position + Vector2.right * 100f;

            CmdTrySpawnBomb(invalidPosition);
        }
    }

    [Command]
    private void CmdTrySpawnBomb(Vector2 requestedPosition)
    {
        if (bombPrefab == null)
        {
            TargetBombRejected(connectionToClient, "bombPrefab이 비어 있습니다.");
            return;
        }

        if (remainingBombs <= 0)
        {
            TargetBombRejected(connectionToClient, "남은 폭탄이 없습니다.");
            return;
        }

        if (NetworkTime.time < nextBombAllowedTime)
        {
            TargetBombRejected(connectionToClient, "아직 폭탄 쿨타임입니다.");
            return;
        }

        float distance = Vector2.Distance(transform.position, requestedPosition);

        if (distance > maxSpawnDistance)
        {
            TargetBombRejected(connectionToClient, "폭탄 생성 거리를 초과했습니다.");
            return;
        }

        remainingBombs--;
        nextBombAllowedTime = NetworkTime.time + bombCooldown;

        GameObject bomb = Instantiate(
            bombPrefab,
            requestedPosition,
            Quaternion.identity
        );

        NetworkServer.Spawn(bomb);

        Debug.Log(
            $"서버 승인: 플레이어 {netId}가 {requestedPosition} 위치에 " +
            $"폭탄 {bomb.name}을 생성했습니다."
        );
    }

    [TargetRpc]
    private void TargetBombRejected(
        NetworkConnectionToClient target,
        string reason
    )
    {
        Debug.Log($"폭탄 생성 거절: {reason}");
    }

    private void OnBombCountChanged(int oldCount, int newCount)
    {
        Debug.Log($"남은 폭탄 변경: {oldCount} → {newCount}");
    }
}
