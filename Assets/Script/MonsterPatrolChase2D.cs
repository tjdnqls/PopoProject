// ===================== MonsterABPatrolFSM.cs =====================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MonsterABPatrolFSM : MonoBehaviour, IDamageable
{
    public enum State { Patrol, Alert, Chase, AttackWindup, Return }

    [Header("Animation")]
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;

    [Header("Stop-All on Tag Hit")]
    [SerializeField] private string stopOnTag = "Monkill";
    [SerializeField] private bool freezeRigidbodyOnStop = true;   // FreezeAll로 완전 고정
    [SerializeField] private bool disableComponentOnStop = true;  // 이 스크립트 비활성화
    private bool isStoppedByTag = false;

    [Header("Waypoints (A <-> B 왕복)")]
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private Vector2 fallbackLocalA = new Vector2(-3f, 0f);
    [SerializeField] private Vector2 fallbackLocalB = new Vector2(3f, 0f);
    [SerializeField] private float arriveEps = 0.08f;

    [Header("Move")]
    [SerializeField] private float patrolSpeed = 2.4f;
    [SerializeField] private float chaseSpeed = 3.8f;
    [SerializeField] private float accel = 25f;

    [Header("Ground / Obstacle Layers")]
    [SerializeField] private LayerMask groundMask;   // 바닥
    [SerializeField] private LayerMask obstacleMask; // 벽/턱 등
    [SerializeField] private LayerMask playerMask;   // 플레이어(핑/충돌 구분용)

    [Header("Ray Probes")]
    [SerializeField] private float wallCheckDist = 0.30f; // 정면벽
    [SerializeField] private float lowWallCheckDist = 0.25f; // 하단앞벽(턱)
    [SerializeField] private float ledgeForward = 0.25f; // 발끝 전방
    [SerializeField] private float ledgeDownDist = 0.60f; // 낙하 체크 깊이
    [SerializeField] private float feetYOffset = 0.05f; // 발 위치 y보정
    [SerializeField] private float lowWallYOffset = 0.10f; // 하단앞벽 레이 y

    [Header("Melee Hitbox (child pulse)")]
    [SerializeField] private GameObject meleeHitbox;   // 자식 히트박스 오브젝트(콜라이더 포함)
    [SerializeField] private float meleeActiveSeconds = 0.2f; // 활성 시간
    [SerializeField] private bool useHitboxDamage = true;     // true면 히트박스 방식으로만 데미지


    [Header("Aggro Detection (by Ping)")]
    [Tooltip("어그로 후보를 모을 반경(핑 발사 트리거)")]
    [SerializeField] private float pingScanRadius = 6f;
    [Tooltip("핑 프리팹(반드시 Rigidbody2D + Trigger Collider2D 포함)")]
    [SerializeField] private AggroPing2D pingPrefab;
    [Tooltip("핑 발사 위치(없으면 눈 위치에서 발사)")]
    [SerializeField] private Transform pingMuzzle;
    [Tooltip("같은 타겟으로 핑을 재발사하기 전 최소 쿨타임")]
    [SerializeField] private float pingCooldownPerTarget = 0.5f;
    [Tooltip("한 번에 발사할 수 있는 핑의 최대 수(스팸 방지)")]
    [SerializeField] private int maxConcurrentPings = 4;

    [Header("Ping Flight")]
    [SerializeField] private float pingSpeed = 18f;
    [SerializeField] private float pingLifetime = 0.8f;
    [Tooltip("핑의 진행 방향을 약간 예측 보정(0=없음)")]
    [SerializeField] private float pingLeadFactor = 0f;

    [Header("Alert / Chase / Return")]
    [SerializeField] private float alertStopSec = 0.5f; // 5) 잠시 멈춤
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Vector2 exclamationOffset = new Vector2(0f, 1.2f);
    [Tooltip("추격이 이 시간 이상 지속되면 종료 후 원래 자리로 복귀")]
    [SerializeField] private float maxChaseSeconds = 5f;

    [Header("Attack (7~8)")]
    [SerializeField] private float attackRange = 1.2f; // (디버그 원 표시용; 실제 판정은 EdgeDistance 사용)
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackWindupSec = 1.0f;   // 1초 점점 붉어짐
    [SerializeField] private float attackRecoverSec = 0.2f;  // 후딜
    [SerializeField] private Color attackColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Attack Tuning (Edge Distance)")]
    [Tooltip("콜라이더-간 가장자리 거리로 본 사거리")]
    [SerializeField] private float attackEdgeRange = 0.35f;
    [Tooltip("수직 위치 허용치(같은 발판 정도로 인정)")]
    [SerializeField] private float attackVerticalTolerance = 0.9f;

    [Header("Death")]
    [SerializeField] private float despawnDelay = 3f; // 피격 사망 후 3초 뒤 제거

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logPing = false;

    // 내부 상태
    private State state;
    private Vector2 wpA, wpB;
    private int patrolTargetIndex; // 0:A, 1:B
    private int dir;               // +1/-1
    private Vector2 homePos;
    private float chaseStartTime;
    private Color defaultColor;
    private Coroutine alertCo, attackCo;

    // 핑 관리
    private readonly Dictionary<int, float> _lastPingTime = new Dictionary<int, float>(); // key: target.GetInstanceID()
    private int _activePings = 0;

    // 탐지 버퍼
    private static readonly Collider2D[] _hits = new Collider2D[16];

    // 타겟
    private Transform currentTarget;

    // 기타
    private LayerMask blockingMask; // obstacle에서 player 제외한 벽 마스크
    private bool isDying = false;

    // ---------- Small helpers (safe animation & flip) ----------
    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        if (anim.IsOneShotActive) return;         // ★ 1회 재생 보호
        anim.Play(key, forceRestart);
    }
    private void PlayOnce(string key, string fallback = null, bool forceRestart = true)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        anim.PlayOnce(key, fallback, forceRestart);
    }
    private void SetFlipByDir(int d)
    {
        if (sr) sr.flipX = d < 0;
    }

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!body) body = GetComponent<Collider2D>();
        if (!sr) sr = GetComponentInChildren<SpriteRenderer>();
        defaultColor = sr ? sr.color : Color.white;

        rb.freezeRotation = true;
        homePos = transform.position;

        // obstacle에서 player를 제외 (플레이어를 벽으로 보지 않기)
        blockingMask = obstacleMask & ~playerMask;

        // 웨이포인트 확정
        wpA = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        wpB = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        // 시작 타깃: 가까운 포인트
        patrolTargetIndex = (Vector2.SqrMagnitude((Vector2)transform.position - wpA) <=
                             Vector2.SqrMagnitude((Vector2)transform.position - wpB)) ? 0 : 1;

        dir = ((GetPatrolTarget().x - transform.position.x) >= 0f) ? +1 : -1;
        state = State.Patrol;
    }

    private void OnEnable()
    {
        var v = rb.linearVelocity; v.x = dir * patrolSpeed; rb.linearVelocity = v;
        PlayAnim("Run", true); // 활성화 시 이동 시작 = Run
        SetFlipByDir(dir);
    }

    private void FixedUpdate()
    {
        if (isStoppedByTag || isDying)
        {
            StopHorizontal();
            return;
        }

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Alert: TickAlert(); break;
            case State.Chase: TickChase(); break;
            case State.AttackWindup: StopHorizontal(); break; // 애니는 코루틴에서 처리
            case State.Return: TickReturn(); break;
        }
    }

    // ============ Patrol ============
    private void TickPatrol()
    {
        PlayAnim("Run");
        Vector2 target = GetPatrolTarget();
        dir = (target.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(dir);

        MoveHorizontalTowards(dir * patrolSpeed);

        if (Mathf.Abs(target.x - transform.position.x) <= arriveEps)
        {
            TogglePatrolTarget();
        }
        else if (FrontWall(dir) || LowFrontWall(dir) || LedgeAhead(dir))
        {
            TogglePatrolTarget();
            dir = -dir;
            SetFlipByDir(dir);
            MoveHorizontalTowards(dir * patrolSpeed);
        }

        // 핑 기반 후보 스캔 & 발사
        ScanAndShootPings();
    }

    private void TickAlert()
    {
        StopHorizontal();
        PlayAnim("Idle"); // 플레이어 감지 후 잠깐 멈춤 = Idle
    }

    // ============ Chase ============
    private void TickChase()
    {
        PlayAnim("Run");

        if (!currentTarget)
        {
            EnterReturn(); return;
        }

        // 추격 시간 제한
        if (Time.time - chaseStartTime >= maxChaseSeconds)
        {
            EnterReturn(); return;
        }

        int chaseDir = (currentTarget.position.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(chaseDir);

        if (FrontWall(chaseDir) || LowFrontWall(chaseDir) || LedgeAhead(chaseDir))
            StopHorizontal();
        else
            MoveHorizontalTowards(chaseDir * chaseSpeed);

        // 공격 트리거 (콜라이더-간 거리 + 수직 허용)
        if (WithinAttackWindow(currentTarget))
            EnterAttack(currentTarget);
    }

    // ============ Attack ============
    private void EnterAttack(Transform target)
    {
        if (attackCo != null) StopCoroutine(attackCo);
        state = State.AttackWindup;
        attackCo = StartCoroutine(AttackRoutine(target));
    }

    private IEnumerator AttackRoutine(Transform snapshotTarget)
    {
        if (!sr) yield break;

        // 공격 준비 (점점 붉어짐)
        Color startC = sr.color; float t = 0f;
        PlayOnce("AttackStart");
        while (t < attackWindupSec)
        {
            t += Time.fixedDeltaTime;
            float a = Mathf.Clamp01(t / attackWindupSec);
            sr.color = Color.Lerp(startC, attackColor, a);
            yield return new WaitForFixedUpdate();
        }

        // === 공격 개시 ===
        if (sr) sr.color = defaultColor;
        PlayOnce("Attack", "Idle");

        if (useHitboxDamage && meleeHitbox)
        {
            // 히트박스 무장 & 활성 → 대기 → 비활성 (첫 적중만 유효)
            var hb = meleeHitbox.GetComponent<MeleeHitboxOnce>();
            if (hb) hb.Arm(attackDamage, transform);
            else meleeHitbox.SetActive(true);

            yield return new WaitForSeconds(meleeActiveSeconds);

            if (hb) hb.Disarm();
            else meleeHitbox.SetActive(false);
        }
        else
        {
            // 기존 단발 판정 유지가 필요하면 여기 사용
            if (snapshotTarget && WithinAttackWindow(snapshotTarget))
                ApplyDamage(snapshotTarget);
        }

        // 후딜
        float r = 0f;
        while (r < attackRecoverSec) { r += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }

        // 복구
        sr.color = startC;
        state = State.Chase;
    }

    // ============ Return ============
    private void TickReturn()
    {
        PlayAnim("Run");

        int retDir = (homePos.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(retDir);

        if (FrontWall(retDir) || LowFrontWall(retDir) || LedgeAhead(retDir))
            StopHorizontal();
        else
            MoveHorizontalTowards(retDir * patrolSpeed);

        if (Mathf.Abs(homePos.x - transform.position.x) <= arriveEps)
            KickstartPatrolLoop();
    }

    private void KickstartPatrolLoop()
    {
        int nearestIdx = (Vector2.SqrMagnitude((Vector2)transform.position - wpA) <=
                          Vector2.SqrMagnitude((Vector2)transform.position - wpB)) ? 0 : 1;
        int farIdx = (nearestIdx == 0) ? 1 : 0;

        patrolTargetIndex = farIdx;
        dir = (GetPatrolTarget().x > transform.position.x) ? +1 : -1;

        if (FrontWall(dir) || LowFrontWall(dir) || LedgeAhead(dir))
        {
            patrolTargetIndex = nearestIdx;
            dir = -dir;
        }

        rb.WakeUp();
        var v = rb.linearVelocity; v.x = dir * Mathf.Max(0.5f, patrolSpeed * 0.6f); rb.linearVelocity = v;
        SetFlipByDir(dir);
        state = State.Patrol;
        PlayAnim("Run", true);
    }

    // ============ State transitions ============
    private void EnterAlert()
    {
        state = State.Alert;
        StopHorizontal();
        PlayAnim("Idle", true);

        if (alertCo != null) StopCoroutine(alertCo);
        alertCo = StartCoroutine(AlertThenChase());
    }

    private IEnumerator AlertThenChase()
    {
        if (exclamationPrefab)
        {
            var go = Instantiate(exclamationPrefab, (Vector2)transform.position + exclamationOffset, Quaternion.identity, transform);
            Destroy(go, alertStopSec + 0.2f);
        }

        float t = 0f;
        while (t < alertStopSec) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        EnterChase();
    }

    private void EnterChase()
    {
        if (!currentTarget) { state = State.Return; return; }
        state = State.Chase;
        chaseStartTime = Time.time;
        PlayAnim("Run", true);
    }

    private void EnterReturn()
    {
        state = State.Return;
        StopHorizontal();
        currentTarget = null;
        PlayAnim("Run");
    }

    // ============ Ping-based Detection ============
    private void ScanAndShootPings()
    {
        if (!pingPrefab) return;
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerMask,
            useTriggers = true
        };

        int n = Physics2D.OverlapCircle((Vector2)transform.position, pingScanRadius, filter, _hits);
        if (n <= 0) return;

        for (int i = 0; i < n; i++)
        {
            if (_activePings >= maxConcurrentPings) break;

            var col = _hits[i];
            if (!col) continue;
            Transform cand = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform;
            if (!cand) continue;

            int key = cand.GetInstanceID();
            if (_lastPingTime.TryGetValue(key, out float last) && (Time.time - last) < pingCooldownPerTarget)
                continue;

            FirePing(cand);
            _lastPingTime[key] = Time.time;
            _activePings++;
        }
    }

    private void FirePing(Transform target)
    {
        Vector2 origin = pingMuzzle ? (Vector2)pingMuzzle.position : Eyes();
        Vector2 aim = TargetAimPoint(target);

        // 간단한 예측(선택)
        if (pingLeadFactor > 0f && target.TryGetComponent<Rigidbody2D>(out var trb))
        {
            Vector2 vel = trb.linearVelocity;
            aim += vel * pingLeadFactor;
        }

        var ping = Instantiate(pingPrefab, origin, Quaternion.identity);
        ping.Init(
            owner: this,
            target: target,
            speed: pingSpeed,
            lifetime: pingLifetime,
            groundMask: groundMask,
            playerMask: playerMask,
            obstacleMask: obstacleMask,
            onDespawn: () => { _activePings = Mathf.Max(0, _activePings - 1); }
        );

        if (logPing) Debug.Log($"[Monster] FirePing -> {target.name}");
    }

    // 핑이 플레이어에 명중했을 때 호출됨
    public void OnAggroPingHit(Transform hitPlayer)
    {
        if (!hitPlayer) return;
        currentTarget = hitPlayer; // 이 순간에만 어그로 확정
        EnterAlert();              // 5) 잠시 멈추고 느낌표 → 6) 추격
    }

    // ============ Damage / Death ============
    // 플레이어 공격이 이 몬스터를 때렸을 때 호출되길 원한다면
    // 1) IDamageable로 직접 호출되거나
    // 2) SendMessage("OnHit", damage) 로도 동작하도록 OnHit 제공
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (isDying) return;
        StartCoroutine(HitDeathRoutine());
    }

    public void OnHit(int damage) // SendMessage 호환
    {
        if (isDying) return;
        StartCoroutine(HitDeathRoutine());
    }

    private IEnumerator HitDeathRoutine()
    {
        isDying = true;

        // 로직/물리 정지 (컴포넌트는 유지해서 코루틴 & 애니 동작)
        if (alertCo != null) { StopCoroutine(alertCo); alertCo = null; }
        if (attackCo != null) { StopCoroutine(attackCo); attackCo = null; }
        state = State.Patrol;
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            if (freezeRigidbodyOnStop)
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        if (body) body.enabled = false; // 추가 타격/충돌 차단

        // Hit → Death 체인
        PlayOnce("Hit", "Death");

        // 충분히 보여주고 제거
        yield return new WaitForSeconds(despawnDelay);

        Destroy(gameObject);
    }

    // ============ Helpers ============
    private Vector2 Eyes()
    {
        var b = body.bounds;
        return new Vector2(b.center.x, b.max.y + 0.01f + 0.5f); // 눈 높이 약간 위
    }
    private Vector2 TargetAimPoint(Transform t)
    {
        if (!t) return transform.position;
        if (t.TryGetComponent<Collider2D>(out var c))
            return (Vector2)c.bounds.center + new Vector2(0f, 0.25f);
        return (Vector2)t.position + new Vector2(0f, 0.25f);
    }

    private void StopAllBehaviours(string reason)
    {
        isStoppedByTag = true;

        // 코루틴 중단 + 색상 복구
        if (alertCo != null) { StopCoroutine(alertCo); alertCo = null; }
        if (attackCo != null) { StopCoroutine(attackCo); attackCo = null; }
        state = State.Patrol;
        if (sr) sr.color = defaultColor;

        // 물리 정지
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            if (freezeRigidbodyOnStop)
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        // 더 이상 로직 돌리지 않기
        if (disableComponentOnStop)
            enabled = false;

#if UNITY_EDITOR
        Debug.Log($"[Monster] Stopped by tag '{stopOnTag}' ({reason})", this);
#endif
    }

    private void ApplyDamage(Transform target)
    {
        if (!target) return;

        if (target.TryGetComponent<IDamageable>(out var dmg))
            dmg.TakeDamage(attackDamage, transform.position, new Vector2(dir, 0));
        else if (target.TryGetComponent<PlayerHealth>(out var hp))
            hp.ApplyDamage(attackDamage);
        else
            target.SendMessage("OnHit", attackDamage, SendMessageOptions.DontRequireReceiver);
    }

    // Movement / Probes
    private void MoveHorizontalTowards(float targetSpeedX)
    {
        Vector2 v = rb.linearVelocity;
        v.x = Mathf.MoveTowards(v.x, targetSpeedX, accel * Time.fixedDeltaTime);
        rb.linearVelocity = v;
    }
    private void StopHorizontal()
    {
        Vector2 v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
    }
    private Vector2 Feet()
    {
        var b = body.bounds;
        return new Vector2(b.center.x, b.min.y + feetYOffset);
    }

    // ★ 플레이어를 벽으로 보지 않도록 playerMask 제외한 blockingMask 사용
    private bool FrontWall(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + 0.02f), 0.15f);
        var hit = Physics2D.Raycast(origin, Vector2.right * d, wallCheckDist, blockingMask);
        if (hit && currentTarget && hit.collider && hit.collider.transform.IsChildOf(currentTarget))
            return false;
        return hit;
    }
    private bool LowFrontWall(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + 0.02f), lowWallYOffset);
        var hit = Physics2D.Raycast(origin, Vector2.right * d, lowWallCheckDist, blockingMask);
        if (hit && currentTarget && hit.collider && hit.collider.transform.IsChildOf(currentTarget))
            return false;
        return hit;
    }
    private bool LedgeAhead(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + ledgeForward), 0.02f);
        return !Physics2D.Raycast(origin, Vector2.down, ledgeDownDist, groundMask);
    }

    private Vector2 GetPatrolTarget() => (patrolTargetIndex == 0) ? wpA : wpB;
    private void TogglePatrolTarget() => patrolTargetIndex = (patrolTargetIndex == 0) ? 1 : 0;

    // === 공격 거리: 콜라이더-대-콜라이더 가장자리 거리 사용
    private float EdgeDistanceTo(Transform t)
    {
        if (!t) return float.MaxValue;

        if (body && t.TryGetComponent<Collider2D>(out var tc))
        {
            var d = Physics2D.Distance(body, tc);
            return d.isOverlapped ? 0f : d.distance;
        }
        // 폴백: 피벗 거리
        return Vector2.Distance(t.position, transform.position);
    }

    private bool WithinAttackWindow(Transform t)
    {
        if (!t) return false;
        float vy = Mathf.Abs(t.position.y - transform.position.y);
        if (vy > attackVerticalTolerance) return false;
        return EdgeDistanceTo(t) <= attackEdgeRange;
    }

    private float DistanceTo(Transform t) => t ? Vector2.Distance(t.position, transform.position) : float.MaxValue;

    private void OnCollisionEnter2D(Collision2D c)
    {
        if (!isStoppedByTag && c.collider && c.collider.CompareTag(stopOnTag))
            StopAllBehaviours($"Collision with {stopOnTag}");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isStoppedByTag && other && other.CompareTag(stopOnTag))
            StopAllBehaviours($"Trigger with {stopOnTag}");
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector2 a = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        Vector2 b = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        Gizmos.color = Color.green; Gizmos.DrawSphere(a, 0.08f);
        Gizmos.color = Color.blue; Gizmos.DrawSphere(b, 0.08f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);

        // 기존 원(피벗 기준)
        Gizmos.color = new Color(1, 0.5f, 0f, 0.2f); Gizmos.DrawWireSphere(transform.position, pingScanRadius);
        Gizmos.color = new Color(1, 0, 0, 0.2f); Gizmos.DrawWireSphere(transform.position, attackRange);

        // EdgeDistance용 시각 보조 (근사)
        Gizmos.color = new Color(1, 0, 0, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackEdgeRange);
    }
}

// 선택형 인터페이스
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal);
}

public class PlayerHealth : MonoBehaviour
{
    public int hp = 3;
    public void ApplyDamage(int dmg)
    {
        hp -= dmg;
        if (hp <= 0) { /* 사망 처리 */ }
    }
}
