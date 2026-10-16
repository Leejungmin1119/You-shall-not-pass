using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 서버에서 손에 든 아이템 하나를 관리하고 소유자의 구매·사용 입력과 커서 표시를 연결한다.
/// 부활해도 보유 상태는 유지하며 인벤토리나 취소 기능은 아직 사용하지 않는다.
/// </summary>
[RequireComponent(typeof(PlayerCost), typeof(PlayerState))]
public sealed class PlayerHeldItem : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnHeldItemChanged))] private int heldItemId;
    [SyncVar] private bool hasActiveMissiles;
    private readonly List<Missile> activeMissiles = new List<Missile>();
    private float nextAimSendTime;
    private double nextServerAimTime;
    private const float AimInterval = 0.05f;
    private PlayerState state;
    private ConveyorController conveyor;
    private SpearNetwork itemNetwork;
    private ItemData heldData;
    private bool requestPending;
    private double nextUseTime;
    private readonly List<SpriteRenderer> previews = new List<SpriteRenderer>(3);
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData pointerData;
    public int HeldItemId => heldItemId;
    public bool HasHeldItem => heldItemId != 0;
    public bool CanHoldItem => state != null && state.Status == PlayerStatus.Alive && !HasHeldItem;
    public bool CanRequestPurchase => isLocalPlayer && isClient && !requestPending && CanHoldItem;

    /// <summary>자기 상태와 게임 씬의 구매·생성 담당을 보관한다.</summary>
    private void Awake()
    {
        state = GetComponent<PlayerState>();
        FindSceneReferences();
    }

    /// <summary>씬 초기화 순서와 무관하게 필요한 참조를 다시 찾는다.</summary>
    private void FindSceneReferences()
    {
        conveyor = FindFirstObjectByType<ConveyorController>(FindObjectsInactive.Include);
        itemNetwork = FindFirstObjectByType<SpearNetwork>(FindObjectsInactive.Include);
    }

    /// <summary>소유자의 커서 표시를 현재 보유 상태와 맞춘다.</summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        FindSceneReferences();
        RefreshHeldVisual();
    }

    /// <summary>입력 관리자가 갱신된 뒤 필드 사용 입력과 표시 위치를 처리한다.</summary>
    private void LateUpdate()
    {
        if (!isLocalPlayer || conveyor == null || Mouse.current == null) return;
        Vector2 screenPosition = Mouse.current.position.ReadValue();
        // 가상 창이 포커스를 잃거나 최소화될 때 전달되는 유효하지 않은 좌표는 표시에도 쓰지 않는다.
        if (!IsValidScreenPosition(screenPosition))
        {
            if (conveyor.HeldIcon != null) conveyor.HeldIcon.enabled = false;
            foreach (SpriteRenderer marker in previews) if (marker != null) marker.enabled = false;
            return;
        }
        UpdateMissileAim(screenPosition);
        UpdateHeldVisual(screenPosition);
        if (InputManager.AttackWasPressed)
            RequestUse(screenPosition);
    }

    /// <summary>서버에서 이 발사자의 미사일만 조준 입력을 받도록 등록한다.</summary>
    [Server]
    public void RegisterMissile(Missile missile)
    {
        if (!activeMissiles.Contains(missile)) activeMissiles.Add(missile);
        hasActiveMissiles = activeMissiles.Count > 0;
    }

    /// <summary>마지막 미사일이 사라지면 소유자의 좌표 전송을 중단한다.</summary>
    public void UnregisterMissile(Missile missile)
    {
        if (!isServer) return;
        activeMissiles.Remove(missile);
        hasActiveMissiles = activeMissiles.Count > 0;
    }

    /// <summary>비행 중에만 현재 카메라의 마우스 좌표를 초당 최대 20회 보낸다.</summary>
    private void UpdateMissileAim(Vector2 screenPosition)
    {
        if (!hasActiveMissiles || Time.unscaledTime < nextAimSendTime || IsPointerOverUI(screenPosition)) return;
        Camera camera = conveyor.OutputCamera;
        Vector2 point = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
        nextAimSendTime = Time.unscaledTime + AimInterval;
        CmdUpdateMissileAim(point);
    }

    /// <summary>소유자의 요청만 받아 유효한 조준점을 해당 소유자의 미사일에 적용한다.</summary>
    [Command(channel = Channels.Unreliable)]
    private void CmdUpdateMissileAim(Vector2 position)
    {
        if (!hasActiveMissiles || NetworkServer.isLoadingScene || itemNetwork == null ||
            NetworkTime.time < nextServerAimTime || !itemNetwork.IsValidAim(position)) return;
        nextServerAimTime = NetworkTime.time + AimInterval * 0.8f;
        foreach (Missile missile in activeMissiles)
            if (missile != null) missile.SetAim(position);
    }

    /// <summary>빈손인 소유자만 판매 항목 번호를 서버에 전달한다.</summary>
    public bool RequestPurchase(uint offerId)
    {
        if (!CanRequestPurchase || conveyor == null) return false;
        requestPending = true;
        CmdRequestPurchase(offerId);
        return true;
    }

    /// <summary>서버가 판매 목록과 잔액을 확인하고 구매 결과를 요청자에게 보낸다.</summary>
    [Command]
    private void CmdRequestPurchase(uint offerId)
    {
        bool success = !NetworkServer.isLoadingScene && conveyor != null && conveyor.TryCollect(offerId, this);
        TargetPurchaseResult(offerId, success);
    }

    /// <summary>서버에서 구매 검사가 끝난 아이템을 손에 넣는다.</summary>
    [Server]
    public void HoldItem(ItemData data)
    {
        heldItemId = data.Id;
    }

    /// <summary>UI 위 클릭을 차단하고 현재 카메라의 월드 좌표와 화면 상단을 전달한다.</summary>
    public bool RequestUse(Vector2 screenPosition)
    {
        if (!isLocalPlayer || !isClient || requestPending || !HasHeldItem ||
            state.Status != PlayerStatus.Alive || conveyor == null || conveyor.OutputCamera == null ||
            !IsValidScreenPosition(screenPosition) || IsPointerOverUI(screenPosition))
            return false;
        Camera camera = conveyor.OutputCamera;
        float depth = -camera.transform.position.z;
        Vector2 cursor = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        float startY = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, camera.pixelRect.yMax, depth)).y;
        requestPending = true;
        CmdRequestUse(cursor, startY);
        return true;
    }

    /// <summary>서버의 실제 보유 아이템만 사용하고 성공한 경우에만 손을 비운다.</summary>
    [Command]
    private void CmdRequestUse(Vector2 cursor, float startY)
    {
        ItemData data = conveyor != null ? conveyor.GetItemData(heldItemId) : null;
        bool success = !NetworkServer.isLoadingScene && state.Status == PlayerStatus.Alive &&
            NetworkTime.time >= nextUseTime && data != null && itemNetwork != null &&
            itemNetwork.TryUse(data, netIdentity, cursor, startY);
        if (success)
        {
            heldItemId = 0;
            nextUseTime = NetworkTime.time + Mathf.Max(0f, itemNetwork.UseCooldown);
        }
        TargetUseResult(success);
    }

    /// <summary>구매 응답을 받으면 중복 요청 잠금을 풀고 판매 UI의 대기 상태를 해제한다.</summary>
    [TargetRpc]
    private void TargetPurchaseResult(uint offerId, bool success)
    {
        requestPending = false;
        if (conveyor != null) conveyor.CompleteCollectRequest(offerId);
        Debug.Log(success ? "구매 완료: 아이템을 손에 들었습니다." : "구매 실패: 상품, 잔액 또는 손에 든 상태를 확인하세요.");
    }

    /// <summary>사용 실패 시에도 보유 아이템은 유지하며 다음 입력을 허용한다.</summary>
    [TargetRpc]
    private void TargetUseResult(bool success)
    {
        requestPending = false;
        if (!success) Debug.Log("사용 실패: 사용 위치 또는 재사용 대기 시간을 확인하세요.");
    }

    /// <summary>서버에서 보유 종류가 바뀌면 소유자의 아이콘과 미리보기를 갱신한다.</summary>
    private void OnHeldItemChanged(int previous, int current)
    {
        if (isLocalPlayer) RefreshHeldVisual();
    }

    /// <summary>현재 아이템의 커서 아이콘과 종류별 착지 미리보기를 준비한다.</summary>
    private void RefreshHeldVisual()
    {
        ClearPreviews();
        heldData = conveyor != null ? conveyor.GetItemData(heldItemId) : null;
        if (conveyor == null || conveyor.HeldIcon == null) return;
        conveyor.HeldIcon.sprite = heldData != null ? heldData.Icon : null;
        conveyor.HeldIcon.enabled = heldData != null;
        conveyor.HeldIcon.rectTransform.localScale = Vector3.one * (heldData != null ? heldData.HeldIconScale : 1f);
        if (heldData == null || heldData.Prefab == null || heldData.Placement != ItemPlacement.Drop) return;
        for (int index = 0; index < heldData.Count; index++)
        {
            SpriteRenderer marker = heldData.Prefab.CreatePlacementPreview(Vector2.zero);
            if (marker != null) previews.Add(marker);
        }
    }

    /// <summary>커서 아이콘은 클릭을 가로채지 않고, UI 밖에서는 착지할 곳을 표시한다.</summary>
    private void UpdateHeldVisual(Vector2 screenPosition)
    {
        if (conveyor.HeldIcon != null)
        {
            conveyor.HeldIcon.enabled = heldData != null;
            float iconScale = heldData != null ? heldData.HeldIconScale : 1f;
            conveyor.HeldIcon.rectTransform.position = screenPosition + new Vector2(24f, -24f) * iconScale;
        }
        bool show = heldData != null && !requestPending && !IsPointerOverUI(screenPosition) &&
            conveyor.OutputCamera != null && itemNetwork != null;
        for (int index = 0; index < previews.Count; index++)
        {
            SpriteRenderer marker = previews[index];
            if (!show) { marker.enabled = false; continue; }
            Camera camera = conveyor.OutputCamera;
            float depth = -camera.transform.position.z;
            Vector2 cursor = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
            float x = cursor.x + (index - (heldData.Count - 1) * 0.5f) * heldData.Spacing;
            float top = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, camera.pixelRect.yMax, depth)).y;
            RaycastHit2D hit = Physics2D.Raycast(new Vector2(x, top), Vector2.down, 200f, itemNetwork.GroundLayer);
            marker.enabled = hit;
            if (hit) marker.transform.position = new Vector3(x, hit.point.y, 0f);
        }
    }

    /// <summary>포커스 전환 중의 잘못된 값과 게임 화면 밖 좌표를 입력 단계에서 차단한다.</summary>
    private bool IsValidScreenPosition(Vector2 position)
    {
        return !float.IsNaN(position.x) && !float.IsNaN(position.y) &&
            !float.IsInfinity(position.x) && !float.IsInfinity(position.y) &&
            conveyor != null && conveyor.OutputCamera != null &&
            conveyor.OutputCamera.pixelWidth > 0 && conveyor.OutputCamera.pixelHeight > 0 &&
            conveyor.OutputCamera.pixelRect.Contains(position);
    }

    /// <summary>컨베이어와 UI 영역만 확인하고 월드의 물리 판정은 제외한다.</summary>
    private bool IsPointerOverUI(Vector2 position)
    {
        if (conveyor != null && conveyor.IsPointerOverViewport(position)) return true;
        if (EventSystem.current == null) return false;
        if (pointerData == null) pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = position;
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointerData, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }

    /// <summary>보유 변경 또는 연결 종료 시 월드 미리보기를 지운다.</summary>
    private void ClearPreviews()
    {
        foreach (SpriteRenderer marker in previews)
            if (marker != null) Destroy(marker.gameObject);
        previews.Clear();
    }

    /// <summary>소유자 연결 종료 시 씬의 커서 표시를 원상태로 돌린다.</summary>
    public override void OnStopClient()
    {
        ClearPreviews();
        if (isLocalPlayer && conveyor != null && conveyor.HeldIcon != null)
        {
            conveyor.HeldIcon.enabled = false;
            conveyor.HeldIcon.rectTransform.localScale = Vector3.one;
        }
        base.OnStopClient();
    }
}
