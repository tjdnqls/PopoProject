using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class PoPoFSM : MonoBehaviour
{
    public enum State { Idle, LookAt, JumpPrep, Jumping, LandAttack, Recover, Dead }

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Animator animator;                 // optional
    [SerializeField] private SpriteAnimationManager spriteAnim; // optional (프로젝트에 있으면 연결)
    [SerializeField] private Transform feet;
    [SerializeField] private GameObject attackHitbox;           // 반드시 Trigger Collider

    [Header("Anim Names")]
    [SerializeField] private string idleAnim = "Idle";
    [SerializeField] private string lookAnim = "Idle";
    [SerializeField] private string jumpAnim = "Jump";
    [SerializeField] private string attackAnim = "Attack";

    [Header("Layers/Tags")]
    [SerializeField] private string groundLayerName = "Ground";
    [SerializeField] private string wallLayerName = "Ground";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask playerMask;

    [Header("Detect/Timing")]
    [SerializeField] private float detectRadius = 6.0f;   // 플레이어 발견 반경
    [SerializeField] private float lookAtSeconds = 1.0f;  // 응시 시간
    [SerializeField] private float jumpPrepSeconds = 0.08f;
    [SerializeField] private float landAttackSeconds = 0.22f;
    [SerializeField] private float recoverSeconds = 0.5f;

    [Header("Jump Constraints")]
    [Tooltip("한 번의 점프로 허용하는 최대 수평 이동거리")]
    [SerializeField] private float maxJumpDistanceX = 7.0f;
    [Tooltip("시작점 대비 아펙스의 최대 상승 높이")]
    [SerializeField] private float maxJumpHeight = 3.2f;

    [Header("Jump Solver")]
    [Tooltip("T 추정용 목표 수평 속도")]
    [SerializeField] private float desiredHorizSpeed = 6.0f;
    [SerializeField] private float minFlightTime = 0.35f;
    [SerializeField] private float maxFlightTime = 0.9f;
    [Tooltip("초기 속도 상한")]
    [SerializeField] private float maxLaunchSpeed = 14.0f;
    [Tooltip("항상 위로 뜨게 최소 초기 상승속도")]
    [SerializeField] private float minUpVelocity = 3.5f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckRadius = 0.09f;

    [Header("Landing Gate")]
    [Tooltip("이륙 후 최소 체공 시간. 이 시간 전의 지면 접촉은 무시")]
    [SerializeField] private float minAirborneBeforeLand = 0.06f;
    [Tooltip("이륙이 지연될 때 위로 보정하는 최대 대기 시간")]
    [SerializeField] private float takeoffTimeout = 0.2f;

    [Header("Stay Still On Land")]
    [SerializeField] private bool freezeXDuringAttackAndRecover = true;

    [Header("Overlap Fix While Jumping")]
    [Tooltip("점프 중 겹침 보정 반복 최대 횟수(프레임당)")]
    [SerializeField] private int maxDepenIterations = 3;
    [Tooltip("겹침 분리 시 추가 여유 거리")]
    [SerializeField] private float depenPadding = 0.01f;

    [Header("Collision Toggle Mode")]
    [Tooltip("true면 레이어 스왑 방식(권장), false면 전역 IgnoreLayerCollision 사용")]
    [SerializeField] private bool useLayerSwap = true;
    [Tooltip("평상시 레이어 이름(플레이어와 절대 같은 레이어 금지)")]
    [SerializeField] private string popoLayerName = "PoPo";
    [Tooltip("상승 중(충돌 비활성화) 레이어 이름(Ground/Wall과 충돌 꺼진 레이어)")]
    [SerializeField] private string popoJumpLayerName = "PoPoJump";

    [Header("Landing Warning")]
    [Tooltip("착지 지점 경고 프리팹(빨간 박스). 자동 파괴")]
    [SerializeField] private GameObject landingWarnPrefab;
    [SerializeField] private float landingWarnSeconds = 0.6f;
    [SerializeField] private Vector2 landingWarnSize = new Vector2(0.35f, 0.18f);
    [Tooltip("표면에서 띄울 높이(법선 방향 오프셋)")]
    [SerializeField] private float landingWarnSnapUp = 0.02f;
    [Tooltip("보조용 위→아래 스냅 탐색 높이")]
    [SerializeField] private float landingWarnProbeUp = 3.0f;

    [Header("Ground Thickness Filter (tile)")]
    [Tooltip("한 타일의 픽셀 크기(예: 16, 32)")]
    [SerializeField] private int tilePixels = 16;
    [Tooltip("경고 박스를 띄우려면 바닥 두께가 최소 몇 타일이어야 하는지(요청: 2칸)")]
    [SerializeField] private int minGroundTiles = 2;
    [Tooltip("씬의 Pixels Per Unit (보통 타일 픽셀과 동일 값)")]
    [SerializeField] private float pixelsPerUnit = 16f;

    [Header("Misc")]
    [SerializeField] private Transform player; // 비우면 런타임에 자동 탐색
    [SerializeField] private bool faceFlipByScale = true;
    [SerializeField] private float faceFlipThreshold = 0.01f;
    [SerializeField] private bool drawGizmos = true;

    // Internal
    private State state = State.Idle;
    private float originalGravityScale;
    private bool collisionsIgnored;     // 상승 중 충돌 무시 상태
    private int selfLayer;
    private int popoLayer, popoJumpLayer;
    private ContactFilter2D depenFilter;
    private readonly Collider2D[] overlapBuf = new Collider2D[12];

    private GameObject landingWarnInstance; // 경고 박스 재사용

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        feet = transform;
    }

    private void OnValidate()
    {
        if (groundMask.value == 0 && !string.IsNullOrEmpty(groundLayerName))
            groundMask = LayerMask.GetMask(groundLayerName);
        if (wallMask.value == 0 && !string.IsNullOrEmpty(wallLayerName))
            wallMask = LayerMask.GetMask(wallLayerName);
        if (playerMask.value == 0)
            playerMask = LayerMask.GetMask("Player");
    }

    private void Awake()
    {
        originalGravityScale = rb.gravityScale;
        selfLayer = gameObject.layer;

        popoLayer = LayerMask.NameToLayer(popoLayerName);
        popoJumpLayer = LayerMask.NameToLayer(popoJumpLayerName);
        if (useLayerSwap && (popoLayer < 0 || popoJumpLayer < 0))
        {
            useLayerSwap = false; // 준비 안 되어 있으면 전역 토글로 폴백
        }

        depenFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundMask | wallMask,
            useTriggers = false
        };

        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag(playerTag);
            if (found != null) player = found.transform;
        }

        // 안전장치: 공격 히트박스는 반드시 Trigger
        if (attackHitbox != null)
        {
            var col = attackHitbox.GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }
    }

    private void OnEnable()
    {
        SwitchState(State.Idle);
    }

    private void OnDisable()
    {
        // 혹시 남아있을 수 있는 충돌 비활성화 상태 복구
        if (useLayerSwap) gameObject.layer = popoLayer >= 0 ? popoLayer : selfLayer;
        else SetIgnoreGroundWall(false);
    }

    private void SwitchState(State next)
    {
        state = next;
        switch (state)
        {
            case State.Idle: PlatAnime(idleAnim); break;
            case State.LookAt: PlatAnime(lookAnim); break;
            case State.JumpPrep: PlatAnime(jumpAnim); break;
            case State.Jumping: PlatAnime(jumpAnim); break;
            case State.LandAttack: PlatAnime(attackAnim); break;
            case State.Recover: PlatAnime(idleAnim); break;
        }
    }

    private void Update()
    {
        if (state == State.Dead) return;

        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag(playerTag);
            if (found != null) player = found.transform;
        }

        switch (state)
        {
            case State.Idle:
                TickIdle();
                break;
            case State.LookAt:
                FaceTo(player != null ? (player.position.x - transform.position.x) : 0f);
                break;
            case State.Jumping:
                // 하강 진입하면 충돌 복구
                if (collisionsIgnored && rb.linearVelocity.y <= 0f)
                    ToggleAscentCollision(false);

                // 혹시 겹치면 위치 보정
                ResolvePenetrationWhileJumping();
                break;
        }
    }

    private void TickIdle()
    {
        if (player == null) return;
        if (Vector2.SqrMagnitude(player.position - transform.position) <= detectRadius * detectRadius)
        {
            StartCoroutine(CoLookThenJump());
        }
    }

    private IEnumerator CoLookThenJump()
    {
        SwitchState(State.LookAt);

        // 1) 1초 응시
        float t = lookAtSeconds;
        while (t > 0f)
        {
            if (player != null) FaceTo(player.position.x - transform.position.x);
            t -= Time.deltaTime;
            yield return null;
        }

        // 2) 점프 준비 + (미리) 착지 경고 박스 표시
        SwitchState(State.JumpPrep);

        // 목표: 플레이어 겨냥(수평거리/점프높이 제한 준수)
        Vector2 start = transform.position;
        Vector2 desired = player != null ? (Vector2)player.position : start + Vector2.right;

        float dx = Mathf.Clamp(desired.x - start.x, -maxJumpDistanceX, maxJumpDistanceX);
        Vector2 target = new Vector2(start.x + dx, desired.y);

        float g = Physics2D.gravity.y * rb.gravityScale; // negative
        float vyCap = Mathf.Sqrt(Mathf.Max(0.0001f, 2f * -g * maxJumpHeight));

        Vector2 v0;
        if (!ComputeLaunchVelocityCapped(start, target, desiredHorizSpeed, minFlightTime, maxFlightTime, g, vyCap, maxLaunchSpeed, out v0))
        {
            // 도달 불가면 가능한 y로 보정
            float T = Mathf.Clamp(Mathf.Abs(dx) / Mathf.Max(0.01f, desiredHorizSpeed), minFlightTime, maxFlightTime);
            float dyReachable = vyCap * T + 0.5f * g * T * T; // g<0
            target.y = start.y + dyReachable;
            ComputeLaunchVelocityCapped(start, target, desiredHorizSpeed, minFlightTime, maxFlightTime, g, vyCap, maxLaunchSpeed, out v0);
        }
        v0 = Vector2.ClampMagnitude(v0, maxLaunchSpeed);

        // ★ 사이드뷰용: 라인캐스트로 첫 충돌점 계산 → 표면 위로 스냅 (플레이어 무시 + 최소 바닥 두께 필터)
        Vector2 predictedLanding = PredictLandingPointOnGround_Filtered(
            start, v0, rb.gravityScale, groundMask | wallMask, Mathf.Max(0.25f, maxFlightTime + 0.3f)
        );
        float warnLife = Mathf.Max(landingWarnSeconds, jumpPrepSeconds + 0.15f);
        SpawnLandingWarn(predictedLanding, warnLife);

        // 점프 준비 연출
        yield return new WaitForSeconds(jumpPrepSeconds);

        // 3) 발사
        SwitchState(State.Jumping);
        rb.gravityScale = originalGravityScale;
        rb.linearVelocity = v0;

        // 상승 동안 Ground/Wall 충돌 비활성화
        ToggleAscentCollision(true);

        // 4) 착지 게이트: 반드시 공중을 떠난 뒤 + 하강 상태에서만 착지 처리
        float launchTime = Time.time;
        bool leftGround = false;

        yield return null; // 한 프레임 넘겨 속도 적용

        while (state == State.Jumping)
        {
            bool groundedNow = IsGrounded();

            if (!leftGround)
            {
                if (!groundedNow) leftGround = true;
                else if (Time.time - launchTime > takeoffTimeout)
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(rb.linearVelocity.y, minUpVelocity));
            }
            else
            {
                if (groundedNow && rb.linearVelocity.y <= 0f && Time.time - launchTime > minAirborneBeforeLand)
                {
                    ToggleAscentCollision(false);
                    StartCoroutine(CoLandAttackThenRecover());
                    yield break;
                }
            }

            yield return null;
        }
    }

    private IEnumerator CoLandAttackThenRecover()
    {
        SwitchState(State.LandAttack);

        // 제자리 유지
        rb.linearVelocity = Vector2.zero;
        var prevConstraints = rb.constraints;
        if (freezeXDuringAttackAndRecover)
            rb.constraints = prevConstraints | RigidbodyConstraints2D.FreezePositionX;

        if (attackHitbox != null) attackHitbox.SetActive(true);
        yield return new WaitForSeconds(landAttackSeconds);
        if (attackHitbox != null) attackHitbox.SetActive(false);

        SwitchState(State.Recover);
        rb.linearVelocity = Vector2.zero;
        yield return new WaitForSeconds(recoverSeconds);

        if (freezeXDuringAttackAndRecover)
            rb.constraints = prevConstraints;

        StartCoroutine(CoLookThenJump());
    }

    // ===== Collision/Overlap helpers =====

    private void ToggleAscentCollision(bool ignore)
    {
        if (collisionsIgnored == ignore) return;
        collisionsIgnored = ignore;

        if (useLayerSwap)
        {
            // 레이어 스왑: Player와 다른 전용 레이어를 쓰세요.
            if (ignore) gameObject.layer = (popoJumpLayer >= 0 ? popoJumpLayer : selfLayer);
            else gameObject.layer = (popoLayer >= 0 ? popoLayer : selfLayer);
        }
        else
        {
            SetIgnoreGroundWall(ignore); // 전역 매트릭스 토글(플레이어와 같은 레이어면 사용 금지)
        }
    }

    // 전역 IgnoreLayerCollision 방식 (여러 PoPo가 있으면 레이어 스왑을 권장)
    private void SetIgnoreGroundWall(bool ignore)
    {
        int mask = groundMask.value | wallMask.value;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) != 0)
                Physics2D.IgnoreLayerCollision(gameObject.layer, layer, ignore);
        }
    }

    // 점프 중 겹침 보정(벽/바닥 사이에 끼는 케이스 방지)
    private void ResolvePenetrationWhileJumping()
    {
        if (body == null) return;

        int count = body.Overlap(depenFilter, overlapBuf);
        if (count <= 0) return;

        int iterations = Mathf.Min(count, maxDepenIterations);
        for (int i = 0; i < iterations; i++)
        {
            var other = overlapBuf[i];
            if (other == null) continue;

            ColliderDistance2D dist = Physics2D.Distance(body, other);
            if (dist.isOverlapped)
            {
                float push = -dist.distance + depenPadding;
                Vector2 delta = dist.normal * push;
                transform.position += (Vector3)delta;
            }
        }
    }

    // ===== Landing warn =====
    private void SpawnLandingWarn(Vector2 pos, float lifeSeconds)
    {
        if (landingWarnPrefab == null) return;

        if (landingWarnInstance != null)
        {
            landingWarnInstance.transform.position = pos;
            return;
        }

        landingWarnInstance = Instantiate(landingWarnPrefab, pos, Quaternion.identity);
        var srWarn = landingWarnInstance.GetComponent<SpriteRenderer>();
        if (srWarn != null)
        {
            // ★ 프리팹 알파(투명도) 보존: 빨강 틴트만 적용하고 알파는 기존 값 유지
            var c = srWarn.color;                    // 프리팹에 설정된 색/알파
            srWarn.color = new Color(1f, 0f, 0f, c.a);
            srWarn.sortingOrder = 9999;             // 최상단 표시
        }
        landingWarnInstance.transform.localScale = new Vector3(landingWarnSize.x, landingWarnSize.y, 1f);

        Destroy(landingWarnInstance, lifeSeconds);
        StartCoroutine(ClearWarnRefAfter(lifeSeconds));
    }

    private IEnumerator ClearWarnRefAfter(float t)
    {
        yield return new WaitForSeconds(t + 0.01f);
        landingWarnInstance = null;
    }

    // Player를 무시하고, '최소 2칸' 두께의 Ground/Wall에서만 착지 지점을 스냅해서 반환
    private Vector2 PredictLandingPointOnGround_Filtered(Vector2 start, Vector2 v0, float gravityScale,
                                                         LayerMask hitMask, float maxSecs)
    {
        float g = Physics2D.gravity.y * gravityScale;
        Vector2 pos = start;
        Vector2 vel = v0;

        int steps = 40;
        float dt = Mathf.Max(0.008f, maxSecs / steps);

        for (int i = 0; i < steps; i++)
        {
            Vector2 nextPos = pos + vel * dt + 0.5f * new Vector2(0f, g) * dt * dt;

            // Player를 무시하기 위해 LinecastAll 후 필터링
            var hits = Physics2D.LinecastAll(pos, nextPos, hitMask);
            if (TryPickValidGround(hits, out RaycastHit2D valid))
            {
                Vector2 n = valid.normal.normalized;
                float up = Mathf.Max(groundCheckRadius, landingWarnSnapUp);
                return valid.point + n * (up + 0.001f);
            }

            pos = nextPos;
            vel.y += g * dt;
        }

        // 보조: 예측 X 근처에서 위→아래 캐스트(All) 후 유효 바닥만 선택
        Vector2 probeStart = pos + Vector2.up * landingWarnProbeUp;
        var downs = Physics2D.RaycastAll(probeStart, Vector2.down, landingWarnProbeUp * 2f, hitMask);
        if (TryPickValidGround(downs, out RaycastHit2D groundHit))
        {
            float up = Mathf.Max(groundCheckRadius, landingWarnSnapUp);
            return groundHit.point + Vector2.up * (up + 0.001f);
        }

        return pos;
    }

    private bool TryPickValidGround(RaycastHit2D[] hits, out RaycastHit2D valid)
    {
        valid = default;
        if (hits == null || hits.Length == 0) return false;

        float minThickWorld = MinGroundThicknessWorld();

        // 가장 가까운 것부터 순회
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;

            // Player 무시 (레이어/태그 양쪽으로 필터)
            int layer = h.collider.gameObject.layer;
            if ((playerMask.value & (1 << layer)) != 0) continue;
            if (h.collider.CompareTag(playerTag)) continue;

            // Ground/Wall만 허용
            if (((groundMask.value | wallMask.value) & (1 << layer)) == 0) continue;

            // '최소 2칸' 두께 필터
            float thick = h.collider.bounds.size.y;
            if (thick + 1e-4f < minThickWorld) continue;

            valid = h;
            return true;
        }
        return false;
    }

    private float MinGroundThicknessWorld()
    {
        // (최소 타일 수 × 타일 픽셀) / PPU
        float px = Mathf.Max(1, tilePixels) * Mathf.Max(1, minGroundTiles);
        return px / Mathf.Max(1f, pixelsPerUnit);
    }

    // ===== Utilities =====

    private bool IsGrounded()
    {
        if (feet == null) feet = transform;
        return Physics2D.OverlapCircle((Vector2)feet.position, groundCheckRadius, groundMask) != null;
    }

    private void FaceTo(float dirX)
    {
        if (!faceFlipByScale) return;
        if (Mathf.Abs(dirX) < faceFlipThreshold) return;
        var s = transform.localScale;
        s.x = dirX >= 0 ? Mathf.Abs(s.x) : -Mathf.Abs(s.x);
        transform.localScale = s;
    }

    // 제한(아펙스 높이, 최대 속도)을 고려해 발사 속도 계산
    private bool ComputeLaunchVelocityCapped(
        Vector2 from, Vector2 to, float horizSpeed, float minT, float maxT,
        float g, float vyCap, float speedCap, out Vector2 v0)
    {
        Vector2 d = to - from;
        float dx = d.x;
        float dy = d.y;

        float T = Mathf.Clamp(Mathf.Abs(dx) / Mathf.Max(0.01f, horizSpeed), minT, maxT);

        for (int i = 0; i < 12; i++)
        {
            float vx = dx / T;
            float vy = (dy - 0.5f * g * T * T) / T;

            float sp = Mathf.Sqrt(vx * vx + vy * vy);

            if (vy <= vyCap && sp <= speedCap)
            {
                vy = Mathf.Max(vy, minUpVelocity); // 항상 위로 뜨게
                v0 = new Vector2(vx, vy);
                return true;
            }

            float nextT = Mathf.Min(maxT, T * 1.2f);
            if (Mathf.Approximately(nextT, T)) break;
            T = nextT;
        }

        v0 = Vector2.zero;
        return false;
    }

    // === Anim Helpers ===
    private void PlatAnime(string stateName, float normalizedTime = 0f, float fadeSeconds = 0f)
    {
        if (string.IsNullOrEmpty(stateName)) return;

        if (animator != null)
        {
            if (fadeSeconds > 0f)
                animator.CrossFadeInFixedTime(stateName, fadeSeconds, 0, normalizedTime);
            else
                animator.Play(stateName, 0, normalizedTime);
        }
        else if (spriteAnim != null)
        {
            spriteAnim.Play(stateName);
        }
    }

    // ===== Gizmos =====
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectRadius);

        if (feet != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(feet.position, groundCheckRadius);
        }
    }
}
