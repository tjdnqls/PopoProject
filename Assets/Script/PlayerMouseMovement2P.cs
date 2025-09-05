//using UnityEngine;
//using UnityEngine.Rendering;
//using UnityEngine.SceneManagement;

//public class PlayerMouseMovement2 : MonoBehaviour
//{
//    // === 필수 컴포넌트 / 레이어 ===
//    public Rigidbody2D rb;
//    public Animator rb2;
//    [Header("Layers")]
//    // ▼▼ LayerMask 대신 "이름"만 설정 (기본값 그대로 쓰면 인스펙터 세팅 불필요)
//    [Header("Layer Names (auto-resolve)")]
//    [SerializeField] private string groundLayerName = "Ground";
//    [SerializeField] private string eventLayerName = "EventGround, OneWayGround";
//    [SerializeField] private string trapLayerName = "Trap";
//    [SerializeField] private string slimeLayerName = "Slime";
//    [SerializeField] private string playerLayerName = "Player";

//    // 내부 캐시 (코드에서만 사용)
//    private int groundMask, eventMask, trapMask, slimeMask;
//    private LayerMask slimeLayerMask; // ContactFilter2D 용
//    private int trapLayerIndex;

//    // === Slime Stick Tuning ===
//    [SerializeField] private float slimeStickPush = 22f;     // 벽 안쪽으로 누르는 연속 힘
//    [SerializeField] private float slimeNormalClamp = 20f;   // 벽 바깥 성분 속도 깎는 최대치
//    [SerializeField] private float carrySlideMaxFall = -11f; // 캐리 중 슬라이드 최대 하강속도(더 빠르게)

//    // === Wall Detach(이탈 유예창) ===
//    [SerializeField] private float wallDetachGrace = 0.13f; // 벽점프 직후 이 시간 동안 슬라임 무시
//    [System.NonSerialized] public float ignoreSlimeUntil = -1f; // Attractor에서 참조
//    public bool IsSlimeSuppressed => Time.time < ignoreSlimeUntil;

//    [Header("F Pulse Object")]
//    [SerializeField] private GameObject selectedObject; // F로 잠깐 켜질 오브젝트(Trigger 갖고 있어야 함)
//    [SerializeField] private float fPulseDuration = 0.3f; // 0.3초 유지
//    private float fPulseOffAt = -1f;

//    [Header("플레이어 ID 설정")]
//    public SwapController.PlayerChar playerID;
//    public SwapController swap;

//    // === 접지/레이 거리 ===
//    [Header("Ray distances")]
//    public float groundrayDistance = 1.3f;
//    public float breakrayDistance = 1.4f;
//    public float checkceilingtrap = 0.7f;

//    // === 키보드 이동 파라미터 (아이워너 느낌) ===
//    [Header("Keyboard Movement (IWB-style)")]
//    [SerializeField] public float moveSpeed = 9.5f;
//    [SerializeField] private float accel = 180f;
//    [SerializeField] private float decel = 220f;
//    [SerializeField] private float airAccel = 130f;
//    [SerializeField] private float airDecel = 150f;
//    [SerializeField] private float jumpVelocity = 11.8f;
//    [SerializeField] private float gravityScaleNormal = 3.2f;
//    [SerializeField] public float gravityScaleFall = 5.0f;
//    [SerializeField] private float cutJumpFactor = 0.45f;
//    [SerializeField] private float maxFallSpeed = -28f;
//    [SerializeField] private float coyoteTime = 0.06f;
//    [SerializeField] private float jumpBuffer = 0.08f;

//    [Header("Slime Friction Control")]
//    [SerializeField] private PhysicsMaterial2D slimeNoFrictionMat; // friction=0, bounciness=0짜리 간단한 2D 물리 머티리얼
//    private PhysicsMaterial2D _originalMat;
//    private bool _appliedNoFriction;

//    [Header("Ground Snap")]
//    [SerializeField] private Collider2D bodyCollider; // 플레이어 메인 콜라이더
//    [SerializeField] private float snapProbe = 0.20f;  // 발밑 레이 길이
//    [SerializeField] private float snapSkin = 0.02f;  // 여유치

//    [Header("Jump Feel Tuning")]
//    [SerializeField] private float minJumpHoldTime = 0.06f; // 최소 점프 유지 시간
//    [SerializeField] private float apexThreshold = 0.8f;     // |v.y|가 이 이하이면 정점 근처로 간주
//    [SerializeField] private float apexHangMultiplier = 0.7f; // 정점 근처 중력 완화(1보다 작게)
//    [SerializeField] private float gravitySmoothTime = 0.06f; // 중력 목표값 스무딩 시간

//    [Header("Carry Throw (Ballistic)")]
//    [SerializeField] private float carryThrowUpSpeed = 12f;        // 위로 던지는 Y속도
//    [SerializeField] private float carryThrowSideSpeed = 8f;       // 좌우 던질 때 X속도
//    [SerializeField] private float carryThrowSeparation = 0.18f;   // 캐리 해제 시 초기 분리 거리
//    [SerializeField] private float carryThrowBallisticMinTime = 1f; // 최소 탄도 유지 시간(초)
//    [SerializeField] private bool carryThrowHoldUntilGrounded = false; // 착지할 때까지 유지

//    [Header("Slime Stick Tuning")]
//    [SerializeField] private float slimeInwardHoldSpeed = 0.8f;   // 벽쪽으로 살짝 밀어주는 목표 속도(접촉 유지용)
//    [SerializeField] private float slimeInwardAccel = 35f;        // 그 목표로 당기는 가속
//    [SerializeField] private float wallSlideMaxFallCarrying = -12f; // 캐리 중 슬라이드 최대 하강속도(더 빠르게)

