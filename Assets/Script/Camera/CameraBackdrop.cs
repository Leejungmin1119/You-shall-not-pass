using UnityEngine;

/// <summary>화면 채움 또는 넓은 월드 배경을 표시하고, 카메라가 경계를 넘으면 여유 영역을 늘린다.</summary>
[ExecuteAlways, DefaultExecutionOrder(10000), RequireComponent(typeof(SpriteRenderer))]
public sealed class CameraBackdrop : MonoBehaviour
{
    [SerializeField] private Camera outputCamera;
    [SerializeField, Min(1f)] private float overscan = 1.03f;
    [SerializeField] private float backgroundDepth = 10f;
    [SerializeField] private bool worldLandscape;
    [SerializeField] private Rect landscapeArea = new Rect(-30f, -22f, 130f, 80f);
    private SpriteRenderer visual;

    public void Configure(Camera camera)
    {
        outputCamera = camera;
        FitToCamera(camera);
    }

    public void ConfigureLandscape(Camera camera, Rect area)
    {
        outputCamera = camera;
        landscapeArea = area;
        worldLandscape = true;
        FitToCamera(camera);
    }

    private void OnEnable() => FitToCamera(outputCamera);
    private void LateUpdate() => FitToCamera(outputCamera);

    public void FitToCamera(Camera camera)
    {
        if (camera == null || !camera.orthographic) return;
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (visual.sprite == null) return;
        Vector2 art = visual.sprite.bounds.size;
        float height = camera.orthographicSize * 2f;
        if (worldLandscape)
        {
            Vector2 worldCenter = landscapeArea.center;
            Vector3 view = camera.transform.position;
            float halfWidth = Mathf.Max(landscapeArea.width * 0.5f,
                Mathf.Abs(view.x - worldCenter.x) + height * camera.aspect * 0.5f);
            float halfHeight = Mathf.Max(landscapeArea.height * 0.5f,
                Mathf.Abs(view.y - worldCenter.y) + height * 0.5f);
            float worldScale = Mathf.Max(halfWidth * 2f / art.x, halfHeight * 2f / art.y) * overscan;
            transform.SetPositionAndRotation(new Vector3(worldCenter.x, worldCenter.y, backgroundDepth), Quaternion.identity);
            transform.localScale = Vector3.one * worldScale;
            return;
        }
        float scale = Mathf.Max(height * camera.aspect / art.x, height / art.y) * overscan;
        Vector3 center = camera.transform.position;
        transform.SetPositionAndRotation(new Vector3(center.x, center.y, backgroundDepth), Quaternion.identity);
        transform.localScale = Vector3.one * scale;
    }
}
