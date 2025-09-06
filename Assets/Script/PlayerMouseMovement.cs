using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
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

    [Header("Carry Drop/Throw Spawn")]
    [SerializeField] private float carryDropForward = 0.35f; // 바라보는 방향으로 얼마나 앞에 둘지

    // 내부 캐시 (코드에서만 사용)
    private int groundMask, eventMask, trapMask, slimeMask;
    private LayerMask slimeLayerMask; // ContactFilter2D 용
    private int trapLayerIndex;

    // === Slime Stick Tuning ===
    [SerializeField] private float slimeStickPush = 22f;
    [SerializeField] private float slimeNormalClamp = 20f;
    [SerializeField] private float carrySlideMaxFall = -11f;

    // === Wall Detach(이탈 유예창) ===
    [SerializeField] private float wallDetachGrace = 0.13f;
    [System.NonSerialized] public float ignoreSlimeUntil = -1f;
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

    // === Carry Timing (anim-driven) ===
    [Header("Carry Timing (Anim-driven)")]
    [SerializeField] private bool useAnimDrivenCarry = true;
    [SerializeField] private float carryStartMinLock = 0.08f;
    [SerializeField] private float carryEndMinLock = 0.06f;

    // ▼ 정확히 0.6초 후에 보이게 고정
    [SerializeField] private float revealDelayOnDrop = 0.6f; // EXACT 0.6s
    private Coroutine _carryLockCo;

    private Coroutine _revealCo; // P2 복귀 코루틴
    private Coroutine _throwResetCo; // throw 종료 타이머


    // === 접지/레이 거리 ===
    [Header("Ray distances")]
    public float groundrayDistance = 1.3f;
    public float breakrayDistance = 1.4f;
    public float checkceilingtrap = 0.7f;

    [Header("Carry Cooldown")]
    [SerializeField] private float carryCooldown = 1.4f;   // 해제 후 추가 쿨타임
    private float nextCarryAllowedAt = 0f;                    // 다음 캐리 허용 시각

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
    public bool carryset = false;
    public bool isCarrying = false;
    public bool isCarried = false;
    private Transform otherOriginalParent;

    [Header("Carry Gravity")]
    [SerializeField] private float carryGravityMul = 1.15f;
    [SerializeField] private float carryFallGravityMul = 1.25f;

    private float baseGravityNormal;
    private float baseGravityFall;

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
    [SerializeField] public Animator animator;
    int count = 0;
    int jumpbool;
    float janit = 0;

    // === 공격 ===
    [Header("Attack")]
    [SerializeField] private float attackCooldown = 1.0f;   // 쿨타임 1초
    [SerializeField] private float attackDuration = 0.5f;   // 공격 애니 길이(자동 종료)
    private float nextAttackTime = 0f;                      // 다음 사용 가능 시각
    private float attackEndTime = -1f;                      // 공격 종료 시각
    private bool attack = false;                            // 공격 중 여부 (애니 bool과 동기화)

    public int maxHP = 5;
    public int currentHP;

    public bool IsDead { get; private set; } = false;

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
    private int playerLayerIndexSelf;

    // SmoothDamp용
    bool touchingLeftSlime, touchingRightSlime;
    bool touchL_byCollision, touchR_byCollision;
    bool touchL_byTrigger, touchR_byTrigger;

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
        if (!bodyCollider) bodyCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!bodyCollider)
        {
            bodyCollider = GetComponent<Collider2D>();
            if (!bodyCollider) bodyCollider = GetComponentInChildren<Collider2D>();
        }

        _originalMat = bodyCollider ? bodyCollider.sharedMaterial : null;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (playerID == SwapController.PlayerChar.P1) maxHP = p1MaxHP;
        else if (playerID == SwapController.PlayerChar.P2) maxHP = p2MaxHP;
        currentHP = maxHP;
        Physics2D.queriesHitTriggers = true;

        baseGravityNormal = gravityScaleNormal;
        baseGravityFall = gravityScaleFall;

        TryResolveSwap();
        ResolveLayerMasks();
        ApplyLayerIgnores();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            TryResolveSwap();
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
        if (groundMask == 0) Debug.LogWarning($"[Player] Ground layer(s) '{groundLayerName}' not found.");
        if (eventMask == 0) Debug.LogWarning($"[Player] Event layer(s) '{eventLayerName}' not found.");
        if (trapMask == 0) Debug.LogWarning($"[Player] Trap layer(s) '{trapLayerName}' not found.");
        if (slimeMask == 0) Debug.LogWarning($"[Player] Slime layer(s) '{slimeLayerName}' not found.");
        if (trapLayerIndex < 0) Debug.LogWarning($"[Player] Trap layer index for '{trapLayerName}' not found.");
        if (playerLayerIndexSelf < 0) Debug.LogWarning($"[Player] Player layer '{playerLayerName}' not found.");
    }

    void Update()
    {
        bool isSelected = (swap != null && swap.charSelect == playerID);
        bool suppressed = Time.time < swapSuppressUntil;
        bool locked = suppressed || Time.time < inputLockUntil;

        if (IsDead)
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

        if (playerID == SwapController.PlayerChar.P1)
        {
            bool shiftDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            bool canToggleCarry = !locked && Time.time >= nextCarryAllowedAt;

            if (shiftDown && canToggleCarry)
            {
                if (!isCarrying) TryStartCarryNow();
                else StopCarry();
            }
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

        // 좌/우 입력
        float left = (!locked && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))) ? -1f : 0f;
        float right = (!locked && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))) ? 1f : 0f;
        lefthold = (!locked && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)));
        righthold = (!locked && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)));
        rawX = Mathf.Clamp(left + right, -1f, 1f);

        // 점프 입력 버퍼
        if (!locked && Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;
        jumpHeld = !locked && Input.GetKey(KeyCode.Space);

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

        // ==== 벽점프(반대 방향키) ====
        bool awayLeft = touchingRightSlime && (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow));
        bool awayRight = touchingLeftSlime && (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow));
        bool spaceOK = requireSpaceForWallJump ? Input.GetKey(KeyCode.Space) : true;

        if (!groundedForWall && spaceOK && (awayLeft || awayRight) && !(isCarrying || isCarried))
        {
            ignoreSlimeUntil = Time.time + wallDetachGrace;
            touchingLeftSlime = touchingRightSlime = false;

            Vector2 v2 = rb.linearVelocity;
            if (awayLeft) v2.x = -Mathf.Abs(wallJumpHorizontal);
            if (awayRight) v2.x = Mathf.Abs(wallJumpHorizontal);
            v2.y = wallJumpVertical;
            rb.linearVelocity = v2;
        }

        // 디버그(F9)
        if (Input.GetKeyDown(KeyCode.F9))
        {
            Debug.Log($"[SLIME] grounded(IsGrounded)={groundedForWall}, L={touchingLeftSlime}, R={touchingRightSlime}, isDiving={isDiving}");
        }

        if (!locked && Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
            !locked && Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
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
            if (janit < 0.1f) { JumpAni(); }
        }
        else
        {
            janit = 0;
        }
    }

    void FixedUpdate()
    {
        bool groundedStrict = IsGroundedStrictSmall();

        Vector2 v = rb.linearVelocity;

        bool grounded = groundedStrict;

        bool minTimeNotPassed = Time.time < ballisticThrowEndTime;

        bool ballistic = ballisticThrowActive &&
                         (minTimeNotPassed || (carryThrowHoldUntilGrounded && !groundedStrict));

        if (ballisticThrowActive && !ballistic)
            ballisticThrowActive = false;

        // --- 목표 속도 & 가감속 ---
        if (ballistic)
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
        if (!ballistic)
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
        else
        {
            float desiredGravity = (v.y < -0.01f) ? baseGravityFall : baseGravityNormal;

            if (isCarrying)
                desiredGravity *= (v.y < -0.01f) ? carryFallGravityMul : carryGravityMul;

            if (!groundedStrict && Mathf.Abs(v.y) <= apexThreshold)
                desiredGravity = Mathf.Min(desiredGravity, baseGravityNormal * apexHangMultiplier);

            rb.gravityScale = Mathf.SmoothDamp(rb.gravityScale, desiredGravity, ref gravitySmoothVel, gravitySmoothTime);
        }

        if (v.y < maxFallSpeed) v.y = maxFallSpeed;

        bool touchingSlimeNow = !groundedStrict && (touchingLeftSlime || touchingRightSlime);
        SetFrictionless(touchingSlimeNow);

        // === SLIME STICK / SLIDE ===
        bool onSlimeRaw = !groundedStrict && (touchingLeftSlime || touchingRightSlime);
        bool allowStick = !IsSlimeSuppressed && !(isCarrying || isCarried);
        bool onSlime = onSlimeRaw && allowStick;

        bool pressingIntoWall =
            allowStick && onSlimeRaw &&
            ((touchingLeftSlime && rawX < -0.01f) ||
             (touchingRightSlime && rawX > 0.01f));

        if (pressingIntoWall)
        {
            rawX = 0f;
            if (v.y > 0f) v.y = 0f;
        }

        if (onSlime)
        {
            Vector2 wallNormal = touchingLeftSlime ? Vector2.right : Vector2.left;
            rb.AddForce(-wallNormal * slimeStickPush, ForceMode2D.Force);

            float vn = Vector2.Dot(v, wallNormal);
            if (vn > 0f)
            {
                float cut = Mathf.Min(vn, slimeNormalClamp);
                v -= wallNormal * cut;
            }

            if (v.y < wallSlideMaxFall) v.y = wallSlideMaxFall;
        }
        else if (onSlimeRaw && (isCarrying || isCarried))
        {
            if (v.y < carrySlideMaxFall) v.y = carrySlideMaxFall;
        }

        rb.linearVelocity = v;

        bool groundedThisFrame = groundedStrict;
        if (!wasGrounded && groundedThisFrame)
        {
            JumpedAni();

            if (isDiving)
            {
                var hit = IsBreak();
                if (hit.collider != null && hit.collider.CompareTag("Breakable"))
                    Destroy(hit.collider.gameObject);
            }
            isDiving = false;

            ballisticThrowActive = false;
        }
        wasGroundedThisFrame = groundedThisFrame; // fix typo -> declare var correctly
        wasGrounded = groundedThisFrame;

        touchL_byTrigger = touchR_byTrigger = false;
    }

    /* ===================== 충돌/트리거에서 슬라임 판정 보강 ===================== */

    void OnCollisionStay2D(Collision2D col)
    {
        if (playerID == SwapController.PlayerChar.P1 && otherPlayer != null)
        {
            var op = col.collider.GetComponentInParent<PlayerMouseMovement>();
            if (op != null && op == otherPlayer)
                isCarried = true;
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
            if (op != null && op == otherPlayer)
                isCarried = false;
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

    private static bool IsInLayerMask(int layer, LayerMask mask)
        => (mask.value & (1 << layer)) != 0;

    /* ===================== 바운스/트랩 등 ===================== */

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Trap"))
        {
            return;
        }

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

        if (otherPlayer == null || isCarrying || bodyCollider == null || otherPlayer.bodyCollider == null)
            return;

        var d = Physics2D.Distance(bodyCollider, otherPlayer.bodyCollider);
        bool closeEnough = d.isOverlapped || d.distance <= carryPickupMaxGap;
        if (!closeEnough)
        {
            Debug.Log($"[Carry] too far: overlapped={d.isOverlapped}, dist={d.distance:F3}, need<={carryPickupMaxGap:F3}");
            return;
        }

        StartCarry();
    }

    private void TryResolveSwap()
    {
        if (swap != null) return;

        var go = GameObject.FindWithTag("Swap");
        if (go != null)
        {
            swap = go.GetComponent<SwapController>();
            if (swap == null)
                Debug.LogWarning("[Player] Tag 'Swap' 오브젝트에 SwapController 컴포넌트가 없습니다.", go);
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
    private void SetOtherPlayerVisible(bool visible)
    {
        if (!otherPlayer) return;
        var rends = otherPlayer.GetComponentsInChildren<Renderer>(true);
        Debug.Log("공주 내려놓음");
        for (int i = 0; i < rends.Length; i++) rends[i].enabled = visible;
    }

    // P2를 delay 뒤에 다시 보이게 (정확히 0.6초: Real-time 기준)
    private IEnumerator RevealOtherAfter(float delay)
    {
        // 정확한 실시간 딜레이(타임스케일 영향 없음)
        yield return new WaitForSecondsRealtime(delay);
        SetOtherPlayerVisible(true);
    }

    private IEnumerator ResetThrowAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (rb2) rb2.SetBool("carry", false);
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
        otherOriginalParent = otherPlayer.transform.parent;

        otherPlayer.transform.SetParent(this.transform, true);
        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

        SetOtherPlayerVisible(false);

        // 애니 기반 입력잠금 시작
        BeginCarryStartLock();
        rb2.SetBool("carry", true);

        if (_revealCo != null) { StopCoroutine(_revealCo); _revealCo = null; }
    }

    private void StopCarry()
    {
        if (otherPlayer == null || !isCarrying) return;

        gravityScaleFall = 4.0f;
        moveSpeed = 7.0f;
        extraAirJumps = 1;

        int horizSign = 0;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizSign = +1;
        else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizSign = -1;
        bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

        int facingSign = (transform.localScale.x >= 0f) ? +1 : -1;
        int spawnSign = (horizSign != 0) ? horizSign : facingSign;

        Vector3 spawnOffset = new Vector3(spawnSign * carryDropForward,
                                          carryOffsetY + carryThrowSeparation, 0f);

        // 분리 & 위치
        otherPlayer.transform.SetParent(otherOriginalParent, worldPositionStays: true);
        otherPlayer.transform.position = transform.position + spawnOffset;

        // 물리/상태 복구
        otherPlayer.rb.simulated = true;
        otherPlayer.isCarried = false;

        bool anyDir = (horizSign != 0) || upHeld;

        if (!anyDir)
        {
            // === DROP: 0.6초 후에 보이기 유지 ===
            otherPlayer.rb.linearVelocity = Vector2.zero;
            otherPlayer.ballisticThrowActive = false;

            isCarrying = false;
            carryset = false;
            rb2.SetBool("carry", false);

            BeginCarryEndLock();

            if (_revealCo != null) StopCoroutine(_revealCo);
            _revealCo = StartCoroutine(RevealOtherAfter(0.6f)); // 정확히 0.6초

            Debug.Log("[Carry] DROP (no input)");
            return;
        }

        // === THROW: 즉시 보이기 ===
        float vx = horizSign * carryThrowSideSpeed;
        float vy = carryThrowUpSpeed;
        otherPlayer.rb.linearVelocity = new Vector2(vx, vy);

        otherPlayer.ballisticThrowActive = true;
        otherPlayer.ballisticThrowEndTime = Time.time + otherPlayer.carryThrowBallisticMinTime;
        otherPlayer.didCutThisJump = true;
        otherPlayer.lastJumpStartTime = Time.time;
        otherPlayer.ignoreGroundUntil = Time.time + otherPlayer.postJumpGroundIgnore;

        isCarrying = false;
        carryset = false;

        // 캐리 애니는 해제, 던지기 애니는 on
        rb2.SetBool("throw", true);

        // 애니 기반 잠금(기존 로직 유지)
        BeginCarryEndLock();

        // ▶ P1은 0.7초 동안 이동 불가 + 그 뒤 throw false
        if (playerID == SwapController.PlayerChar.P1)
        {
            inputLockUntil = Mathf.Max(inputLockUntil, Time.time + 0.6f);

            if (_throwResetCo != null) StopCoroutine(_throwResetCo);
            _throwResetCo = StartCoroutine(ResetThrowAfter(0.6f));
        }


        
        // 던지기는 바로 가시화
        if (_revealCo != null) { StopCoroutine(_revealCo); _revealCo = null; }
        SetOtherPlayerVisible(true);

        Debug.Log("[Carry] THROW input: up=" + upHeld + " horiz=" + horizSign + " vel=" + new Vector2(vx, vy));
    }


    private void ApplyLayerIgnores()
    {
        if (playerLayerIndexSelf >= 0)
            Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, playerLayerIndexSelf, true);
    }

    public void TakeDamage(int dmg = 1)
    {
        if (IsDead || _sceneReloading) return;

        int amount = Mathf.Max(1, dmg);
        currentHP = Mathf.Max(0, currentHP - amount);
        Debug.Log($"플레이어 HP: {currentHP}");

        if (currentHP <= 0)
        {
            Die();
        }
    }

    public void SuppressInputFor(float seconds, bool zeroHorizontalVelocity = true)
    {
        swapSuppressUntil = Time.time + Mathf.Max(0f, seconds);

        rawX = 0f;
        jumpHeld = false;
        lastJumpPressedTime = -999f;
        ResetAnimStates();

        if (zeroHorizontalVelocity && rb)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
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
    }

    private void RunAni()
    {
        rb2.SetBool("run", true);
        lefthold = false;
        righthold = false;
    }
    private void RunexitAni()
    {
        rb2.SetBool("run", false);
    }
    private void JumpAni()
    {
        rb2.SetBool("jump", true);
    }
    private void JumpedAni()
    {
        rb2.SetBool("jump", false);
        rb2.SetBool("jumped", true);
    }

    // === 공격 시작/종료 ===
    private void StartAttack()
    {
        attack = true;
        rb2.SetBool("attack", true);

        // 히트박스/이펙트 펄스
        if (selectedObject)
        {
            selectedObject.SetActive(true);
            fPulseOffAt = Time.time + fPulseDuration;
        }

        attackEndTime = Time.time + attackDuration; // 공격 애니 자동 종료
        nextAttackTime = Time.time + attackCooldown; // 쿨타임 시작
    }

    private void EndAttack()
    {
        attack = false;
        rb2.SetBool("attack", false);
        // selectedObject는 fPulseDuration 타이머로 별도 종료됨
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

    private IEnumerator LockForAnimation(float minLockSeconds, bool zeroHorizontalVelocity)
    {
        float t0 = Time.time;

        inputLockUntil = Mathf.Max(inputLockUntil, t0 + 0.0001f);

        if (zeroHorizontalVelocity && rb)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        yield return null; // 상태 전이 후 길이 측정

        float clipLen = useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f;
        float lockFor = Mathf.Max(minLockSeconds, clipLen);

        inputLockUntil = Time.time + lockFor;

        while (Time.time < inputLockUntil)
            yield return null;

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

        Vector2 boxCenter = new Vector2(b.center.x, b.min.y + skin * 0.5f);
        Vector2 boxSize = new Vector2(Mathf.Max(0.02f, b.size.x * 0.9f), skin);

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, groundMask);
#if UNITY_EDITOR
        Color c = hit ? Color.green : Color.red;
        Debug.DrawLine(new Vector2(boxCenter.x - boxSize.x * 0.5f, boxCenter.y),
                       new Vector2(boxCenter.x + boxSize.x * 0.5f, boxCenter.y), c, 0f, false);
#endif
        return hit != null;
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

    // === 슬라임 접촉 감지: Collider.Cast 기반 ===
    bool TouchingSlimeSideCast(int sign)
    {
        if (!bodyCollider) return false;

        Vector2 dir = (sign < 0) ? Vector2.left : Vector2.right;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useLayerMask = true;
        filter.SetLayerMask(slimeLayerMask);
        filter.useTriggers = true;

        RaycastHit2D[] hits = new RaycastHit2D[2];
        int count = bodyCollider.Cast(dir, filter, hits, 0.03f);
        if (count > 0) return true;

        Bounds b = bodyCollider.bounds;
        float padX = 0.04f;
        Vector2 size = new Vector2(0.12f, b.size.y * 0.8f);
        Vector2 center = (Vector2)b.center + new Vector2(sign * (b.extents.x + size.x * 0.5f + padX), 0f);
        bool boxHit = Physics2D.OverlapBox(center, size, 0f, slimeMask);
#if UNITY_EDITOR
        Color c = (count > 0 || boxHit) ? Color.green : Color.red;
        Debug.DrawLine(center + Vector2.up * size.y * 0.5f, center - Vector2.up * size.y * 0.5f, c, 0f, false);
#endif
        return boxHit;
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
    }

    // 내부 필드 보정
    private bool wasGroundedThisFrame; // added to fix reference in FixedUpdate
}