//    // 내부 상태
//    private bool ballisticThrowActive = false;
//    private float ballisticThrowEndTime = -1f;

//    private float lastJumpStartTime = -999f;
//    // 스왑/컷신 등에서 입력/애니를 잠깐 꺼두는 억제 타이머
//    private float swapSuppressUntil = -999f;

//    private bool didCutThisJump = false;
//    private float gravitySmoothVel = 0f; // SmoothDamp용

//    // === 더블 점프 ===
//    [Header("Extra Jumps")]
//    [SerializeField] public int extraAirJumps = 1; // 2단 점프 = 공중 추가 1회
//    private int airJumpsLeft = 0;

//    // === 바운스 패널 관련(유지) ===
//    [Header("Bounce Panels")]
//    [SerializeField] private float bounceImpulseX = 12f;
//    [SerializeField] private float bounceImpulseY = 15f;
//    [SerializeField] private float inputLockAfterImpulse = 0.12f;
//    [SerializeField] private float bounceProtectDuration = 0.06f;

//    [Header("Slime Wall")]
//    [SerializeField] private LayerMask slimeLayer;     // 슬라임(벽) 레이어
//    [SerializeField] private float wallCheckDist = 0.18f; // (호환 유지)
//    [SerializeField] private float wallSlideMaxFall = -5.5f; // 슬라이드 최대 하강속도(음수)
//    [SerializeField] private float wallJumpHorizontal = 9.0f; // 벽에서 튕겨나가는 X 속도
//    [SerializeField] private float wallJumpVertical = 11.5f; // 벽점프 Y 속도
//    [SerializeField] private bool requireSpaceForWallJump = false;
//    [SerializeField] private bool resetAirJumpsOnWallJump = true;

//    // === 내려찍기 (추가) ===
//    [Header("Dive (Down Slam)")]
//    [SerializeField] private float diveSpeed = -36f;          // 내려찍기 목표 하강 속도(음수)
//    [SerializeField] private float diveGravityScale = 7.5f;   // 내려찍기 중 중력 배수
//    private bool isDiving = false;

//    // === 캐리(안아 들기) 관련 ===
//    [Header("Carry (P1 carries P2)")]
//    public PlayerMouseMovement otherPlayer;   // P1에서 P2를 드래그로 연결
//    public float carryOffsetY = 0.5f;
//    public float carryPickupMaxGap = 0.15f;   // 두 콜라이더가 겹치거나 거의 닿아있는 허용 간격
//    public bool carryset = false;
//    public bool isCarrying = false;
//    public bool isCarried = false;            // 내가 들려있는 상태(P2에서 true)
//    private Transform otherOriginalParent;

//    [Header("Carry Gravity")]
//    [SerializeField] private float carryGravityMul = 1.15f;       // 캐리 중 상승/정점 구간 중력 가중치
//    [SerializeField] private float carryFallGravityMul = 1.25f;   // 캐리 중 낙하 구간 중력 가중치

//    // 기본 중력 값을 저장해 두고, 캐리 여부에 따라 곱해 씁니다.
//    private float baseGravityNormal;
//    private float baseGravityFall;

//    [Header("Ground Check Fix")]
//    [SerializeField] private float groundCheckSkin = 0.04f;   // 발바닥 스킨 두께
//    [SerializeField] private float postJumpGroundIgnore = 0.06f; // 점프/투척 직후 접지 무시 시간
//    private float ignoreGroundUntil = -1f;

//    // === Bounce 속도 튜닝 ===
//    [Header("Bounce Speed Tuning")]
//    [SerializeField] private bool smoothBounce = true;      // Impulse 대신 램프-업 사용
//    [SerializeField] private float bounceTargetSpeed = 14f; // 바운스 목표 속도(벡터 크기)
//    [SerializeField] private float bounceRampTime = 0.12f;  // 목표 속도까지 부드럽게 가속 시간
//    [SerializeField] private float bounceMaxSpeed = 18f;    // 안전 속도 캡

//    [Header("애니메이션")]
//    [SerializeField] public Animator animator;
//    int count = 0;
//    int jumpbool;
//    float janit = 0;

//    public int maxHP = 5;
//    public int currentHP;

//    public bool IsDead { get; private set; } = false; // 외부에서 읽기 가능, 내부에서만 세팅

//    // === 내부 상태 ===
//    private float lastGroundedTime = -999f;
//    private float lastJumpPressedTime = -999f;
//    private float rawX = 0f;
//    private bool jumpHeld = false;
//    private float inputLockUntil = -999f;
//    private bool isBouncing = false;
//    private float bounceProtectUntil = -999f;
//    private bool inBounceFlight = false;
//    private float bounceRampTimer = 0f;
//    private Vector2 bounceTargetVel;
//    private float bounceVxRef = 0f, bounceVyRef = 0f;
//    private bool lefthold;
//    private bool righthold;
//    private bool prevSelected = false;
//    private int playerLayerIndexSelf;

//    // SmoothDamp용
//    bool touchingLeftSlime, touchingRightSlime;
//    bool touchL_byCollision, touchR_byCollision;
//    bool touchL_byTrigger, touchR_byTrigger;

//    // 접지 전/후 변화 감지용
//    private bool wasGrounded = false;

//    // === 스케일/방향 (기존 외부 의존 고려해 유지) ===
//    public float dir = 1f;
//    public bool dirseto = true;
//    public bool chasize = true;
//    public float dirsetofl = 1f;

