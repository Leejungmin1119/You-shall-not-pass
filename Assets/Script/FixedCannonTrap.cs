using Mirror;
using UnityEngine;

/// <summary>대포의 발사 방향. Auto는 기존 shootLeft 값을 그대로 따른다.</summary>
public enum CannonShotDirection { Auto, Left, Right, Up, Down }

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
    [SerializeField] private CannonShotDirection shotDirection = CannonShotDirection.Auto;
    [Tooltip("0보다 크면 경기 시작 후 이 주기마다 추가로 발사한다. 움직이는 판의 편도 시간과 같게 두면 판이 맨 위·맨 아래에 올 때 반드시 발사한다.")]
    [SerializeField, Min(0f)] private float endpointShotPeriod = 0f;
    private double nextShotAt;
    private int observedRound = -1;
    private int lastEndpointShot = -1;
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
            lastEndpointShot = -1;
            wasRunning = true;
        }
        if (projectilePrefab == null || muzzle == null) return;

        bool nearEndpoint = false;
        if (endpointShotPeriod > 0f)
        {
            double age = now - race.PhaseStartedAt;
            int index = Mathf.FloorToInt((float)(age / endpointShotPeriod));
            if (index > lastEndpointShot)
            {
                lastEndpointShot = index;
                FireOnce();
            }
            // 맨 위·맨 아래 발사와 거의 겹치는 일반 발사는 건너뛴다.
            double sinceEndpoint = age - index * (double)endpointShotPeriod;
            nearEndpoint = sinceEndpoint < 0.25d || endpointShotPeriod - sinceEndpoint < 0.25d;
        }

        if (now < nextShotAt) return;
        if (!nearEndpoint) FireOnce();

        // 지연된 프레임의 탄환을 한꺼번에 쏟아내지 않고 원래 주기를 유지한다.
        nextShotAt += fireInterval;
        if (nextShotAt <= now) nextShotAt = now + fireInterval;
    }

    /// <summary>현재 대포 입구 위치에서 탄환 하나를 만들어 발사하고 소리를 낸다.</summary>
    private void FireOnce()
    {
        var projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
        projectile.Initialize(this, race, muzzle.position, ShotVector(),
            projectileSpeed, projectileLifetime, groundLayer);
        NetworkServer.Spawn(projectile.gameObject);

        RpcPlayFireSound(muzzle.position);
    }

    /// <summary>설정된 발사 방향을 벡터로 돌려준다. Auto는 기존 좌우 설정을 따른다.</summary>
    private Vector2 ShotVector()
    {
        switch (shotDirection)
        {
            case CannonShotDirection.Left: return Vector2.left;
            case CannonShotDirection.Right: return Vector2.right;
            case CannonShotDirection.Up: return Vector2.up;
            case CannonShotDirection.Down: return Vector2.down;
            default: return shootLeft ? Vector2.left : Vector2.right;
        }
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
