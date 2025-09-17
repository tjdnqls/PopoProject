using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMouseMovement : MonoBehaviour
{
    // === 필수 컴포넌트 / 레이어 ===
    public Rigidbody2D rb;
    public Animator rb2;

    [Header("Layers")]
    // ▼▼ LayerMask 대신 "이름"만 설정 (기본값 그대로 쓰면 인스펙터 세팅 불필요)
    [Header("Layer Names (auto-resolve)")]
    [SerializeField] private string groundLayerName = "Ground";
    [SerializeField] private string eventLayerName = "EventGround, OneWayGround";
    [SerializeField] private string trapLayerName = "Trap";
    [SerializeField] private string slimeLayerName = "Slime";
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string monsterLayerName = "Monster";

    [Header("Carry Drop/Throw Spawn")]
    [SerializeField] private float carryDropForward = 0.4f;   // 내려놓기 시, 바라보는 방향 앞으로 얼마나 둘지

    [Header("Auto Carry (Midair Catch)")]
    [SerializeField] private bool autoCatchEnabled = true;          // 오토 캐치 On/Off
    [SerializeField] private Vector2 headCatchBoxSize = new(0.70f, 0.35f); // P1 머리 위 캐치 박스 크기
    [SerializeField] private Vector2 headCatchBoxOffset = new(0f, 0.10f);  // P1 머리 위 기준 추가 오프셋(Y는 머리 윗면에서 더 올림)
    [SerializeField] private float autoCatchMinHeightAbove = 0.05f; // P2 발이 머리보다 최소 이만큼 높아야(두 번째 조건용)
    [SerializeField] private bool onlyCatchDuringThrowWindow = false; // true면 '던진 뒤(탄도창)'에만 캐치
    [SerializeField] private float autoCatchCooldown = 0.15f;       // 반복 캐치 튕김 방지
    [SerializeField] private float autoCatchBlockOnThrow = 0.2f; // 던진 직후 차단 시간
    private float autoCatchSuppressUntil = -1f;                  // 이 시각 전엔 오토캐치 금지
    private float nextAutoCatchAllowedAt = 0f;
    // === Auto Catch – Whole Body ===
    [Header("Auto Catch – Whole Body")]
    [SerializeField] private bool autoCatchUseWholeBody = true;           // 몸 전체 판정 사용
    [SerializeField] private Vector2 bodyCatchPadding = new(0.06f, 0.06f); // P1 바디 박스 확장량(여유)

    // === Camera Auto-Focus (to P1) ===
    [Header("Camera Auto-Focus (to P1)")]
    [SerializeField] private bool autoForceViewToP1OnCatch = true;        // 캐치 시 카메라/시점 P1 강제
    [SerializeField] private UnityEngine.Events.UnityEvent onForceViewToP1; // (선택) 카메라 스크립트 훅
    // === Auto Carry Gate ===
    [Header("Auto Carry Gate")]
    [SerializeField] private bool autoCatchRequireP2Descending = true;         // P2가 내려오는 중일 때만
    [SerializeField] private float autoCatchMaxHoriz = 0.60f;                  // 가로 허용 오차
    [SerializeField] private Vector2 autoCatchVerticalRange = new(0.05f, 0.90f); // 머리 위 최소/최대 높이
    [SerializeField] private bool autoCatchDisallowIfBlockingCeiling = true;   // 사이에 지형/트랩 있으면 금지
    [SerializeField] private LayerMask autoCatchObstructionMask;               // Ground|Event|Trap 등

    [SerializeField] private bool autoCatchDisallowIfP1Busy = true;            // P1이 바쁠 땐 금지(공격/락 등)
    [SerializeField] private bool autoCatchDisallowIfP2Hidden = true;          // P2가 숨김상태면 금지

    // Animator 파라미터 이름(오토캐치용)
    [SerializeField] private string carryingBoolName = "carrying";             // 오토캐치 ON/OFF
    [SerializeField] private string carryEndTriggerName = "carryEnd";          // 캐리 해제 연출용 트리거(있으면 사용)
    [SerializeField] private string carryEndStateName = "CarryEnd";            // 없으면 이 상태로 크로스페이드
    // 오토캐치 시 캐리 애니를 몇 프레임부터 시작할지
    [SerializeField] private int autoCatchCarryStartFrame = 6;

    // 캐리 애니메이션 상태 이름(Animator의 State 이름)
    [SerializeField] private string carryStateName = "Carry";

    // (선택) 정확한 길이/프레임을 얻고 싶으면 클립 참조도 함께 지정
    [SerializeField] private AnimationClip carryClipRef;
    // === 던지기 시작 위치(인스펙터로 조정) ===
    [Header("Throw Start (Inspector Control)")]
    [Tooltip("던지기 시작 지연(초). 이 시간이 지난 뒤 보이면서 실제로 날아가기 시작합니다.")]
    [SerializeField] private float throwDelay = 0.25f;
    [Tooltip("월드 좌표로 지정할 수 있는 시작 위치. 설정되면 오프셋 대신 이 위치를 사용합니다.")]
    [SerializeField] private Transform throwStartWorldPoint;
    [Tooltip("P1의 현재 위치 기준 로컬 오프셋 (x는 좌/우 방향에 따라 자동으로 부호가 붙습니다).")]
    [SerializeField] private Vector2 throwStartLocalOffset = new(0.35f, 0.6f);
    [Tooltip("로컬 오프셋 X를 P1의 바라보는 방향 기준으로 좌/우 반전할지 여부")]
    [SerializeField] private bool throwStartUseFacing = true;

    // 내부 캐시 (코드에서만 사용)
    private int groundMask, eventMask, trapMask, slimeMask;
    private LayerMask slimeLayerMask; // ContactFilter2D 용
    private int trapLayerIndex;
    public Player1HP dead;
    // Throw 중엔 Run 애니메이션을 잠깐 막기 위한 플래그
    [SerializeField] private bool throwmanager = true;
    // === Slime Stick Tuning ===
    [SerializeField] private float slimeStickPush = 22f;
    [SerializeField] private float slimeNormalClamp = 20f;
    [SerializeField] private float carrySlideMaxFall = -11f;

    // === Ceiling Slime (Head Stick) ===
    [Header("Ceiling Slime (Head Stick)")]
    [SerializeField] private bool enableCeilingSlime = true;
    [SerializeField] private float ceilingStickMaxTime = 5f;      // 5초 유지
    [SerializeField] private float ceilingReleaseFade = 0.6f;     // 서서히 떨어지는 시간
    [SerializeField] private float ceilingKeepGap = 0.02f;        // 머리-천장 간격 유지
    [SerializeField] private float ceilingRestickBlock = 0.25f;   // 떨어진 직후 재부착 금지 시간

    private bool stickingToCeiling = false;
    private float ceilingStickStartTime = -1f;
    private float ceilingReleaseUntil = -1f;   // >0 이면 release 페이드 중
    private float ignoreCeilingUntil = -1f;    // 이 시각 전엔 머리로 다시 안 붙음
    private float lastCeilingY = 0f;           // 붙었던 천장 Y 캐시(유지용)

    // === Wall Detach(이탈 유예창) ===
    [SerializeField] private float wallDetachGrace = 0.13f;
    [NonSerialized] public float ignoreSlimeUntil = -1f;
    public bool IsSlimeSuppressed => Time.time < ignoreSlimeUntil;

    [Header("F Pulse Object")]
    [SerializeField] private GameObject selectedObject;
    [SerializeField] private float fPulseDuration = 0.3f;
    private float fPulseOffAt = -1f;

    [Header("플레이어 ID 설정")]
    public SwapController.PlayerChar playerID;
    public SwapController swap;

    [Header("Carry Animation/Lock")]
    [SerializeField] private float carryLockDuration = 0.6f; // 기존 고정 잠금(백업용)
    [SerializeField] private string carryBoolName = "carry"; // Animator Bool 파라미터명

    // === 캐리 시작 규칙 ===
    [Header("Carry Start Rules")]
    [SerializeField] private bool requireGroundedForCarryStart = true;       // P1 접지 필수
    [SerializeField] private bool requireOtherGroundedForCarryStart = false; // P2 접지까지 필수

    // === Carry Timing (anim-driven) ===
    [Header("Carry Timing (Anim-driven)")]
    [SerializeField] private bool useAnimDrivenCarry = true;
    [SerializeField] private float carryStartMinLock = 0.08f;
    [SerializeField] private float carryEndMinLock = 0.06f;
    // ▼ 내려놓기: 정확히 0.6초 후에 보이게 고정
    [SerializeField] private float revealDelayOnDrop = 0.6f; // EXACT 0.6s
    private Coroutine _carryLockCo;
    private Coroutine _revealCo;       // P2 복귀 코루틴
    private Coroutine _throwResetCo;   // throw 종료 타이머
    private Coroutine _delayedThrowCo; // 던지기 지연 코루틴

    // === 접지/레이 거리 ===
    [Header("Ray distances")]
    public float groundrayDistance = 1.3f;
    public float breakrayDistance = 1.4f;
    public float checkceilingtrap = 0.7f;

    [Header("Carry Cooldown")]
    [SerializeField] private float carryCooldown = 1.4f; // 해제 후 추가 쿨타임
    private float nextCarryAllowedAt = 1f;               // 다음 캐리 허용 시각

    // === 키보드 이동 파라미터 (아이워너 느낌) ===
    [Header("Keyboard Movement (IWB-style)")]
    [SerializeField] public float moveSpeed = 9.5f;
    [SerializeField] private float accel = 180f;
    [SerializeField] private float decel = 220f;
    [SerializeField] private float airAccel = 130f;
    [SerializeField] private float airDecel = 150f;
    [SerializeField] private float jumpVelocity = 11.8f;
    [SerializeField] private float gravityScaleNormal = 3.2f;
    [SerializeField] public float gravityScaleFall = 5.0f;
    [SerializeField] private float cutJumpFactor = 0.45f;
    [SerializeField] private float maxFallSpeed = -28f;
    [SerializeField] private float coyoteTime = 0.06f;
    [SerializeField] private float jumpBuffer = 0.08f;

    [Header("Slime Friction Control")]
    [SerializeField] private PhysicsMaterial2D slimeNoFrictionMat;
    private PhysicsMaterial2D _originalMat;
    private bool _appliedNoFriction;
    // === Ceiling Slime (Head Stick) ===
    [Header("Ceiling Slime (Head Stick)")]
    [SerializeField] private float headCheckDist = 0.12f;              // 머리 위 슬라임 감지 거리
    [SerializeField] private float ceilingStickDuration = 5f;           // 붙어있는 시간
    [SerializeField] private float ceilingReleaseBlendTime = 0.8f;      // 떨어질 때 부드럽게 전환
    [SerializeField] private float ceilingReleaseSlideMaxFall = -2.5f;  // 해제 직후 잠깐 천천히 낙하
    [SerializeField] private float ceilingAttachSkin = 0.01f;           // 천장에 스냅 붙일 때 여유

    private float ceilingStickUntil = -1f;
 

    [Header("Ground Snap")]
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private float snapProbe = 0.20f;
    [SerializeField] private float snapSkin = 0.02f;

    [Header("Jump Feel Tuning")]
    [SerializeField] private float minJumpHoldTime = 0.06f;
    [SerializeField] private float apexThreshold = 0.8f;
    [SerializeField] private float apexHangMultiplier = 0.7f;
    [SerializeField] private float gravitySmoothTime = 0.06f;

    [Header("Carry Throw (Ballistic)")]
    [SerializeField] private float carryThrowUpSpeed = 12f;
    [SerializeField] private float carryThrowSideSpeed = 8f;
    [SerializeField] private float carryThrowSeparation = 0.18f;
    [SerializeField] private float carryThrowBallisticMinTime = 1f;
    [SerializeField] private bool carryThrowHoldUntilGrounded = false;

    // === Carry 안전 검사 ===
    [Header("Carry Safety Check")]
    [SerializeField] private float releaseAheadCheckDist = 0.45f; // 앞쪽 벽 체크 거리
    [SerializeField] private float releaseGroundProbeDown = 2.0f; // 낭떠러지(아래) 체크 거리
    [SerializeField] private Vector2 releaseSpawnBoxSize = new(0.40f, 0.90f); // P2가 설 자리 박스 크기

    [Header("Slime Stick Tuning")]
    [SerializeField] private float slimeInwardHoldSpeed = 0.8f;
    [SerializeField] private float slimeInwardAccel = 35f;
    [SerializeField] private float wallSlideMaxFallCarrying = -12f;

    // 내부 상태
    private bool ballisticThrowActive = false;
    private float ballisticThrowEndTime = -1f;
    private float lastJumpStartTime = -999f;
    private float swapSuppressUntil = -999f;
    private bool didCutThisJump = false;
    private float gravitySmoothVel = 0f;
    private int playerLayerIndexSelf;
    private int monsterLayerIndex; // 추가

    // === 더블 점프 ===
    [Header("Extra Jumps")]
    [SerializeField] public int extraAirJumps = 1;
    private int airJumpsLeft = 0;

    // === 바운스 패널 관련(유지) ===
    [Header("Bounce Panels")]
    [SerializeField] private float bounceImpulseX = 12f;
    [SerializeField] private float bounceImpulseY = 15f;
    [SerializeField] private float inputLockAfterImpulse = 0.12f;
    [SerializeField] private float bounceProtectDuration = 0.06f;

    [Header("Slime Wall")]
    [SerializeField] private LayerMask slimeLayer;
    [SerializeField] private float wallCheckDist = 0.18f;
    [SerializeField] private float wallSlideMaxFall = -5.5f;
    [SerializeField] private float wallJumpHorizontal = 9.0f;
    [SerializeField] private float wallJumpVertical = 11.5f;
    [SerializeField] private bool requireSpaceForWallJump = false;
    [SerializeField] private bool resetAirJumpsOnWallJump = true;
    // --- 월점프 후 '반대키' 입력 잠금 ---
    [Header("Wall Jump Input Lock")]
    [SerializeField] private float wallOppositeInputLock = 0.5f; // 0.5초 잠금
    private float oppositeInputLockUntil = -1f;
    private int oppositeInputLockedDir = 0; // -1 = Left(A/←)을 막음, +1 = Right(D/→)을 막음

    [Header("Slime Stick Grace")]
    [SerializeField] private float slimeStickAfterLeave = 0.3f;   // 떨어진 뒤 유지 시간
    private float lastSlimeTouchAt = -999f;                      // 마지막 접촉 시각
    private int lastSlimeSide = 0;  // -1 = 왼쪽 벽(법선 +X), +1 = 오른쪽 벽(법선 -X)

    // === 내려찍기 (추가) ===
    [Header("Dive (Down Slam)")]
    [SerializeField] private float diveSpeed = -36f;
    [SerializeField] private float diveGravityScale = 7.5f;
    private bool isDiving = false;

    // === 캐리(안아 들기) 관련 ===
    [Header("Carry (P1 carries P2)")]
    public PlayerMouseMovement otherPlayer;
    public float carryOffsetY = 0.5f;
    public float carryPickupMaxGap = 0.15f;
    public bool carryset = false;   // 호환 유지
    public bool isCarrying = false;
    public bool isCarried = false;
    private Transform otherOriginalParent;

    [Header("Carry Gravity")]
    [SerializeField] private float carryGravityMul = 1.15f;
    [SerializeField] private float carryFallGravityMul = 1.25f;
    private float baseGravityNormal;
    private float baseGravityFall;

    // --- 월점프 직후 같은 벽 재부착 금지용 ---
    [Tooltip("월점프 직후 같은 벽으로 재부착 금지 시간")]
    [SerializeField] private float wallRegrabBlock = 0.30f;
    private float wallRegrabUntil = -1f; // 이 시각 전까지 재부착 금지
                                         // -1 = 왼쪽벽(법선 +X), +1 = 오른쪽벽(법선 -X), 0 = 없음
    private int wallRegrabSide = 0;

    [Header("Health Setup")]
    [SerializeField] private int p1MaxHP = 2;
    [SerializeField] private int p2MaxHP = 1;

    // 내부 상태
    private bool _sceneReloading = false; // 리로드 중복 방지

    [Header("Ground Check Fix")]
    [SerializeField] private float groundCheckSkin = 0.04f;
    [SerializeField] private float postJumpGroundIgnore = 0.06f;
    private float ignoreGroundUntil = -1f;

    // === Bounce 속도 튜닝 ===
    [Header("Bounce Speed Tuning")]
    [SerializeField] private bool smoothBounce = true;
    [SerializeField] private float bounceTargetSpeed = 14f;
    [SerializeField] private float bounceRampTime = 0.12f;
    [SerializeField] private float bounceMaxSpeed = 18f;

    [Header("애니메이션")]
    // rb2 사용 (Animator)
    private float janit = 0f;

    // === 공격 ===
    [Header("Attack")]
    [SerializeField] private float attackCooldown = 1.0f; // 쿨타임 1초
    [SerializeField] private float attackDuration = 0.5f; // 공격 애니 길이(자동 종료)
    [SerializeField] private float attackHitDelay = 0.2f;
    private float nextAttackTime = 0f; // 다음 사용 가능 시각
    private float attackEndTime = -1f; // 공격 종료 시각
    private bool attack = false;       // 공격 중 여부 (애니 bool과 동기화)
    private Coroutine _attackPulseCo; // 지연 코루틴 핸들
    public int maxHP = 5;
    public int currentHP;
    public bool IsDead { get; private set; } = false;

    // === Death Fall ===
    [Header("Death Fall")]
    [SerializeField] private float deadHorizontalDamp = 6f;   // 사망 후 가로 감쇠 속도(0이면 감쇠 안 함)
    [SerializeField] private bool keepFallingOnDeath = true;  // 사망해도 계속 낙하

    [Header("Step Up (auto climb small ledges)")]
    [SerializeField] private bool enableStepUp = true;   // 자동 스텝업 On/Off
    [SerializeField] private float stepUpMax = 0.18f;    // 최대 올라탈 높이(0.1~0.2 추천)
    [SerializeField] private float stepForward = 0.10f;  // 발 앞쪽 탐색 거리
    [SerializeField] private float stepUpSkin = 0.01f;   // 살짝 더 올려 겹침 방지
    [SerializeField] private float stepOnlyWhenFallingVy = 0.05f; // 위로 점프중엔 스킵
                                                                  // --- 추가 파라미터 ---
    [SerializeField] private float seamFixProbe = 0.03f;  // 발 앞 세로면 탐색 폭
    [SerializeField] private float seamFixLift = 0.03f;  // 살짝 들어올릴 높이
    // === 내부 상태 ===
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private float rawX = 0f;
    private bool jumpHeld = false;
    private float inputLockUntil = -999f;

    private bool isBouncing = false;
    private float bounceProtectUntil = -999f;
    private bool inBounceFlight = false;
    private float bounceRampTimer = 0f;
    private Vector2 bounceTargetVel;
    private float bounceVxRef = 0f, bounceVyRef = 0f;

    private bool lefthold;
    private bool righthold;
    private bool prevSelected = false;
    private bool lockedall;

    private bool touchingLeftSlime, touchingRightSlime;
    private bool touchL_byCollision, touchR_byCollision;
    private bool touchL_byTrigger, touchR_byTrigger;

    // 접지 전/후 변화 감지용
    private bool wasGrounded = false;

    // === 스케일/방향 ===
    public float dir = 1f;
    public bool dirseto = true;
    public bool chasize = true;
    public float dirsetofl = 1f;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!bodyCollider)
        {
            bodyCollider = GetComponent<Collider2D>();
            if (!bodyCollider) bodyCollider = GetComponentInChildren<Collider2D>();
        }
        if (!rb2) rb2 = GetComponent<Animator>();

        _originalMat = bodyCollider ? bodyCollider.sharedMaterial : null;

        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        maxHP = playerID == SwapController.PlayerChar.P1 ? p1MaxHP : p2MaxHP;
        currentHP = maxHP;

        Physics2D.queriesHitTriggers = true;

        baseGravityNormal = gravityScaleNormal;
        baseGravityFall = gravityScaleFall;

        TryResolveSwap();
        ResolveLayerMasks();
        ApplyLayerIgnores();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) TryResolveSwap();
    }