//    void Awake()
//    {
//        if (!rb) rb = GetComponent<Rigidbody2D>();
//        if (!bodyCollider) bodyCollider = GetComponent<Collider2D>();
//        animator = GetComponent<Animator>();
//        if (!rb) rb = GetComponent<Rigidbody2D>();
//        if (!bodyCollider)
//        {
//            bodyCollider = GetComponent<Collider2D>();
//            if (!bodyCollider) bodyCollider = GetComponentInChildren<Collider2D>();
//        }

//        _originalMat = bodyCollider ? bodyCollider.sharedMaterial : null;
//        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
//        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
//        currentHP = maxHP;
//        Physics2D.queriesHitTriggers = true;



//        // ★ 기본 중력값 저장
//        baseGravityNormal = gravityScaleNormal;
//        baseGravityFall = gravityScaleFall;

//        TryResolveSwap();
//        ResolveLayerMasks();
//        ApplyLayerIgnores();   // ★ 추가
//    }

//    void OnEnable()
//    {
//        SceneManager.sceneLoaded += OnSceneLoaded; // ★ 씬 로드 때마다 재시도
//    }

//    void OnDisable()
//    {
//        SceneManager.sceneLoaded -= OnSceneLoaded;
//    }

//#if UNITY_EDITOR
//    // 에디터에서 값이 바뀌거나 스크립트 리컴파일 시에도 연결 시도
//    void OnValidate()
//    {
//        if (!Application.isPlaying)
//            TryResolveSwap();
//    }
//#endif

//    private int GetMaskFromCsv(string namesCsv)
//    {
//        if (string.IsNullOrWhiteSpace(namesCsv)) return 0;
//        // "A, B ,C" → ["A","B","C"]
//        string[] parts = namesCsv.Split(',');
//        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
//        return LayerMask.GetMask(parts);
//    }

//    private void ResolveLayerMasks()
//    {
//        // 단일/다중 레이어 이름 모두 지원
//        groundMask = GetMaskFromCsv(groundLayerName);
//        eventMask = GetMaskFromCsv(eventLayerName);
//        trapMask = GetMaskFromCsv(trapLayerName);
//        slimeMask = GetMaskFromCsv(slimeLayerName);

//        slimeLayerMask = slimeMask; // ContactFilter2D 용
//        trapLayerIndex = LayerMask.NameToLayer(trapLayerName.Trim());
//        playerLayerIndexSelf = LayerMask.NameToLayer(playerLayerName.Trim());
//        if (groundMask == 0) Debug.LogWarning($"[Player] Ground layer(s) '{groundLayerName}' not found.");
//        if (eventMask == 0) Debug.LogWarning($"[Player] Event layer(s) '{eventLayerName}' not found.");
//        if (trapMask == 0) Debug.LogWarning($"[Player] Trap layer(s) '{trapLayerName}' not found.");
//        if (slimeMask == 0) Debug.LogWarning($"[Player] Slime layer(s) '{slimeLayerName}' not found.");
//        if (trapLayerIndex < 0) Debug.LogWarning($"[Player] Trap layer index for '{trapLayerName}' not found.");
//        if (playerLayerIndexSelf < 0) Debug.LogWarning($"[Player] Player layer '{playerLayerName}' not found.");
//    }

//    void Update()
//    {
//        bool isSelected = (swap != null && swap.charSelect == playerID);
//        bool suppressed = Time.time < swapSuppressUntil;
//        bool locked = suppressed || Time.time < inputLockUntil;

//        if (prevSelected && !isSelected)
//        {
//            ResetAnimStates();
//        }
//        prevSelected = isSelected;

//        if (!isSelected)
//        {
//            rawX = 0f;
//            jumpHeld = false;
//            return;
//        }

//        // === P1 전용: Shift로 캐리 토글 ===
//        if (playerID == SwapController.PlayerChar.P1)
//        {
//            bool shiftDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
//            if (shiftDown)
//            {
//                if (!isCarrying)
//                {
//                    TryStartCarryNow();
//                }
//                else
//                {
//                    StopCarry(); // 동시입력(방향키 + 캐리) 처리 포함
//                }
//            }
//        }

//        if (prevSelected && !isSelected)
//        {
//            ResetAnimStates();
//        }
//        prevSelected = isSelected;

//        if (!isSelected)
//        {
//            rawX = 0f;
//            jumpHeld = false;
//            return;
//        }

//        // 좌/우 입력
//        float left = (!locked && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))) ? -1f : 0f;
//        float right = (!locked && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))) ? 1f : 0f;
//        lefthold = (!locked && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)));
//        righthold = (!locked && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)));
//        rawX = Mathf.Clamp(left + right, -1f, 1f);

//        // 점프 입력 버퍼
//        if (!locked && Input.GetKeyDown(KeyCode.Space))
//            lastJumpPressedTime = Time.time;
//        jumpHeld = !locked && Input.GetKey(KeyCode.Space);

//        // --- F 키: selectedObject를 fPulseDuration 동안만 활성 ---
//        if (selectedObject)
//        {
//            if (Input.GetKeyDown(KeyCode.F))
//            {
//                selectedObject.SetActive(true);
//                fPulseOffAt = Time.time + fPulseDuration; // 타이머 리셋
//            }
//            if (selectedObject.activeSelf && Time.time >= fPulseOffAt)
//            {
//                selectedObject.SetActive(false);
//            }
//        }

