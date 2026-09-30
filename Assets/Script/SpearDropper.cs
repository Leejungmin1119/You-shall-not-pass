using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 필드를 좌클릭하면 커서의 x좌표, 화면 상단에서 창을 떨어뜨린다.
/// </summary>
public class SpearDropper : MonoBehaviour
{
    [Header("Spear")]
    public LayerMask GroundLayer;
    public float FallSpeed = 30f;
    public float StuckDuration = 2f;

    [Header("Placeholder Visual")]
    public Vector2 SpearSize = new Vector2(0.2f, 1.5f);
    public Color SpearColor = new Color(0.9f, 0.75f, 0.2f);
    public int SortingOrder = 10;

    private static Sprite _placeholderSprite;

    private Camera _camera;

    private void Awake()
    {
        _camera = Camera.main;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Drop(mouse.position.ReadValue());
    }

    public Spear Drop(Vector2 screenPosition)
    {
        float x = _camera.ScreenToWorldPoint(screenPosition).x;
        float top = _camera.ViewportToWorldPoint(new Vector3(0f, 1f, 0f)).y;

        GameObject spearObject = new GameObject("Spear");
        spearObject.transform.position = new Vector3(x, top, 0f);
        spearObject.transform.localScale = new Vector3(SpearSize.x, SpearSize.y, 1f);

        SpriteRenderer spriteRenderer = spearObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetPlaceholderSprite();
        spriteRenderer.color = SpearColor;
        spriteRenderer.sortingOrder = SortingOrder;

        Spear spear = spearObject.AddComponent<Spear>();
        spear.Launch(FallSpeed, StuckDuration, GroundLayer);
        return spear;
    }

    private static Sprite GetPlaceholderSprite()
    {
        if (_placeholderSprite == null)
        {
            // 피벗을 아래 끝에 둬서 오브젝트 위치가 곧 창끝이 되게 한다. 꽂힐 때 창끝이 지면에 닿는다.
            Texture2D texture = Texture2D.whiteTexture;
            _placeholderSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0f),
                texture.width);
        }

        return _placeholderSprite;
    }
}
