using Mirror;
using UnityEngine;

/// <summary>맵에 고정된 대포. 발판 충돌과 별개로 서버에서 일정 간격의 직선 탄환을 생성한다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(NetworkIdentity), typeof(BoxCollider2D))]
public sealed class FixedCannonTrap : NetworkBehaviour
{
    [SerializeField] private CannonballProjectile projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private RaceManager race;
    [SerializeField] private FallZone fallZone;
    [SerializeField, Min(0.1f)] private float fireInterval = 3f;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 8f;
    [SerializeField, Min(0.1f)] private float projectileLifetime = 7f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private bool shootLeft = true;
    private double nextShotAt;
    private int observedRound = -1;
    private bool wasRunning;

    public float FireInterval => fireInterval;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (race == null) race = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
        if (fallZone == null) fallZone = FindFirstObjectByType<FallZone>(FindObjectsInactive.Include);
        wasRunning = false;
        observedRound = -1;
    }

    private void FixedUpdate()
    {
        if (!isServer) return;
        bool running = race != null && race.IsSimulating && !NetworkServer.isLoadingScene;
        if (!running) { wasRunning = false; return; }
        double now = (race != null ? race.SimulationTime : NetworkTime.time);
        if (!wasRunning || observedRound != race.RoundNumber)
        {
            nextShotAt = now + fireInterval;
            observedRound = race.RoundNumber;
            wasRunning = true;
        }
        if (now < nextShotAt || projectilePrefab == null || muzzle == null) return;
        var projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
        projectile.Initialize(this, race, muzzle.position, shootLeft ? Vector2.left : Vector2.right,
            projectileSpeed, projectileLifetime, groundLayer);
        NetworkServer.Spawn(projectile.gameObject);

        RpcPlayFireSound(muzzle.position);

        // 지연된 프레임의 탄환을 한꺼번에 쏟아내지 않고 원래 주기를 유지한다.
        nextShotAt += fireInterval;
        if (nextShotAt <= now) nextShotAt = now + fireInterval;
    }

    /// <summary>발사가 확정된 경우에만 각 화면의 공용 재생기에 대포음을 요청한다.</summary>
    [ClientRpc]
    private void RpcPlayFireSound(Vector2 position)
    {
        SoundPlayer.Play(SoundType.CannonFire, position);
    }

    /// <summary>고정 함정의 적중으로 사망과 위치 복구를 처리한다.</summary>
    [Server]
    public bool TryKill(PlayerState player, Vector2 hitOrigin)
    {
        if (player == null || !player.CanReceiveHit || fallZone == null ||
            !fallZone.TryGetRespawnPosition(player.transform.position, out Vector2 position) ||
            !player.TryDie(hitOrigin)) return false;
        player.Respawn(position);
        return true;
    }
}