//        bool grounded = IsGroundedStrictSmall();
//        if (grounded)
//        {
//            lastGroundedTime = Time.time;
//            airJumpsLeft = extraAirJumps;
//        }

//        // 방향 뒤집기
//        if (rawX != 0f)
//        {
//            dir = rawX > 0 ? 1f : -1f;
//            transform.localScale = new Vector3(dirseto ? dir : dir * 0.5f, dirsetofl, dirsetofl);
//        }

//        // 천장 트랩
//        CheckCeilingTrap();

//        // 바닥 트랩 즉사
//        var breakHit = IsBreak();
//        if (breakHit.collider != null && breakHit.collider.CompareTag("Trap"))
//        {
//            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
//            return;
//        }

//        // (사이즈 프리셋)
//        if (chasize)
//        {
//            dirseto = true;
//            dir = Mathf.Sign(dir == 0 ? 1f : dir);
//            dirsetofl = 1f;
//            groundrayDistance = 1.3f;
//            breakrayDistance = 1.4f;
//            checkceilingtrap = 0.7f;
//        }
//        else
//        {
//            dirseto = false;
//            dir = 0.5f;
//            dirsetofl = 0.5f;
//            groundrayDistance = 0.7f;
//            breakrayDistance = 0.6f;
//            checkceilingtrap = 0.35f;
//        }

//        // ==== 슬라임 접촉 상태 ====
//        bool groundedForWall = IsGrounded();
//        bool castL = !groundedForWall && TouchingSlimeSideCast(-1);
//        bool castR = !groundedForWall && TouchingSlimeSideCast(+1);
//        touchingLeftSlime = !groundedForWall && (castL || touchL_byCollision || touchL_byTrigger);
//        touchingRightSlime = !groundedForWall && (castR || touchR_byCollision || touchR_byTrigger);



//        // ==== 벽점프(반대 방향키) ====
//        bool awayLeft = touchingRightSlime && (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow));
//        bool awayRight = touchingLeftSlime && (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow));
//        bool spaceOK = requireSpaceForWallJump ? Input.GetKey(KeyCode.Space) : true;

//        if (!groundedForWall && spaceOK && (awayLeft || awayRight) && !(isCarrying || isCarried))
//        {
//            ignoreSlimeUntil = Time.time + wallDetachGrace;               // ★ 이탈 유예 시작
//            touchingLeftSlime = touchingRightSlime = false;

//            Vector2 v2 = rb.linearVelocity;
//            if (awayLeft) v2.x = -Mathf.Abs(wallJumpHorizontal);
//            if (awayRight) v2.x = Mathf.Abs(wallJumpHorizontal);
//            v2.y = wallJumpVertical;
//            rb.linearVelocity = v2;
//        }

//        // 디버그(F9)
//        if (Input.GetKeyDown(KeyCode.F9))
//        {
//            Debug.Log($"[SLIME] grounded(IsGrounded)={groundedForWall}, L={touchingLeftSlime}, R={touchingRightSlime}, isDiving={isDiving}");
//        }

//        if (!locked && Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
//            !locked && Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
//        {
//            RunAni();
//        }
//        else
//        {
//            RunexitAni();
//        }

//        if (!locked && jumpHeld)
//        {
//            janit += Time.deltaTime;
//            if (janit < 0.1f) { JumpAni(); }
//        }
//        else
//        {
//            janit = 0;
//        }
//    }

//    void FixedUpdate()
//    {
//        bool groundedStrict = IsGroundedStrictSmall();



//        Vector2 v = rb.linearVelocity;


//        bool grounded = groundedStrict; // ← 여기서부터는 groundedStrict만 씀

//        bool minTimeNotPassed = Time.time < ballisticThrowEndTime;

//        // 최소시간이 남아있거나, 옵션이 켜져 있고 아직 안착했으면 계속 탄도 유지
//        bool ballistic = ballisticThrowActive &&
//                         (minTimeNotPassed || (carryThrowHoldUntilGrounded && !groundedStrict));

//        if (ballisticThrowActive && !ballistic)
//            ballisticThrowActive = false;

//        // --- 목표 속도 & 가감속 ---
//        if (ballistic)
//        {
//            // ★ 탄도 중 조향: 입력이 있을 때만 가속, 입력이 없으면 X속도를 줄이지 않음(감속 없음)
//            if (Mathf.Abs(rawX) > 0.01f)
//            {
//                float targetX = rawX * moveSpeed;
//                float a = airAccel;
//                v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
//            }
//            // 입력이 없으면 v.x 유지(공력감 유지)
//        }
//        else
//        {
//            float targetX = rawX * moveSpeed;
//            float a = grounded
//                ? (Mathf.Sign(targetX) == Mathf.Sign(v.x) ? accel : decel)
//                : (Mathf.Abs(targetX) > Mathf.Abs(v.x) ? airAccel : airDecel);
//            v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
//        }

//        // --- 점프 처리(탄도 중엔 짧점/점프 스킵) ---
//        bool buffered = (Time.time - lastJumpPressedTime) <= jumpBuffer;
//        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;
//        if (!ballistic)
//        {
//            if (buffered && (canCoyote || airJumpsLeft > 0))
//            {
//                v.y = jumpVelocity;
//                lastJumpStartTime = Time.time;
//                didCutThisJump = false;
//                if (!canCoyote && !grounded) airJumpsLeft = Mathf.Max(airJumpsLeft - 1, 0);
//                lastJumpPressedTime = -999f;
//                lastGroundedTime = -999f;

//                ignoreGroundUntil = Time.time + postJumpGroundIgnore;
//            }
//        }

