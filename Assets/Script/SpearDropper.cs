using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 필드를 좌클릭하면 커서의 x좌표에서 창을 떨어뜨린다.
/// 네트워크에 연결돼 있으면 호스트에 요청만 보내고, 창은 호스트가 정한 뒤 모든 PC에서 만든다.
/// </summary>
public class SpearDropper : MonoBehaviour
{
    [Header("Spear")]
    public LayerMask GroundLayer;
    public float FallSpeed = 30f;
    public float StuckDuration = 2f;
    // 창이 시작하는 월드 y. 카메라 위쪽 끝을 쓰면 PC마다 값이 달라질 수 있어 고정값으로 둔다.
    public float SpawnHeight = 8f;

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

    /// <summary>
    /// 접속 중이면 호스트에 요청만 보내고 null을 돌려준다(창은 호스트의 응답으로 생긴다).
    /// 네트워크가 없는 씬에서는 그 자리에서 창을 만들어 돌려준다.
    /// </summary>
    public Spear Drop(Vector2 screenPosition)
    {
        if (_camera == null) return null;
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return null;
        Vector2 cursorWorld = _camera.ScreenToWorldPoint(screenPosition);
        if (RaceSession.Instance != null)
        {
            NetworkClient.localPlayer?.GetComponent<NetworkRaceInventory>()?.RequestUse(cursorWorld);
            return null;
        }

        if (NetworkClient.isConnected)
        {
            SpearNetwork.SendUseRequest(cursorWorld);
            return null;
        }

        // 네트워크 매니저가 있는 씬에서 접속 전에 누른 클릭은 무시한다. 혼자만 보이는 창이 생기면 안 된다.
        if (NetworkManager.singleton != null)
        {
            return null;
        }

        return Spawn(cursorWorld.x, SpawnHeight, 0, -1, true, 0f);
    }

    public Spear Spawn(float x, float startY, uint spearId, int ownerId, bool isJudge, float catchUpSeconds)
    {
        GameObject spearObject = new GameObject("Spear");
        spearObject.transform.position = new Vector3(x, startY, 0f);
        spearObject.transform.localScale = new Vector3(SpearSize.x, SpearSize.y, 1f);

        SpriteRenderer spriteRenderer = spearObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetPlaceholderSprite();
        spriteRenderer.color = SpearColor;
        spriteRenderer.sortingOrder = SortingOrder;

        Spear spear = spearObject.AddComponent<Spear>();
        spear.Launch(FallSpeed, StuckDuration, GroundLayer, isJudge, spearId, ownerId, catchUpSeconds);
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
