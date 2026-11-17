using UnityEngine;

/// <summary>
/// 화면을 채우는 가로 반복 배경 한 겹. 세로는 카메라에 고정하고,
/// 가로는 카메라가 움직인 거리의 일부만큼만 흘려 멀리 있는 것처럼 보이게 한다.
/// </summary>
[ExecuteAlways, DefaultExecutionOrder(10000), RequireComponent(typeof(SpriteRenderer))]
public sealed class ParallaxBackdrop : MonoBehaviour
{
    [SerializeField] private Camera outputCamera;
    [Tooltip("카메라가 움직인 거리 대비 배경이 화면에서 흐르는 비율. 0이면 화면에 고정, 1이면 지형과 같은 속도입니다.")]
    [SerializeField, Range(0f, 1f)] private float scrollRatio = 0.1f;
    [Tooltip("흐르는 속도의 기준이 되는 시야 크기(카메라 Orthographic Size). 전체 보기처럼 시야가 넓어져도 화면 속 그림 위치가 밀리지 않게 합니다.")]
    [SerializeField, Min(0.01f)] private float referenceOrthographicSize = 8f;
    [SerializeField, Min(1f)] private float overscan = 1.03f;
    [SerializeField] private float backgroundDepth = 10f;
    private SpriteRenderer visual;

    /// <summary>에디터 도구가 출력 카메라와 흐르는 비율을 지정한다.</summary>
    public void Configure(Camera camera, float ratio, float referenceSize)
    {
        outputCamera = camera;
        scrollRatio = Mathf.Clamp01(ratio);
        referenceOrthographicSize = Mathf.Max(0.01f, referenceSize);
        FitToCamera(camera);
    }

    private void OnEnable() => FitToCamera(outputCamera);

    // Cinemachine이 카메라 위치를 정한 뒤에 맞춰야 한 프레임 늦게 따라오지 않는다.
    private void LateUpdate() => FitToCamera(outputCamera);

    /// <summary>시야 높이에 맞춰 키우고, 시야 폭을 덮을 만큼 가로로 이어 붙인 뒤 흐른 만큼 옮긴다.</summary>
    public void FitToCamera(Camera camera)
    {
        if (camera == null || !camera.orthographic) return;
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (visual.sprite == null) return;

        Vector2 art = visual.sprite.bounds.size;
        float viewHeight = camera.orthographicSize * 2f;
        float viewWidth = viewHeight * camera.aspect;
        float scale = viewHeight * overscan / art.y;
        float tileWidth = art.x * scale;

        // 그림은 가운데에서 좌우로 반 장까지만 밀리므로 시야 폭 + 한 장이면 빈 곳이 생기지 않는다.
        // 장수를 홀수로 고정해 화면 비율이 바뀌어도 이음새 위치가 반 장씩 튀지 않게 한다.
        int tiles = Mathf.Max(3, Mathf.CeilToInt(viewWidth / tileWidth + 1f));
        if (tiles % 2 == 0) tiles++;

        if (visual.drawMode != SpriteDrawMode.Tiled)
        {
            visual.drawMode = SpriteDrawMode.Tiled;
            visual.tileMode = SpriteTileMode.Continuous;
        }

        Vector2 size = new Vector2(art.x * tiles, art.y);
        if (visual.size != size) visual.size = size;

        // 흐른 양을 기준 시야에서의 그림 한 장 폭으로 나눠 센다. 한 장을 넘으면 같은 그림이 이어지므로 되감는다.
        float referenceTileWidth = art.x * referenceOrthographicSize * 2f * overscan / art.y;
        Vector3 view = camera.transform.position;
        float phase = Mathf.Repeat(view.x * scrollRatio / referenceTileWidth + 0.5f, 1f) - 0.5f;

        transform.SetPositionAndRotation(
            new Vector3(view.x - phase * tileWidth, view.y, backgroundDepth), Quaternion.identity);
        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