//        // --- 내려찍기/짧점 처리 ---
//        if (isDiving && !grounded)
//        {
//            rb.gravityScale = diveGravityScale;
//            v.y = Mathf.Min(v.y, diveSpeed);
//        }
//        else
//        {
//            // ★ 탄도 중엔 짧점(cut jump) 비활성화
//            if (!ballistic)
//            {
//                if (!grounded && !jumpHeld && v.y > 0f
//                    && !didCutThisJump
//                    && Time.time - lastJumpStartTime >= minJumpHoldTime)
//                {
//                    v.y *= cutJumpFactor;
//                    didCutThisJump = true;
//                }
//            }

//            float desiredGravity = (v.y < -0.01f) ? baseGravityFall : baseGravityNormal;

//            if (isCarrying)
//                desiredGravity *= (v.y < -0.01f) ? carryFallGravityMul : carryGravityMul;

//            if (!groundedStrict && Mathf.Abs(v.y) <= apexThreshold)
//                desiredGravity = Mathf.Min(desiredGravity, baseGravityNormal * apexHangMultiplier);

//            rb.gravityScale = Mathf.SmoothDamp(rb.gravityScale, desiredGravity, ref gravitySmoothVel, gravitySmoothTime);
//        }

//        if (v.y < maxFallSpeed) v.y = maxFallSpeed;
//        // 슬라임 접촉 중이면 마찰 0, 아니면 원복
//        bool touchingSlimeNow = !groundedStrict && (touchingLeftSlime || touchingRightSlime);

//        // 이탈 유예창 동안에도 마찰로 인해 X→Y 감쇠가 생기면 각도가 망가질 수 있으니,
//        // 접촉만 하고 있으면 유예 중이라도 마찰은 끄는 걸 권장합니다.
//        SetFrictionless(touchingSlimeNow);
//        // === SLIME STICK / SLIDE ===
//        bool onSlimeRaw = !groundedStrict && (touchingLeftSlime || touchingRightSlime);
//        bool allowStick = !IsSlimeSuppressed && !(isCarrying || isCarried);
//        bool onSlime = onSlimeRaw && allowStick;

//        // 지금 '벽에 붙어있고', 그 벽 쪽으로 입력을 누르고 있는가?
//        bool pressingIntoWall =
//            allowStick && onSlimeRaw &&
//            ((touchingLeftSlime && rawX < -0.01f) ||
//             (touchingRightSlime && rawX > 0.01f));

//        if (pressingIntoWall)
//        {
//            // 1) X 가속 자체를 막아 수직 보정으로 올라타는 현상 차단
//            rawX = 0f;

//            // 2) 혹시 위로 밀렸다면 상승을 0으로 클램프(포물선 각도 보존)
//            if (v.y > 0f) v.y = 0f;
//        }

//        if (onSlime)
//        {
//            Vector2 wallNormal = touchingLeftSlime ? Vector2.right : Vector2.left;

//            // 1) 벽 안쪽으로 지속 푸시
//            rb.AddForce(-wallNormal * slimeStickPush, ForceMode2D.Force);

//            // 2) 벽 바깥 성분만 감쇠
//            float vn = Vector2.Dot(v, wallNormal);
//            if (vn > 0f)
//            {
//                float cut = Mathf.Min(vn, slimeNormalClamp);
//                v -= wallNormal * cut;
//            }

//            // 3) 하강속도 캡
//            if (v.y < wallSlideMaxFall) v.y = wallSlideMaxFall;
//        }
//        else if (onSlimeRaw && (isCarrying || isCarried))
//        {
//            // 캐리 중에는 더 빠르게 미끄러지고 벽점프 금지(위에서 이미 금지)
//            if (v.y < carrySlideMaxFall) v.y = carrySlideMaxFall;
//        }


//        rb.linearVelocity = v;

//        // 슬라임 슬라이드/스냅 등 기존 로직 그대로…
//        bool groundedThisFrame = groundedStrict;
//        if (!wasGrounded && groundedThisFrame)
//        {
//            JumpedAni();

//            if (isDiving)
//            {
//                var hit = IsBreak();
//                if (hit.collider != null && hit.collider.CompareTag("Breakable"))
//                    Destroy(hit.collider.gameObject);
//            }
//            isDiving = false;

//            // ★ 착지하면 탄도 종료
//            ballisticThrowActive = false;
//        }
//        wasGrounded = groundedThisFrame;

//        touchL_byTrigger = touchR_byTrigger = false;
//    }

//    /* ===================== 충돌/트리거에서 슬라임 판정 보강 ===================== */

//    void OnCollisionStay2D(Collision2D col)
//    {
//        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;

//        for (int i = 0; i < col.contactCount; i++)
//        {
//            var n = col.GetContact(i).normal;
//            if (n.x > 0.35f) touchL_byCollision = true;
//            if (n.x < -0.35f) touchR_byCollision = true;
//        }

//        // === 캐리 대상 접촉 체크 ===
//        if (playerID == SwapController.PlayerChar.P1 && otherPlayer != null)
//        {
//            var op = col.collider.GetComponentInParent<PlayerMouseMovement>();
//            if (op != null && op == otherPlayer)
//                isCarried = true;
//        }
//        // === 캐리 대상 이탈 체크 ===
//        if (playerID == SwapController.PlayerChar.P1 && otherPlayer != null)
//        {
//            var op = col.collider.GetComponentInParent<PlayerMouseMovement>();
//            if (op != null && op == otherPlayer)
//                isCarried = false;
//        }
//    }

