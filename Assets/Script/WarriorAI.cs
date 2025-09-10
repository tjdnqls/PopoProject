// ===================== ChargerSentinelAI.cs (Monkill death + blood) =====================
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class ChargerSentinelAI : MonoBehaviour
{
    public enum State { Idle, Preparing, Dashing, Recover, Dead }

    // ---------- Animation ----------
    [Header("Animation")]
    public SpriteAnimationManager anim;  // 없으면 무시
    public Animator animator;            // 없으면 무시
    [SerializeField] private string idleAnim = "Idle";
    [SerializeField] private string prepareAnim = "deshStay";
    [SerializeField] private string dashAnim = "Dash";
    [SerializeField] private string hitAnim = "Hit";
    [SerializeField] private string deathAnim = "Death";

    // ---------- Layers / Refs ----------
    [Header("Layers")]
    [SerializeField] private string groundLayerName = "Ground";
    [SerializeField] private string monsterLayerName = "Monster";
    [SerializeField] private string monkillLayerName = "Monkill"; // 즉사용

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;

    // ---------- Detect / Timings ----------
    [Header("Detect")]
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float detectRadius = 4f;
    [SerializeField] private float verticalTolerance = 2.0f;

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

    // ---------- Death / Blood ----------
    [Header("Death")]
    [SerializeField] private float despawnDelay = 5f;    // 요청: 5초 뒤 파괴
    [SerializeField] private float feetYOffset = 0.05f;

    [Header("Death VFX (Blood)")]
    [SerializeField] private GameObject blood0Prefab;     // Blood_0
    [SerializeField] private GameObject blood1Prefab;     // Blood_1
    [SerializeField] private int burstBloodCount = 10;    // 즉시 터뜨릴 개수
    [SerializeField] private float burstRadius = 0.35f;
    [SerializeField] private Vector2 burstSpeedRange = new Vector2(1.2f, 3.0f);
    [SerializeField] private float sustainDelay = 0.0f;   // Death 시작과 동시에 뿜게 0
    [SerializeField] private float sustainDuration = 5.0f;// death 동안 지속(=despawnDelay)
    [SerializeField] private Vector2 sustainIntervalRange = new Vector2(0.06f, 0.20f);
    [SerializeField] private float sustainJitter = 0.06f;
    [SerializeField] private float bloodLifetime = 3.0f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawKillBox = false;

    private State state;
    private Transform currentTarget;
    private float _timer;
    private int _dashDir, _plannedDashDir;
    private bool _mustDashOnce;
    private Color _baseColor = Color.white;

    private int _groundLayer, _monsterLayer, _monkillLayer, _myLayer;
    private static readonly Collider2D[] _buf = new Collider2D[8];
    private static readonly Collider2D[] _killBuf = new Collider2D[16];

    private GameObject _previewGO;
    private SpriteRenderer _previewSR;

    // death lock
    private bool isDying = false;
    private Vector3 _deathPos;
    private Quaternion _deathRot;
    private Vector3 _deathFeetPos;

    // ---------- Unity ----------
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
        _baseColor = sr ? sr.color : Color.white;

        _groundLayer = LayerMask.NameToLayer(groundLayerName);
        _monsterLayer = LayerMask.NameToLayer(monsterLayerName);
        _monkillLayer = LayerMask.NameToLayer(monkillLayerName);
        _myLayer = gameObject.layer;

        // 내 레이어는 Ground & Monkill만 충돌, 나머지는 전부 무시
        for (int i = 0; i < 32; i++)
        {
            bool allow = (i == _groundLayer) || (i == _monkillLayer);
            Physics2D.IgnoreLayerCollision(_myLayer, i, !allow);
        }

        state = State.Idle;
        EnsurePreviewObject();
        HidePreview();
    }

    private void OnEnable()
    {
        state = State.Idle;
        _timer = 0f;
        _mustDashOnce = false;
        _plannedDashDir = 0;
        isDying = false;
        if (sr) sr.color = _baseColor;
        PlayAnim(idleAnim, true);
        HidePreview();
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

        if (state == State.Dashing) KillSweepAhead();
    }

    private void LateUpdate()
    {
        if (state == State.Dead)
        {
            // 위치/회전 고정
            transform.position = _deathPos;
            transform.rotation = _deathRot;
        }
    }

    // ---------- State Ticks ----------
    private void TickIdle()
    {
        StopHorizontal();
        if (TryDetectNearest(out Transform t))
        {
            currentTarget = t;
            EnterPrepare();
        }
    }

    private void TickPreparing()
    {
        StopHorizontal();
        if (!StillValidTarget() && !_mustDashOnce) { EnterIdle(); return; }

        if (StillValidTarget()) _plannedDashDir = (currentTarget.position.x >= transform.position.x) ? +1 : -1;
        else if (_plannedDashDir == 0) _plannedDashDir = (sr && sr.flipX) ? -1 : +1;

        _timer += Time.fixedDeltaTime;
        if (sr)
        {
            float a = Mathf.Clamp01(_timer / prepareSeconds) * prepareTintStrength;
            sr.color = Color.Lerp(_baseColor, prepareColor, a);
            sr.flipX = (_plannedDashDir < 0);
        }
        if (showDashPreview) UpdateDashPreview(_plannedDashDir);
        if (_timer >= prepareSeconds) EnterDash();
    }

    private void TickDashing()
    {
        Vector2 v = rb.linearVelocity; v.x = _dashDir * dashSpeed; rb.linearVelocity = v;
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

    // ---------- Transitions ----------
    private void EnterIdle()
    {
        state = State.Idle; _timer = 0f; _mustDashOnce = false; _plannedDashDir = 0;
        if (sr) sr.color = _baseColor;
        HidePreview(); currentTarget = null;
        PlayAnim(idleAnim);
    }

    private void EnterPrepare()
    {
        state = State.Preparing; _timer = 0f; _mustDashOnce = true;
        _plannedDashDir = (currentTarget && currentTarget.position.x < transform.position.x) ? -1 : +1;
        if (sr) sr.flipX = (_plannedDashDir < 0);
        if (showDashPreview) ShowPreview();
        PlayAnim(prepareAnim, true);
    }

    private void EnterDash()
    {
        if (!StillValidTarget() && !_mustDashOnce) { EnterIdle(); return; }
        state = State.Dashing; _timer = 0f;
        _dashDir = (_plannedDashDir != 0) ? _plannedDashDir
                 : (currentTarget && currentTarget.position.x >= transform.position.x ? +1 : -1);
        if (sr) sr.flipX = (_dashDir < 0);
        Vector2 v = rb.linearVelocity; v.x = _dashDir * dashSpeed; rb.linearVelocity = v;
        if (sr) sr.color = _baseColor;
        _mustDashOnce = false;
        HidePreview();
        CameraShaker.Shake(0.4f, 0.2f);
        PlayAnim(dashAnim, true);
    }

    private void EnterRecover()
    {
        state = State.Recover; _timer = 0f;
        StopHorizontal(); HidePreview();
        PlayAnim(idleAnim);
    }

    // ---------- Helpers ----------
    private void StopHorizontal()
    {
        Vector2 v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
    }

    private bool StillValidTarget()
    {
        if (!currentTarget) return false;
        if (((1 << currentTarget.gameObject.layer) & playerMask) == 0) return false;
        if (Vector2.Distance(currentTarget.position, transform.position) > detectRadius) return false;
        if (Mathf.Abs(currentTarget.position.y - transform.position.y) > verticalTolerance) return false;
        return true;
    }

    private bool TryDetectNearest(out Transform t)
    {
        t = null;
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = playerMask, useTriggers = true };
        int n = Physics2D.OverlapCircle((Vector2)transform.position, detectRadius, filter, _buf);
        if (n <= 0) return false;
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = _buf[i]; if (!c) continue;
            Transform cand = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            float vy = Mathf.Abs(cand.position.y - transform.position.y); if (vy > verticalTolerance) continue;
            float d = ((Vector2)cand.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; t = cand; }
        }
        return t != null;
    }

    // ---------- Kill sweep without collision (for Monster layer) ----------
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

    // ---------- Monkill 즉사 처리 ----------
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

        // 스냅샷 (위치 고정)
        _deathPos = transform.position;
        _deathRot = transform.rotation;
        _deathFeetPos = GetFeetWorld();

        // 모든 동작/표시 정리
        StopHorizontal();
        HidePreview();
        if (sr) sr.color = Color.white;

        // 즉시 혈흔 버스트(중앙에서)
        SpawnBloodBurst(GetBodyCenter(), burstBloodCount);

        // 발 지속 분출
        if ((blood0Prefab || blood1Prefab) && sustainDuration > 0f)
            StartCoroutine(FootBloodSustain());

        // 물리 봉인
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.simulated = false;
        }
        if (body) body.enabled = false;

        // Hit → Death 연출
        StartCoroutine(DeathAnimThenDespawn());
    }

    private IEnumerator DeathAnimThenDespawn()
    {
        PlayAnim(hitAnim, true);
        yield return null;              // 한 프레임만 보여주고
        PlayAnim(deathAnim, true);

        float t = 0f;
        while (t < despawnDelay) { t += Time.deltaTime; yield return null; }
        Destroy(gameObject);
    }

    // ---------- Blood helpers ----------
    private Vector3 GetBodyCenter()
    {
        if (body != null) return body.bounds.center;
        return transform.position;
    }
    private Vector3 GetFeetWorld()
    {
        if (body != null)
        {
            var b = body.bounds;
            return new Vector3(2f + feetYOffset, transform.position.z);
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
            float spd = Random.Range(burstSpeedRange.x * 0.6f, burstSpeedRange.y);

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

    // ---------- Preview ----------
    private void EnsurePreviewObject()
    {
        if (_previewGO) return;
        _previewGO = new GameObject("DashPreview");
        _previewGO.transform.SetParent(transform, false);
        _previewSR = _previewGO.AddComponent<SpriteRenderer>();

        var tex = Texture2D.whiteTexture;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
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

        Vector3 center = new Vector3(
            b.center.x + dirSign * (dist * 0.5f),
            b.center.y, b.center.z
        );

        var ps = GetParentScaleAbs();
        float safeX = (ps.x <= 0.0001f) ? 1f : ps.x;
        float safeY = (ps.y <= 0.0001f) ? 1f : ps.y;

        _previewGO.transform.position = center;
        _previewGO.transform.rotation = Quaternion.identity;
        _previewGO.transform.localScale = new Vector3(widthWorld / safeX, heightWorld / safeY, 1f);
        if (_previewSR) _previewSR.color = new Color(1f, 0f, 0f, previewAlpha);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }

    // ---------- Animation helper ----------
    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (anim != null) { anim.Play(key, forceRestart); return; }
        if (animator != null) animator.Play(key, 0, 0f);
    }

    // 외부에서 즉사시키고 싶을 때 쓸 수 있는 메시지 훅(선택)
    public void OnHit(int damage) { StartDeathSequence("OnHit"); }
}