#endif

    private int GetMaskFromCsv(string namesCsv)
    {
        if (string.IsNullOrWhiteSpace(namesCsv)) return 0;
        string[] parts = namesCsv.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return LayerMask.GetMask(parts);
    }

    private void ResolveLayerMasks()
    {
        groundMask = GetMaskFromCsv(groundLayerName);
        eventMask = GetMaskFromCsv(eventLayerName);
        trapMask = GetMaskFromCsv(trapLayerName);
        slimeMask = GetMaskFromCsv(slimeLayerName);

        slimeLayerMask = slimeMask;
        trapLayerIndex = LayerMask.NameToLayer(trapLayerName.Trim());
        playerLayerIndexSelf = LayerMask.NameToLayer(playerLayerName.Trim());
        monsterLayerIndex = LayerMask.NameToLayer(monsterLayerName.Trim());

        if (groundMask == 0) Debug.LogWarning($"[Player] Ground layer(s) '{groundLayerName}' not found.");
        if (eventMask == 0) Debug.LogWarning($"[Player] Event layer(s) '{eventLayerName}' not found.");
        if (trapMask == 0) Debug.LogWarning($"[Player] Trap layer(s) '{trapLayerName}' not found.");
        if (slimeMask == 0) Debug.LogWarning($"[Player] Slime layer(s) '{slimeLayerName}' not found.");
        if (trapLayerIndex < 0) Debug.LogWarning($"[Player] Trap layer index for '{trapLayerName}' not found.");
        if (playerLayerIndexSelf < 0) Debug.LogWarning($"[Player] Player layer '{playerLayerName}' not found.");
        if (monsterLayerIndex < 0) Debug.LogWarning($"[Player] Monster layer '{monsterLayerName}' not found.");
    }

    //공격 중(P1) 입력락 여부
    private bool AttackLocksInput()
    {
        return attack && playerID == SwapController.PlayerChar.P1;
    }

    void Update()
    {
        bool isSelected = (swap != null && swap.charSelect == playerID);
        bool suppressed = Time.time < swapSuppressUntil;

        // ★ 변경: 공격 중(P1)에는 입력 자체를 잠그기
        bool attackLock = AttackLocksInput();
        bool locked = suppressed || Time.time < inputLockUntil || attackLock;
        lockedall = locked; // 전체 트랜지션 중에는 모든 입력/조작 봉인

        if (SpiralBoxWipe.IsBusy || IsDead)
        {
            rawX = 0f;
            jumpHeld = false;
            return;
        }

        if (prevSelected && !isSelected)
        {
            ResetAnimStates();
        }
        prevSelected = isSelected;

        if (!isSelected)
        {
            rawX = 0f;
            jumpHeld = false;
            return;
        }

        // 좌/우 입력 (월점프 이후 '벽쪽 키' 잠금 반영)
        bool blockL = IsDirBlocked(-1);
        bool blockR = IsDirBlocked(+1);

        float left = (!locked && !blockL && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))) ? -1f : 0f;
        float right = (!locked && !blockR && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))) ? +1f : 0f;

        lefthold = (!locked && !blockL && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)));
        righthold = (!locked && !blockR && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)));

        rawX = Mathf.Clamp(left + right, -1f, 1f);

        // 점프 입력 버퍼
        if (!locked && Input.GetKeyDown(KeyCode.Space)) lastJumpPressedTime = Time.time;
        jumpHeld = !locked && Input.GetKey(KeyCode.Space);

        // 캐리 토글 (P1만)
        if (playerID == SwapController.PlayerChar.P1)
        {
            bool shiftDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            bool canToggleCarry = !locked && Time.time >= nextCarryAllowedAt;
            if (shiftDown && canToggleCarry)
            {
                if (!isCarrying)
                {
                    if (CanStartCarryNow()) TryStartCarryNow();
                    else Debug.Log("[Carry] Start blocked (must be grounded or rule not met).");
                }
                else
                {
                    StopCarry();
                }
            }
        }
        // Update() 안, 입력 처리들 아래 아무 위치에 추가
        if (!locked)
        {
            bool downPressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);

            // 천장에 붙어있을 때 아래키 => 즉시 해제+급강하
            if (downPressed && stickingToCeiling)
            {
                EndCeilingStick(forceDive: true);
            }
            // 일반 공중에서도 아래키로 급강하 시작
            else if (downPressed && !IsGroundedStrictSmall())
            {
                isDiving = true;
            }
        }

        // --- 공격 처리 & F Pulse ---
        if (selectedObject)
        {
            // 공격 시작: 쿨타임 체크
            if (Input.GetKeyDown(KeyCode.F) && !isCarrying && Time.time >= nextAttackTime)
            {
                StartAttack();
            }
            // 공격 종료: 시간 만료
            if (attack && Time.time >= attackEndTime)
            {
                EndAttack();
            }
            // 이펙트/히트박스 펄스 종료
            if (selectedObject.activeSelf && Time.time >= fPulseOffAt)
            {
                selectedObject.SetActive(false);
            }
        }

        bool grounded = IsGroundedStrictSmall();
        if (grounded)
        {
            lastGroundedTime = Time.time;
            airJumpsLeft = extraAirJumps;
        }

        // 방향 뒤집기
        if (rawX != 0f)
        {
            dir = rawX > 0 ? 1f : -1f;
            transform.localScale = new Vector3(dirseto ? dir : dir * 1f, dirsetofl, dirsetofl);
        }

        // 천장 트랩
        CheckCeilingTrap();

        // 바닥 트랩 즉사
        var breakHit = IsBreak();
        if (breakHit.collider != null && breakHit.collider.CompareTag("Trap"))
        {
            return;
        }

        // (사이즈 프리셋)
        if (chasize)
        {
            dirseto = true;
            dir = Mathf.Sign(dir == 0 ? 1f : dir);
            dirsetofl = 1f;
            groundrayDistance = 1.3f;
            breakrayDistance = 1.4f;
            checkceilingtrap = 0.7f;
        }
        else
        {
            dirseto = false;
            dir = 1f;
            dirsetofl = 1f;
            groundrayDistance = 0.7f;
            breakrayDistance = 0.6f;
            checkceilingtrap = 0.35f;
        }

        // ==== 슬라임 접촉 상태 ====
        bool groundedForWall = IsGrounded();
        bool castL = !groundedForWall && TouchingSlimeSideCast(-1);
        bool castR = !groundedForWall && TouchingSlimeSideCast(+1);
        touchingLeftSlime = !groundedForWall && (castL || touchL_byCollision || touchL_byTrigger);
        touchingRightSlime = !groundedForWall && (castR || touchR_byCollision || touchR_byTrigger);

        if (touchingLeftSlime) { lastSlimeTouchAt = Time.time; lastSlimeSide = -1; }
        if (touchingRightSlime) { lastSlimeTouchAt = Time.time; lastSlimeSide = +1; }

        bool touchingGroundAny = IsGroundedStrictSmall() || IsGrounded();

        bool awayLeft = touchingRightSlime && (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow));
        bool awayRight = touchingLeftSlime && (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow));
        bool spaceOK = requireSpaceForWallJump ? Input.GetKey(KeyCode.Space) : true;

        if (!touchingGroundAny && spaceOK && (awayLeft || awayRight) && !(isCarrying || isCarried))
        {
            // 슬라임 재부착 억제(기존)
            ignoreSlimeUntil = Time.time + wallDetachGrace;

            // 월점프 속도 설정 (기존 코드 유지)
            Vector2 v2 = rb.linearVelocity;
            if (awayLeft) v2.x = -Mathf.Abs(wallJumpHorizontal);
            if (awayRight) v2.x = Mathf.Abs(wallJumpHorizontal);
            v2.y = wallJumpVertical;
            rb.linearVelocity = v2;

            // awayLeft  : 오른쪽 벽에 붙어 있다가 왼쪽으로 점프 → '오른쪽(벽쪽) 키' 잠금 => +1
            // awayRight : 왼쪽  벽에 붙어 있다가 오른쪽으로 점프 → '왼쪽(벽쪽) 키' 잠금 => -1
            int wallSideDir = awayLeft ? +1 : -1;
            oppositeInputLockUntil = Time.time + wallOppositeInputLock;
            oppositeInputLockedDir = wallSideDir;

            // (선택) 완전 확실히 끊고 싶으면 재부착 억제를 잠금시간과 동기화
            ignoreSlimeUntil = Mathf.Max(ignoreSlimeUntil, Time.time + wallOppositeInputLock);

        }

        // === Run 애니메이션 ===
        if (!lockedall && throwmanager &&
            (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
             Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)))
        {
            RunAni();
        }
        else
        {
            RunexitAni();
        }

        if (!locked && jumpHeld)
        {
            janit += Time.deltaTime;
            if (janit < 0.1f)
            {
                JumpAni();
            }
        }
        else
        {
            janit = 0;
        }
    }

    void FixedUpdate()
    {
        // === 사망했으면 물리 낙하만 처리 ===
        if (IsDead && keepFallingOnDeath && rb != null)
        {
            // 떨어질 때는 낙하용 중력 유지
            rb.gravityScale = gravityScaleFall;

            Vector2 dv = rb.linearVelocity;

            // 수평은 서서히 감쇠(자연스러운 정지)
            if (deadHorizontalDamp > 0f)
                dv.x = Mathf.MoveTowards(dv.x, 0f, deadHorizontalDamp * Time.fixedDeltaTime);

            // 최대 낙하 속도 하한 유지
            if (dv.y < maxFallSpeed) dv.y = maxFallSpeed;

            rb.linearVelocity = dv;
            return; // 나머지 이동/점프/슬라임 로직 전부 우회
        }

        bool groundedStrict = IsGroundedStrictSmall();
      

        Vector2 v = rb.linearVelocity;
        bool grounded = groundedStrict;

        // === Ceiling Slime: 감지 & 유지 ===
        RaycastHit2D upHit = default;
        bool canCeilingStick = enableCeilingSlime && !isCarrying && !isCarried && Time.time >= ignoreCeilingUntil;
        bool headTouchesSlime = false;

        // 머리 위 슬라임 감지(안전하게 out 초기화)
        if (canCeilingStick)
            headTouchesSlime = TouchingSlimeCeilingCast(out upHit);

        // 아직 안 붙어있고 머리로 닿았으면 붙기 시작
        if (!stickingToCeiling && headTouchesSlime)
        {
            StartCeilingStick(upHit);
        }

        // 붙어있는 동안 유지/해제 판정
        if (stickingToCeiling)
        {
            bool downHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool timeUp = (Time.time - ceilingStickStartTime) >= ceilingStickMaxTime;
            bool lostContact = !headTouchesSlime;

            // ① 아래키 즉시 급강하
            if (downHeld) { EndCeilingStick(forceDive: true); }

            // ② 시간 만료/접촉 끊김 → 서서히 해제
            else if (timeUp || lostContact) { BeginCeilingRelease(); }

            // ★ 붙어있는 프레임: Y축 고정(미끄러짐 제거), 좌우는 정상 이동
            if (stickingToCeiling)
            {
                rb.gravityScale = 0f; // 중력 0 → 마찰 발생 원인 제거
                v.y = 0f;

                // 머리-천장 간격 유지(살짝 스냅)
                float targetCeilY = (upHit.collider ? upHit.point.y : lastCeilingY);
                float targetTopY = targetCeilY - ceilingKeepGap;
                float top = bodyCollider.bounds.max.y;
                float deltaY = targetTopY - top;
                if (Mathf.Abs(deltaY) > 0.0005f)
                    rb.position = rb.position + new Vector2(0f, deltaY);
            }
        }

        // 해제 페이드 중이면 중력을 서서히 복원
        if (!stickingToCeiling && ceilingReleaseUntil > 0f)
        {
            if (Time.time < ceilingReleaseUntil)
            {
                float t = 1f - ((ceilingReleaseUntil - Time.time) / ceilingReleaseFade);
                rb.gravityScale = Mathf.Lerp(0f, baseGravityFall, t);
            }
            else
            {
                rb.gravityScale = baseGravityFall;
                ceilingReleaseUntil = -1f;
            }
        }


        bool minTimeNotPassed = Time.time < ballisticThrowEndTime;
        bool ballistic = ballisticThrowActive && (minTimeNotPassed || (carryThrowHoldUntilGrounded && !groundedStrict));
        if (ballisticThrowActive && !ballistic) ballisticThrowActive = false;

        // --- 목표 속도 & 가감속 ---
        if (AttackLocksInput())
        {
            // ★ 공격 중(P1)에는 가로속도를 빠르게 0으로 수렴 → 좌우이동 불가 보장
            v.x = Mathf.MoveTowards(v.x, 0f, decel * Time.fixedDeltaTime * 2f);
        }
        else if (ballistic)
        {
            if (Mathf.Abs(rawX) > 0.01f)
            {
                float targetX = rawX * moveSpeed;
                float a = airAccel;
                v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
            }
        }
        else
        {
            float targetX = rawX * moveSpeed;
            float a = grounded
                ? (Mathf.Sign(targetX) == Mathf.Sign(v.x) ? accel : decel)
                : (Mathf.Abs(targetX) > Mathf.Abs(v.x) ? airAccel : airDecel);
            v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
        }

        // --- 점프 처리 ---
        bool buffered = (Time.time - lastJumpPressedTime) <= jumpBuffer;
        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;

        // ★ 공격 중(P1)에는 점프 생성 금지
        if (!ballistic && !AttackLocksInput())
        {
            if (buffered && (canCoyote || airJumpsLeft > 0))
            {
                v.y = jumpVelocity;
                lastJumpStartTime = Time.time;
                didCutThisJump = false;

                if (!canCoyote && !grounded) airJumpsLeft = Mathf.Max(airJumpsLeft - 1, 0);

                lastJumpPressedTime = -999f;
                lastGroundedTime = -999f;
                ignoreGroundUntil = Time.time + postJumpGroundIgnore;
            }
        }

        // --- 내려찍기 / 컷점프(비활성) ---
        if (isDiving && !grounded)
        {
            rb.gravityScale = diveGravityScale;
            v.y = Mathf.Min(v.y, diveSpeed);
        }
        else if (stickingToCeiling)
        {
            // 천장에 붙어있는 동안엔 중력/낙하 정지
            rb.gravityScale = 0f;
            if (v.y < 0f) v.y = 0f;

            // 지정 시간이 지나면 해제 시작(부드럽게 떨어짐 구간 세팅)
            if (Time.time >= ceilingStickUntil)
            {
                stickingToCeiling = false;
                ceilingReleaseUntil = Time.time + ceilingReleaseBlendTime;
            }
        }
        else
        {
            float desiredGravity = (v.y < -0.01f) ? baseGravityFall : baseGravityNormal;
            if (isCarrying) desiredGravity *= (v.y < -0.01f) ? carryFallGravityMul : carryGravityMul;
            if (!groundedStrict && Mathf.Abs(v.y) <= apexThreshold)
                desiredGravity = Mathf.Min(desiredGravity, baseGravityNormal * apexHangMultiplier);
            rb.gravityScale = Mathf.SmoothDamp(rb.gravityScale, desiredGravity, ref gravitySmoothVel, gravitySmoothTime);
        }

        if (v.y < maxFallSpeed) v.y = maxFallSpeed;

        bool groundedAny = groundedStrict || IsGrounded();
        bool touchingSlimeNow = !groundedAny && (touchingLeftSlime || touchingRightSlime);

        SetFrictionless(touchingSlimeNow);

        // === SLIME STICK / SLIDE ==
        bool onSlimeRaw = !groundedAny && (touchingLeftSlime || touchingRightSlime);

        // (기존) 아주 작은 턱 오르기
        TryStepUpSmallLedge(rawX);

        // 벽을 계속 밀고 있으면 수평입력 억제
        bool allowStick = !IsSlimeSuppressed && !(isCarrying || isCarried);
        bool pressingIntoWall =
            allowStick && onSlimeRaw &&
            ((touchingLeftSlime && rawX < -0.01f) || (touchingRightSlime && rawX > 0.01f));
        if (pressingIntoWall)
        {
            rawX = 0f;
            if (v.y > 0f) v.y = 0f;
        }

        if (allowStick && onSlimeRaw)
        {
            Vector2 wallNormal =
                touchingLeftSlime ? Vector2.right :
                touchingRightSlime ? Vector2.left : Vector2.zero;

            if (wallNormal != Vector2.zero)
            {
                rb.AddForce(-wallNormal * slimeStickPush, ForceMode2D.Force);

                float vn = Vector2.Dot(v, wallNormal);
                if (vn > 0f)
                {
                    float cut = Mathf.Min(vn, slimeNormalClamp);
                    v -= wallNormal * cut;
                }

                if (v.y < wallSlideMaxFall) v.y = wallSlideMaxFall;
            }
        }
        else if (onSlimeRaw && (isCarrying || isCarried))
        {
            if (v.y < carrySlideMaxFall) v.y = carrySlideMaxFall;
        }


        // === 천장 해제 직후 잠깐 천천히 낙하(부드러운 이탈감) ===
        if (Time.time < ceilingReleaseUntil)
        {
            if (v.y < ceilingReleaseSlideMaxFall) v.y = ceilingReleaseSlideMaxFall;
        }

        FixVerticalSeam(rawX);

        rb.linearVelocity = v;

        bool groundedThisFrame = groundedStrict;
        if (!wasGrounded && groundedThisFrame)
        {
            JumpedAni();
            wallRegrabUntil = -1f;
            wallRegrabSide = 0;

            // ▼ 추가: P2가 착지한 그 프레임에 ground=false
            if (playerID == SwapController.PlayerChar.P2 && rb2)
                rb2.SetBool("ground", false);

            // P2가 착지하면 throwed 해제
            if (playerID == SwapController.PlayerChar.P2 && rb2)
            {
                rb2.SetBool("throwed", false);
            }

            if (isDiving)
            {
                var hit = IsBreak();
                if (hit.collider != null && hit.collider.CompareTag("Breakable"))
                    Destroy(hit.collider.gameObject);
            }
            isDiving = false;
            ballisticThrowActive = false;
        }

        wasGrounded = groundedThisFrame;
        touchL_byTrigger = touchR_byTrigger = false;

        // --- Midair Auto-Catch (P1 only) ---
        if (playerID == SwapController.PlayerChar.P1 &&
            autoCatchEnabled &&
            !isCarrying &&
            otherPlayer != null &&
            Time.time >= nextAutoCatchAllowedAt &&
            Time.time >= autoCatchSuppressUntil)   // ← 던진 직후 0.2s 차단
        {
            TryAutoCatchMidair();
        }

    }

    /* ===================== 충돌/트리거에서 슬라임 판정 보강 ===================== */
    void OnCollisionStay2D(Collision2D col)
    {
        if (playerID == SwapController.PlayerChar.P1 && otherPlayer != null)
        {
            var op = col.collider.GetComponentInParent<PlayerMouseMovement>();
            if (op != null && op == otherPlayer) isCarried = true;
        }

        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;

        for (int i = 0; i < col.contactCount; i++)
        {
            var n = col.GetContact(i).normal;
            if (n.x > 0.35f) touchL_byCollision = true;
            if (n.x < -0.35f) touchR_byCollision = true;
        }
    }

    void OnCollisionExit2D(Collision2D col)
    {
        // 캐리 대상 이탈 체크는 Exit에서 처리
        if (playerID == SwapController.PlayerChar.P1 && otherPlayer != null)
        {
            var op = col.collider.GetComponentInParent<PlayerMouseMovement>();
            if (op != null && op == otherPlayer) isCarried = false;
        }

        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;
        touchL_byCollision = false;
        touchR_byCollision = false;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        float ox = other.bounds.center.x;
        float px = transform.position.x;
        if (ox > px) touchR_byTrigger = true;
        else touchL_byTrigger = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        float ox = other.bounds.center.x;
        float px = transform.position.x;
        if (ox > px) touchR_byTrigger = false;
        else touchL_byTrigger = false;
    }

    private static bool IsInLayerMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

    /* ===================== 바운스/트랩 등 ===================== */
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Trap")) return;

        if (collision.collider.CompareTag("BounceLeftUp"))
        {
            Bounce(new Vector2(-bounceImpulseX, bounceImpulseY));
            return;
        }
        if (collision.collider.CompareTag("BounceRightUp"))
        {
            Bounce(new Vector2(+bounceImpulseX, bounceImpulseY));
            return;
        }
    }

    private void TryStartCarryNow()
    {
        if (Time.time < nextCarryAllowedAt) return;
        if (!CanStartCarryNow()) return; // 공중 시전 차단 핵심

        if (otherPlayer == null || isCarrying || bodyCollider == null || otherPlayer.bodyCollider == null) return;

        var d = Physics2D.Distance(bodyCollider, otherPlayer.bodyCollider);
        bool closeEnough = d.isOverlapped || d.distance <= carryPickupMaxGap;
        if (!closeEnough)
        {
            Debug.Log($"[Carry] too far: overlapped={d.isOverlapped}, dist={d.distance:F3}, need<={carryPickupMaxGap:F3}");
            return;
        }
        StartCarry();
    }

    // 캐리 시작 가능 여부 게이트
    private bool CanStartCarryNow()
    {
        if (isCarrying || isCarried) return false;
        if (requireGroundedForCarryStart && !IsGroundedStrictSmall()) return false;

        if (otherPlayer == null) return false;
        if (requireOtherGroundedForCarryStart && !otherPlayer.IsGroundedStrictSmall_Public()) return false;

        if (ballisticThrowActive) return false;
        return true;
    }

    private void TryResolveSwap()
    {
        if (swap != null) return;
        var go = GameObject.FindWithTag("Swap");
        if (go != null)
        {
            swap = go.GetComponent<SwapController>();
            if (swap == null) Debug.LogWarning("[Player] Tag 'Swap' 오브젝트에 SwapController 컴포넌트가 없습니다.", go);
        }
        else
        {
            Debug.LogWarning("[Player] 태그 'Swap' 오브젝트를 씬에서 찾지 못했습니다.");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryResolveSwap();
        ApplyLayerIgnores();
    }

    private void Bounce(Vector2 impulse)
    {
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(impulse, ForceMode2D.Impulse);

        isBouncing = true;
        bounceProtectUntil = Time.time + bounceProtectDuration;
        inputLockUntil = Time.time + inputLockAfterImpulse;
    }

    // P2 렌더러 토글(자식까지 전부)
    public void SetOtherPlayerVisible(bool visible)
    {
        if (!otherPlayer) return;
        var rends = otherPlayer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) rends[i].enabled = visible;
    }

    // P2를 delay 뒤에 다시 보이게 (정확히 0.6초: Real-time 기준)
    private IEnumerator RevealOtherAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        SetOtherPlayerVisible(true);
    }

    private IEnumerator ResetThrowAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (rb2) rb2.SetBool("carry", false);
        if (rb2) rb2.SetBool("carrying", false);
        if (rb2) rb2.SetBool("throw", false);
    }

    private void StartCarry()
    {
        if (otherPlayer == null || isCarrying) return;

        gravityScaleFall = 6.0f;
        extraAirJumps = 0;
        isCarrying = true;
        carryset = true;

        otherPlayer.rb.linearVelocity = Vector2.zero;
        otherPlayer.rb.simulated = false;
        otherPlayer.isCarried = true;
        if (rb2) rb2.SetBool("ground", true);
        otherOriginalParent = otherPlayer.transform.parent;

        otherPlayer.transform.SetParent(this.transform, true);
        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

        SetOtherPlayerVisible(false);

        // 애니 기반 입력잠금 시작
        BeginCarryStartLock();
        if (rb2) rb2.SetBool("carry", true);
        lockedall = true;

        if (_revealCo != null)
        {
            StopCoroutine(_revealCo);
            _revealCo = null;
            lockedall = false;
        }
        ForceViewToP1IfNeeded();
    }

    private void StopCarry()
    {
        if (otherPlayer == null || !isCarrying) return;

        // ====== 기본 상태/입력 해석 ======
        gravityScaleFall = 4.0f;
        moveSpeed = 7.0f;
        extraAirJumps = 1;

        int horizSign = 0;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizSign = +1;
        else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizSign = -1;
        bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

        int facingSign = (transform.localScale.x >= 0f) ? +1 : -1;
        int spawnSign = (horizSign != 0) ? horizSign : facingSign;
        int dirSign = spawnSign >= 0 ? +1 : -1;

        bool anyDir = (horizSign != 0) || upHeld; // false=DROP, true=THROW

        // ====== 스폰 좌표 미리 계산 ======
        Vector3 dropSpawnOffset = new(spawnSign * carryDropForward, carryOffsetY + carryThrowSeparation, 1f);
        Vector3 dropPos = transform.position + dropSpawnOffset;
        
        Vector3 throwPos;
        if (throwStartWorldPoint != null) throwPos = throwStartWorldPoint.position;
        else
        {
            float xoff = throwStartLocalOffset.x;
            if (throwStartUseFacing) xoff *= spawnSign;
            throwPos = transform.position + new Vector3(xoff, throwStartLocalOffset.y, 0f);
        }

        // ====== 드롭 전용 안전 검사 ======
        if (!anyDir) // DROP only
        {
            // 규칙 1: 접지 중에만 드롭 허용
            if (!IsGroundedStrictSmall())
            {
                Debug.Log("[Carry] Drop blocked: not grounded.");
                return;
            }
            // 규칙 2: 앞벽/낭떠러지/스폰영역 막힘 중 하나라도 있으면 드롭 금지
            bool wallAhead = HasWallAhead(dirSign);
            bool cliffAhead = !HasGroundBelow(new Vector2(dropPos.x, transform.position.y));
            bool areaBlocked = IsAreaBlocked((Vector2)dropPos);
            if (wallAhead || cliffAhead || areaBlocked)
            {
                Debug.Log($"[Carry] Drop blocked: wallAhead={wallAhead}, cliffAhead={cliffAhead}, areaBlocked={areaBlocked}");
                return;
            }
        }

        // 던지기(THROW)는 위 조건과 무관하게 항상 진행
        // ====== 여기부터 실제 캐리 해제 공통 처리 ======
        otherPlayer.transform.SetParent(otherOriginalParent, worldPositionStays: true);

        if (!anyDir)
        {
            // ---- DROP: 0.6초 뒤 보이기 ----
            otherPlayer.transform.position = dropPos;
            if (rb2) rb2.SetBool("ground", false);
            otherPlayer.rb.simulated = true;
            otherPlayer.isCarried = false;
            otherPlayer.rb.linearVelocity = Vector2.zero;
            otherPlayer.ballisticThrowActive = false;

            int faceToP1 = (transform.position.x > otherPlayer.transform.position.x) ? +1 : -1;
            otherPlayer.ForceFaceSign(faceToP1);

            isCarrying = false;
            carryset = false;

            // 애니 상태 정리
            if (rb2)
            {
                AnimatorSetBoolSafe(rb2, carryBoolName, false);      // 수동 캐리일 수도 있으니 OFF
                AnimatorSetBoolSafe(rb2, carryingBoolName, false);   // ★ 오토캐치 OFF
                if (!string.IsNullOrEmpty(carryEndTriggerName))      // 트리거가 있으면 사용
                    AnimatorSetTriggerSafe(rb2, carryEndTriggerName);
                else if (!string.IsNullOrEmpty(carryEndStateName))   // 없으면 상태로 강제 전이
                    rb2.CrossFadeInFixedTime(carryEndStateName, 0.05f, 0, 0f);
            }
            BeginCarryEndLock();

            // P2는 0.6s 뒤 보이기(기존 로직 유지)
            if (_revealCo != null) StopCoroutine(_revealCo);
            _revealCo = StartCoroutine(RevealOtherAfter(revealDelayOnDrop));

            Debug.Log("[Carry] DROP");
            return;
        }

        // ---- THROW: 지연 후 보이면서 비행 시작 ----
        otherPlayer.transform.position = throwPos;
        otherPlayer.rb.simulated = false; // 던지기 시작 순간까지 비활성
        otherPlayer.isCarried = false;
        SetOtherPlayerVisible(false);

        Vector2 initialVelocity = upHeld
            ? new Vector2(0f, carryThrowUpSpeed)
            : new Vector2(spawnSign * carryThrowSideSpeed, carryThrowUpSpeed);

        autoCatchSuppressUntil = Time.time + autoCatchBlockOnThrow;

        isCarrying = false;
        carryset = false;
        // 애니 상태 정리(던질 땐 즉시 해제 연출)
        if (rb2)
        {
            AnimatorSetBoolSafe(rb2, carryBoolName, false);
            AnimatorSetBoolSafe(rb2, carryingBoolName, false);    // ★ 오토캐치 OFF
            if (!string.IsNullOrEmpty(carryEndTriggerName))
                AnimatorSetTriggerSafe(rb2, carryEndTriggerName);
            else if (!string.IsNullOrEmpty(carryEndStateName))
                rb2.CrossFadeInFixedTime(carryEndStateName, 0.05f, 0, 0f);
        }

        // 지면/공중에 따라 throw / jumpthrow 선택
        bool groundedNow = IsGroundedStrictSmall();
        if (rb2)
        {
            rb2.SetBool("throw", false);
            rb2.SetBool("run", false);
            rb2.SetBool("throw", true);
            rb2.SetBool("throwed", true);
        }

        throwmanager = false;

        BeginCarryEndLock();

        if (playerID == SwapController.PlayerChar.P1)
        {
            CancelCarryLock();
            inputLockUntil = Time.time + 0.45f;

            if (_throwResetCo != null) StopCoroutine(_throwResetCo);
            _throwResetCo = StartCoroutine(ResetThrowAfter(0.6f));
        }

        otherPlayer.ForceFaceSign(spawnSign);

        if (_delayedThrowCo != null) StopCoroutine(_delayedThrowCo);
        _delayedThrowCo = StartCoroutine(DelayedThrow(throwDelay, initialVelocity));

        Debug.Log($"[Carry] THROW scheduled after {throwDelay:F2}s: startPos={throwPos}, vel={initialVelocity}, grounded={groundedNow}");
    }

    private IEnumerator DelayedThrow(float delay, Vector2 initialVelocity)
    {
        // 정확한 실시간 지연(타임스케일 영향 없음)
        yield return new WaitForSecondsRealtime(delay);

        // 보이게
        SetOtherPlayerVisible(true);

        // 물리 켜고 실제 비행 시작
        if (otherPlayer != null && otherPlayer.rb != null)
        {
            otherPlayer.rb.simulated = true;

            // 던지기 시작 애니 플래그 (P2)
            if (otherPlayer.rb2) otherPlayer.rb2.SetBool("throwe", true);
            if (otherPlayer.rb2) otherPlayer.rb2.SetBool("throwed", true);

            // 탄도 유지 플래그/타이밍 세팅
            otherPlayer.ballisticThrowActive = true;
            otherPlayer.ballisticThrowEndTime = Time.time + otherPlayer.carryThrowBallisticMinTime;
            otherPlayer.didCutThisJump = true;
            otherPlayer.lastJumpStartTime = Time.time;
            otherPlayer.ignoreGroundUntil = Time.time + otherPlayer.postJumpGroundIgnore;

            // 초기 속도 적용
            otherPlayer.rb.linearVelocity = initialVelocity;
            throwmanager = true;
        }

        _delayedThrowCo = null;
    }

    private void ApplyLayerIgnores()
    {
        if (playerLayerIndexSelf >= 0)
        {
            // 자기 자신끼리 충돌 무시
            Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, playerLayerIndexSelf, true);

            // Player vs Monster 충돌 무시
            if (monsterLayerIndex >= 0)
                Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, monsterLayerIndex, true);
        }
    }

    public void TakeDamage(int dmg = 1)
    {
        if (IsDead || _sceneReloading) return;
        int amount = Mathf.Max(1, dmg);
        currentHP = Mathf.Max(0, currentHP - amount);
        Debug.Log($"플레이어 HP: {currentHP}");
        if (currentHP <= 0) Die();
    }

    public void SuppressInputFor(float seconds, bool zeroHorizontalVelocity = true)
    {
        swapSuppressUntil = Time.time + Mathf.Max(0f, seconds);
        rawX = 0f;
        jumpHeld = false;
        lastJumpPressedTime = -999f;
        ResetAnimStates();
        if (zeroHorizontalVelocity && rb) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void ResetAnimStates()
    {
        if (!rb2) return;
        rb2.SetBool("run", false);
        rb2.SetBool("jump", false);
        rb2.SetBool("jumped", false);
        rb2.SetBool("attack", false);
        attack = false;
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // P2는 무조건 씬 리로드
        if (playerID == SwapController.PlayerChar.P2)
        {
            if (_sceneReloading) return;
            _sceneReloading = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        // ▼ 사망 후에도 낙하하도록 물리 상태 보장
        if (rb)
        {
            rb.simulated = true; // 혹시 꺼졌을 경우 대비
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = gravityScaleFall; // 하강 중 중력
        }
    }

    private void RunAni()
    {
        if (rb2) rb2.SetBool("run", true);
        lefthold = false;
        righthold = false;
    }

    private void RunexitAni()
    {
        if (rb2) rb2.SetBool("run", false);
    }

    private void JumpAni()
    {
        if (rb2) rb2.SetBool("jump", true);
    }

    private void JumpedAni()
    {
        if (rb2)
        {
            rb2.SetBool("jump", false);
            rb2.SetBool("jumped", true);
        }
    }

    // === 공격 시작/종료 ===
    private void StartAttack()
    {
        attack = true;
        if (rb2) rb2.SetBool("attack", true);

        // ★ 추가: 공격 시작 시 이동/점프 버퍼 초기화(버퍼 점프 방지)
        rawX = 0f;
        jumpHeld = false;
        lastJumpPressedTime = -999f;

        if (_attackPulseCo != null) StopCoroutine(_attackPulseCo);
        _attackPulseCo = StartCoroutine(ActivateSelectedAfterDelay(attackHitDelay));

        attackEndTime = Time.time + attackDuration; // 공격 애니 자동 종료
        nextAttackTime = Time.time + attackCooldown; // 쿨타임 시작
    }

    private void EndAttack()
    {
        attack = false;
        if (rb2) rb2.SetBool("attack", false);

        // 지연 코루틴 취소 및 안전 비활성화
        if (_attackPulseCo != null)
        {
            StopCoroutine(_attackPulseCo);
            _attackPulseCo = null;
        }
        if (selectedObject) selectedObject.SetActive(false); // 펄스 타이머와 무관하게 끔
        // selectedObject는 fPulseDuration 타이머로 별도 종료됨
    }

    private IEnumerator ActivateSelectedAfterDelay(float delay)
    {
        // 혹시 켜져있다면 먼저 끄기
        if (selectedObject) selectedObject.SetActive(false);

        float until = Time.time + Mathf.Max(0f, delay);
        // 공격이 유지되는 동안만 대기
        while (Time.time < until)
        {
            if (!attack) yield break; // 공격이 중간에 끝나면 취소
            yield return null;
        }

        if (!attack) yield break;

        if (selectedObject)
        {
            selectedObject.SetActive(true);
            fPulseOffAt = Time.time + fPulseDuration; // 기존 펄스 타이머 그대로 사용
        }

        _attackPulseCo = null;
    }

    // === 애니 기반 잠금 유틸 ===
    private void CancelCarryLock()
    {
        if (_carryLockCo != null)
        {
            StopCoroutine(_carryLockCo);
            _carryLockCo = null;
        }
    }

    private float GetCurrentClipLengthSec(int layer = 0)
    {
        if (!rb2) return 0f;
        var clips = rb2.GetCurrentAnimatorClipInfo(layer);
        if (clips != null && clips.Length > 0 && clips[0].clip)
        {
            float speed = Mathf.Max(0.0001f, rb2.speed);
            return clips[0].clip.length / speed;
        }
        return 0f;
    }

    private void TryStepUpSmallLedge(float dirInput)
    {
        if (!enableStepUp) return;
        if (!bodyCollider) return;
        if (Mathf.Abs(dirInput) < 0.01f) return;

        // 위로 상승 중일 땐 스킵(계단 탈 때만)
        if (rb && rb.linearVelocity.y > stepOnlyWhenFallingVy) return;

        Bounds b = bodyCollider.bounds;
        int sign = dirInput > 0f ? +1 : -1;

        // 발 위치 기준, 발 앞쪽 위에서 아래로 레이캐스트
        float feetY = b.min.y + 0.01f;
        Vector2 rayOrigin = new Vector2(
            b.center.x + sign * (b.extents.x + stepForward),
            feetY + stepUpMax
        );
        float rayLen = stepUpMax + 0.06f;
        int groundOrEvent = groundMask | eventMask; // 원웨이에도 오르고 싶지 않으면 eventMask 빼기

        RaycastHit2D down = Physics2D.Raycast(rayOrigin, Vector2.down, rayLen, groundOrEvent);
#if UNITY_EDITOR
        Debug.DrawRay(rayOrigin, Vector2.down * rayLen, down ? Color.yellow : Color.gray, 0f);
#endif
        if (!down) return;

        float climb = down.point.y - feetY;
        if (climb <= 0f || climb > stepUpMax) return;

        // 머리/몸통 간섭 체크: 현재 위치에서 위로 'climb' 만큼 이동이 가능한지
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = groundOrEvent | trapMask   // 위가 막혀있으면 안 올라감
        };
        RaycastHit2D[] buf = new RaycastHit2D[2];
        int hitCount = bodyCollider.Cast(Vector2.up, filter, buf, climb + stepUpSkin);
        if (hitCount > 0) return;

        // 안전 — 살짝 들어올리기
        if (rb)
            rb.position = rb.position + new Vector2(0f, climb + stepUpSkin);
        else
            transform.position += new Vector3(0f, climb + stepUpSkin, 0f);
    }


    private void FixVerticalSeam(float dirInput)
    {
        if (!bodyCollider) return;
        if (!IsGroundedStrictSmall()) return;
        if (Mathf.Abs(dirInput) < 0.01f) return;

        Bounds b = bodyCollider.bounds;
        int sign = dirInput > 0 ? +1 : -1;

        // 발 앞, 아주 얇은 세로면이 있는지 검사
        Vector2 seamCenter = new Vector2(
            b.center.x + sign * (b.extents.x + seamFixProbe * 0.5f),
            b.min.y + 0.02f
        );
        Vector2 seamSize = new Vector2(seamFixProbe, 0.04f);

        bool hasVerticalFace = Physics2D.OverlapBox(seamCenter, seamSize, 0f, groundMask);
        if (!hasVerticalFace) return;

        // 현재 발 아래가 실제 바닥이면(공중 아님) '이음매'로 간주, 살짝 들어올림
        Vector2 feetCenter = new Vector2(b.center.x, b.min.y - 0.01f);
        Vector2 feetSize = new Vector2(b.size.x * 0.9f, 0.02f);
        bool grounded = Physics2D.OverlapBox(feetCenter, feetSize, 0f, groundMask);
        if (grounded)
        {
            rb.position = rb.position + new Vector2(0f, seamFixLift);
        }
    }
    private bool IsDirBlocked(int dir)
    {
        return Time.time < oppositeInputLockUntil && oppositeInputLockedDir == dir;
    }

    private IEnumerator LockForAnimation(float minLockSeconds, bool zeroHorizontalVelocity)
    {
        float t0 = Time.time;
        inputLockUntil = Mathf.Max(inputLockUntil, t0 + 0.0001f);

        if (zeroHorizontalVelocity && rb) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        yield return null; // 상태 전이 후 길이 측정

        float clipLen = useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f;
        float lockFor = Mathf.Max(minLockSeconds, clipLen);
        inputLockUntil = Time.time + lockFor;

        while (Time.time < inputLockUntil) yield return null;
        _carryLockCo = null;
    }

    private void BeginCarryStartLock()
    {
        CancelCarryLock();
        _carryLockCo = StartCoroutine(LockForAnimation(carryStartMinLock, true));
        float planned = Time.time + Mathf.Max(carryStartMinLock, useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f);
        nextCarryAllowedAt = Mathf.Max(nextCarryAllowedAt, planned);
    }

    private void BeginCarryEndLock()
    {
        CancelCarryLock();
        _carryLockCo = StartCoroutine(LockForAnimation(carryEndMinLock, true));
        float planned = Time.time + Mathf.Max(carryEndMinLock, useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f) + carryCooldown;
        nextCarryAllowedAt = Mathf.Max(nextCarryAllowedAt, planned);
    }

    // 애니메이션 이벤트 훅(선택 사용)
    public void AE_CarryStart_Begin() { }
    public void AE_CarryStart_End() { inputLockUntil = Time.time; }
    public void AE_CarryEnd_Begin() { }
    public void AE_CarryEnd_End() { inputLockUntil = Time.time; }

    // === 탄도 유지 판단용: Ground만 짧게 본다(OneWay/Event는 무시) ===
    private bool IsGroundedStrictSmall()
    {
        if (Time.time < ignoreGroundUntil) return false;
        if (!bodyCollider) return false;

        Bounds b = bodyCollider.bounds;
        float skin = Mathf.Max(0.005f, groundCheckSkin);
        Vector2 boxCenter = new(b.center.x, b.min.y + skin * 0.5f);
        Vector2 boxSize = new(Mathf.Max(0.02f, b.size.x * 0.9f), skin);

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, groundMask);

#if UNITY_EDITOR
        Color c = hit ? Color.green : Color.red;
        Debug.DrawLine(new Vector2(boxCenter.x - boxSize.x * 0.5f, boxCenter.y),
                       new Vector2(boxCenter.x + boxSize.x * 0.5f, boxCenter.y), c, 0f, false);
#endif
        return hit != null;
    }

    // 외부에서 쓸 수 있도록 래퍼
    public bool IsGroundedStrictSmall_Public() => IsGroundedStrictSmall();
    // P1 머리 위 ‘캐치 영역’과 P2의 바디가 겹치면 즉시 캐리로 스냅

    private bool CanEnterAutoCatchGate_Body(Bounds p1Body, Bounds p2Body)
    {
        if (Time.time < autoCatchSuppressUntil) return false;
        if (Time.time < nextAutoCatchAllowedAt) return false;
        if (otherPlayer == null || otherPlayer.rb == null) return false;

        // 던지기 딜레이 중이면 금지
        if (!otherPlayer.rb.simulated) return false;

        // P1이 바쁜 상태(공격/입력락/컷씬 등)면 금지
        if (autoCatchDisallowIfP1Busy && (AttackLocksInput() || lockedall || SpiralBoxWipe.IsBusy)) return false;

        // P2가 숨김 상태면 금지
        if (autoCatchDisallowIfP2Hidden && !AnyRendererVisible(otherPlayer.gameObject)) return false;

        // 던진 뒤 창만 허용 옵션
        if (onlyCatchDuringThrowWindow && !(otherPlayer.ballisticThrowActive && !otherPlayer.IsGroundedStrictSmall_Public()))
            return false;

        // 내려오는 중만 허용 옵션
        if (autoCatchRequireP2Descending && otherPlayer.rb.linearVelocity.y > -0.01f) return false;

        // 사이에 지형/트랩 끼임 금지(옵션)
        if (autoCatchDisallowIfBlockingCeiling)
        {
            Vector2 from = p2Body.center;
            Vector2 to = p1Body.center;
            Vector2 dir = to - from;
            float dist = dir.magnitude;
            if (dist > 0.001f)
            {
                dir /= dist;
                float width = Mathf.Max(0.10f, Mathf.Min(p1Body.size.x, p2Body.size.x) * 0.5f);
                var hit = Physics2D.BoxCast(from, new Vector2(width, 0.08f), 0f, dir, dist, autoCatchObstructionMask);
#if UNITY_EDITOR
                Debug.DrawLine(from, to, hit ? Color.red : Color.green, 0f);
#endif
                if (hit.collider) return false;
            }
        }

        return true;
    }



    private void TryAutoCatchMidair()
    {
        if (otherPlayer == null || bodyCollider == null || otherPlayer.bodyCollider == null) return;

        // 던지기 딜레이 중(물리 off)면 금지
        if (otherPlayer.rb != null && !otherPlayer.rb.simulated) return;

        // THROW 전용 옵션이면 창 유효성 확인
        bool throwWindow = otherPlayer.ballisticThrowActive && !otherPlayer.IsGroundedStrictSmall_Public();
        if (onlyCatchDuringThrowWindow && !throwWindow) return;

        // === 몸 전체 박스(여유 포함)로 판정 ===
        Bounds b1 = bodyCollider.bounds;               // P1 몸
        Bounds b2 = otherPlayer.bodyCollider.bounds;   // P2 몸

        // P1의 바디 박스를 약간 확장해서 캐치 윈도우 완화
        Bounds catchBox = new Bounds(b1.center, b1.size + new Vector3(bodyCatchPadding.x, bodyCatchPadding.y, 0f));
        bool intersects = catchBox.Intersects(b2);
        if (!intersects) return;

        // 게이트(최소한의 안전 조건만) 통과 확인
        if (!CanEnterAutoCatchGate_Body(b1, b2)) return;

        // 스냅 캐치 & 시점 P1 강제
        SnapCatchOtherMidair();
        ForceViewToP1IfNeeded();

        nextAutoCatchAllowedAt = Time.time + autoCatchCooldown;
    }


    private void ForceViewToP1IfNeeded()
    {
        if (!autoForceViewToP1OnCatch) return;

        bool switched = false;
        if (swap != null)
        {
            // 시점이 P2면 P1로 전환
            if (swap.charSelect != SwapController.PlayerChar.P1)
            {
                try { swap.charSelect = SwapController.PlayerChar.P1; switched = true; }
                catch { /* 읽기전용이면 UnityEvent로 처리 */ }
            }
        }

        if (!switched)
        {
            // 카메라 컨트롤러에 직접 바인딩된 훅 호출(선택)
            onForceViewToP1?.Invoke();
        }
    }
    // 애니 없이 곧장 '캐리 상태' 세팅 (StartCarry와 유사하지만 락/모션 없음)
    private void SnapCatchOtherMidair()
    {
        if (otherPlayer == null) return;

        otherPlayer.rb.linearVelocity = Vector2.zero;
        otherPlayer.rb.simulated = false;
        otherPlayer.isCarried = true;
        otherPlayer.ballisticThrowActive = false;
        if (rb2) rb2.SetBool("ground", true);

        if (otherPlayer.rb2)
        {
            AnimatorSetBoolSafe(otherPlayer.rb2, "throwe", false);
            AnimatorSetBoolSafe(otherPlayer.rb2, "throwed", false);
        }

        SetOtherPlayerVisible(false);

        otherOriginalParent = otherPlayer.transform.parent;
        otherPlayer.transform.SetParent(this.transform, true);
        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

        isCarrying = true;
        carryset = true;

        // ★ 오토캐치: 'carrying'만 On
        if (rb2)
        {
            AnimatorSetBoolSafe(rb2, carryingBoolName, true);
            // 수동 캐리용 'carry'는 건드리지 않음
        }
    }


    private void SetFrictionless(bool on)
    {
        if (!bodyCollider) return;

        if (on)
        {
            if (!_appliedNoFriction)
            {
                if (!slimeNoFrictionMat)
                {
                    slimeNoFrictionMat = new PhysicsMaterial2D("Runtime_NoFric");
                    slimeNoFrictionMat.friction = 0f;
                    slimeNoFrictionMat.bounciness = 0f;
                }
                bodyCollider.sharedMaterial = slimeNoFrictionMat;
                _appliedNoFriction = true;
            }
        }
        else
        {
            if (_appliedNoFriction)
            {
                bodyCollider.sharedMaterial = _originalMat;
                _appliedNoFriction = false;
            }
        }
    }

    // 머리 위 슬라임 감지
    private bool TouchingSlimeCeilingCast(out RaycastHit2D upHit)
    {
        upHit = default;
        if (!bodyCollider) return false;

        // 1) Collider.Cast로 머리 위 3~4cm 체크
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = slimeLayerMask, useTriggers = true };
        RaycastHit2D[] hits = new RaycastHit2D[2];
        int cnt = bodyCollider.Cast(Vector2.up, filter, hits, 0.04f);
        if (cnt > 0) { upHit = hits[0]; return true; }

        // 2) 보강: 아주 얇은 박스로 한 번 더
        Bounds b = bodyCollider.bounds;
        Vector2 size = new Vector2(b.size.x * 0.9f, 0.06f);
        Vector2 center = new Vector2(b.center.x, b.max.y + size.y * 0.5f);
        var col = Physics2D.OverlapBox(center, size, 0f, slimeMask);