//    private void TryStartCarryNow()
//    {
//        if (otherPlayer == null || isCarrying || bodyCollider == null || otherPlayer.bodyCollider == null)
//            return;

//        // 두 콜라이더 사이 거리 계산
//        var d = Physics2D.Distance(bodyCollider, otherPlayer.bodyCollider);

//        // d.isOverlapped == true  : 이미 겹쳐 있음 → OK
//        // d.distance <= threshold : 거의 붙어 있음  → OK
//        bool closeEnough = d.isOverlapped || d.distance <= carryPickupMaxGap;

//        if (!closeEnough)
//        {
//            Debug.Log($"[Carry] too far: overlapped={d.isOverlapped}, dist={d.distance:F3}, need<={carryPickupMaxGap:F3}");
//            return;
//        }

//        StartCarry(); // 실제 캐리 시작
//    }

//    void OnCollisionExit2D(Collision2D col)
//    {
//        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;
//        touchL_byCollision = false;
//        touchR_byCollision = false;
//    }

//    void OnTriggerStay2D(Collider2D other)
//    {
//        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;

//        float ox = other.bounds.center.x;
//        float px = transform.position.x;
//        if (ox > px) touchR_byTrigger = true;
//        else touchL_byTrigger = true;
//    }

//    void OnTriggerExit2D(Collider2D other)
//    {
//        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;

//        float ox = other.bounds.center.x;
//        float px = transform.position.x;
//        if (ox > px) touchR_byTrigger = false;
//        else touchL_byTrigger = false;
//    }

//    private static bool IsInLayerMask(int layer, LayerMask mask)
//        => (mask.value & (1 << layer)) != 0;

//    /* ===================== 바운스/트랩 등 ===================== */

//    void OnCollisionEnter2D(Collision2D collision)
//    {
//        if (collision.gameObject.layer == LayerMask.NameToLayer("Trap"))
//        {
//            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
//            return;
//        }

//        if (collision.collider.CompareTag("BounceLeftUp"))
//        {
//            Bounce(new Vector2(-bounceImpulseX, bounceImpulseY));
//            return;
//        }
//        if (collision.collider.CompareTag("BounceRightUp"))
//        {
//            Bounce(new Vector2(+bounceImpulseX, bounceImpulseY));
//            return;
//        }
//    }

//    private void TryResolveSwap()
//    {
//        if (swap != null) return;

//        var go = GameObject.FindWithTag("Swap"); // 하이러키에서 태그 "Swap" 찾기
//        if (go != null)
//        {
//            swap = go.GetComponent<SwapController>();
//            if (swap == null)
//                Debug.LogWarning("[Player] Tag 'Swap' 오브젝트에 SwapController 컴포넌트가 없습니다.", go);
//        }
//        else
//        {
//            Debug.LogWarning("[Player] 태그 'Swap' 오브젝트를 씬에서 찾지 못했습니다.");
//        }
//    }

//    // 씬 로드 후에도 다시 시도 (어디티브 로드 대비)
//    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
//    {
//        TryResolveSwap();
//        ApplyLayerIgnores();
//    }

//    private void Bounce(Vector2 impulse)
//    {
//        rb.linearVelocity = Vector2.zero;
//        rb.AddForce(impulse, ForceMode2D.Impulse);

//        isBouncing = true;
//        bounceProtectUntil = Time.time + bounceProtectDuration;

//        inputLockUntil = Time.time + inputLockAfterImpulse;
//    }

//    private void StartCarry()
//    {
//        if (otherPlayer == null || isCarrying) return;

//        gravityScaleFall = 6.0f;
//        extraAirJumps = 0;
//        isCarrying = true;
//        carryset = true;
//        otherPlayer.rb.linearVelocity = Vector2.zero;
//        otherPlayer.rb.simulated = false;
//        otherPlayer.isCarried = true;
//        otherOriginalParent = otherPlayer.transform.parent;

//        // ✅ 월드 변환 유지
//        otherPlayer.transform.SetParent(this.transform, true);
//        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

//        // ★ 자동 탄도/무시 제거 (던질 때만 설정)
//        // otherPlayer.ballisticThrowActive = false; // 명시적으로 꺼 둠
//    }

//    private void StopCarry()
//    {
//        if (otherPlayer == null || !isCarrying) return;

//        // P1 자체 상태 복구(원 코드 유지)
//        gravityScaleFall = 4.0f;
//        moveSpeed = 7.0f;
//        extraAirJumps = 1;

//        // === 동시입력으로 발사 방향 결정 ===
//        int horizSign = 0;
//        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizSign = +1;
//        else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizSign = -1;

//        bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

//        // 부모 원복(월드 좌표 유지) + 겹침 방지 살짝 띄우기
//        otherPlayer.transform.SetParent(otherOriginalParent, worldPositionStays: true);
//        Vector3 dropOffset = new Vector3(horizSign * carryThrowSeparation,
//                                         carryOffsetY + carryThrowSeparation, 0f);
//        otherPlayer.transform.position = transform.position + dropOffset;

//        // 물리 복구
//        otherPlayer.rb.simulated = true;
//        otherPlayer.isCarried = false;

//        // === 입력 없으면 드롭만 (제자리 해제)
//        bool anyDir = (horizSign != 0) || upHeld;
//        if (!anyDir)
//        {
//            otherPlayer.rb.linearVelocity = Vector2.zero;
//            otherPlayer.ballisticThrowActive = false;
//            isCarrying = false;
//            carryset = false;
//            Debug.Log("[Carry] DROP (no input)");
//            return;
//        }

