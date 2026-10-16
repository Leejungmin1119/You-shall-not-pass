using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수직으로 떨어져 지면에 꽂힌 뒤 일정 시간 남았다가 사라지는 창.
/// 움직임이 정해져 있어서 모든 PC가 각자 계산하고, 적중 판정은 판정자(호스트) PC에서만 한다.
/// </summary>
public class Spear : MonoBehaviour
{
    // 판정자 PC에서 공격자가 맞은 순간 한 번 알린다. 라운드 흐름이 생기면 여기에 붙어
    // 적중자 +1점과 라운드 종료를 처리한다.
    public static event Action<Spear, PlayerMovement> AttackerHit;

    // 구덩이 위로 떨어뜨리면 닿을 지면이 없어 영원히 남으므로, 이만큼 떨어지면 없앤다.
    private const float MaxFallDistance = 50f;

    private const float TargetRefreshInterval = 0.25f;

    private static PlayerMovement[] s_targets = Array.Empty<PlayerMovement>();
    private static float s_targetsRefreshedAt = float.NegativeInfinity;

    private float _fallSpeed;
    private float _stuckDuration;
    private LayerMask _groundLayer;
    private bool _isJudge;
    private readonly HashSet<int> raceVictims = new HashSet<int>();

    private bool _isStuck;
    private float _stuckTimer;
    private float _fallenDistance;

    public uint SpearId { get; private set; }
    public int OwnerId { get; private set; }
    public bool IsStuck => _isStuck;
    public bool HasHit { get; private set; }
    public PlayerMovement HitAttacker { get; private set; }

    /// <param name="isJudge">이 PC가 적중을 판정하는지. 호스트이거나 네트워크가 없을 때만 true.</param>
    /// <param name="catchUpSeconds">
    /// 요청이 호스트를 거쳐 도착하는 동안 호스트 쪽 창은 이미 내려와 있다.
    /// 그만큼 앞당겨 시작해서 모든 PC의 창 위치를 맞춘다.
    /// </param>
    public void Launch(float fallSpeed, float stuckDuration, LayerMask groundLayer,
        bool isJudge, uint spearId, int ownerId, float catchUpSeconds)
    {
        _fallSpeed = fallSpeed;
        _stuckDuration = stuckDuration;
        _groundLayer = groundLayer;
        _isJudge = isJudge;
        SpearId = spearId;
        OwnerId = ownerId;

        if (catchUpSeconds > 0f)
        {
            Advance(catchUpSeconds);
        }
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    private void Advance(float deltaTime)
    {
        if (_isStuck)
        {
            // 꽂힌 창은 가시처럼 남아 있어서, 공격자가 걸어와 닿아도 적중이다.
            CheckAttackerHit(transform.position.y);

            _stuckTimer += deltaTime;
            if (_stuckTimer >= _stuckDuration)
            {
                Destroy(gameObject);
            }

            return;
        }

        float step = _fallSpeed * deltaTime;
        float previousY = transform.position.y;

        // 한 프레임 이동 거리가 발판 두께보다 길어질 수 있어, 위치만 옮기면 발판을 뚫고 지나간다.
        // 이번 프레임에 지나갈 경로를 선으로 검사해 그 사이의 지면을 잡는다.
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, step, _groundLayer);
        if (hit)
        {
            transform.position = hit.point;
            _isStuck = true;
            CheckAttackerHit(previousY);
            return;
        }

        transform.position += Vector3.down * step;
        CheckAttackerHit(previousY);

        _fallenDistance += step;
        if (_fallenDistance >= MaxFallDistance)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 이번 프레임에 창이 지나간 범위 전체를 공격자와 겹치는지 검사한다.
    /// 낙하 속도가 빨라 현재 위치만 검사하면 얇은 구간을 건너뛰고 지나칠 수 있어서,
    /// 이전 위치의 창 윗끝부터 현재 창끝까지를 하나의 상자로 잡는다.
    /// </summary>
    private void CheckAttackerHit(float previousY)
    {
        bool raceMode = RaceSession.Instance != null;
        if (!_isJudge || (!raceMode && HasHit))
        {
            return;
        }

        RefreshTargets();

        float bottom = transform.position.y;
        float top = previousY + transform.localScale.y;
        float minX = transform.position.x - transform.localScale.x * 0.5f;
        float maxX = transform.position.x + transform.localScale.x * 0.5f;

        foreach (PlayerMovement target in s_targets)
        {
            if (target == null)
            {
                continue;
            }

            if (raceMode && (raceVictims.Contains(target.GetInstanceID()) ||
                !target.TryGetComponent(out PlayerState state) || !state.IsDamageable)) continue;

            // 다른 PC의 캐릭터는 Rigidbody2D.simulated가 꺼져 있어 Physics2D.OverlapBox에 잡히지 않는다.
            // (실험으로 확인: simulated=false면 overlap=False, bounds는 정상)
            // 그래서 콜라이더의 bounds를 직접 비교한다.
            Collider2D targetCollider = target.GetComponent<Collider2D>();
            if (targetCollider == null)
            {
                continue;
            }

            Bounds bounds = targetCollider.bounds;
            bool overlaps = bounds.min.x < maxX && bounds.max.x > minX &&
                            bounds.min.y < top && bounds.max.y > bottom;
            if (!overlaps)
            {
                continue;
            }

            if (raceMode) raceVictims.Add(target.GetInstanceID());
            HasHit = true;
            HitAttacker = target;

            Debug.Log($"[창] 공격자 적중: {target.name}, 창 위치 {transform.position}, " +
                      $"{(_isStuck ? "꽂힌 창에 닿음" : "떨어지는 창에 맞음")}");

            AttackerHit?.Invoke(this, target);
            if (!raceMode) return;
        }
    }

    private static void RefreshTargets()
    {
        if (Time.unscaledTime - s_targetsRefreshedAt < TargetRefreshInterval)
        {
            return;
        }

        s_targets = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        s_targetsRefreshedAt = Time.unscaledTime;
    }
}
