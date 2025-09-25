// ===================== P2PrincessSpriteTether.cs (Unity 6.1, Sparkle 통합) =====================
// - 렌더링: SpriteRenderer 직선 라인 (투명도 기본 50%, 굵기 기본 0.28)
// - 타겟: Princess 레이어 오브젝트의 "콜라이더 합산 바운즈 중앙"에 연결
// - 탐지: CircleCollider2D probe + ContactFilter2D + Physics2D.OverlapCollider(static)
// - Sparkle: 라인 근처 랜덤 위치에 스폰, 2초 후 비활성(풀링). 색/크기/스폰 간격 조절 가능
// - 금지 API 미사용: OverlapCircleNonAlloc, (instance)OverlapCollider

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class P2PrincessSpriteTether : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private string princessLayerName = "Princess";
    [SerializeField, Min(0f)] private float searchRadius = 12f;
    [SerializeField, Min(0f)] private float detachHysteresis = 1.0f;
    [SerializeField] private bool requireLineOfSight = false;
    [SerializeField] private LayerMask losObstacleMask = 0;
    [SerializeField, Min(0f)] private float losSkin = 0.02f;

    [Header("Sprite Line")]
    [Tooltip("라인 스프라이트(없으면 1x1 화이트 런타임 생성)")]
    [SerializeField] private Sprite lineSprite;
    [Tooltip("라인 색상(기본 알파 0.5)")]
    [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.5f);
    [Tooltip("라인 두께(월드 유닛)")]
    [SerializeField, Min(0.001f)] private float lineThickness = 0.28f;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 0;

    [Header("Anchors")]
    [Tooltip("시작점 기준(없으면 본인 Transform)")]
    [SerializeField] private Transform startAnchor;
    [SerializeField] private Vector3 startOffset = Vector3.zero;

    [Header("Sparkle FX")]
    [Tooltip("반짝이 프리팹(없으면 기본 1x1 화이트 스프라이트 생성)")]
    [SerializeField] private GameObject sparklePrefab;
    [SerializeField, Min(1)] private int sparklePoolSize = 24;
    [SerializeField, Min(0.01f)] private float sparkleLifetime = 2f;
    [Tooltip("스폰 간격 랜덤 범위(초)")]
    [SerializeField] private Vector2 sparkleSpawnIntervalRange = new Vector2(0.08f, 0.25f);
    [Tooltip("라인에 수직 방향 최대 오프셋(월드 유닛)")]
    [SerializeField] private float sparklePerpRadius = 0.35f;
    [Tooltip("라인 방향으로의 소폭 지터(월드 유닛)")]
    [SerializeField] private float sparkleAlongJitter = 0.15f;
    [Tooltip("스케일 랜덤 범위")]
    [SerializeField] private Vector2 sparkleScaleRange = new Vector2(0.6f, 1.25f);
    [Tooltip("반짝이 색상(알파는 개별 페이드로 처리)")]
    [SerializeField] private Color sparkleColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private bool sparklesEnabled = true;

    [Header("Runtime (read-only)")]
    [SerializeField] private Transform currentTarget;

    // Internal: detection
    private int _princessMask;
    private CircleCollider2D _probe;
    private readonly List<Collider2D> _overlaps = new List<Collider2D>(64);
    private ContactFilter2D _filter;

    // Internal: line
    private GameObject _lineGO;
    private SpriteRenderer _sr;
    private Vector3 _lastA, _lastB;
    private bool _lineVisible;

    // Internal: sparkle
    private Transform _sparkleRoot;
    private readonly List<GameObject> _sparklePool = new List<GameObject>(64);
    private int _poolCursor;
    private float _nextSparkleTime;

    void Awake()
    {
        ResolvePrincessMask();
        EnsureProbe();
        EnsureSpriteLine();
        EnsureSparklePool();
        BuildFilter();
    }

    void OnValidate()
    {
        ResolvePrincessMask();
        if (_probe != null) _probe.radius = searchRadius;

        if (_sr == null && Application.isEditor && !Application.isPlaying)
            EnsureSpriteLine();

        if (_sr != null)
        {
            _sr.color = lineColor;
            _sr.sortingLayerName = sortingLayerName;
            _sr.sortingOrder = sortingOrder;
        }

        BuildFilter();
    }

    void LateUpdate()
    {
        Vector3 startPos = GetStartPos();

        if (_probe != null)
        {
            _probe.transform.position = startPos;
            _probe.radius = searchRadius;
        }

        UpdateTarget(startPos);
        RenderLine(startPos, currentTarget);
        UpdateSparkles();
    }

    // ───────── Detection ─────────

    private Vector3 GetStartPos()
    {
        var t = startAnchor ? startAnchor : transform;
        return t.position + t.TransformVector(startOffset);
    }

    private void UpdateTarget(Vector3 startPos)
    {
        if (currentTarget != null &&
            !IsTargetStillValid(currentTarget, startPos, searchRadius + detachHysteresis))
        {
            currentTarget = null;
        }

        if (currentTarget == null)
        {
            _overlaps.Clear();
            int count = (_probe != null) ? Physics2D.OverlapCollider(_probe, _filter, _overlaps) : 0;

            Transform best = null;
            float bestDistSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = _overlaps[i];
                if (!col) continue;

                Vector3 center = GetColliderGroupCenter(col.transform);
                float d2 = (center - startPos).sqrMagnitude;

                if (d2 < bestDistSqr)
                {
                    if (!requireLineOfSight || HasLineOfSight(startPos, center))
                    {
                        bestDistSqr = d2;
                        best = col.transform;
                    }
                }
            }

            currentTarget = best;
        }
    }

    private bool IsTargetStillValid(Transform t, Vector3 startPos, float maxDist)
    {
        if (t == null) return false;
        if (((1 << t.gameObject.layer) & _princessMask) == 0) return false;

        Vector3 center = GetColliderGroupCenter(t);
        if (Vector3.Distance(startPos, center) > maxDist) return false;

        if (requireLineOfSight && !HasLineOfSight(startPos, center)) return false;
        return true;
    }

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector2 dir = (to - from);
        float dist = dir.magnitude;
        if (dist <= Mathf.Epsilon) return true;

        var hit = Physics2D.Raycast(from + (Vector3)(dir.normalized * losSkin),
                                    dir.normalized,
                                    Mathf.Max(0f, dist - 2 * losSkin),
                                    losObstacleMask);
        return hit.collider == null;
    }

    private Vector3 GetColliderGroupCenter(Transform t)
    {
        var cols = t.GetComponentsInChildren<Collider2D>(true);
        if (cols != null && cols.Length > 0)
        {
            Bounds b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b.center;
        }

        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs != null && rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.center;
        }

        return t.position;
    }

    // ───────── Line Rendering (Straight) ─────────

    private void RenderLine(Vector3 startPos, Transform target)
    {
        if (_sr == null) EnsureSpriteLine();

        if (target == null)
        {
            _sr.enabled = false;
            _lineVisible = false;
            return;
        }

        Vector3 endPos = GetColliderGroupCenter(target);

        Vector3 dir = endPos - startPos;
        float length = dir.magnitude;

        if (length < 0.0001f)
        {
            _sr.enabled = false;
            _lineVisible = false;
            return;
        }

        Vector3 mid = (startPos + endPos) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        _lineGO.transform.position = mid;
        _lineGO.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (_sr.drawMode != SpriteDrawMode.Simple)
        {
            _sr.size = new Vector2(length, lineThickness);
            _lineGO.transform.localScale = Vector3.one;
        }
        else
        {
            Vector2 spriteSize = (_sr.sprite != null) ? _sr.sprite.bounds.size : new Vector2(1f, 1f);
            float sx = (spriteSize.x > 0f) ? (length / spriteSize.x) : length;
            float sy = (spriteSize.y > 0f) ? (lineThickness / spriteSize.y) : lineThickness;
            _lineGO.transform.localScale = new Vector3(sx, sy, 1f);
        }

        _sr.enabled = true;
        _lineVisible = true;
        _lastA = startPos;
        _lastB = endPos;
    }

    private void EnsureSpriteLine()
    {
        if (_lineGO == null)
        {
            _lineGO = new GameObject("PrincessTetherSprite");
            _lineGO.transform.SetParent(transform, false);
        }

        if (_sr == null)
            _sr = _lineGO.AddComponent<SpriteRenderer>();

        if (lineSprite == null)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.name = "PTether_White_1x1";
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            lineSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        _sr.sprite = lineSprite;
        _sr.color = (lineColor.a <= 0f) ? new Color(lineColor.r, lineColor.g, lineColor.b, 0.5f) : lineColor;
        _sr.sortingLayerName = sortingLayerName;
        _sr.sortingOrder = sortingOrder;
        _sr.drawMode = SpriteDrawMode.Tiled;
        _sr.enabled = false;
    }

    // ───────── Sparkles ─────────

    private void UpdateSparkles()
    {
        if (!sparklesEnabled || !_lineVisible) return;

        if (Time.time >= _nextSparkleTime)
        {
            SpawnOneSparkleNearLine();
            float interval = Random.Range(sparkleSpawnIntervalRange.x, sparkleSpawnIntervalRange.y);
            _nextSparkleTime = Time.time + Mathf.Max(0.01f, interval);
        }
    }

    private void EnsureSparklePool()
    {
        if (_sparkleRoot == null)
        {
            var root = new GameObject("PrincessTetherSparkles");
            root.transform.SetParent(transform, false);
            _sparkleRoot = root.transform;
        }

        if (sparklePrefab == null)
        {
            // 기본 1x1 스프라이트 사각형으로 대체
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.name = "PTether_Sparkle_White_1x1";
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            var spr = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            var go = new GameObject("DefaultSparkle");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.color = sparkleColor;
            go.AddComponent<PTetherSparkle>().lifetime = sparkleLifetime;
            go.SetActive(false);
            sparklePrefab = go;
        }

        // 프리팹이 PTetherSparkle을 보장
        if (sparklePrefab.GetComponent<PTetherSparkle>() == null)
            sparklePrefab.AddComponent<PTetherSparkle>().lifetime = sparkleLifetime;

        // 풀 구성
        _sparklePool.Clear();
        for (int i = 0; i < sparklePoolSize; i++)
        {
            var inst = Instantiate(sparklePrefab, _sparkleRoot);
            var sp = inst.GetComponent<PTetherSparkle>();
            sp.lifetime = sparkleLifetime;
            var sr = inst.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = sparkleColor;
            inst.SetActive(false);
            _sparklePool.Add(inst);
        }
        _poolCursor = 0;
    }

    private void SpawnOneSparkleNearLine()
    {
        if (_sparklePool.Count == 0) return;

        var go = NextSparkleFromPool();
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = sparkleColor;

        Vector3 a = _lastA;
        Vector3 b = _lastB;
        Vector3 dir = (b - a);
        float len = dir.magnitude;
        if (len < 0.0001f) return;

        Vector3 dirN = dir / len;
        Vector3 perp = Vector3.Cross(dirN, Vector3.forward);

        float t = Random.value; // 0~1
        Vector3 basePos = Vector3.Lerp(a, b, t);
        float along = Random.Range(-sparkleAlongJitter, sparkleAlongJitter);
        float perpOff = Random.Range(-sparklePerpRadius, sparklePerpRadius);

        Vector3 pos = basePos + dirN * along + perp * perpOff;
        go.transform.position = pos;

        float ang = Random.Range(0f, 360f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, ang);

        float s = Random.Range(sparkleScaleRange.x, sparkleScaleRange.y);
        go.transform.localScale = new Vector3(s, s, 1f);

        var sp = go.GetComponent<PTetherSparkle>();
        sp.lifetime = sparkleLifetime;
        go.SetActive(true);
        sp.Replay();
    }

    private GameObject NextSparkleFromPool()
    {
        // 순환 커서 방식
        for (int i = 0; i < _sparklePool.Count; i++)
        {
            _poolCursor = (_poolCursor + 1) % _sparklePool.Count;
            if (!_sparklePool[_poolCursor].activeSelf)
                return _sparklePool[_poolCursor];
        }
        // 전부 사용 중이면 가장 오래된 것 재사용
        _poolCursor = (_poolCursor + 1) % _sparklePool.Count;
        return _sparklePool[_poolCursor];
    }

    // ───────── Setup / Utils ─────────

    private void EnsureProbe()
    {
        if (_probe != null) return;
        var go = new GameObject("PrincessDetectProbe2D");
        go.transform.SetParent(transform, false);
        _probe = go.AddComponent<CircleCollider2D>();
        _probe.isTrigger = true;
        _probe.radius = searchRadius;
    }

    private void BuildFilter()
    {
        _filter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = true
        };
        _filter.SetLayerMask(_princessMask);
    }

    private void ResolvePrincessMask()
    {
        if (string.IsNullOrWhiteSpace(princessLayerName)) { _princessMask = 0; return; }
        string[] names = princessLayerName.Split(',');
        for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
        _princessMask = LayerMask.GetMask(names);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = GetStartPos();
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.3f);
        Gizmos.DrawWireSphere(p, searchRadius);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.2f);
        Gizmos.DrawWireSphere(p, searchRadius + detachHysteresis);
    }
}

// ===================== PTetherSparkle (내부 반짝이 컴포넌트) =====================
// - 활성화되면 lifetime 동안 알파 페이드 후 자동 비활성
// - 파티클 없이 SpriteRenderer만으로 처리(프리팹에서 SpriteRenderer 권장)

sealed class PTetherSparkle : MonoBehaviour
{
    [SerializeField, Min(0.01f)] public float lifetime = 2f;

    private float _t;
    private SpriteRenderer _sr;
    private Color _base;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _base = _sr.color;
    }

    void OnEnable()
    {
        Replay();
    }

    public void Replay()
    {
        _t = lifetime;
        if (_sr != null) _base = _sr.color;
    }

    void Update()
    {
        _t -= Time.deltaTime;
        if (_sr != null)
        {
            float k = Mathf.Clamp01(_t / lifetime);
            var c = _base;
            c.a = _base.a * k; // 선형 페이드
            _sr.color = c;
        }
        if (_t <= 0f) gameObject.SetActive(false);
    }
}