//        // === 입력이 있으면 발사: 위/좌/우/대각선 모두 지원 ===
//        float vx = horizSign * carryThrowSideSpeed;
//        float vy = carryThrowUpSpeed; // 포물선/수직 모두 위속도 부여
//        if (!upHeld && horizSign != 0)
//        {
//            // 예시처럼 "오른쪽 + 캐리" 만으로도 포물선: vy 동일 유지
//            // (원한다면 vy 스케일링 가능)
//        }
//        else if (upHeld && horizSign == 0)
//        {
//            // 위만: 수직 발사
//        }
//        // (upHeld && horizSign != 0) → 대각선 발사

//        otherPlayer.rb.linearVelocity = new Vector2(vx, vy);

//        // ★ 탄도 비행 활성화 (최소시간 보장)
//        otherPlayer.ballisticThrowActive = true;
//        otherPlayer.ballisticThrowEndTime = Time.time + otherPlayer.carryThrowBallisticMinTime;

//        // 컷점프 방지
//        otherPlayer.didCutThisJump = true;
//        otherPlayer.lastJumpStartTime = Time.time;

//        // 발사 직후 바닥 스침 무시
//        otherPlayer.ignoreGroundUntil = Time.time + otherPlayer.postJumpGroundIgnore;

//        isCarrying = false;
//        carryset = false;

//        Debug.Log("[Carry] THROW input: up=" + upHeld + " horiz=" + horizSign + " vel=" + new Vector2(vx, vy));
//    }

//    private void ApplyLayerIgnores()
//    {
//        if (playerLayerIndexSelf >= 0)
//            Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, playerLayerIndexSelf, true);
//    }

//    public void TakeDamage(int dmg = 1)
//    {
//        if (IsDead) return; // 이미 죽었으면 무시
//        currentHP -= dmg;
//        Debug.Log("플레이어 HP: " + currentHP);
//        if (currentHP <= 0) { Die(); }
//    }

//    public void SuppressInputFor(float seconds, bool zeroHorizontalVelocity = true)
//    {
//        swapSuppressUntil = Time.time + Mathf.Max(0f, seconds);

//        rawX = 0f;
//        jumpHeld = false;
//        lastJumpPressedTime = -999f;
//        ResetAnimStates();

//        if (zeroHorizontalVelocity && rb)
//            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
//    }

//    private void ResetAnimStates()
//    {
//        if (!rb2) return;
//        rb2.SetBool("run", false);
//        rb2.SetBool("jump", false);
//        rb2.SetBool("jumped", false);
//    }

//    private void Die()
//    {
//        IsDead = true;
//        Debug.Log("플레이어 사망!");
//    }

//    private void RunAni()
//    {
//        rb2.SetBool("run", true);
//        lefthold = false;
//        righthold = false;
//    }
//    private void RunexitAni()
//    {
//        rb2.SetBool("run", false);
//    }
//    private void JumpAni()
//    {
//        rb2.SetBool("jump", true);
//    }
//    private void JumpedAni()
//    {
//        rb2.SetBool("jump", false);
//        rb2.SetBool("jumped", true);
//    }

//    // === 탄도 유지 판단용: Ground만 짧게 본다(OneWay/Event는 무시) ===
//    private bool IsGroundedStrictSmall()
//    {
//        if (Time.time < ignoreGroundUntil) return false;
//        if (!bodyCollider) return false;

//        Bounds b = bodyCollider.bounds;
//        float skin = Mathf.Max(0.005f, groundCheckSkin);

//        // 발바닥에서 살짝 위로 올린 얇은 박스
//        Vector2 boxCenter = new Vector2(b.center.x, b.min.y + skin * 0.5f);
//        Vector2 boxSize = new Vector2(Mathf.Max(0.02f, b.size.x * 0.9f), skin);

//        // Ground 레이어만 본다 (OneWay/Event 무시)
//        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, groundMask);
//#if UNITY_EDITOR
//        Color c = hit ? Color.green : Color.red;
//        Debug.DrawLine(new Vector2(boxCenter.x - boxSize.x * 0.5f, boxCenter.y),
//                       new Vector2(boxCenter.x + boxSize.x * 0.5f, boxCenter.y), c, 0f, false);
//#endif
//        return hit != null;
//    }
//    private void SetFrictionless(bool on)
//    {
//        if (!bodyCollider) return;

//        if (on)
//        {
//            if (!_appliedNoFriction)
//            {
//                if (!slimeNoFrictionMat)
//                {
//                    // 인스펙터에 안 넣어도 돌아가게 런타임용 임시 생성
//                    slimeNoFrictionMat = new PhysicsMaterial2D("Runtime_NoFric");
//                    slimeNoFrictionMat.friction = 0f;
//                    slimeNoFrictionMat.bounciness = 0f;
//                }
//                bodyCollider.sharedMaterial = slimeNoFrictionMat;
//                _appliedNoFriction = true;
//            }
//        }
//        else
//        {
//            if (_appliedNoFriction)
//            {
//                bodyCollider.sharedMaterial = _originalMat;
//                _appliedNoFriction = false;
//            }
//        }
//    }
//    private void GroundSnap(ref Vector2 vel)
//    {
//        if (vel.y > 0f) return;
//        if (!bodyCollider) return;

//        int groundOrEventMask = groundMask | eventMask;

//        Bounds b = bodyCollider.bounds;
//        Vector2 originCenter = new Vector2(b.center.x, b.min.y + 0.01f);
//        float probe = snapProbe + snapSkin;

//        Vector2 left = originCenter + Vector2.left * (b.extents.x * 0.6f);
//        Vector2 right = originCenter + Vector2.right * (b.extents.x * 0.6f);

