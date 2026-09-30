using UnityEngine;

/// <summary>
/// 수직으로 떨어져 지면에 꽂힌 뒤 일정 시간 남았다가 사라지는 창.
/// </summary>
public class Spear : MonoBehaviour
{
    // 구덩이 위로 떨어뜨리면 닿을 지면이 없어 영원히 남으므로, 이만큼 떨어지면 없앤다.
    private const float MaxFallDistance = 50f;

    private float _fallSpeed;
    private float _stuckDuration;
    private LayerMask _groundLayer;

    private bool _isStuck;
    private float _stuckTimer;
    private float _fallenDistance;

    public bool IsStuck => _isStuck;

    public void Launch(float fallSpeed, float stuckDuration, LayerMask groundLayer)
    {
        _fallSpeed = fallSpeed;
        _stuckDuration = stuckDuration;
        _groundLayer = groundLayer;
    }

    private void Update()
    {
        if (_isStuck)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= _stuckDuration)
            {
                Destroy(gameObject);
            }

            return;
        }

        float step = _fallSpeed * Time.deltaTime;

        // 한 프레임 이동 거리가 발판 두께보다 길어질 수 있어, 위치만 옮기면 발판을 뚫고 지나간다.
        // 이번 프레임에 지나갈 경로를 선으로 검사해 그 사이의 지면을 잡는다.
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, step, _groundLayer);
        if (hit)
        {
            transform.position = hit.point;
            _isStuck = true;
            return;
        }

        transform.position += Vector3.down * step;

        _fallenDistance += step;
        if (_fallenDistance >= MaxFallDistance)
        {
            Destroy(gameObject);
        }
    }
}