#if UNITY_EDITOR
        Color c = col ? Color.green : Color.red;
        Debug.DrawLine(new Vector2(center.x - size.x * 0.5f, center.y),
                       new Vector2(center.x + size.x * 0.5f, center.y), c, 0f, false);
#endif

        if (col)
        {
            // 점만 필요하니 간단 레이로 보정
            upHit = Physics2D.Raycast(new Vector2(b.center.x, b.max.y), Vector2.up, 0.08f, slimeMask);
            return true;
        }
        return false;
    }

    private void StartCeilingStick(RaycastHit2D upHit)
    {
        stickingToCeiling = true;
        ceilingStickStartTime = Time.time;
        ceilingReleaseUntil = -1f;

        lastCeilingY = upHit.collider ? upHit.point.y : bodyCollider.bounds.max.y + 0.02f;

        // 중력 끄고 Y속도 0으로(슬라이드 방지)
        rb.gravityScale = 0f;
        var v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        // 간격 맞춰 스냅
        float targetTop = lastCeilingY - ceilingKeepGap;
        float top = bodyCollider.bounds.max.y;
        float dy = targetTop - top;
        if (Mathf.Abs(dy) > 0.0005f)
            rb.position = rb.position + new Vector2(0f, dy);
    }

    private void BeginCeilingRelease()
    {
        if (!stickingToCeiling) return;
        stickingToCeiling = false;

        // 0 → baseGravityFall로 부드럽게 복원
        ceilingReleaseUntil = Time.time + ceilingReleaseFade;

        // 같은 프레임 재부착 방지 & 살짝 아래로 밀어줌
        ignoreCeilingUntil = Time.time + ceilingRestickBlock;
        var v = rb.linearVelocity;
        v.y = Mathf.Min(v.y, -0.1f);
        rb.linearVelocity = v;
    }

    private void EndCeilingStick(bool forceDive)
    {
        stickingToCeiling = false;
        ceilingReleaseUntil = -1f;
        ignoreCeilingUntil = Time.time + ceilingRestickBlock;

        if (forceDive)
        {
            // 즉시 급강하
            rb.gravityScale = diveGravityScale;
            var v = rb.linearVelocity;
            v.y = Mathf.Min(v.y, diveSpeed);
            rb.linearVelocity = v;
            isDiving = true;
        }
        else
        {
            // 즉시 해제(자연 낙하)
            rb.gravityScale = baseGravityFall;
            var v = rb.linearVelocity;
            v.y = Mathf.Min(v.y, -0.1f);
            rb.linearVelocity = v;
        }
    }


    private static bool AnimatorHasParam(Animator a, string name, AnimatorControllerParameterType t)
    {
        if (!a || string.IsNullOrEmpty(name)) return false;
        foreach (var p in a.parameters) if (p.name == name && p.type == t) return true;
        return false;
    }
    private static void AnimatorSetBoolSafe(Animator a, string name, bool v)
    {
        if (AnimatorHasParam(a, name, AnimatorControllerParameterType.Bool)) a.SetBool(name, v);
    }
    private static void AnimatorSetTriggerSafe(Animator a, string name)
    {
        if (AnimatorHasParam(a, name, AnimatorControllerParameterType.Trigger)) a.SetTrigger(name);
    }
    private static bool AnyRendererVisible(GameObject go)
    {
        if (!go) return false;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) if (rends[i].enabled) return true;
        return false;
    }

    // 머리 위 슬라임 감지 (Ceiling)
    

    
    // === 슬라임 접촉 감지: Collider.Cast 기반 ===
    private bool TouchingSlimeSideCast(int sign)
    {
        if (!bodyCollider) return false;

        Vector2 dir = (sign < 0) ? Vector2.left : Vector2.right;

        ContactFilter2D filter = new();
        filter.useLayerMask = true;
        filter.SetLayerMask(slimeLayerMask);
        filter.useTriggers = true;

        RaycastHit2D[] hits = new RaycastHit2D[2];
        int count = bodyCollider.Cast(dir, filter, hits, 0.03f);
        if (count > 0) return true;

        Bounds b = bodyCollider.bounds;
        float padX = 0.04f;
        Vector2 size = new(0.12f, b.size.y * 0.8f);
        Vector2 center = (Vector2)b.center + new Vector2(sign * (b.extents.x + size.x * 0.5f + padX), 0f);

        bool boxHit = Physics2D.OverlapBox(center, size, 0f, slimeMask);

#if UNITY_EDITOR
        Color c = (count > 0 || boxHit) ? Color.green : Color.red;
        Debug.DrawLine(center + Vector2.up * size.y * 0.5f,
                       center - Vector2.up * size.y * 0.5f, c, 0f, false);
#endif
        return boxHit;
    }

    // 앞에 벽(지형)이 있는지 — Ground만 벽으로 판단(OneWay/Event는 통과 가능이라 보통 벽 아님)
    private bool HasWallAhead(int dirSign)
    {
        if (!bodyCollider) return false;
        var b = bodyCollider.bounds;

        Vector2 origin = new(b.center.x + dirSign * (b.extents.x + 0.02f), b.center.y);
        Vector2 size = new(Mathf.Max(0.12f, b.size.x * 0.6f), Mathf.Max(0.20f, b.size.y * 0.6f));
        float dist = Mathf.Max(0.05f, releaseAheadCheckDist);

        // Ground 레이어만 벽으로 판정
        int wallMask = groundMask;
        var hit = Physics2D.BoxCast(origin, size, 0f, new Vector2(dirSign, 0f), dist, wallMask);

#if UNITY_EDITOR
        Debug.DrawRay(origin, Vector2.right * dirSign * dist, hit.collider ? Color.yellow : Color.gray, 0f);
#endif
        return hit.collider != null;
    }

    // 해당 위치 아래에 '설 수 있는' 지면이 있는지 — Ground 또는 Event(원웨이 포함) 허용
    private bool HasGroundBelow(Vector2 worldPos)
    {
        int groundOrEvent = groundMask | eventMask;
        Vector2 start = new(worldPos.x, worldPos.y + 0.1f); // 살짝 위에서 아래로
        var hit = Physics2D.Raycast(start, Vector2.down, Mathf.Max(0.05f, releaseGroundProbeDown), groundOrEvent);

#if UNITY_EDITOR
        Debug.DrawRay(start, Vector2.down * releaseGroundProbeDown, hit.collider ? Color.green : Color.red, 0f);
#endif
        return hit.collider != null;
    }

    // 해당 스폰 지점이 지형으로 막혀 있는지(겹침) — Ground/Event 모두 막힘으로 판단
    private bool IsAreaBlocked(Vector2 center)
    {
        int blockMask = groundMask | eventMask;
        var hit = Physics2D.OverlapBox(center, releaseSpawnBoxSize, 0f, blockMask);

#if UNITY_EDITOR
        Color c = hit ? Color.red : Color.green;
        Debug.DrawLine(center + Vector2.left * 0.2f, center + Vector2.right * 0.2f, c, 0f);
#endif
        return hit != null;
    }

    /* ===================== 레이 감지 유지 ===================== */
    public bool IsGrounded()
    {
        float rayDistance = groundrayDistance;

        Vector2 center = transform.position + Vector3.down * 0.2f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        bool centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, groundMask);
        bool leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, groundMask);
        bool rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, groundMask);

        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit ? Color.green : Color.red);
        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit ? Color.green : Color.red);
        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit ? Color.green : Color.red);

        return centerHit || leftHit || rightHit;
    }

    public RaycastHit2D IsBreak()
    {
        float rayDistance = breakrayDistance;

        Vector2 center = transform.position + Vector3.down * 0.2f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, eventMask);
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, eventMask);
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, eventMask);

        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit.collider ? Color.cyan : Color.gray);
        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit.collider ? Color.cyan : Color.gray);
        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit.collider ? Color.cyan : Color.gray);

        if (centerHit.collider != null) return centerHit;
        if (leftHit.collider != null) return leftHit;
        if (rightHit.collider != null) return rightHit;
        return new RaycastHit2D();
    }

    private void CheckCeilingTrap()
    {
        float rayDistance = checkceilingtrap;

        Vector2 center = transform.position + Vector3.up * 0.5f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.up, rayDistance, trapMask);
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.up, rayDistance, trapMask);
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.up, rayDistance, trapMask);

        Debug.DrawRay(center, Vector2.up * rayDistance, centerHit.collider ? Color.magenta : Color.gray);
        Debug.DrawRay(left, Vector2.up * rayDistance, leftHit.collider ? Color.magenta : Color.gray);
        Debug.DrawRay(right, Vector2.up * rayDistance, rightHit.collider ? Color.magenta : Color.gray);

        bool hitTrap =
            (centerHit.collider && ((trapLayerIndex >= 0 && centerHit.collider.gameObject.layer == trapLayerIndex) || centerHit.collider.CompareTag("Trap"))) ||
            (leftHit.collider && ((trapLayerIndex >= 0 && leftHit.collider.gameObject.layer == trapLayerIndex) || leftHit.collider.CompareTag("Trap"))) ||
            (rightHit.collider && ((trapLayerIndex >= 0 && rightHit.collider.gameObject.layer == trapLayerIndex) || rightHit.collider.CompareTag("Trap")));
        _ = hitTrap; // 현재는 시각 디버그만
    }

    // === 유틸: 스프라이트 좌우 강제 (항상 '자기 자신'만 수정) ===
    public void ForceFaceSign(int sign)
    {
        sign = sign >= 0 ? 1 : -1;
        var ls = transform.localScale;
        transform.localScale = new Vector3(Mathf.Abs(ls.x) * sign, ls.y, ls.z);
        dir = sign;
    }
}
