using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class CatAIFSM : MonoBehaviour
{
    public enum State { Wander, PauseToMeow, MeowOnce, LoungeOnce, LayedHold, Flee }

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteAnimationManager anim;

    [Header("Animation Clip Names")]
    [SerializeField] private string clipIdle = "Idle";
    [SerializeField] private string clipWalk = "Walk";
    [SerializeField] private string clipRun = "Run";
    [SerializeField] private string clipMeow = "Meow";
    [SerializeField] private string clipLaying = "Laying";
    [SerializeField] private string clipStretching = "Stretching";
    [SerializeField] private string clipLayed = "Layed";

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 1.8f;
    [SerializeField] private float runSpeed = 4.5f;
    [SerializeField] private bool faceFlipX = true;

    [Header("Sensing")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private LayerMask monsterMask;
    [SerializeField] private float playerDetectRadius = 1.8f;
    [SerializeField] private float monsterDetectRadius = 6f;

    [Header("Probes")]
    [SerializeField] private float wallCheckDistance = 0.22f;
    [SerializeField] private float cliffCheckForward = 0.30f;
    [SerializeField] private float cliffCheckDown = 0.55f;
    [SerializeField] private float skin = 0.04f;

    [Header("Behavior Timing")]
    [SerializeField] private float idleBeforeMeowSec = 2f;
    [SerializeField] private float meowCooldownSec = 10f;
    [SerializeField] private float fleeMaxDurationSec = 10f;
    [SerializeField] private float layedHoldSeconds = 2f;

    [Header("Random Lounge")]
    [Tooltip("초당 발생율(예: 0.1 = 초당 10%)")]
    [SerializeField] private float loungeRatePerSec = 0.10f;

    [Header("Start Direction")]
    [SerializeField] private bool startRandomDirection = true;
    [SerializeField] private int startDir = +1;

    [Header("VFX (Meow)")]
    [SerializeField] private GameObject meowVFXPrefab;
    [SerializeField] private Vector2 meowVFXOffsetLocal = new Vector2(0.45f, 0.55f);
    [SerializeField] private bool meowVFXFlipWithFacing = true;
    [SerializeField] private bool meowVFXParentToCat = false;
    [SerializeField] private float meowVFXAutoDestroy = 1.6f;

    [Header("Collision Ignore")]
    [SerializeField] private bool ignoreCollisionWithPlayerAndMonster = true;

    // ---------- Gore on Monkill ----------
    [Header("Gore (Monkill)")]
    [SerializeField] private string killLayerName = "Monkill";
    [SerializeField] private GameObject upperPrefab; // 상반신
    [SerializeField] private GameObject lowerPrefab; // 하반신
    [SerializeField] private GameObject blood0Prefab; // Blood_0
    [SerializeField] private GameObject blood1Prefab; // Blood_1
    [SerializeField] private int burstBloodCount = 10;
    [SerializeField] private float burstRadius = 0.35f;
    [SerializeField] private Vector2 burstSpeedRange = new Vector2(1.2f, 3.0f);
    [SerializeField] private float halvesLifetime = 10f;
    [SerializeField] private float emitIntervalMin = 0.06f;
    [SerializeField] private float emitIntervalMax = 0.20f;
    [SerializeField] private Vector2 upperEmitLocalOffset = new Vector2(0f, -0.05f);
    [SerializeField] private Vector2 lowerEmitLocalOffset = new Vector2(0f, +0.05f);
    [SerializeField] private float emitJitter = 0.06f;

    // runtime
    private State _state = State.Wander;
    private int _dir = +1;
    private float _stateTimer;
    private float _fleeStartTime;
    private int _fleeDir = +1;
    private float _nextMeowAvailableTime = 0f;
    private bool _lastLoungeWasLaying;
    private readonly Collider2D[] _scanBuf = new Collider2D[8];
    private Transform _lastSeenPlayer;

    private int _killLayer = -1;
    private bool _gibbed = false; // 중복 방지

    void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        anim = GetComponentInChildren<SpriteAnimationManager>();
    }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!body) body = GetComponent<Collider2D>();
        if (!anim) anim = GetComponentInChildren<SpriteAnimationManager>();

        _dir = startRandomDirection ? (UnityEngine.Random.value < 0.5f ? -1 : +1)
                                    : (startDir >= 0 ? +1 : -1);

        if (ignoreCollisionWithPlayerAndMonster)
            ApplyLayerIgnores();

        _killLayer = LayerMask.NameToLayer(killLayerName);
        if (_killLayer < 0) Debug.LogWarning($"[CatAIFSM] killLayer '{killLayerName}' not found.");

        WarnIfMissingClips();
    }

    void Start()
    {
        if (_state == State.Wander)
            PlayLoop(clipWalk, true);
    }

    void Update()
    {
        // 1) 몬스터 감지 → Flee 우선
        if (TryDetectNearest(monsterMask, monsterDetectRadius, out Transform monster))
        {
            if (_state != State.Flee)
            {
                _state = State.Flee;
                _fleeStartTime = Time.time;
                _fleeDir = (transform.position.x - monster.position.x) >= 0f ? +1 : -1;
                PlayLoop(clipRun, true, interruptOneShot: true);
            }
        }

        switch (_state)
        {
            case State.Wander:
                TickWander();

                if (Time.time >= _nextMeowAvailableTime &&
                    TryDetectNearest(playerMask, playerDetectRadius, out var player))
                {
                    _lastSeenPlayer = player;
                    FaceTowards(_lastSeenPlayer);
                    _state = State.PauseToMeow;
                    _stateTimer = idleBeforeMeowSec;
                    PlayLoop(clipIdle, true);
                    StopX();
                }
                else
                {
                    float p = 1f - Mathf.Exp(-loungeRatePerSec * Time.deltaTime);
                    if (UnityEngine.Random.value < p)
                    {
                        _state = State.LoungeOnce;
                        StopX();
                        _lastLoungeWasLaying = (UnityEngine.Random.value < 0.5f);
                        string choose = _lastLoungeWasLaying ? clipLaying : clipStretching;
                        anim.PlayOnce(choose, fallback: null, forceRestart: true);
                    }
                }
                break;

            case State.PauseToMeow:
                StopX();
                if (TryDetectNearest(playerMask, playerDetectRadius, out var pNow))
                {
                    _lastSeenPlayer = pNow;
                    FaceTowards(_lastSeenPlayer);
                }
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                {
                    if (!_lastSeenPlayer && TryDetectNearest(playerMask, playerDetectRadius, out var pSnap))
                        _lastSeenPlayer = pSnap;
                    FaceTowards(_lastSeenPlayer);

                    _state = State.MeowOnce;
                    anim.PlayOnce(clipMeow, fallback: clipWalk, forceRestart: true);
                    _nextMeowAvailableTime = Time.time + meowCooldownSec;
                    SpawnMeowVFX();
                }
                break;

            case State.MeowOnce:
                StopX();
                if (!anim.IsOneShotActive)
                {
                    PickRandomDir();
                    _state = State.Wander;
                    PlayLoop(clipWalk, false);
                }
                break;

            case State.LoungeOnce:
                StopX();
                if (!anim.IsOneShotActive)
                {
                    if (_lastLoungeWasLaying && anim.HasClip(clipLayed))
                    {
                        PlayLoop(clipLayed, true);
                        _state = State.LayedHold;
                        _stateTimer = layedHoldSeconds;
                    }
                    else
                    {
                        PickRandomDir();
                        _state = State.Wander;
                        PlayLoop(clipWalk, true);
                    }
                }
                break;

            case State.LayedHold:
                StopX();
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                {
                    PickRandomDir();
                    _state = State.Wander;
                    PlayLoop(clipWalk, true);
                }
                break;

            case State.Flee:
                TickFlee();
                break;
        }

        // 스프라이트 좌우 뒤집기
        if (faceFlipX && anim != null)
        {
            var sr = anim.GetComponentInChildren<SpriteRenderer>();
            if (sr) sr.flipX = (_dir < 0);
        }
    }

    void FixedUpdate()
    {
        switch (_state)
        {
            case State.Wander:
                EnsureWalkAnim();
                MoveX(walkSpeed, obeyObstacles: true);
                break;
            case State.Flee:
                MoveX(runSpeed, obeyObstacles: false);
                break;
        }
    }

    // ----- Monkill 충돌 감지 -----
    void OnTriggerEnter2D(Collider2D other)
    {
        if (_gibbed || _killLayer < 0 || other == null) return;
        if (other.gameObject.layer == _killLayer) DoGoreAndDie();
    }
    void OnCollisionEnter2D(Collision2D c)
    {
        if (_gibbed || _killLayer < 0 || c.collider == null) return;
        if (c.collider.gameObject.layer == _killLayer) DoGoreAndDie();
    }

    private void DoGoreAndDie()
    {
        if (_gibbed) return;
        _gibbed = true;

        Vector3 center = body ? (Vector3)body.bounds.center : transform.position;
        float halfGap = body ? Mathf.Clamp(body.bounds.extents.y * 0.25f, 0.03f, 0.20f) : 0.08f;

        // 상/하반신 위치
        Vector3 posUpper = center + new Vector3(0f, +halfGap, 0f);
        Vector3 posLower = center + new Vector3(0f, -halfGap, 0f);

        // 프리팹 스폰
        GameObject upper = upperPrefab ? Instantiate(upperPrefab, posUpper, Quaternion.identity) : null;
        GameObject lower = lowerPrefab ? Instantiate(lowerPrefab, posLower, Quaternion.identity) : null;

        // 좌우 방향 맞추기
        bool faceLeft = (_dir < 0);
        ApplyFacingToGib(upper, faceLeft);
        ApplyFacingToGib(lower, faceLeft);

        // 즉시 분출(10개)
        BurstBlood(center, burstBloodCount);

        // 각 잔해에서 지속 분출
        if (upper) AttachEmitter(upper, upperEmitLocalOffset);
        if (lower) AttachEmitter(lower, lowerEmitLocalOffset);

        // 수명
        if (upper) Destroy(upper, halvesLifetime);
        if (lower) Destroy(lower, halvesLifetime);

        // 원본 제거
        Destroy(gameObject);
    }

    private void ApplyFacingToGib(GameObject go, bool flipLeft)
    {
        if (!go) return;
        var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
        bool any = false;
        foreach (var sr in srs) { sr.flipX = flipLeft; any = true; }
        if (!any)
        {
            var sc = go.transform.localScale;
            sc.x = Mathf.Abs(sc.x) * (flipLeft ? -1f : 1f);
            go.transform.localScale = sc;
        }
    }

    private void BurstBlood(Vector3 center, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var prefab = (UnityEngine.Random.value < 0.5f) ? blood0Prefab : blood1Prefab;
            if (!prefab) continue;

            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
            float dist = UnityEngine.Random.Range(0.05f, burstRadius);
            Vector3 pos = center + (Vector3)(dir * dist);

            var go = Instantiate(prefab, pos, Quaternion.identity);
            // 리지드바디가 있으면 튀게 만든다
            if (go.TryGetComponent<Rigidbody2D>(out var r2d))
            {
                float spd = UnityEngine.Random.Range(burstSpeedRange.x, burstSpeedRange.y);
                r2d.AddForce(dir * spd, ForceMode2D.Impulse);
                r2d.AddTorque(UnityEngine.Random.Range(-10f, 10f), ForceMode2D.Impulse);
            }

            // 프리팹이 자체 소멸이 없다면 안전망(3초)
            Destroy(go, 3f);
        }
    }

    private void AttachEmitter(GameObject host, Vector2 localOffset)
    {
        var em = host.AddComponent<BloodEmitter2D>();
        em.blood0 = blood0Prefab;
        em.blood1 = blood1Prefab;
        em.intervalMin = emitIntervalMin;
        em.intervalMax = emitIntervalMax;
        em.localOffset = localOffset;
        em.jitter = emitJitter;
        em.autoDestroySeconds = 3f; // 개별 혈흔 수명
    }

    // ====== STATE TICKS ======
    private void TickWander()
    {
        if (ShouldFlip(_dir)) _dir = -_dir;
    }

    private void TickFlee()
    {
        bool monsterInRange = TryDetectNearest(monsterMask, monsterDetectRadius, out _);
        bool overTime = (Time.time - _fleeStartTime) >= fleeMaxDurationSec;

        if (!monsterInRange || overTime)
        {
            _state = State.Wander;
            PlayLoop(clipWalk, true, interruptOneShot: true);
            PickRandomDir();
            return;
        }
        _dir = _fleeDir;
    }

    // ====== MOVEMENT / PHYSICS ======
    private void MoveX(float speed, bool obeyObstacles)
    {
        if (obeyObstacles && ShouldFlip(_dir)) _dir = -_dir;
        var v = rb.linearVelocity;
        v.x = _dir * speed;
        rb.linearVelocity = v;
    }
    private void StopX()
    {
        var v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
    }
    private bool ShouldFlip(int dirSign)
    {
        Bounds b = body.bounds;
        Vector2 originMid = new Vector2(b.center.x, b.center.y);
        Vector2 originFoot = new Vector2(b.center.x, b.min.y + skin);

        Vector2 wallOrigin = originMid + new Vector2(dirSign * (b.extents.x + skin), 0f);
        RaycastHit2D hitWall = Physics2D.Raycast(wallOrigin, new Vector2(dirSign, 0f), wallCheckDistance, obstacleMask);

        Vector2 cliffProbe = originFoot + new Vector2(dirSign * (b.extents.x + cliffCheckForward), 0f);
        RaycastHit2D hitGround = Physics2D.Raycast(cliffProbe, Vector2.down, cliffCheckDown, obstacleMask);

        return hitWall.collider != null || hitGround.collider == null;
    }

    // ====== SENSING ======
    private bool TryDetectNearest(LayerMask mask, float radius, out Transform nearest)
    {
        nearest = null;

        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(mask);

        int count = Physics2D.OverlapCircle((Vector2)transform.position, radius, filter, _scanBuf);

        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var c = _scanBuf[i];
            if (!c) continue;

            float d = ((Vector2)c.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = c.transform; }
        }
        return nearest != null;
    }

    // ====== HELPERS ======
    private void PlayLoop(string name, bool forceRestart, bool interruptOneShot = false)
    {
        if (anim == null || string.IsNullOrEmpty(name)) return;
        anim.Play(name, forceRestart: forceRestart, interruptOneShot: interruptOneShot);
    }
    private void EnsureWalkAnim()
    {
        if (anim == null) return;
        if (anim.IsOneShotActive) return;
        if (anim.IsPlaying(clipWalk)) return;
        PlayLoop(clipWalk, false);
    }
    private void PickRandomDir() => _dir = (UnityEngine.Random.value < 0.5f) ? -1 : +1;
    private void FaceTowards(Transform target)
    {
        if (!target) return;
        _dir = (target.position.x - transform.position.x) >= 0f ? +1 : -1;
    }
    private void WarnIfMissingClips()
    {
        if (!anim) return;
        string[] needed = { clipIdle, clipWalk, clipRun, clipMeow, clipLaying, clipStretching, clipLayed };
        foreach (var n in needed)
        {
            if (!string.IsNullOrEmpty(n) && !anim.HasClip(n))
                Debug.LogWarning($"[CatAIFSM] Missing clip '{n}' in SpriteAnimationManager on {name}");
        }
    }
    private void SpawnMeowVFX()
    {
        if (!meowVFXPrefab) return;

        float xSign = (_dir >= 0) ? +1f : -1f;
        Vector3 local = new Vector3(meowVFXOffsetLocal.x * xSign, meowVFXOffsetLocal.y, 0f);
        Vector3 worldPos = transform.position + local;

        Transform parent = meowVFXParentToCat ? transform : null;
        GameObject vfx = Instantiate(meowVFXPrefab, worldPos, Quaternion.identity, parent);

        if (meowVFXFlipWithFacing && vfx)
        {
            bool flip = (_dir < 0);
            var srs = vfx.GetComponentsInChildren<SpriteRenderer>(true);
            bool any = false;
            foreach (var sr in srs) { sr.flipX = flip; any = true; }
            if (!any)
            {
                Vector3 sc = vfx.transform.localScale;
                sc.x = Mathf.Abs(sc.x) * (flip ? -1f : 1f);
                vfx.transform.localScale = sc;
            }
        }

        if (meowVFXAutoDestroy > 0f) Destroy(vfx, meowVFXAutoDestroy);
    }

    // 레이어 충돌 전역 무시(플레이어/몬스터)
    private void ApplyLayerIgnores()
    {
        int myLayer = gameObject.layer;
        IgnoreLayerMaskCollisions(myLayer, playerMask, true);
        IgnoreLayerMaskCollisions(myLayer, monsterMask, true);
    }
    private static void IgnoreLayerMaskCollisions(int myLayer, LayerMask mask, bool ignore)
    {
        int bits = mask.value;
        for (int layer = 0; layer < 32; layer++)
        {
            if (((bits >> layer) & 1) != 0)
                Physics2D.IgnoreLayerCollision(myLayer, layer, ignore);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, monsterDetectRadius);

        if (body)
        {
            Bounds b = body.bounds;
            int s = (_dir >= 0) ? +1 : -1;
            Vector3 wallOrigin = new Vector3(b.center.x + s * (b.extents.x + skin), b.center.y, 0f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(wallOrigin, wallOrigin + new Vector3(s * wallCheckDistance, 0f, 0f));

            Vector3 cliffProbe = new Vector3(b.center.x + s * (b.extents.x + cliffCheckForward), b.min.y + skin, 0f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(cliffProbe, cliffProbe + new Vector3(0f, -cliffCheckDown, 0f));
        }
    }
}
