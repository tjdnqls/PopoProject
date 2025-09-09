using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class WarriorAI : MonoBehaviour
{
    public enum State { Idle, Patrol, PreChasePause, Chase, Jumping, RunAttack, Dashing, Hit, Death }

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private SpriteAnimationManager anim;
    [SerializeField] private Transform player;

    [Header("Combat")]
    [SerializeField, Tooltip("플레이어를 공격으로 전환하는 감지 범위")]
    private float runAttackRange = 1.6f;

    // ===== Waypoints (A <-> B) =====
    [Header("Waypoints (A <-> B)")]
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private Vector2 fallbackLocalA = new Vector2(-3f, 0f);
    [SerializeField] private Vector2 fallbackLocalB = new Vector2(3f, 0f);
    [SerializeField] private float arriveEps = 0.08f;
    private Vector2 wpA, wpB;
    private int patrolTargetIndex; // 0:A, 1:B
    private int dir = +1;

    [Header("Layers")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask monsterMask;

    private LayerMask solidMask;    // (wall|ground)
    private LayerMask blockingMask; // (obstacle|ground) - player

    // ===== Sensor Global Controls =====
    [Header("Sensor Global Controls")]
    [Tooltip("모든 '레이 길이(거리)'를 일괄 스케일합니다. 1=기본, 0.5=절반, 2=두 배.")]
    [SerializeField, Range(0.2f, 3f)] private float rayLengthScale = 1f;

    [Tooltip("모든 '박스/오버랩 너비·높이'를 일괄 스케일합니다. 1=기본, 0.5=얇게, 2=두껍게.")]
    [SerializeField, Range(0.2f, 3f)] private float raySizeScale = 1f;

    [Tooltip("Scene 뷰에서 기즈모(센서/착지 예측선 등)를 표시합니다.")]
    [SerializeField] private bool showGizmos = true;

    [Tooltip("Play 중 Game 뷰에 Debug.DrawLine을 표시합니다.")]
    [SerializeField] private bool showDebugLines = true;

    // ===== Move / Detect =====
    [Header("Move / Detect")]
    [SerializeField, Tooltip("추격 달리기 속도")]
    private float runSpeed = 3.6f;

    [SerializeField, Tooltip("공격이 모두 끝난 뒤 추격 재개 전 대기 시간")]
    private float postAttackChaseDelay = 0.2f;

    [SerializeField, Tooltip("순찰 속도")]
    private float patrolSpeed = 2.4f;

    [SerializeField, Tooltip("x가속 (MoveTowards step)")]
    private float accel = 25f;

    [SerializeField, Tooltip("플레이어 감지 반경(PreChasePause 후 추격)")]
    private float detectRadius = 8f;

    [SerializeField, Tooltip("플레이어 감지 시 잠깐 멈추는 시간(초)")]
    private float preChasePauseSec = 1.0f;

    private readonly Collider2D[] _atkBuf = new Collider2D[4];

    // ===== Jump =====
    [Header("Jump")]
    [SerializeField, Tooltip("점프 초기 상승 속도")]
    private float jumpVy = 9.0f;

    [SerializeField, Tooltip("최대 점프 수평 전진량(점프 중 x 유지 상한 계산용)")]
    private float maxJumpForward = 4.5f;

    [SerializeField, Tooltip("점프로 오를 수 있는 최대 높이")]
    private float maxJumpHeight = 2.4f;

    [SerializeField, Tooltip("점프 궤적 계산용 중력 가정값")]
    private float gravityForCalc = 18.0f;

    [SerializeField, Tooltip("벽 위 착지 시, 벽 전면에서 이만큼 앞에 떨어지도록 x 보정")]
    private float landOnTopOffsetX = 0.55f;

    [SerializeField, Tooltip("점프 여유량(벽 높이가 최대 점프보다 이 값만큼 낮아야 점프로 처리)")]
    private float jumpTopClearance = 0.08f;

    // ===== Grounded =====
    [Header("Grounded")]
    [SerializeField, Tooltip("접지 체크 레이 길이")]
    private float groundedCheckDist = 0.2f;

    [SerializeField, Tooltip("발바닥 레이 시작 y 오프셋")]
    private float footRayYOffset = 0.05f;

    // ===== Front Wall Check (Rays) =====
    [Header("Front Wall Check")]
    [SerializeField, Tooltip("정면 벽 감지 레이 길이(발목 높이)")]
    private float wallCheckDist = 0.30f;

    [SerializeField, Tooltip("정면 벽 감지 레이 길이(허리 높이)")]
    private float midWallCheckDist = 0.30f;

    [SerializeField, Tooltip("정면 벽 감지 레이 길이(어깨 높이)")]
    private float topWallCheckDist = 0.30f;

    // ===== Front Block Detector (Box) =====
    [Header("Front Block Detector (Box)")]
    [Tooltip("정면 박스 감지 너비(월드 단위). 너무 크면 오탐, 너무 작으면 미탐.")]
    [SerializeField] private float frontProbeWidth = 0.18f;

    [Tooltip("정면 박스 감지 높이 비율(몸체 높이 대비). 0.8이면 몸의 80% 높이를 스캔.")]
    [SerializeField, Range(0.2f, 1.2f)] private float frontProbeHeightScale = 0.8f;

    [Tooltip("몸 전면에서 박스를 얼마나 떼어 둘지(패드).")]
    [SerializeField] private float frontProbeForwardPad = 0.02f;

    [Tooltip("막힘 조건이 잠깐 풀려도 타이머를 유지하는 유예시간(초).")]
    [SerializeField] private float blockedGrace = 0.15f;

    // 내부 상태
    private float blockedGraceUntil = -1f;

    // ===== Dash =====
    [Header("Dash (벽 관통)")]
    [SerializeField, Tooltip("대쉬 속도(관통 중 x 이동 속력)")]
    private float dashSpeed = 12.0f;

    [SerializeField, Tooltip("대쉬 지속 시간(초)")]
    private float dashDuration = 0.22f;

    [SerializeField, Tooltip("다음 대쉬까지 쿨다운(초)")]
    private float dashCooldown = 1.0f;

    [SerializeField, Tooltip("관통 후 착지할 x를 벽면에서 이만큼 더 앞쪽으로 검색 시작")]
    private float dashLandOffset = 0.6f;

    [SerializeField, Tooltip("관통 후 바닥을 찾을 최대 탐사 높이")]
    private float dashGroundProbe = 4.0f;

    [SerializeField, Tooltip("관통 시 충돌 여유(오버랩 박스 축소량)")]
    private float dashClearancePad = 0.12f;

    [SerializeField, Tooltip("대쉬 시작 시 Y를 이만큼 띄워서(들뜸) 관통 안정성 확보")]
    private float dashLiftY = 0.1f;

    private float nextDashTime = -999f;

    // ===== Chase Stall → Force Dash =====
    [Header("Force Dash When Stuck")]
    [SerializeField, Tooltip("추격 중 벽에 막혀 거의 움직이지 않으면, 이 시간(초) 후 강제 대쉬")]
    private float forceDashAfter = 2.0f;

    [SerializeField, Tooltip("정지 판정용 속도 임계값(|vx| < 이 값이면 거의 정지로 간주)")]
    private float blockedSpeedEpsilon = 0.05f;

    [SerializeField, Tooltip("정지 판정용 위치 임계값(|Δx| < 이 값이면 거의 정지로 간주)")]
    private float blockedPosEpsilon = 0.02f;

    private float blockedSince = -1f;
    private float lastX = 0f;

    // ===== Combat / Attack =====
    [Header("Attack Settings")]
    [SerializeField, Tooltip("공격 사거리 진입 시 '서서 붉어짐' 유지 시간")]
    private float attackStayWindup = 1.0f;

    [SerializeField, Tooltip("히트박스 자식 오브젝트(공격 시 0.2초 활성)")]
    private GameObject attackHitObject;

    [SerializeField, Tooltip("히트박스 활성 시간")]
    private float hitboxActiveTime = 1f;

    [SerializeField, Tooltip("공격 종료 뒤 공격 트리거 비활성 시간")]
    private float postAttackNoTrigger = 0.3f;

    [SerializeField, Tooltip("공격 플러스 발동 확률(0~1)")]
    private float attackPlusChance = 0.3f;

    [SerializeField, Tooltip("색상 복귀 페이드 시간")]
    private float colorFadeBack = 0.15f;

    private float atkLockUntil = -999f;

    // ★ 공격 재진입 방지 & 이번 시퀀스의 어택 플러스 결정
    private bool isAttacking = false;
    private bool attackPlusThisSeq = false;

    // ===== State =====
    private State state = State.Idle;
    private float preChaseUntil = -1f;

    private bool jumpPlanned = false;
    private float plannedLandX = 0f;
    private float plannedJumpVxAbs = 0f;
    private int plannedJumpFace = +1;

    private Bounds B => body.bounds;

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        if (!anim) anim = GetComponentInChildren<SpriteAnimationManager>();
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!body) body = GetComponent<Collider2D>();
        if (!sr) sr = GetComponentInChildren<SpriteRenderer>();

        if (!player)
        {
            var pgo = GameObject.FindGameObjectWithTag("Player");
            if (pgo) player = pgo.transform;
        }

        solidMask = wallMask | groundMask;
        blockingMask = (obstacleMask | groundMask) & ~playerMask;

        wpA = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        wpB = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        patrolTargetIndex = (Vector2.SqrMagnitude((Vector2)transform.position - wpA) <=
                             Vector2.SqrMagnitude((Vector2)transform.position - wpB)) ? 0 : 1;
        dir = ((GetPatrolTarget().x - transform.position.x) >= 0f) ? +1 : -1;
    }

    private void OnEnable()
    {
        state = (waypointA || waypointB) ? State.Patrol : State.Idle;
        var v = rb.linearVelocity; v.x = dir * patrolSpeed; rb.linearVelocity = v;
        SetFlipByDir(dir);
        PlayIdle();
        lastX = transform.position.x;
        blockedSince = -1f;
        blockedGraceUntil = -1f;
        isAttacking = false;
        attackPlusThisSeq = false;
    }

    private void Update()
    {
        if (state == State.Death || state == State.Hit) return;

        if (state == State.Patrol) dir = (GetPatrolTarget().x >= transform.position.x) ? +1 : -1;
        else if (player) dir = (player.position.x >= transform.position.x) ? +1 : -1;

        SetFlipByDir(dir);

        if (showDebugLines) DrawDebugLines();

        TryDetectPlayer();

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.PreChasePause: TickPreChasePause(); break;
            case State.Chase: TickChase(); break;
            case State.Jumping: TickJumping(); break;
            case State.Dashing: break;
            case State.RunAttack: break;
            case State.Idle: break;
        }
    }

    // ===== Detect → PreChasePause =====
    private void TryDetectPlayer()
    {
        if (!player) return;
        if (state == State.Chase || state == State.Jumping || state == State.Dashing || state == State.RunAttack) return;

        if (Vector2.Distance(player.position, transform.position) <= detectRadius)
        {
            state = State.PreChasePause;
            preChaseUntil = Time.time + Mathf.Max(0f, preChasePauseSec);
            var v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
            PlayIdle();
            blockedSince = -1f;
            blockedGraceUntil = -1f;
        }
    }

    private void TickPreChasePause()
    {
        if (Time.time >= preChaseUntil) EnterChase();
    }

    private void EnterChase()
    {
        if (!player) return;
        state = State.Chase;
        PlayRun();
        var v = rb.linearVelocity; v.x = runSpeed * dir; rb.linearVelocity = v;
        blockedSince = -1f;
        blockedGraceUntil = -1f;
        lastX = transform.position.x;
    }

    // ===== Patrol =====
    private void TickPatrol()
    {
        PlayRun();
        Vector2 target = GetPatrolTarget();
        dir = (target.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(dir);
        MoveHorizontalTowards(dir * patrolSpeed);

        if (Mathf.Abs(target.x - transform.position.x) <= arriveEps)
        {
            TogglePatrolTarget();
            return;
        }

        if (FrontWallHit(dir, out _))
        {
            TogglePatrolTarget();
            dir = -dir;
            SetFlipByDir(dir);
        }
    }

    // ===== Chase =====
    private void TickChase()
    {
        if (!player) { state = (waypointA || waypointB) ? State.Patrol : State.Idle; return; }
        PlayRun();

        // 공격 트리거 (★ 한 번만: isAttacking 플래그로 재진입 차단)
        if (!isAttacking && Time.time >= atkLockUntil && state == State.Chase && PlayerInFrontInRange(runAttackRange))
        {
            isAttacking = true;
            attackPlusThisSeq = (Random.value < Mathf.Clamp01(attackPlusChance)); // 이번 시퀀스에 한 번만 결정
            StartCoroutine(CoAttackSequence());
            return;
        }

        // --- 막힘/정지 감지 + 2초 후 강제 대쉬 ---
        bool frontBlockedBox = FrontBlockedBox(out _); // 안정적인 '막힘' 판정
        bool nearlyStopped = Mathf.Abs(rb.linearVelocity.x) < blockedSpeedEpsilon
                          || Mathf.Abs(transform.position.x - lastX) < blockedPosEpsilon;

        if (state == State.Chase)
        {
            if (frontBlockedBox && nearlyStopped)
            {
                if (blockedSince < 0f) blockedSince = Time.time;
                blockedGraceUntil = -1f;

                if (Time.time - blockedSince >= forceDashAfter)
                {
                    if (player)
                    {
                        dir = (player.position.x >= transform.position.x) ? +1 : -1;
                        SetFlipByDir(dir);
                    }

                    float nearFaceX;
                    if (!FrontWallRay(dir, out RaycastHit2D frontHitR))
                        nearFaceX = B.center.x + dir * (B.extents.x + 0.2f);
                    else
                        nearFaceX = frontHitR.point.x;

                    float landX;
                    if (!TryFindDashExitX(nearFaceX, out landX))
                    {
                        float forward = Mathf.Max(dashLandOffset, dashSpeed * dashDuration * 0.9f);
                        landX = nearFaceX + dir * forward;
                    }
                    StartCoroutine(CoDashTo(landX));

                    blockedSince = -1f;
                    blockedGraceUntil = -1f;
                    lastX = transform.position.x;
                    return;
                }
            }
            else
            {
                if (blockedSince >= 0f)
                {
                    if (blockedGraceUntil < 0f) blockedGraceUntil = Time.time + blockedGrace;
                    if (Time.time >= blockedGraceUntil)
                    {
                        blockedSince = -1f;
                        blockedGraceUntil = -1f;
                    }
                }
            }
        }

        // --- 3개의 전면 레이가 모두 '수직면' 히트면 → 무조건 대쉬 (점프 시도 X) ---
        if (FrontThreeRaysAllHit(dir, out float nearFaceX3))
        {
            if (Time.time >= nextDashTime)
            {
                if (!TryFindDashExitX(nearFaceX3, out float dashLandX)) // 실패해도 강행
                {
                    float forward = Mathf.Max(dashLandOffset, dashSpeed * dashDuration * 0.9f);
                    dashLandX = nearFaceX3 + dir * forward;
                }
                StartCoroutine(CoDashTo(dashLandX));
                lastX = transform.position.x;
                return;
            }
            else
            {
                HoldChase();
                lastX = transform.position.x;
                return;
            }
        }

        // --- 일반 벽 대응: 점프로 가능? 아니면 대쉬 ---
        if (FrontWallRay(dir, out RaycastHit2D frontHit2))
        {
            if (IsWallJumpable(frontHit2, out float landX))
            {
                PlanJumpTo(landX);
                TryJump();
            }
            else
            {
                if (Time.time >= nextDashTime && TryFindDashExitX(frontHit2.point.x, out float dashLandX))
                    StartCoroutine(CoDashTo(dashLandX));
                else
                    HoldChase();
            }
            lastX = transform.position.x;
            return;
        }

        // 벽이 없으면 계속 전진
        MoveHoriz(runSpeed * dir);
        lastX = transform.position.x;
    }

    // ===== Front Rays =====
    private bool FrontWallHit(int d, out RaycastHit2D chosenHit)
    {
        chosenHit = default;
        Bounds b = B;

        float[] ySamples = new float[] { b.min.y + 0.20f, b.center.y, b.max.y - 0.20f };
        float[] dists = new float[]
        {
            wallCheckDist * rayLengthScale,
            midWallCheckDist * rayLengthScale,
            topWallCheckDist * rayLengthScale
        };

        float startX = b.center.x + d * (b.extents.x + 0.02f);

        for (int i = 0; i < ySamples.Length; i++)
        {
            Vector2 origin = new Vector2(startX, ySamples[i]);
            var hit = Physics2D.Raycast(origin, Vector2.right * d, dists[i], solidMask);
            if (showDebugLines)
                Debug.DrawLine(origin, origin + Vector2.right * d * dists[i], hit ? Color.red : new Color(1f, 0f, 0f, 0.25f));
            if (IsVerticalSurface(hit, d))
            {
                chosenHit = hit;
                return true;
            }
        }
        return false;
    }

    // 세 레이가 모두 '수직면' 히트하면 true
    private bool FrontThreeRaysAllHit(int d, out float nearFaceX)
    {
        nearFaceX = 0f;
        Bounds b = B;
        float startX = b.center.x + d * (b.extents.x + 0.02f);

        float[] ys = new float[] { b.min.y + 0.20f, b.center.y, b.max.y - 0.20f };
        float[] ds = new float[]
        {
            wallCheckDist * rayLengthScale,
            midWallCheckDist * rayLengthScale,
            topWallCheckDist * rayLengthScale
        };

        int count = 0;
        float sumX = 0f;

        for (int i = 0; i < 3; i++)
        {
            Vector2 o = new Vector2(startX, ys[i]);
            var hit = Physics2D.Raycast(o, Vector2.right * d, ds[i], solidMask);
            if (showDebugLines)
                Debug.DrawLine(o, o + Vector2.right * d * ds[i], hit ? Color.red : new Color(1f, 0f, 0.25f, 0.25f));
            if (IsVerticalSurface(hit, d))
            {
                count++;
                sumX += hit.point.x;
            }
        }

        if (count == 3)
        {
            nearFaceX = sumX / 3f;
            return true;
        }
        return false;
    }

    // 벽 위 착지 가능 여부
    private bool IsWallJumpable(RaycastHit2D wallHit, out float landX)
    {
        landX = 0f;
        Bounds b = B;

        float frontX = wallHit.point.x;
        float targetX = frontX + dir * landOnTopOffsetX;
        float scanTopY = b.min.y + maxJumpHeight + 1.0f;

        Vector2 downFrom = new Vector2(targetX, scanTopY);
        float downLen = (scanTopY - (b.min.y - 1.0f)) * rayLengthScale;
        var down = Physics2D.Raycast(downFrom, Vector2.down, downLen, groundMask);

        if (showDebugLines)
            Debug.DrawLine(downFrom, downFrom + Vector2.down * downLen, down ? Color.green : new Color(0f, 1f, 0f, 0.25f));

        if (!down) return false;

        float wallTopY = down.point.y;
        float needed = wallTopY - b.min.y;
        bool can = needed <= (maxJumpHeight - Mathf.Max(0f, jumpTopClearance));

        if (!can) return false;

        landX = targetX;
        return true;
    }

    // 대쉬 착지 x 탐색
    private bool TryFindDashExitX(float nearFaceX, out float landX)
    {
        landX = 0f;
        Bounds b = B;
        float dashMaxForward = Mathf.Max(0.1f, dashSpeed * dashDuration) * 1.6f;

        float step = 0.08f;
        float start = dashLandOffset;
        float end = dashMaxForward;

        for (float s = start; s <= end; s += step)
        {
            float candidateX = nearFaceX + dir * s;

            if (!IsSafeHorizontal(candidateX)) continue;

            Vector2 downFrom = new Vector2(candidateX, b.center.y + maxJumpHeight + 1.0f);
            float downLen = (dashGroundProbe + maxJumpHeight + 1.0f) * rayLengthScale;
            var down = Physics2D.Raycast(downFrom, Vector2.down, downLen, groundMask);

            if (showDebugLines)
                Debug.DrawLine(downFrom, downFrom + Vector2.down * downLen, down ? Color.green : new Color(0f, 1f, 0f, 0.25f));

            if (!down) continue;

            landX = down.point.x;
            return true;
        }
        return false;
    }

    private bool IsSafeHorizontal(float targetX)
    {
        Bounds b = B;
        Vector2 size = new Vector2(
            Mathf.Max(0.05f, (b.size.x - dashClearancePad) * raySizeScale),
            Mathf.Max(0.05f, (b.size.y - dashClearancePad) * raySizeScale)
        );
        Vector2 center = new Vector2(targetX, b.center.y);
        Collider2D hit = Physics2D.OverlapBox(center, size, 0f, solidMask);
        return hit == null;
    }

    // ===== Jump =====
    private void PlanJumpTo(float landX)
    {
        plannedLandX = landX;
        plannedJumpFace = dir;

        float T = Mathf.Max(0.1f, 2f * jumpVy / gravityForCalc);
        float dx = Mathf.Abs(landX - transform.position.x);
        float vDesired = dx / T;
        float vMin = runSpeed * 0.7f;
        float vMax = runSpeed * 1.8f;
        plannedJumpVxAbs = Mathf.Clamp(vDesired, vMin, vMax);

        jumpPlanned = true;
    }

    private void TryJump()
    {
        if (!IsGrounded()) return;

        var v = rb.linearVelocity;
        v.y = jumpVy;
        v.x = jumpPlanned ? plannedJumpVxAbs * plannedJumpFace : runSpeed * dir;
        rb.linearVelocity = v;

        state = State.Jumping;
        PlayJump();
    }

    private void TickJumping()
    {
        if (jumpPlanned)
        {
            var v = rb.linearVelocity;
            v.x = plannedJumpVxAbs * plannedJumpFace;
            rb.linearVelocity = v;
        }

        // 점프 중 하강 시, 아래 바닥이 가까이 있으면 바로 스냅 착지
        if (rb.linearVelocity.y <= 0f)
        {
            Vector2 from = new Vector2(B.center.x, B.min.y + 0.02f);
            float len = (groundedCheckDist + 0.3f) * rayLengthScale; // 살짝 여유
            var down = Physics2D.Raycast(from, Vector2.down, len, groundMask);
            if (down.collider)
            {
                // 바닥으로 스냅
                float delta = (down.point.y - B.min.y) + 0.005f;
                transform.position = new Vector3(transform.position.x, transform.position.y + delta, transform.position.z);
                rb.linearVelocity = Vector2.zero;
                jumpPlanned = false;
                state = State.Chase;
                PlayRun();
                return;
            }
        }

        if (IsGrounded())
        {
            jumpPlanned = false;
            state = State.Chase;
            PlayRun();
        }
    }

    private bool IsGrounded()
    {
        Vector2 p = new Vector2(B.center.x, B.min.y + footRayYOffset);
        float len = groundedCheckDist * rayLengthScale;
        var hit = Physics2D.Raycast(p, Vector2.down, len, groundMask);
        if (showDebugLines)
            Debug.DrawLine(p, p + Vector2.down * len, hit ? Color.green : new Color(0f, 1f, 0f, 0.25f));
        return hit.collider != null;
    }

    // ===== Dash (확실한 관통) =====
    private IEnumerator CoDashTo(float landX)
    {
        nextDashTime = Time.time + dashCooldown;
        state = State.Dashing;
        PlayDash(); // 애니메이션 확실히 재생

        // 캐시: 현재 콜라이더 바운드/크기/센터 오프셋
        Bounds b0 = body.bounds;
        Vector2 cachedSize = new Vector2(
            Mathf.Max(0.05f, (b0.size.x - dashClearancePad) * raySizeScale),
            Mathf.Max(0.05f, (b0.size.y - dashClearancePad) * raySizeScale)
        );
        float centerYOffset = b0.center.y - transform.position.y;

        // 모든 콜라이더 비활성 + 물리 완전 비활성
        Collider2D[] cols = GetComponentsInChildren<Collider2D>(includeInactive: false);
        bool[] wasEnabled = new bool[cols.Length];
        for (int i = 0; i < cols.Length; i++) { wasEnabled[i] = cols[i].enabled; cols[i].enabled = false; }

        float prevGravity = rb.gravityScale;
        bool prevSimulated = rb.simulated;
        Vector2 prevVel = rb.linearVelocity;
        float prevAngVel = rb.angularVelocity;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = 0f;
        rb.simulated = false; // ← 관통 보장

        // --- 시작 시 Y를 들어주기 ---
        float startY = transform.position.y + dashLiftY;
        transform.position = new Vector3(transform.position.x, startY, transform.position.z);

        float dashTime = 0f;
        float dashMax = Mathf.Max(0.05f, dashDuration);
        float dashDir = Mathf.Sign(landX - transform.position.x);
        float speed = Mathf.Abs(dashSpeed);

        while (dashTime < dashMax)
        {
            dashTime += Time.deltaTime;
            float step = speed * Time.deltaTime * dashDir;
            Vector3 p = transform.position;
            p.x += step; p.y = startY; // y 고정
            transform.position = p;

            if ((dashDir > 0f && p.x >= landX) || (dashDir < 0f && p.x <= landX))
                break;

            yield return null;
        }

        // 최종 위치 고정
        Vector3 endP = transform.position; endP.x = landX; endP.y = startY; transform.position = endP;
        Physics2D.SyncTransforms();

        // 착지 안전성 검사/보정 (캐시된 크기/센터 기반)
        float centerYEnd = endP.y + centerYOffset;
        if (!IsSafeHorizontalCustom(endP.x, cachedSize, centerYEnd))
        {
            Vector3 pos = endP;
            if (TryResolvePenetrationCustom(ref pos, cachedSize, centerYOffset))
            {
                transform.position = pos;
                Physics2D.SyncTransforms();
            }
        }

        // 물리/콜라이더 복구
        rb.simulated = prevSimulated;
        rb.gravityScale = prevGravity;
        rb.linearVelocity = prevVel;
        rb.angularVelocity = prevAngVel;

        for (int i = 0; i < cols.Length; i++) cols[i].enabled = wasEnabled[i];

        state = State.Chase; PlayRun();
    }

    private bool IsSafeHorizontalCustom(float targetX, Vector2 size, float centerY)
    {
        Vector2 center = new Vector2(targetX, centerY);
        Collider2D hit = Physics2D.OverlapBox(center, size, 0f, solidMask);
        return hit == null;
    }

    private bool TryResolvePenetrationCustom(ref Vector3 pos, Vector2 size, float centerYOffset)
    {
        float[] dxs = new float[] { 0.10f * dir, -0.10f * dir, 0.0f, 0.18f * dir, -0.18f * dir };
        float[] dys = new float[] { 0.10f, 0.18f, -0.06f, 0f };
        foreach (float dx in dxs)
            foreach (float dy in dys)
            {
                Vector3 test = new Vector3(pos.x + dx, pos.y + dy, pos.z);
                float testCenterY = test.y + centerYOffset;
                if (IsSafeHorizontalCustom(test.x, size, testCenterY)) { pos = test; return true; }
            }
        return false;
    }

    // ===== Actions / Attack Sequence =====
    private IEnumerator CoAttackSequence()
    {
        state = State.RunAttack;
        rb.linearVelocity = Vector2.zero;

        // 안전장치
        if (!sr || !anim)
        {
            isAttacking = false;
            attackPlusThisSeq = false;
            state = State.Chase;
            PlayRun();
            yield break;
        }

        // 1) attackStay : 붉어짐 + 대기
        PlayAttackStay();
        yield return StartCoroutine(FadeSpriteColor(sr.color, new Color(1f, 0.35f, 0.35f, sr.color.a), attackStayWindup));

        // 2) 무조건 attack 실행
        yield return StartCoroutine(FadeSpriteColor(sr.color, Color.white, colorFadeBack));
        PlayAttack();
        yield return StartCoroutine(ActivateHitObjectFor(hitboxActiveTime));
        yield return new WaitForSeconds(0.6f);
        // 3) 이번 시퀀스에서만 미리 정한 Attack Plus 사용
        if (attackPlusThisSeq)
        {
            yield return StartCoroutine(FadeSpriteColor(sr.color, new Color(1f, 0.35f, 0.35f, sr.color.a), 1.0f));
            yield return StartCoroutine(FadeSpriteColor(sr.color, Color.white, colorFadeBack));
            PlayAttackPlus();
            yield return StartCoroutine(ActivateHitObjectFor(hitboxActiveTime));
        }

        // 4) 후딜 락 + 추격 재개 판단
        atkLockUntil = Time.time + postAttackNoTrigger;

        sr.color = Color.white;
        state = State.Chase;
        PlayRun();

        // ★ 재장전
        isAttacking = false;
        attackPlusThisSeq = false;
    }

    private IEnumerator ActivateHitObjectFor(float seconds)
    {
        if (attackHitObject)
        {
            attackHitObject.SetActive(true);
            yield return new WaitForSeconds(seconds);
            attackHitObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(seconds);
        }
    }

    private IEnumerator FadeSpriteColor(Color from, Color to, float duration)
    {
        if (duration <= 0f) { sr.color = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            sr.color = Color.Lerp(from, to, k);
            yield return null;
        }
        sr.color = to;
    }

    // ===== Helpers =====
    private void MoveHoriz(float vx)
    {
        var v = rb.linearVelocity; v.x = vx; rb.linearVelocity = v;
    }

    private void MoveHorizontalTowards(float targetSpeedX)
    {
        Vector2 v = rb.linearVelocity;
        v.x = Mathf.MoveTowards(v.x, targetSpeedX, accel * Time.deltaTime);
        rb.linearVelocity = v;
    }

    private void HoldChase()
    {
        var v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
        PlayIdle(); // 상태는 Chase 유지
    }

    private void SetFlipByDir(int d) { if (sr) sr.flipX = d < 0; }

    private static bool IsVerticalSurface(RaycastHit2D hit, int d)
    {
        if (!hit) return false;
        return Mathf.Abs(hit.normal.x) > 0.5f && Mathf.Sign(-hit.normal.x) == d;
    }

    private bool PlayerInFrontInRange(float range)
    {
        if (!player) return false;

        Bounds b = B;
        float width = Mathf.Max(0.05f, range * rayLengthScale);
        float height = Mathf.Max(0.10f, b.size.y * 0.55f * raySizeScale);

        Vector2 size = new Vector2(width, height);
        Vector2 center = new Vector2(b.center.x + dir * (b.extents.x + size.x * 0.5f), b.center.y);

        var filter = new ContactFilter2D();
        filter.SetLayerMask(playerMask);
        filter.useLayerMask = true;

        int hitCount = Physics2D.OverlapBox(center, size, 0f, filter, _atkBuf);

        for (int i = 0; i < hitCount; i++)
        {
            var col = _atkBuf[i];
            if (!col) continue;
            if (col.transform.root == transform.root) continue; // 자기 자신/자식 제거

            // 앞쪽에 있는지(수평 방향)
            if (Mathf.Sign(col.bounds.center.x - b.center.x) != dir) continue;

            // 수직 차이 너무 크면 제외(필요시 조정)
            if (Mathf.Abs(col.bounds.center.y - b.center.y) > b.size.y * 0.7f) continue;

            return true;
        }
        return false;
    }

    // 정면 '넓은' 박스 감지: 벽/기둥/경사 포함 안정적으로 막힘 확인
    private bool FrontBlockedBox(out Collider2D col)
    {
        Bounds b = B;
        Vector2 size = new Vector2(
            frontProbeWidth * raySizeScale,
            Mathf.Max(0.05f, b.size.y * frontProbeHeightScale * raySizeScale)
        );
        Vector2 center = new Vector2(
            b.center.x + dir * (b.extents.x + frontProbeForwardPad + size.x * 0.5f),
            b.center.y
        );
        col = Physics2D.OverlapBox(center, size, 0f, solidMask);

#if UNITY_EDITOR
        if (showDebugLines)
        {
            Vector2 h = size * 0.5f;
            Vector2 A = center + new Vector2(-h.x, -h.y);
            Vector2 Bp = center + new Vector2(h.x, -h.y);
            Vector2 C = center + new Vector2(h.x, h.y);
            Vector2 D = center + new Vector2(-h.x, h.y);
            Debug.DrawLine(A, Bp, Color.yellow);
            Debug.DrawLine(Bp, C, Color.yellow);
            Debug.DrawLine(C, D, Color.yellow);
            Debug.DrawLine(D, A, Color.yellow);
        }
#endif
        return col != null;
    }

    // 정면 레이로 히트 포인트만 얻기(점프/대쉬 계산용). 수직면이 아니어도 OK.
    private bool FrontWallRay(int d, out RaycastHit2D hit)
    {
        Bounds b = B;
        float maxDist = Mathf.Max(wallCheckDist, midWallCheckDist, topWallCheckDist) * rayLengthScale + 0.6f;
        float startX = b.center.x + d * (b.extents.x + 0.02f);
        Vector2 origin = new Vector2(startX, b.center.y);
        hit = Physics2D.Raycast(origin, Vector2.right * d, maxDist, solidMask);

#if UNITY_EDITOR
        if (showDebugLines)
            Debug.DrawLine(origin, origin + Vector2.right * d * maxDist, hit ? Color.magenta : new Color(1f, 0f, 1f, 0.2f));
#endif
        return hit.collider != null;
    }

    private Vector2 GetPatrolTarget() => (patrolTargetIndex == 0) ? wpA : wpB;
    private void TogglePatrolTarget() => patrolTargetIndex = (patrolTargetIndex == 0) ? 1 : 0;

    // ===== Anim =====
    private void PlayRun() { if (anim) anim.Play("run"); }
    private void PlayIdle() { if (anim) anim.Play("idle"); }
    private void PlayAttack() { if (anim) anim.Play("attack"); Debug.Log("공격 실행"); }
    private void PlayAttackStay() { if (anim) anim.Play("attackStay"); }
    private void PlayAttackPlus() { if (anim) anim.Play("attackPlus"); }
    private void PlayJump() { if (anim) anim.Play("jump"); }
    private void PlayDash() { if (anim) anim.Play("dash"); } // ← 이름 수정

    // ===== Debug =====
    private void DrawDebugLines()
    {
        Vector2 gp = new Vector2(B.center.x, B.min.y + footRayYOffset);
        Debug.DrawLine(gp, gp + Vector2.down * (groundedCheckDist * rayLengthScale), Color.green);

        float startX = B.center.x + dir * (B.extents.x + 0.02f);
        Vector2 a = new Vector2(startX, B.min.y + 0.20f);
        Vector2 m = new Vector2(startX, B.center.y);
        Vector2 t = new Vector2(startX, B.max.y - 0.20f);
        Debug.DrawLine(a, a + Vector2.right * dir * (wallCheckDist * rayLengthScale), Color.red);
        Debug.DrawLine(m, m + Vector2.right * dir * (midWallCheckDist * rayLengthScale), Color.red);
        Debug.DrawLine(t, t + Vector2.right * dir * (topWallCheckDist * rayLengthScale), Color.red);
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos || body == null) return;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);

        Bounds b = body.bounds;
        float startX = b.center.x + dir * (b.extents.x + 0.02f);

        Vector3 a = new Vector3(startX, b.min.y + 0.20f, 0f);
        Vector3 m = new Vector3(startX, b.center.y, 0f);
        Vector3 t = new Vector3(startX, b.max.y - 0.20f, 0f);
        Gizmos.DrawLine(a, a + Vector3.right * dir * (wallCheckDist * rayLengthScale));
        Gizmos.DrawLine(m, m + Vector3.right * dir * (midWallCheckDist * rayLengthScale));
        Gizmos.DrawLine(t, t + Vector3.right * dir * (topWallCheckDist * rayLengthScale));

        Gizmos.color = Color.green;
        Vector3 gp = new Vector3(b.center.x, b.min.y + footRayYOffset, 0f);
        Gizmos.DrawLine(gp, gp + Vector3.down * (groundedCheckDist * rayLengthScale));

        Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
        float dashMaxForward = Mathf.Max(0.1f, dashSpeed * dashDuration) * 1.6f;
        Vector3 o = new Vector3(startX, b.center.y, 0f);
        Gizmos.DrawLine(o, o + Vector3.right * dir * dashMaxForward);
    }
}
