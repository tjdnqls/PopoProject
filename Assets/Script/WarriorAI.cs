using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class ChargerSentinelAI : MonoBehaviour
{
    public enum State { Idle, Preparing, Dashing, Recover, Dead }

    // ---------- Animation ----------
    [Header("Animation")]
    public SpriteAnimationManager anim;   // 없으면 무시
    public Animator animator;             // 없으면 무시
    [SerializeField] private string idleAnim = "Idle";
    [SerializeField] private string prepareAnim = "deshStay";
    [SerializeField] private string dashAnim = "Dash";
    [SerializeField] private string hitAnim = "Hit";
    [SerializeField] private string deathAnim = "Death";
    [SerializeField] private string attackOmegaAnim = "attackOmega";
    [SerializeField] private float onHitShakeAmp = 0.6f;
    [SerializeField] private float onHitShakeDur = 0.25f;
    [SerializeField] private GameObject windEffect;

    // ---------- Layers / Refs ----------
    [Header("Layers")]
    [SerializeField] private string groundLayerName = "Ground";
    [SerializeField] private string monsterLayerName = "Monster";
    [SerializeField] private string monkillLayerName = "Monkill";
    [SerializeField] private string backMonsterLayerName = "BackMonster";
    [SerializeField] private string boxLayerName = "Box";
    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;

    // ---------- Detect / Timings ----------
    [Header("Detect (Radius)")]
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float detectRadius = 4f;

    [Tooltip("최초 감지 이후 감지 반경 배수(어그로 후 확대)")]
    [SerializeField] private float postAggroDetectMultiplier = 3f;

    [Header("Detect Rules (LOS/Height)")]
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private float ignoreIfHigherThan = 5f;
    [SerializeField] private float ignoreIfLowerThan = 1f;

    [Header("Prepare (turn red)")]
    [SerializeField] private float prepareSeconds = 2.0f;
    [SerializeField, Range(0f, 1f)] private float prepareTintStrength = 0.8f;
    [SerializeField] private Color prepareColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.35f;

    [Header("Loop/Recover")]
    [SerializeField] private float recoverSeconds = 0.2f;

    // ---------- Kill sweep (no physical collision) ----------
    [Header("Monster Kill Scan (while Dashing)")]
    [SerializeField] private Vector2 killBoxPadding = new Vector2(0.10f, 0.05f);
    [SerializeField] private float killBoxForward = 0.10f;
    [SerializeField] private int killDamage = 9999;

    // ---------- Dash Preview ----------
    [Header("Dash Preview (Preparing)")]
    [SerializeField] private bool showDashPreview = true;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.25f;
    [SerializeField] private float previewThickness = 0.20f;
    [SerializeField] private bool previewClampToGround = true;
    [SerializeField] private string previewSortingLayerName = "Effects";
    [SerializeField] private int previewSortingOrder = 100;

    private GameObject _previewGO;
    private SpriteRenderer _previewSR;

    // ---------- Offscreen Edge Indicator ----------
    [Header("Offscreen Edge Indicator")]
    [SerializeField] private bool showOffscreenIndicator = true;
    [SerializeField] private bool indicatorDuringPrepare = true;
    [SerializeField] private Sprite indicatorSprite;
    [SerializeField] private Color indicatorColor = new Color(1f, 0.25f, 0.25f, 0.95f);
    [SerializeField, Range(0f, 0.49f)] private float indicatorEdgeInset = 0.06f;
    [SerializeField] private float indicatorScaleWorld = 0.8f;
    [SerializeField] private string indicatorSortingLayerName = "Effects";
    [SerializeField] private int indicatorSortingOrder = 200;

    private Camera _cam;
    private GameObject _indicatorGO;
    private SpriteRenderer _indicatorSR;

    // ---------- Death / Blood ----------
    [Header("Death")]
    [SerializeField] private float despawnDelay = 5f;
    [SerializeField] private float feetYOffset = 0.05f;

    [Header("Death VFX (Blood)")]
    [SerializeField] private GameObject blood0Prefab;     // Blood_0
    [SerializeField] private GameObject blood1Prefab;     // Blood_1
    [SerializeField] private int burstBloodCount = 10;
    [SerializeField] private float burstRadius = 0.35f;
    [SerializeField] private Vector2 burstSpeedRange = new Vector2(1.2f, 3.0f);
    [SerializeField] private float sustainDelay = 0.0f;
    [SerializeField] private float sustainDuration = 5.0f;
    [SerializeField] private Vector2 sustainIntervalRange = new Vector2(0.06f, 0.20f);
    [SerializeField] private float sustainJitter = 0.06f;
    [SerializeField] private float bloodLifetime = 3.0f;

    // ---------- Player/BackMonster damage while dashing ----------
    [Header("Dash Contact (stop & attack)")]
    [SerializeField] private int dashPlayerDamage = 1;
    [SerializeField] private Vector2 playerBoxPadding = new Vector2(0.10f, 0);
    [SerializeField] private float playerBoxForward = 0.10f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawKillBox = false;

    // ---------- Runtime ----------
    private State state;
    private Transform currentTarget;
    private float _timer;
    private int _dashDir, _plannedDashDir;
    private bool _mustDashOnce;
    private Color _baseColor = Color.white;
    private bool _stoppedThisDash;
    private int _groundLayer, _monsterLayer, _monkillLayer, _backMonsterLayer, _myLayer, _boxLayer;
    private static readonly Collider2D[] _buf = new Collider2D[8];
    private static readonly Collider2D[] _killBuf = new Collider2D[16];
    private static readonly Collider2D[] _playerBuf = new Collider2D[16];

    private bool isDying = false;
    private Vector3 _deathPos;
    private Quaternion _deathRot;
    private Vector3 _deathFeetPos;

    private readonly HashSet<int> _hitPlayerRootsThisDash = new();

    private LayerMask AggroMask => playerMask | (1 << _backMonsterLayer);
    private float DetectRadiusNow => detectRadius * (_aggroBoosted ? Mathf.Max(1f, postAggroDetectMultiplier) : 1f);

    private bool _aggroBoosted = false;

    // ====== 추가: 대시 중 중력 무효 + Y 고정 ======
    [Header("Dash Line Lock")]
    [SerializeField] private bool lockYWhileDashing = true;
    private float _dashStartY;
    private float _origGravityScale = 1f;

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

        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        _origGravityScale = rb.gravityScale;

        _baseColor = sr ? sr.color : Color.white;
        _boxLayer = LayerMask.NameToLayer(boxLayerName);
        _groundLayer = LayerMask.NameToLayer(groundLayerName);
        _monsterLayer = LayerMask.NameToLayer(monsterLayerName);
        _monkillLayer = LayerMask.NameToLayer(monkillLayerName);
        _backMonsterLayer = LayerMask.NameToLayer(backMonsterLayerName);
        _myLayer = gameObject.layer;

        for (int i = 0; i < 32; i++)
        {
            bool allow = (i == _groundLayer) || (i == _monkillLayer) || (i == _boxLayer) ;
            Physics2D.IgnoreLayerCollision(_myLayer, i, !allow);
        }

        _cam = Camera.main;
        EnsurePreviewObject();
        EnsureIndicatorObject();
        HidePreview();
        HideIndicator();
        state = State.Idle;
    }

    private void OnEnable()
    {
        state = State.Idle;
        _timer = 0f;
        _mustDashOnce = false;
        _plannedDashDir = 0;
        isDying = false;
        _aggroBoosted = false;
        if (sr) sr.color = _baseColor;
        PlayAnim(idleAnim, true);
        HidePreview();
        HideIndicator();
        RestoreGravity();
    }

    private void OnDisable()
    {
        RestoreGravity();
    }

    private void FixedUpdate()
    {
        if (state == State.Dead) { StopHorizontal(); return; }

        switch (state)
        {
            case State.Idle: TickIdle(); break;
            case State.Preparing: TickPreparing(); break;
            case State.Dashing: TickDashing(); break;
            case State.Recover: TickRecover(); break;
        }

        if (state == State.Dashing)
        {
            KillSweepAhead();
            DamagePlayersAhead();
        }
    }

    private void LateUpdate()
    {
        if (!_cam) _cam = Camera.main;

        if (state == State.Dead)
        {
            transform.position = _deathPos;
            transform.rotation = _deathRot;
            HideIndicator();
            return;
        }

        if (showDashPreview && state == State.Preparing) UpdateDashPreview(_plannedDashDir);
        else HidePreview();

        bool wantIndicator = showOffscreenIndicator &&
                             (state == State.Dashing || (indicatorDuringPrepare && state == State.Preparing));
        if (wantIndicator) UpdateOffscreenIndicator();
        else HideIndicator();
    }

    // ========== States ==========
    private void TickIdle()
    {
        StopHorizontal();

        if (TryDetectNearest(out Transform t))
        {
            currentTarget = t;
            EnterPrepare();
        }
        else
        {
            currentTarget = null;
        }
    }

    private void TickPreparing()
    {
        StopHorizontal();
        if (!StillValidTarget() && !_mustDashOnce) { EnterIdle(); return; }

        if (_plannedDashDir == 0)
            _plannedDashDir = (currentTarget && currentTarget.position.x < transform.position.x) ? -1 : +1;

        _timer += Time.fixedDeltaTime;
        if (sr)
        {
            float a = Mathf.Clamp01(_timer / prepareSeconds) * prepareTintStrength;
            sr.color = Color.Lerp(_baseColor, prepareColor, a);
            sr.flipX = (_plannedDashDir < 0);
        }
        if (_timer >= prepareSeconds) EnterDash();
    }

    private void TickDashing()
    {
        // y축 고정 + 중력 무효 상태 유지
        if (lockYWhileDashing)
        {
            Vector2 v = rb.linearVelocity;
            v.x = _dashDir * dashSpeed;
            v.y = 0f;
            rb.linearVelocity = v;

            // 고저차 지형에서도 y를 강제로 고정
            rb.position = new Vector2(rb.position.x, _dashStartY);
        }
        else
        {
            Vector2 v = rb.linearVelocity; v.x = _dashDir * dashSpeed; rb.linearVelocity = v;
        }

        _timer += Time.fixedDeltaTime;
        if (_timer >= dashDuration) EnterRecover();
    }

    private void TickRecover()
    {
        StopHorizontal();
        _timer += Time.fixedDeltaTime;
        if (_timer >= recoverSeconds)
        {
            if (TryDetectNearest(out Transform t)) { currentTarget = t; EnterPrepare(); }
            else EnterIdle();
        }
    }

    private void EnterIdle()
    {
        state = State.Idle; _timer = 0f; _mustDashOnce = false; _plannedDashDir = 0;
        if (sr) sr.color = _baseColor;
        HidePreview(); currentTarget = null;
        RestoreGravity();
        PlayAnim(idleAnim);
    }

    private void EnterPrepare()
    {
        state = State.Preparing; _timer = 0f; _mustDashOnce = true;
        _aggroBoosted = true;
        _plannedDashDir = (currentTarget && currentTarget.position.x < transform.position.x) ? -1 : +1;
        if (sr) sr.flipX = (_plannedDashDir < 0);
        ShowPreview();
        RestoreGravity();
        PlayAnim(prepareAnim, true);
    }

    private void EnterDash()
    {
        _stoppedThisDash = false;
        _hitPlayerRootsThisDash.Clear();
        if (!StillValidTarget() && !_mustDashOnce) { EnterIdle(); return; }

        state = State.Dashing; _timer = 0f;
        _dashDir = (_plannedDashDir != 0) ? _plannedDashDir
                 : (currentTarget && currentTarget.position.x >= transform.position.x ? +1 : -1);
        if (sr) sr.flipX = (_dashDir < 0);
        // 대시 시작 시 중력 제거 + y 고정
        _dashStartY = rb.position.y;
        rb.gravityScale = 0f;

        Vector2 v = rb.linearVelocity; v.x = _dashDir * dashSpeed; v.y = 0f; rb.linearVelocity = v;
        if (sr) sr.color = _baseColor;
        _mustDashOnce = false;
        HidePreview();
        CameraShaker.Shake(0.4f, 0.2f);
        PlayAnim(dashAnim, true);
    }

    private void EnterRecover(string animKeyOverride = null)
    {
        state = State.Recover; _timer = 0f;
        StopHorizontal(); HidePreview();
        RestoreGravity();
        PlayAnim(string.IsNullOrEmpty(animKeyOverride) ? idleAnim : animKeyOverride);
    }

    // ========== Helpers ==========
    private void StopHorizontal()
    {
        Vector2 v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
    }

    private void RestoreGravity()
    {
        rb.gravityScale = _origGravityScale;
    }

    private bool StillValidTarget()
    {
        if (!currentTarget) return false;
        if (((1 << currentTarget.gameObject.layer) & AggroMask) == 0) return false;
        if (Vector2.Distance(currentTarget.position, transform.position) > DetectRadiusNow) return false;

        float dy = currentTarget.position.y - transform.position.y;
        if (dy > ignoreIfHigherThan) return false;
        if (dy < -ignoreIfLowerThan) return false;

        if (requireLineOfSight && !HasLineOfSightTo(currentTarget)) return false;
        return true;
    }

    private bool TryDetectNearest(out Transform t)
    {
        t = null;
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = AggroMask, useTriggers = true };
        int n = Physics2D.OverlapCircle((Vector2)transform.position, DetectRadiusNow, filter, _buf);
        if (n <= 0) return false;

        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = _buf[i]; if (!c) continue;
            Transform cand = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            if (!cand) continue;

            float dy = cand.position.y - transform.position.y;
            if (dy > ignoreIfHigherThan) continue;
            if (dy < -ignoreIfLowerThan) continue;

            if (requireLineOfSight && !HasLineOfSightTo(cand)) continue;

            float d2 = ((Vector2)cand.position - (Vector2)transform.position).sqrMagnitude;
            if (d2 < best) { best = d2; t = cand; }
        }
        return t != null;
    }

    private bool HasLineOfSightTo(Transform target)
    {
        if (!target) return false;

        Vector2 from = body ? (Vector2)body.bounds.center : (Vector2)transform.position;
        Vector2 to;
        if (target.TryGetComponent<Collider2D>(out var tc))
            to = (Vector2)tc.bounds.center;
        else
            to = (Vector2)target.position;

        Vector2 dir = (to - from);
        float dist = dir.magnitude;
        if (dist < 0.001f) return true;
        dir /= dist;

        int mask = 1 << _groundLayer;
        RaycastHit2D hit = Physics2D.Raycast(from, dir, dist, mask);
        return !hit.collider;
    }

    private void KillSweepAhead()
    {
        if (!body) return;
        Bounds b = body.bounds;
        Vector2 size = new Vector2(b.size.x + killBoxPadding.x, b.size.y + killBoxPadding.y);
        Vector2 center = new Vector2(b.center.x + _dashDir * (b.extents.x + killBoxForward), b.center.y);

        var filter = new ContactFilter2D { useLayerMask = true, layerMask = 1 << _monsterLayer, useTriggers = true };
        int hitCount = Physics2D.OverlapBox(center, size, 0f, filter, _killBuf);
        for (int i = 0; i < hitCount; i++)
        {
            var c = _killBuf[i]; if (!c) continue;
            if (c.transform.root == transform.root) continue;
            var targetGo = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;
            targetGo.SendMessage("OnHit", killDamage, SendMessageOptions.DontRequireReceiver);
        }
#if UNITY_EDITOR
        if (drawKillBox)
        {
            Color cc = new Color(1f, 0f, 0f, 0.2f);
            Debug.DrawLine(center + new Vector2(-size.x / 2, -size.y / 2), center + new Vector2(size.x / 2, -size.y / 2), cc, 0f);
            Debug.DrawLine(center + new Vector2(size.x / 2, -size.y / 2), center + new Vector2(size.x / 2, size.y / 2), cc, 0f);
            Debug.DrawLine(center + new Vector2(size.x / 2, size.y / 2), center + new Vector2(-size.x / 2, size.y / 2), cc, 0f);
            Debug.DrawLine(center + new Vector2(-size.x / 2, size.y / 2), center + new Vector2(-size.x / 2, -size.y / 2), cc, 0f);
        }
#endif
    }

    private void DamagePlayersAhead()
    {
        if (!body) return;

        Bounds b = body.bounds;
        Vector2 size = new Vector2(b.size.x + playerBoxPadding.x, b.size.y + playerBoxPadding.y);
        Vector2 center = new Vector2(b.center.x + _dashDir * (b.extents.x + playerBoxForward), b.center.y);

        var filter = new ContactFilter2D { useLayerMask = true, layerMask = AggroMask, useTriggers = true };
        int count = Physics2D.OverlapBox(center, size, 0f, filter, _playerBuf);

        for (int i = 0; i < count; i++)
        {
            var c = _playerBuf[i]; if (!c) continue;
            if (c.transform.root == transform.root) continue;

            var root = c.attachedRigidbody ? c.attachedRigidbody.transform.root : c.transform.root;
            int id = root.GetInstanceID();
            if (_hitPlayerRootsThisDash.Contains(id)) continue;

            DealDamageTo(c.transform, dashPlayerDamage);
            StopDashOnPlayerHit();
            _hitPlayerRootsThisDash.Add(id);
        }
    }

    private void StopDashOnPlayerHit()
    {
        if (_stoppedThisDash) return;
        _stoppedThisDash = true;
        Vector2 v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
        EnterRecover(attackOmegaAnim);
        CameraShaker.Shake(onHitShakeAmp, onHitShakeDur);
    }

    private void DealDamageTo(Transform t, int dmg)
    {
        if (!t) return;

        var dmgIf = t.GetComponentInParent<global::IDamageable>();
        if (dmgIf != null)
        {
            Vector2 hitPoint = body ? (Vector2)body.bounds.center : (Vector2)transform.position;
            Vector2 hitNormal = new Vector2(_dashDir, 0);
            dmgIf.TakeDamage(dmg, hitPoint, hitNormal);
            return;
        }

        var p2 = t.GetComponentInParent<Player2HP>();
        if (p2 != null) { p2.TakeDamage(dmg); return; }

        t.SendMessageUpwards("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
        t.SendMessageUpwards("OnHit", dmg, SendMessageOptions.DontRequireReceiver);
    }

    private void OnCollisionEnter2D(Collision2D c)
    {
        if (state == State.Dead) return;
        if (c.collider.gameObject.layer == _monkillLayer) StartDeathSequence("Monkill Collision");
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state == State.Dead) return;
        if (other.gameObject.layer == _monkillLayer) StartDeathSequence("Monkill Trigger");
    }

    private void StartDeathSequence(string reason)
    {
        if (isDying) return;
        isDying = true;
        state = State.Dead;

        _deathPos = transform.position;
        _deathRot = transform.rotation;
        _deathFeetPos = GetFeetWorld();
        StopHorizontal();
        HidePreview();
        HideIndicator();
        if (sr) sr.color = Color.white;

        SpawnBloodBurst(GetBodyCenter(), burstBloodCount);
        if ((blood0Prefab || blood1Prefab) && sustainDuration > 0f)
            StartCoroutine(FootBloodSustain());

        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.simulated = false;
        }
        if (body) body.enabled = false;

        RestoreGravity();
        StartCoroutine(DeathAnimThenDespawn());
    }

    private IEnumerator DeathAnimThenDespawn()
    {
        PlayAnim(hitAnim, true);
        yield return null;
        PlayAnim(deathAnim, true);

        float t = 0f;
        while (t < despawnDelay) { t += Time.deltaTime; yield return null; }
        Destroy(gameObject);
    }

    private Vector3 GetBodyCenter() => body ? body.bounds.center : transform.position;

    private Vector3 GetFeetWorld()
    {
        if (body != null)
        {
            var b = body.bounds;
            return new Vector3(b.center.x, b.min.y + feetYOffset, transform.position.z);
        }
        return transform.position + new Vector3(0f, -0.25f, 0f);
    }

    private void SpawnBloodBurst(Vector3 center, int count)
    {
        if (!blood0Prefab && !blood1Prefab) return;
        for (int i = 0; i < count; i++)
        {
            var prefab = (Random.value < 0.5f || !blood1Prefab) ? blood0Prefab : blood1Prefab;
            if (!prefab) continue;

            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(0.05f, burstRadius);
            Vector3 pos = center + (Vector3)(dir * dist);

            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (go.TryGetComponent<Rigidbody2D>(out var r2d))
            {
                float spd = Random.Range(burstSpeedRange.x, burstSpeedRange.y);
                r2d.AddForce(dir * spd, ForceMode2D.Impulse);
                r2d.AddTorque(Random.Range(-10f, 10f), ForceMode2D.Impulse);
            }
            if (bloodLifetime > 0f) Destroy(go, bloodLifetime);
        }
    }

    private IEnumerator FootBloodSustain()
    {
        if (sustainDelay > 0f) yield return new WaitForSeconds(sustainDelay);

        float t = 0f;
        while (t < sustainDuration)
        {
            Vector2 jitter = Random.insideUnitCircle * sustainJitter;
            Vector3 pos = _deathFeetPos + (Vector3)jitter;

            Vector2 dir = (Vector2.up + Random.insideUnitCircle * 0.6f).normalized;
            float spd = Random.Range(burstSpeedRange.x * 0.6f, burstBloodCount > 0 ? burstSpeedRange.y : burstSpeedRange.y);

            var prefab = (Random.value < 0.5f || !blood1Prefab) ? blood0Prefab : blood1Prefab;
            if (prefab)
            {
                var go = Instantiate(prefab, pos, Quaternion.identity);
                if (go.TryGetComponent<Rigidbody2D>(out var r2d))
                {
                    r2d.AddForce(dir * spd, ForceMode2D.Impulse);
                    r2d.AddTorque(Random.Range(-12f, 12f), ForceMode2D.Impulse);
                }
                if (bloodLifetime > 0f) Destroy(go, bloodLifetime);
            }

            float wait = Random.Range(sustainIntervalRange.x, sustainIntervalRange.y);
            t += wait;
            yield return new WaitForSeconds(wait);
        }
    }

    private void EnsurePreviewObject()
    {
        if (_previewGO) return;

        _previewGO = new GameObject("DashPreview");
        _previewGO.transform.SetParent(transform, false);

        _previewSR = _previewGO.AddComponent<SpriteRenderer>();
        var tex = Texture2D.whiteTexture;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                   new Vector2(0.5f, 0.5f), tex.width);
        _previewSR.sprite = sprite;
        _previewSR.color = new Color(1f, 0f, 0f, previewAlpha);
        if (!string.IsNullOrEmpty(previewSortingLayerName))
            _previewSR.sortingLayerName = previewSortingLayerName;
        _previewSR.sortingOrder = previewSortingOrder;

        _previewGO.SetActive(false);
    }
    private void ShowPreview() { if (_previewGO) _previewGO.SetActive(true); }
    private void HidePreview() { if (_previewGO) _previewGO.SetActive(false); }

    private Vector2 GetParentScaleAbs()
    {
        var s = transform.lossyScale;
        return new Vector2(Mathf.Abs(s.x), Mathf.Abs(s.y));
    }
    private float ComputeDashDistance(int dirSign)
    {
        float baseDist = Mathf.Abs(dashSpeed) * dashDuration;
        if (!previewClampToGround || !body) return baseDist;

        Bounds b = body.bounds;
        Vector2 size = b.size;
        Vector2 origin = b.center;
        Vector2 dir = new Vector2(dirSign, 0f);
        int mask = 1 << _groundLayer;

        var hit = Physics2D.BoxCast(origin, size, 0f, dir, baseDist, mask);
        return hit.collider ? hit.distance : baseDist;
    }
    private void UpdateDashPreview(int dirSign)
    {
        if (!_previewGO || !body) return;

        float dist = Mathf.Max(0f, ComputeDashDistance(dirSign));
        Bounds b = body.bounds;

        float widthWorld = Mathf.Max(0.01f, dist);
        float heightWorld = Mathf.Max(0.01f, previewThickness);

        Vector3 center = new Vector3(b.center.x + dirSign * (dist * 0.5f), b.center.y, b.center.z);

        var ps = GetParentScaleAbs();
        float safeX = (ps.x <= 0.0001f) ? 1f : ps.x;
        float safeY = (ps.y <= 0.0001f) ? 1f : ps.y;

        _previewGO.transform.position = center;
        _previewGO.transform.rotation = Quaternion.identity;
        _previewGO.transform.localScale = new Vector3(widthWorld / safeX, heightWorld / safeY, 1f);
        if (_previewSR) _previewSR.color = new Color(1f, 0f, 0f, previewAlpha);
    }

    private void EnsureIndicatorObject()
    {
        if (_indicatorGO || !_cam) return;

        _indicatorGO = new GameObject("OffscreenIndicator");
        _indicatorGO.transform.SetParent(_cam.transform, false);

        _indicatorSR = _indicatorGO.AddComponent<SpriteRenderer>();
        _indicatorSR.sprite = indicatorSprite ? indicatorSprite : Texture2D.whiteTexture.ToSprite();
        _indicatorSR.color = indicatorColor;
        if (!string.IsNullOrEmpty(indicatorSortingLayerName))
            _indicatorSR.sortingLayerName = indicatorSortingLayerName;
        _indicatorSR.sortingOrder = indicatorSortingOrder;

        _indicatorGO.transform.localScale = Vector3.one * indicatorScaleWorld;
        _indicatorGO.SetActive(false);
    }
    private void HideIndicator() { if (_indicatorGO && _indicatorGO.activeSelf) _indicatorGO.SetActive(false); }
    private void ShowIndicator() { if (!_indicatorGO) EnsureIndicatorObject(); if (_indicatorGO && !_indicatorGO.activeSelf) _indicatorGO.SetActive(true); }

    private void UpdateOffscreenIndicator()
    {
        if (!_cam || !_indicatorSR || !showOffscreenIndicator) { HideIndicator(); return; }

        if (IsBoundsInCameraView(_cam, body ? body.bounds : new Bounds(transform.position, Vector3.one * 0.2f)))
        {
            HideIndicator();
            return;
        }

        Vector3 v = _cam.WorldToViewportPoint(body ? body.bounds.center : transform.position);
        if (v.z < 0f) { v.x = 1f - v.x; v.y = 1f - v.y; v.z = 0.01f; }

        Vector2 c = new Vector2(0.5f, 0.5f);
        Vector2 to = new Vector2(v.x, v.y) - c;
        if (to.sqrMagnitude < 1e-6f) to = new Vector2(_dashDir != 0 ? _dashDir : 1, 0);
        Vector2 dir = to.normalized;

        float inset = Mathf.Clamp(indicatorEdgeInset, 0f, 0.49f);
        float maxX = 0.5f - inset;
        float maxY = 0.5f - inset;
        float tX = (Mathf.Abs(dir.x) > 1e-6f) ? (maxX / Mathf.Abs(dir.x)) : float.PositiveInfinity;
        float tY = (Mathf.Abs(dir.y) > 1e-6f) ? (maxY / Mathf.Abs(dir.y)) : float.PositiveInfinity;
        float t = Mathf.Min(tX, tY);

        Vector2 edge = c + dir * t;

        ShowIndicator();
        if (_cam.orthographic)
        {
            float h = 2f * _cam.orthographicSize;
            float w = h * _cam.aspect;
            Vector3 local = new Vector3((edge.x - 0.5f) * w, (edge.y - 0.5f) * h, 0f);
            _indicatorGO.transform.localPosition = local;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _indicatorGO.transform.localRotation = Quaternion.Euler(0, 0, ang);
        }
        else
        {
            float depth = Mathf.Max(1f, _cam.nearClipPlane + 0.5f);
            Vector3 world = _cam.ViewportToWorldPoint(new Vector3(edge.x, edge.y, depth));
            _indicatorGO.transform.position = world;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _indicatorGO.transform.rotation = Quaternion.Euler(0, 0, ang);
        }

        _indicatorSR.color = indicatorColor;
    }

    private static bool IsBoundsInCameraView(Camera cam, Bounds b)
    {
        Vector3[] cs = new Vector3[4] {
            new Vector3(b.min.x, b.min.y, b.center.z),
            new Vector3(b.min.x, b.max.y, b.center.z),
            new Vector3(b.max.x, b.min.y, b.center.z),
            new Vector3(b.max.x, b.max.y, b.center.z),
        };
        for (int i = 0; i < cs.Length; i++)
        {
            var v = cam.WorldToViewportPoint(cs[i]);
            if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f)
                return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        float r = Application.isPlaying ? DetectRadiusNow : detectRadius;
        Gizmos.DrawWireSphere(transform.position, r);
    }

    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (anim != null) { anim.Play(key, forceRestart); return; }
        if (animator != null) animator.Play(key, 0, 0f);
    }

    public void OnHit(int damage) { StartDeathSequence("OnHit"); }
}

// ---- Utility ----
public static class SpriteUtilExt
{
    public static Sprite ToSprite(this Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                             new Vector2(0.5f, 0.5f), tex.width);
    }
}
