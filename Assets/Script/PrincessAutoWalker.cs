using UnityEngine;
using UnityEngine.SceneManagement; // ← 추가

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PrincessAutoWalker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] public Animator ani;

    [Header("Move Settings")]
    [SerializeField] private float moveSpeed = 4.0f;
    [SerializeField] private int startDirection = +1;      // +1=오른쪽, -1=왼쪽
    [SerializeField] private bool startMovingOnEnable = true;

    [Header("Step / Wall Check")]
    [Tooltip("한 칸 높이(타일=1이면 1.0)")]
    [SerializeField] private float stepHeight = 1.0f;
    [SerializeField] private float obstacleCheckDistance = 0.25f;
    [SerializeField] private float skin = 0.05f;
    [SerializeField] private LayerMask obstacleMask;        // Ground/Wall 등

    [Header("Ground Check")]
    [SerializeField] private float groundCheckRadius = 0.08f;
    [SerializeField] private float groundCheckOffsetY = 0.02f;

    [Header("Step Hop (포물선 점프)")]
    [SerializeField] private float stepJumpYSpeed = 5.0f;   // Y로 '높게'
    [SerializeField] private float stepHopXDrift = 2.0f;    // X로 '살짝'
    [SerializeField] private float stepHopMaxDuration = 0.30f; // 이 시간 동안 전방 벽 간섭 무시
    [SerializeField] private float stepJumpCooldown = 0.10f;
    [SerializeField] private bool requireGroundedForStepJump = true;

    [Header("Speed Modifiers (Tag)")]
    [SerializeField] private float slowMoveSpeed = 1.0f;    // SlowRun 태그 위 속도
    [SerializeField] private float fastMoveSpeed = 6.0f;    // FastRun 태그 위 속도

    [Header("Knight Ignore")]
    [SerializeField] private string knightTag = "Player";   // 기사(플레이어)와 충돌 무시

    [Header("Trap Reload")] // ← 추가
    [SerializeField] private string trapLayerName = "Trap"; // 레이어 이름 기반
    private int trapLayerIndex = -1;
    private bool isReloading = false;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // 내부 상태
    private int dirSign;                 // +1 or -1
    private bool isMoving = true;        // 벽으로 멈춘 경우 false
    private bool pausedByFall;           // 낙하 중 일시 정지
    private float jumpLockTimer;
    private bool stepHopActive;
    private float stepHopTimer;

    void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        ani = GetComponent<Animator>();
        obstacleMask = LayerMask.GetMask("Ground");
    }

    void OnValidate()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!body) body = GetComponent<Collider2D>();

        stepHeight = Mathf.Max(0.1f, stepHeight);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        obstacleCheckDistance = Mathf.Max(0.05f, obstacleCheckDistance);
        groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
        stepJumpCooldown = Mathf.Max(0f, stepJumpCooldown);
        stepHopMaxDuration = Mathf.Max(0.05f, stepHopMaxDuration);
        startDirection = Mathf.Clamp(startDirection, -1, +1);
        if (startDirection == 0) startDirection = +1;

        ResolveTrapLayerIndex(); // ← 추가
    }

    void Awake()
    {
        dirSign = Mathf.Sign(startDirection) >= 0 ? +1 : -1;
        SetupIgnoreCollisionWithKnights();
        ResolveTrapLayerIndex(); // ← 추가
    }

    void OnEnable()
    {
        isMoving = startMovingOnEnable;
        pausedByFall = false;
        jumpLockTimer = 0f;
        stepHopActive = false;
        stepHopTimer = 0f;
        isReloading = false; // ← 추가

        ApplyRootFlip(dirSign); // 초기 방향 반영
    }

    void FixedUpdate()
    {
        jumpLockTimer = Mathf.Max(0f, jumpLockTimer - Time.fixedDeltaTime);

        bool grounded = IsGrounded();
        bool falling = !grounded && rb.linearVelocity.y < -0.01f;

        // 지면 태그 검사로 방향/속도 조정은 '지면 위'에서만
        float currentSpeed = moveSpeed;
        if (grounded)
        {
            EvaluateFloorTags(ref currentSpeed, out bool foundDirTile, out bool directionChanged);
            ani.SetBool("jump", false);
            ani.SetBool("run", true);
            // 벽에 막혀 멈춰도 방향 타일 밟으면 재출발
            if (!isMoving && foundDirTile) isMoving = true;

            if (directionChanged) ApplyRootFlip(dirSign); // 진행방향 바뀌면 루트 반전
        }

        // 낙하 중에는 X 정지(규칙 7)
        if (falling)
        {
            ani.SetBool("run", false);
            ani.SetBool("jump", true);
            pausedByFall = true;
            stepHopActive = false; // 낙하 진입 시 홉 종료
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (grounded && pausedByFall)
        {
            pausedByFall = false;
            isMoving = true;
        }

        // 전방 상태 선검사
        GetFrontBlockInfo(out bool feetBlocked, out bool topBlocked);

        // 스텝 홉 활성 처리
        if (stepHopActive)
        {
            stepHopTimer -= Time.fixedDeltaTime;

            if (rb.linearVelocity.y > 0.001f)
            {
                rb.linearVelocity = new Vector2(stepHopXDrift * dirSign, rb.linearVelocity.y);
            }

            if (grounded || rb.linearVelocity.y <= 0f || stepHopTimer <= 0f)
            {
                stepHopActive = false;
            }

            if (stepHopActive) return;
        }

        // 정지 상태 자동 재개
        if (!isMoving)
        {
            ani.SetBool("run", false);
            if (!feetBlocked)
            {
                isMoving = true;
            }
            else if (!topBlocked && (jumpLockTimer <= 0f) && (!requireGroundedForStepJump || grounded))
            {
                rb.linearVelocity = new Vector2(stepHopXDrift * dirSign, stepJumpYSpeed);
                stepHopActive = true;
                stepHopTimer = stepHopMaxDuration;
                jumpLockTimer = stepJumpCooldown;
                isMoving = true;
                return;
            }
            else
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }
        }

        // 평소 전진 및 한 칸 승월
        if (feetBlocked)
        {
            bool canStepJump = !topBlocked
                               && (jumpLockTimer <= 0f)
                               && (!requireGroundedForStepJump || grounded);

            if (canStepJump)
            {
                ani.SetBool("jump", true);
                rb.linearVelocity = new Vector2(stepHopXDrift * dirSign, stepJumpYSpeed);
                stepHopActive = true;
                stepHopTimer = stepHopMaxDuration;
                jumpLockTimer = stepJumpCooldown;
                return;
            }
            else
            {
                ani.SetBool("run", false);
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                if (topBlocked) isMoving = false; // 높은 벽 → 정지
                return;
            }
        }

        // 전진
        rb.linearVelocity = new Vector2(currentSpeed * dirSign, rb.linearVelocity.y);
    }

    /// <summary>외부에서 방향 재지정 및 재출발</summary>
    public void SetDirectionAndGo(int sign)
    {
        int newSign = (sign >= 0) ? +1 : -1;
        if (newSign != dirSign)
        {
            dirSign = newSign;
            ApplyRootFlip(dirSign); // 루트 반전
        }
        isMoving = true;
        pausedByFall = false;
    }

    private bool IsGrounded()
    {
        Bounds b = body.bounds;
        Vector2 p = new Vector2(b.center.x, b.min.y + groundCheckOffsetY);
        return Physics2D.OverlapCircle(p, groundCheckRadius, obstacleMask) != null;
    }

    private void GetFrontBlockInfo(out bool feetBlocked, out bool topBlocked)
    {
        Bounds b = body.bounds;
        float s = dirSign;
        Vector2 feet = new Vector2(b.center.x, b.min.y + skin);
        Vector2 top = feet + Vector2.up * (stepHeight - skin);

        feetBlocked = Physics2D.Raycast(feet, Vector2.right * s, obstacleCheckDistance + skin, obstacleMask);
        topBlocked = Physics2D.Raycast(top, Vector2.right * s, obstacleCheckDistance + skin, obstacleMask);
    }

    /// <summary>
    /// 바닥 태그(LeftGo/RightGo/SlowRun/FastRun)를 읽어
    /// - 방향(dirSign) 및 속도(currentSpeed) 결정
    /// - directionTileFound: 방향 타일 감지
    /// - directionChanged: 이번 프레임에 실제 방향 변동
    /// </summary>
    private void EvaluateFloorTags(ref float currentSpeed, out bool directionTileFound, out bool directionChanged)
    {
        directionTileFound = false;
        directionChanged = false;

        Bounds b = body.bounds;
        Vector2 p = new Vector2(b.center.x, b.min.y + groundCheckOffsetY);
        var hits = Physics2D.OverlapCircleAll(p, groundCheckRadius * 1.1f);

        bool sawLeft = false, sawRight = false;
        bool sawSlow = false, sawFast = false;

        foreach (var h in hits)
        {
            if (!h) continue;
            var go = h.gameObject;

            if (go.CompareTag("LeftGo")) sawLeft = true;
            if (go.CompareTag("RightGo")) sawRight = true;

            if (go.CompareTag("SlowRun")) sawSlow = true;
            if (go.CompareTag("FastRun")) sawFast = true;
        }

        int oldSign = dirSign;

        if (sawLeft ^ sawRight)
        {
            dirSign = sawRight ? +1 : -1;
            directionTileFound = true;
        }

        if (sawSlow) currentSpeed = slowMoveSpeed;
        else if (sawFast) currentSpeed = fastMoveSpeed;
        else currentSpeed = moveSpeed;

        directionChanged = (dirSign != oldSign);
    }

    private void SetupIgnoreCollisionWithKnights()
    {
        var knights = GameObject.FindGameObjectsWithTag(knightTag);
        if (knights == null || knights.Length == 0) return;

        var myCols = GetComponentsInChildren<Collider2D>(true);
        foreach (var k in knights)
        {
            foreach (var kc in k.GetComponentsInChildren<Collider2D>(true))
            {
                foreach (var mc in myCols)
                {
                    if (kc && mc) Physics2D.IgnoreCollision(mc, kc, true);
                }
            }
        }
    }

    /// <summary>루트 오브젝트 자체를 좌/우 반전</summary>
    private void ApplyRootFlip(int sign)
    {
        bool faceRight = sign >= 0;
        var t = transform;
        Vector3 s = t.localScale;
        float absx = Mathf.Abs(s.x);
        s.x = faceRight ? absx : -absx;
        t.localScale = s;
    }

    // ==================== Trap 충돌 → 씬 리로드 (추가) ====================

    private void ResolveTrapLayerIndex()
    {
        trapLayerIndex = LayerMask.NameToLayer(trapLayerName);
        if (trapLayerIndex < 0)
            Debug.LogWarning($"[Princess] Trap layer '{trapLayerName}' not found. 레이어 이름을 확인하세요.");
    }

    private bool IsTrap(GameObject go) => trapLayerIndex >= 0 && go.layer == trapLayerIndex;

    private void ReloadSceneOnce()
    {
        if (isReloading) return;
        isReloading = true;
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (IsTrap(c.collider.gameObject)) ReloadSceneOnce();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsTrap(other.gameObject)) ReloadSceneOnce();
    }

    // (선택) 겹친 상태에서 시작했을 때도 안전하게 처리하고 싶다면 주석 해제:
    // void OnCollisionStay2D(Collision2D c) { if (IsTrap(c.collider.gameObject)) ReloadSceneOnce(); }
    // void OnTriggerStay2D(Collider2D other){ if (IsTrap(other.gameObject)) ReloadSceneOnce(); }

    // ====================================================================

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        var c = GetComponent<Collider2D>();
        if (!c) return;

        var b = c.bounds;
        int s = Mathf.Sign(startDirection) >= 0 ? +1 : -1;
        Vector2 feet = new Vector2(b.center.x, b.min.y + skin);
        Vector2 top = feet + Vector2.up * (Mathf.Max(0.1f, stepHeight - skin));

        Gizmos.color = Color.red;
        Gizmos.DrawLine(feet, feet + Vector2.right * s * (obstacleCheckDistance + skin));
        Gizmos.color = Color.green;
        Gizmos.DrawLine(top, top + Vector2.right * s * (obstacleCheckDistance + skin));

        Gizmos.color = Color.yellow;
        Vector2 gp = new Vector2(b.center.x, b.min.y + groundCheckOffsetY);
        Gizmos.DrawWireSphere(gp, groundCheckRadius);
    }
}