//        RaycastHit2D hitC = Physics2D.Raycast(originCenter, Vector2.down, probe, groundOrEventMask);
//        RaycastHit2D hitL = Physics2D.Raycast(left, Vector2.down, probe, groundOrEventMask);
//        RaycastHit2D hitR = Physics2D.Raycast(right, Vector2.down, probe, groundOrEventMask);

//        Debug.DrawRay(originCenter, Vector2.down * probe, hitC.collider ? Color.yellow : Color.gray);
//        Debug.DrawRay(left, Vector2.down * probe, hitL.collider ? Color.yellow : Color.gray);
//        Debug.DrawRay(right, Vector2.down * probe, hitR.collider ? Color.yellow : Color.gray);

//        RaycastHit2D hit = hitC.collider ? hitC : (hitL.collider ? hitL : hitR);
//        if (!hit.collider) return;

//        float currentFootY = b.min.y;
//        float targetFootY = hit.point.y + snapSkin;
//        float delta = targetFootY - currentFootY;

//        if (delta >= -0.001f)
//        {
//            rb.position += new Vector2(0f, delta);
//            vel.y = Mathf.Max(vel.y, 0f);
//            rb.linearVelocity = vel;
//        }
//    }

//    // === 슬라임 접촉 감지: Collider.Cast 기반 ===
//    bool TouchingSlimeSideCast(int sign) // -1=왼쪽, +1=오른쪽
//    {
//        if (!bodyCollider) return false;

//        Vector2 dir = (sign < 0) ? Vector2.left : Vector2.right;

//        ContactFilter2D filter = new ContactFilter2D();
//        filter.useLayerMask = true;
//        filter.SetLayerMask(slimeLayerMask);
//        filter.useTriggers = true;

//        RaycastHit2D[] hits = new RaycastHit2D[2];
//        int count = bodyCollider.Cast(dir, filter, hits, 0.03f);
//        if (count > 0) return true;

//        Bounds b = bodyCollider.bounds;
//        float padX = 0.04f;
//        Vector2 size = new Vector2(0.12f, b.size.y * 0.8f);
//        Vector2 center = (Vector2)b.center + new Vector2(sign * (b.extents.x + size.x * 0.5f + padX), 0f);
//        bool boxHit = Physics2D.OverlapBox(center, size, 0f, slimeMask);
//#if UNITY_EDITOR
//        Color c = (count > 0 || boxHit) ? Color.green : Color.red;
//        Debug.DrawLine(center + Vector2.up * size.y * 0.5f, center - Vector2.up * size.y * 0.5f, c, 0f, false);
//#endif
//        return boxHit;
//    }

//    /* ===================== 레이 감지 유지 ===================== */

//    public bool IsGrounded()
//    {
//        float rayDistance = groundrayDistance;

//        Vector2 center = transform.position + Vector3.down * 0.2f;
//        Vector2 left = center + Vector2.left * 0.1f;
//        Vector2 right = center + Vector2.right * 0.1f;

//        bool centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, groundMask);
//        bool leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, groundMask);
//        bool rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, groundMask);

//        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit ? Color.green : Color.red);
//        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit ? Color.green : Color.red);
//        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit ? Color.green : Color.red);

//        return centerHit || leftHit || rightHit;
//    }

//    public RaycastHit2D IsBreak()
//    {
//        float rayDistance = breakrayDistance;

//        Vector2 center = transform.position + Vector3.down * 0.2f;
//        Vector2 left = center + Vector2.left * 0.1f;
//        Vector2 right = center + Vector2.right * 0.1f;

//        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, eventMask);
//        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, eventMask);
//        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, eventMask);

//        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit.collider ? Color.cyan : Color.gray);
//        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit.collider ? Color.cyan : Color.gray);
//        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit.collider ? Color.cyan : Color.gray);

//        if (centerHit.collider != null) return centerHit;
//        if (leftHit.collider != null) return leftHit;
//        if (rightHit.collider != null) return rightHit;

//        return new RaycastHit2D();
//    }

//    private void CheckCeilingTrap()
//    {
//        float rayDistance = checkceilingtrap;
//        Vector2 center = transform.position + Vector3.up * 0.5f;
//        Vector2 left = center + Vector2.left * 0.1f;
//        Vector2 right = center + Vector2.right * 0.1f;

//        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.up, rayDistance, trapMask);
//        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.up, rayDistance, trapMask);
//        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.up, rayDistance, trapMask);

//        Debug.DrawRay(center, Vector2.up * rayDistance, centerHit.collider ? Color.magenta : Color.gray);
//        Debug.DrawRay(left, Vector2.up * rayDistance, leftHit.collider ? Color.magenta : Color.gray);
//        Debug.DrawRay(right, Vector2.up * rayDistance, rightHit.collider ? Color.magenta : Color.gray);

//        bool hitTrap =
//            (centerHit.collider && ((trapLayerIndex >= 0 && centerHit.collider.gameObject.layer == trapLayerIndex) || centerHit.collider.CompareTag("Trap"))) ||
//            (leftHit.collider && ((trapLayerIndex >= 0 && leftHit.collider.gameObject.layer == trapLayerIndex) || leftHit.collider.CompareTag("Trap"))) ||
//            (rightHit.collider && ((trapLayerIndex >= 0 && rightHit.collider.gameObject.layer == trapLayerIndex) || rightHit.collider.CompareTag("Trap")));

//        if (hitTrap)
//        {
//            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
//        }
//    }
//}
