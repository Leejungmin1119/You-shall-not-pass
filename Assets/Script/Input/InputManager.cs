using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static PlayerInput PlayerInput;

    public static Vector2 Movement;

    //키 입력 확인
    public static bool JumpWasPressed;
    public static bool JumpIsHeld;
    public static bool JumpWasReleased;
    public static bool DashWasPressed;
    // 공격 입력
    public static bool AttackPressed;      // 홀드
    public static bool AttackWasPressed;   // 이번 프레임에 눌림

    // 스킬 입력 확인
    public static bool FirstSkillWasPressed;
    public static bool SecondSkillWasPressed;
    public static bool ThirdSkillWasPressed;
    public static bool JobSkillWasPressed;

    //UI 키 입력 확인
    public static bool MenuOpenInput;
    public static bool MenuCloseInput;
    public static bool InventoryWasPressed;
    public static bool PlayerDetailWasPressed;
    public static bool DeleteSaveWasPressed;
    public static bool MapViewWasPressed;

    // 기타 상호작용 키 입력
    public static bool InteractionWasPressed;
    // 컷씬 UI 상태에서만 사용하는 대사·안내 진행 입력입니다.
    public static bool CutsceneAdvanceWasPressed;
    //플레이어 동작 입력
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction DashAction;
    private InputAction AttackAction;

    // 플레이어 스킬 입력

    private InputAction FirstSkill;
    private InputAction SecondSkill;
    private InputAction ThirdSkill;
    private InputAction JobSkill;

    //상호작용 키
    private InputAction Interaction;
    private InputAction cutsceneAdvance;
    private string interactionOverridePath;
    private bool hasInteractionBindingCache;

    //UI 창 관련
    private InputAction menuOpen;
    private InputAction menuclose;
    private InputAction Inventory;
    private InputAction Playerdetail;
    private InputAction deleteSave;
    private InputAction toggleMapView;

    private void Awake()
    {
        // 키 할당

        //1. Input Action 의 할당되어 있는 기능을 각각 지정
        PlayerInput = GetComponent<PlayerInput>();
        moveAction = PlayerInput.actions["move"];
        jumpAction = PlayerInput.actions.FindAction("Jump");
        DashAction = PlayerInput.actions.FindAction("Dash");
        AttackAction = PlayerInput.actions.FindAction("Attack");

        //스킬 관련
        FirstSkill = PlayerInput.actions.FindAction("Skill1");
        SecondSkill = PlayerInput.actions.FindAction("Skill2");
        ThirdSkill = PlayerInput.actions.FindAction("Skill3");
        JobSkill = PlayerInput.actions.FindAction("JobSkill");

        // UI 창관련
        menuOpen = PlayerInput.actions.FindAction("OpenMenu");
        menuclose = PlayerInput.actions.FindAction("CloseMenu");
        Inventory = PlayerInput.actions.FindAction("Inventory");
        Playerdetail =PlayerInput.actions.FindAction("PlayerDetail");
        deleteSave = PlayerInput.actions.FindAction("DeleteSave", true);
        toggleMapView = PlayerInput.actions.FindAction("Camera/ToggleMapView", true);

        // Player/UI 맵의 상호작용은 이름을 분리해 현재 맵과 무관하게 확실히 찾습니다.
        Interaction = PlayerInput.actions.FindActionMap("Player", true).FindAction("Interaction", true);
        cutsceneAdvance = PlayerInput.actions.FindActionMap("UI", true).FindAction("CutsceneAdvance", true);
        SynchronizeCutsceneAdvanceBinding();
    }

    /// <summary>
    /// 플레이어의 기본 입력 맵과 별도로 전체 맵 보기 입력을 활성화합니다.
    /// </summary>
    private void OnEnable()
    {
        toggleMapView?.Enable();
    }

    /// <summary>
    /// 전체 맵 보기 입력을 해제하고 해당 프레임의 입력 상태를 초기화합니다.
    /// </summary>
    private void OnDisable()
    {
        toggleMapView?.Disable();
        MapViewWasPressed = false;
    }

    private void Update()
    {
        // 2. 실제로 사용자가 커스텀으로 설정한 값에 따라서 각 기능(걷기,달리기,w,a,s,d등 다양한 기능을 쓸 수 있도록 지원)

        Movement = moveAction.ReadValue<Vector2>();//w,a,s,d

        JumpWasPressed = jumpAction.WasPressedThisFrame(); // ?
        JumpWasReleased = jumpAction.WasReleasedThisFrame(); // 점프키를 입력중?
        JumpIsHeld = jumpAction.IsPressed(); // 점프키를 입력?

        // 대쉬 체크
        DashWasPressed = DashAction.WasPressedThisFrame();

        // 공격 키관련 입력 여부 체크
        AttackPressed = AttackAction.IsPressed();                 // 홀드
        AttackWasPressed = AttackAction.WasPressedThisFrame();    // 클릭

        //스킬 키관련 입력 여부 체크
        FirstSkillWasPressed = FirstSkill.WasPressedThisFrame();
        SecondSkillWasPressed = SecondSkill.WasPressedThisFrame();
        ThirdSkillWasPressed = ThirdSkill.WasPressedThisFrame();
        JobSkillWasPressed = JobSkill.WasPressedThisFrame();

        #region UI 키 입력확인

        MenuOpenInput = menuOpen.WasPressedThisFrame();
        MenuCloseInput = menuclose.WasPressedThisFrame();

        InventoryWasPressed = Inventory.WasPressedThisFrame();
        PlayerDetailWasPressed = Playerdetail.WasPressedThisFrame();
        DeleteSaveWasPressed = deleteSave.WasPressedThisFrame();
        MapViewWasPressed = toggleMapView.WasPressedThisFrame();
        #endregion

        // 리바인드 UI가 적용한 오버라이드를 감지해 컷씬 진행 키에 한 번만 반영합니다.
        SynchronizeCutsceneAdvanceBinding();

        InteractionWasPressed = Interaction.WasPressedThisFrame();
        CutsceneAdvanceWasPressed = cutsceneAdvance.WasPressedThisFrame();


    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize() // SceneLoad할때 로드
    {
        JumpWasPressed = false;
        JumpIsHeld = false;
        JumpWasReleased = false;
        DashWasPressed = false;
        AttackPressed = false;
        AttackWasPressed = false;

        FirstSkillWasPressed = false;
        SecondSkillWasPressed = false;
        ThirdSkillWasPressed = false;
        JobSkillWasPressed = false;

        MenuOpenInput = false;
        MenuCloseInput = false;
        InventoryWasPressed = false;
        PlayerDetailWasPressed = false;
        DeleteSaveWasPressed = false;
        MapViewWasPressed = false;

        InteractionWasPressed = false;
        CutsceneAdvanceWasPressed = false;
        Movement = Vector2.zero;
    }

    /// <summary>
    /// 일반 상호작용 키의 오버라이드를 UI 컷씬 진행 키에 복사합니다.
    /// 설정 화면은 Interaction 하나만 변경하면 됩니다.
    /// </summary>
    private void SynchronizeCutsceneAdvanceBinding()
    {
        if (Interaction == null || cutsceneAdvance == null ||
            Interaction.bindings.Count == 0 || cutsceneAdvance.bindings.Count == 0)
            return;

        string overridePath = Interaction.bindings[0].overridePath;
        if (hasInteractionBindingCache && interactionOverridePath == overridePath)
            return;

        hasInteractionBindingCache = true;
        interactionOverridePath = overridePath;
        if (string.IsNullOrEmpty(overridePath))
            cutsceneAdvance.RemoveBindingOverride(0);
        else
            cutsceneAdvance.ApplyBindingOverride(0, overridePath);
    }

}
