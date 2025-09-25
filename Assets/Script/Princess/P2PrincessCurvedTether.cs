// ===================== P2PrincessSpriteTether.cs =====================
// Unity 6.1 / Multi-target / World-space VFX root
// - Guide Line(매우 낮은 알파), LightBeam "~"(거리 비례 알파 & ≤1.5f 파란색), Near-line Sparkle(≤1.5f 파란색)
// - Outline Ring Sparkle: 콜라이더 엣지 "시계 방향(CW)" 공전 → 플레이어 거리/위치 변화와 무관하게 연속 회전
//   ▸ 거리가 ≤ nearBlueDistance 가 되면 즉시 "고정(Freeze)" : 현재 엣지 위치에 멈춰서 유지(회전 종료, 파란색)
//   ▸ 거리가 > nearBlueDistance + ringUnfreezeMargin 이 되면 다시 공전 재개
// - SFX: PrincessMagic (전역 3초 쿨다운, “어느 시점에든 ≥ outer” 이후 “≤ inner”로 들어오면 1회 재생)
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

    [Header("Anchors")]
    [SerializeField] private Transform startAnchor;
    [SerializeField] private Vector3 startOffset = Vector3.zero;

    [Header("Guide Sprite Line (almost invisible)")]
    [SerializeField] private Sprite lineSprite;
    [SerializeField] private Color lineColor = new(1f, 1f, 1f, 0.08f);
    [SerializeField, Min(0.001f)] private float lineThickness = 0.28f;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 0;

    [Header("Near-line Sparkle")]
    [SerializeField] private GameObject sparklePrefab;
    [SerializeField, Min(0)] private int sparklePoolPerTarget = 8;
    [SerializeField, Min(0.01f)] private float sparkleLifetime = 2f;
    [SerializeField] private Vector2 sparkleSpawnIntervalRange = new(0.08f, 0.25f);
    [SerializeField] private float sparklePerpRadius = 0.35f;
    [SerializeField] private float sparkleAlongJitter = 0.15f;
    [SerializeField] private Vector2 sparkleScaleRange = new(0.6f, 1.25f);
    [SerializeField] private Color sparkleColor = Color.white;
    [SerializeField] private bool sparklesEnabled = true;

    [Header("Light Beams ~")]
    [SerializeField] private GameObject beamPrefab;
    [SerializeField, Min(0)] private int beamPoolPerTarget = 6;
    [SerializeField] private Vector2 beamSpawnIntervalRange = new(0.12f, 0.35f);
    [SerializeField] private Vector2 beamSpeedRange = new(3.5f, 6.5f);
    [SerializeField] private Vector2 beamAmplitudeRange = new(0.06f, 0.18f);
    [SerializeField] private Vector2 beamWavelengthRange = new(1.0f, 2.6f);
    [SerializeField, Min(0.02f)] private float beamLength = 1.15f;
    [SerializeField, Min(0.02f)] private float beamThickness = 0.22f;
    [SerializeField] private Color beamColor = new(1f, 0.95f, 0.7f, 0.95f);
    [SerializeField, Range(0f, 0.5f)] private float beamFadeInFrac = 0.22f;
    [SerializeField, Range(0f, 0.5f)] private float beamFadeOutFrac = 0.28f;
    [SerializeField, Range(0f, 0.3f)] private float beamColorVariance = 0.08f;
    [SerializeField] private bool beamsEnabled = true;

    [Header("Beam Trail Sparkle (from beams)")]
    [SerializeField] private GameObject beamTrailPrefab;
    [SerializeField, Min(0)] private int beamTrailPoolPerTarget = 12;
    [SerializeField] private Vector2 beamTrailSpawnIntervalRange = new(0.02f, 0.06f);
    [SerializeField] private Vector2 beamTrailScaleRange = new(0.35f, 0.7f);
    [SerializeField, Min(0.05f)] private float beamTrailLifetime = 0.35f;
    [SerializeField] private Color beamTrailColor = new(1f, 0.95f, 0.75f, 0.92f);
    [SerializeField] private bool beamTrailEnabled = true;

    [Header("Outline Ring Sparkle (around target collider)")]
    [SerializeField] private GameObject ringSparklePrefab;
    [SerializeField, Min(1)] private int ringSampleCount = 28; // 풀 크기(=항상 표시 수)
    [SerializeField] private Vector2 ringScaleRange = new(0.7f, 1.2f);
    [SerializeField] private Color ringSparkleColor = new(1f, 0.95f, 0.75f, 0.9f);
    [SerializeField] private Vector2 ringFlickerInterval = new(0.3f, 0.8f);
    [SerializeField] private bool ringEnabled = true;

    [Header("Outline Ring Motion (Clockwise)")]
    [SerializeField] private Vector2 ringOrbitSpeedRange = new(0.6f, 1.4f); // world units / sec (CW)
    [SerializeField] private bool ringAlignToTangent = true;                // 접선 정렬
    [SerializeField, Min(1f)] private float ringPathQualityMul = 4f;        // 경로 샘플 배수
    [SerializeField, Min(0f)] private float ringUnfreezeMargin = 0.1f;      // 재가동 마진

    [Header("Distance Response")]
    [SerializeField, Range(0f, 1f)] private float beamAlphaFar = 0.15f;
    [SerializeField, Range(0f, 1f)] private float beamAlphaNear = 1.0f;
    [SerializeField, Min(0.1f)] private float beamAlphaExpo = 1.0f;

    [SerializeField, Range(0f, 1f)] private float ringAlphaFar = 0.25f;
    [SerializeField, Range(0f, 1f)] private float ringAlphaNear = 1.0f;
    [SerializeField, Min(0.1f)] private float ringAlphaExpo = 1.0f;

    [Header("Blue Near Visuals (≤ this distance)")]
    [SerializeField, Min(0.01f)] private float nearBlueDistance = 1.5f;
    [SerializeField] private Color nearBlueColor = new(0.45f, 0.75f, 1f, 1f);

    [Header("PrincessMagic SFX")]
    [SerializeField] private string princessMagicSfxKey = "PrincessMagic";
    [SerializeField, Min(0.01f)] private float princessMagicInner = 1.5f; // ≤ 이 거리로 들어오면
    [SerializeField, Min(0.01f)] private float princessMagicOuter = 2.0f; // 그 전에 ≥ 이 거리를 한 번이라도 넘었어야 함
    [SerializeField, Min(0.01f)] private float princessMagicCooldown = 3.0f; // 전역 쿨다운

    // detection internals
    private int _princessMask;
    private CircleCollider2D _probe;
    private readonly List<Collider2D> _overlaps = new(64);
    private ContactFilter2D _filter;

    // per-target
    private readonly Dictionary<Transform, TetherInstance> _tethers = new();

    // world vfx root
    private static Transform _worldVfxRoot;

    // sfx global cooldown
    private float _nextPrincessMagicTime = 0f;

    void Awake()
    {
        ResolvePrincessMask();
        EnsureProbe();
        EnsureWorldVfxRoot();
        BuildFilter();
    }

    void OnValidate()
    {
        ResolvePrincessMask();
        if (_probe != null) _probe.radius = searchRadius;
        BuildFilter();
    }

    void LateUpdate()
    {
        Vector3 startPos = GetStartPos();
        if (_probe != null) { _probe.transform.position = startPos; _probe.radius = searchRadius; }

        SyncTargets(startPos);

        foreach (var kv in _tethers)
            kv.Value.TickAll(startPos, this);
    }

    void OnDisable()
    {
        foreach (var kv in _tethers) kv.Value.SetActive(false);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = GetStartPos();
        Gizmos.color = new(0.2f, 0.6f, 1f, 0.3f); Gizmos.DrawWireSphere(p, searchRadius);
        Gizmos.color = new(1f, 0.6f, 0.2f, 0.2f); Gizmos.DrawWireSphere(p, searchRadius + detachHysteresis);
    }

    // ---------- Detection ----------
    private Vector3 GetStartPos()
    {
        var t = startAnchor ? startAnchor : transform;
        return t.position + t.TransformVector(startOffset);
    }

    private void SyncTargets(Vector3 startPos)
    {
        _overlaps.Clear();
        if (_probe != null) Physics2D.OverlapCollider(_probe, _filter, _overlaps);

        var found = new HashSet<Transform>();
        for (int i = 0; i < _overlaps.Count; i++)
        {
            var col = _overlaps[i]; if (!col) continue;
            var root = col.transform;
            if (!requireLineOfSight || HasLineOfSight(startPos, GetColliderGroupCenter(root)))
                found.Add(root);
        }

        var toRemove = new List<Transform>();
        foreach (var kv in _tethers)
        {
            var t = kv.Key;
            if (t == null) { toRemove.Add(t); continue; }

            if (!found.Contains(t))
            {
                Vector3 c = GetColliderGroupCenter(t);
                bool ok = (Vector3.Distance(startPos, c) <= searchRadius + detachHysteresis) &&
                          (!requireLineOfSight || HasLineOfSight(startPos, c));
                if (!ok) toRemove.Add(t);
            }
        }
        foreach (var t in toRemove)
        {
            if (_tethers.TryGetValue(t, out var ti)) ti.Destroy();
            _tethers.Remove(t);
        }

        foreach (var t in found)
        {
            if (!_tethers.TryGetValue(t, out var ti))
            {
                ti = new TetherInstance(t, this);
                _tethers.Add(t, ti);
            }
            ti.SetActive(true);
        }
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
            Bounds b = cols[0].bounds; for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b.center;
        }
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs != null && rs.Length > 0)
        {
            Bounds b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.center;
        }
        return t.position;
    }

    private void ResolvePrincessMask()
    {
        if (string.IsNullOrWhiteSpace(princessLayerName)) { _princessMask = 0; return; }
        string[] names = princessLayerName.Split(',');
        for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();
        _princessMask = LayerMask.GetMask(names);

        _filter = new ContactFilter2D { useLayerMask = true, useTriggers = true };
        _filter.SetLayerMask(_princessMask);
    }

    private void EnsureProbe()
    {
        if (_probe != null) return;
        var go = new GameObject("PrincessDetectProbe2D");
        go.transform.SetParent(null, true);
        _probe = go.AddComponent<CircleCollider2D>();
        _probe.isTrigger = true; _probe.radius = searchRadius;
    }

    private static void EnsureWorldVfxRoot()
    {
        if (_worldVfxRoot != null) return;
        var root = GameObject.Find("P2PrincessVFXRoot");
        if (root == null) root = new GameObject("P2PrincessVFXRoot");
        _worldVfxRoot = root.transform; _worldVfxRoot.position = Vector3.zero;
    }

    private void BuildFilter()
    {
        _filter.useLayerMask = true; _filter.useTriggers = true;
        _filter.SetLayerMask(_princessMask);
    }

    // ---------- SFX Trigger (armed logic + global cooldown) ----------
    private void TryPlayPrincessMagicArmed(ref bool armed, float currDist, string targetName)
    {
        if (string.IsNullOrEmpty(princessMagicSfxKey)) return;

        // arming: 한 번이라도 outer 이상 떨어지면 무장
        if (currDist >= princessMagicOuter) { armed = true; return; }

        // 발사: 무장 상태 + inner 진입 + 전역 쿨다운 OK
        if (armed && currDist <= princessMagicInner && Time.time >= _nextPrincessMagicTime)
        {
            // ▶ PrincessMagic 재생 (테스트 로그)
            Debug.Log($"[PrincessMagic SFX] Play '{princessMagicSfxKey}' (target='{targetName}', dist={currDist:F2}, t={Time.time:F2})");
            // SoundManager(예시): 위치기반 재생이 있다면 startAnchor 기준으로, 없다면 this.transform
            var posTf = startAnchor != null ? startAnchor : transform;
            SoundManager.Play(princessMagicSfxKey, posTf); // <-- 프로젝트 사운드 매니저 시그니처에 맞게 사용

            _nextPrincessMagicTime = Time.time + princessMagicCooldown;
            armed = false; // 다시 outer로 나갔다가 들어올 때까지 재생 금지
        }
    }

    // ---------- Nested per-target ----------
    private class TetherInstance
    {
        public readonly Transform target;

        private readonly GameObject _lineGO;
        private readonly SpriteRenderer _lineSR;
        private readonly PTetherLineProxy _lineProxy;
        private Vector3 _lastA, _lastB;
        private bool _visible;

        private readonly List<GameObject> _sparkPool = new();
        private int _sparkCursor; private float _nextSparkTime;

        private readonly List<GameObject> _beamPool = new();
        private int _beamCursor; private float _nextBeamTime;

        private readonly List<GameObject> _trailPool = new();
        private int _trailCursor;

        private readonly List<GameObject> _ringPool = new();

        // Ring path (rebuilt only for target shape; offsets는 절대 재초기화하지 않음)
        private Vector3[] _ringPath;     // CW closed path
        private float[] _ringCum;        // cumulative lengths
        private float _ringTotalLen;

        // Ring animation state (index-aligned to pool)
        private float[] _ringOffsets;    // arc offset (m)
        private float[] _ringSpeeds;     // speed (m/s)
        private float[] _ringScales;     // cached scale
        private Vector3[] _ringFrozenPos;// freeze 위치 보관
        private bool _ringFrozen;        // 전체 고정 상태(블루 근접)
        private bool _ringReady;

        // SFX arming per-target
        private bool _sfxArmed;

        private bool _active;

        public TetherInstance(Transform target, P2PrincessSpriteTether owner)
        {
            this.target = target;

            _lineGO = new GameObject("PrincessTetherSprite_Line");
            _lineGO.transform.SetParent(_worldVfxRoot, true);
            _lineSR = _lineGO.AddComponent<SpriteRenderer>();
            _lineSR.sprite = owner.lineSprite ?? MakeWhite1x1("PTether_Line_1x1");
            _lineSR.color = owner.lineColor;
            _lineSR.sortingLayerName = owner.sortingLayerName;
            _lineSR.sortingOrder = owner.sortingOrder;
            _lineSR.drawMode = SpriteDrawMode.Tiled;
            _lineSR.enabled = false;

            _lineProxy = _lineGO.AddComponent<PTetherLineProxy>();

            if (owner.sparklePoolPerTarget > 0) BuildSparklePool(owner);
            if (owner.beamPoolPerTarget > 0) BuildBeamPool(owner);
            if (owner.beamTrailEnabled && owner.beamTrailPoolPerTarget > 0) BuildTrailPool(owner);
            if (owner.ringEnabled && owner.ringSampleCount > 0) BuildRingPool(owner);

            _active = true;
            _sfxArmed = false;
        }

        public void SetActive(bool on)
        {
            _active = on;
            if (!on)
            {
                _lineSR.enabled = false;
                foreach (var go in _sparkPool) if (go.activeSelf) go.SetActive(false);
                foreach (var go in _beamPool) if (go.activeSelf) go.SetActive(false);
                foreach (var go in _trailPool) if (go.activeSelf) go.SetActive(false);
                foreach (var go in _ringPool) if (go.activeSelf) go.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (_lineGO) Object.Destroy(_lineGO);
            foreach (var go in _sparkPool) if (go) Object.Destroy(go);
            foreach (var go in _beamPool) if (go) Object.Destroy(go);
            foreach (var go in _trailPool) if (go) Object.Destroy(go);
            foreach (var go in _ringPool) if (go) Object.Destroy(go);
        }

        public void TickAll(Vector3 startPos, P2PrincessSpriteTether owner)
        {
            if (!_active || target == null) { SetActive(false); return; }

            RenderLine(startPos, owner);

            if (owner.sparklesEnabled && _visible && owner.sparklePoolPerTarget > 0)
                TickNearSparkles(owner);

            if (owner.beamsEnabled && _visible && owner.beamPoolPerTarget > 0)
                TickBeams(owner);

            if (owner.ringEnabled && _visible && _ringPool.Count > 0)
                TickRing(owner);
        }

        // ----- line -----
        private void RenderLine(Vector3 startPos, P2PrincessSpriteTether owner)
        {
            Vector3 endPos = owner.GetColliderGroupCenter(target);
            Vector3 dir = endPos - startPos;
            float length = dir.magnitude;

            if (length < 0.0001f)
            {
                _lineSR.enabled = false; _visible = false;
                _lineProxy.isAlive = false; return;
            }

            Vector3 mid = (startPos + endPos) * 0.5f;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            _lineGO.transform.position = mid;
            _lineGO.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            _lineGO.transform.localScale = Vector3.one;

            if (_lineSR.drawMode != SpriteDrawMode.Simple)
                _lineSR.size = new Vector2(length, owner.lineThickness);
            else
            {
                Vector2 spriteSize = (_lineSR.sprite != null) ? _lineSR.sprite.bounds.size : new(1f, 1f);
                float sx = (spriteSize.x > 0f) ? (length / spriteSize.x) : length;
                float sy = (spriteSize.y > 0f) ? (owner.lineThickness / spriteSize.y) : owner.lineThickness;
                _lineGO.transform.localScale = new Vector3(sx, sy, 1f);
            }

            _lineSR.enabled = true; _visible = true;
            _lastA = startPos; _lastB = endPos;

            // closeness(0~1): 시각화용
            float closeness = (owner.searchRadius > 1e-5f)
                ? Mathf.Clamp01(1f - (length / owner.searchRadius))
                : 1f;

            _lineProxy.isAlive = true;
            _lineProxy.A = _lastA; _lineProxy.B = _lastB; _lineProxy.closeness = closeness;

            // SFX (armed logic)
            owner.TryPlayPrincessMagicArmed(ref _sfxArmed, length, target.name);
        }

        // ----- near-line sparkles -----
        private void BuildSparklePool(P2PrincessSpriteTether owner)
        {
            var prefab = owner.sparklePrefab ?? MakeDefaultSpritePrefab("DefaultSparkle", owner.sparkleColor);
            EnsureComponent<PTetherSparkle>(prefab).lifetime = owner.sparkleLifetime;

            for (int i = 0; i < owner.sparklePoolPerTarget; i++)
            {
                var inst = Object.Instantiate(prefab, _worldVfxRoot);
                var sr = inst.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = owner.sparkleColor;
                    sr.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                    sr.sortingOrder = owner.sortingOrder + 1;
                }
                inst.SetActive(false); _sparkPool.Add(inst);
            }
            _sparkCursor = 0;
        }

        private void TickNearSparkles(P2PrincessSpriteTether owner)
        {
            if (Time.time < _nextSparkTime) return;

            var go = NextInactive(_sparkPool, ref _sparkCursor);
            if (go == null) return;

            Vector3 a = _lastA, b = _lastB;
            Vector3 dir = (b - a);
            float len = dir.magnitude; if (len < 0.0001f) return;
            Vector3 dirN = dir / len;
            Vector3 perp = Vector3.Cross(dirN, Vector3.forward);

            float t = Random.value;
            Vector3 basePos = Vector3.Lerp(a, b, t);
            float along = Random.Range(-owner.sparkleAlongJitter, owner.sparkleAlongJitter);
            float perpOff = Random.Range(-owner.sparklePerpRadius, owner.sparklePerpRadius);

            Vector3 pos = basePos + dirN * along + perp * perpOff;
            go.transform.SetParent(_worldVfxRoot, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(owner.sparkleScaleRange.x, owner.sparkleScaleRange.y);
            go.transform.localScale = new Vector3(s, s, 1f);

            var srGo = go.GetComponent<SpriteRenderer>();
            if (srGo != null)
            {
                bool useBlue = (len <= owner.nearBlueDistance);
                var col = useBlue ? owner.nearBlueColor : owner.sparkleColor;
                srGo.color = col;
                srGo.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                srGo.sortingOrder = owner.sortingOrder + 1;
            }

            var sp = go.GetComponent<PTetherSparkle>();
            sp.lifetime = owner.sparkleLifetime;
            go.SetActive(true); sp.Replay();

            float interval = Random.Range(owner.sparkleSpawnIntervalRange.x, owner.sparkleSpawnIntervalRange.y);
            _nextSparkTime = Time.time + Mathf.Max(0.01f, interval);
        }

        // ----- beams + trail -----
        private void BuildBeamPool(P2PrincessSpriteTether owner)
        {
            var prefab = owner.beamPrefab ?? MakeDefaultSpritePrefab("DefaultLightBeam", owner.beamColor);
            EnsureComponent<PTetherLightBeam>(prefab);

            for (int i = 0; i < owner.beamPoolPerTarget; i++)
            {
                var inst = Object.Instantiate(prefab, _worldVfxRoot);
                var sr = inst.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = owner.beamColor;
                    sr.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                    sr.sortingOrder = owner.sortingOrder + 20;
                    sr.enabled = true;
                }
                inst.SetActive(false); _beamPool.Add(inst);
            }
            _beamCursor = 0;
        }

        private void BuildTrailPool(P2PrincessSpriteTether owner)
        {
            var prefab = owner.beamTrailPrefab ?? MakeDefaultSpritePrefab("DefaultBeamTrail", owner.beamTrailColor);
            EnsureComponent<PTetherSparkle>(prefab).lifetime = owner.beamTrailLifetime;

            for (int i = 0; i < owner.beamTrailPoolPerTarget; i++)
            {
                var inst = Object.Instantiate(prefab, _worldVfxRoot);
                var sr = inst.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = owner.beamTrailColor;
                    sr.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                    sr.sortingOrder = owner.sortingOrder + 22;
                }
                inst.SetActive(false); _trailPool.Add(inst);
            }
            _trailCursor = 0;
        }

        private void TickBeams(P2PrincessSpriteTether owner)
        {
            if (Time.time < _nextBeamTime) return;

            var go = NextInactive(_beamPool, ref _beamCursor);
            if (go == null) return;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Color c = owner.beamColor;
                if (owner.beamColorVariance > 0f)
                {
                    float v = Random.Range(-owner.beamColorVariance, owner.beamColorVariance);
                    c = Color.Lerp(c, Color.white, Mathf.Clamp01(v + 0.5f));
                    c.a = owner.beamColor.a;
                }
                bool beamNearBlue = (Vector3.Distance(_lastA, _lastB) <= owner.nearBlueDistance);
                if (beamNearBlue) { c = owner.nearBlueColor; c.a = owner.beamColor.a; }
                sr.color = c;
                sr.sortingOrder = owner.sortingOrder + 20;
            }

            float speed = Random.Range(owner.beamSpeedRange.x, owner.beamSpeedRange.y);
            float amp = Random.Range(owner.beamAmplitudeRange.x, owner.beamAmplitudeRange.y);
            float wave = Random.Range(owner.beamWavelengthRange.x, owner.beamWavelengthRange.y);
            float startT = Mathf.Clamp01(Random.Range(0f, 0.85f));

            var lb = go.GetComponent<PTetherLightBeam>();
            lb.ConfigureTrail(owner.beamTrailEnabled ? _trailPool : null,
                              ref _trailCursor, _worldVfxRoot,
                              owner.beamTrailLifetime, owner.beamTrailSpawnIntervalRange, owner.beamTrailScaleRange);
            lb.ConfigureDistanceAlpha(owner.beamAlphaFar, owner.beamAlphaNear, owner.beamAlphaExpo);
            lb.ConfigureNearColor(owner.nearBlueDistance, owner.nearBlueColor);

            lb.Begin(_lineProxy, startT, speed, amp, wave,
                     owner.beamLength, owner.beamThickness,
                     owner.beamFadeInFrac, owner.beamFadeOutFrac);
            go.SetActive(true);

            float interval = Random.Range(owner.beamSpawnIntervalRange.x, owner.beamSpawnIntervalRange.y);
            _nextBeamTime = Time.time + Mathf.Max(0.01f, interval);
        }

        // ----- outline ring (CW orbit / freeze-on-blue) -----
        private void BuildRingPool(P2PrincessSpriteTether owner)
        {
            var prefab = owner.ringSparklePrefab ?? MakeDefaultSpritePrefab("DefaultRingSparkle", owner.ringSparkleColor);
            EnsureComponent<PTetherRingSparkle>(prefab);

            for (int i = 0; i < owner.ringSampleCount; i++)
            {
                var inst = Object.Instantiate(prefab, _worldVfxRoot);
                var sr = inst.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = owner.ringSparkleColor;
                    sr.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                    sr.sortingOrder = owner.sortingOrder + 30;
                }
                inst.SetActive(false); _ringPool.Add(inst);
            }

            _ringOffsets = new float[_ringPool.Count];
            _ringSpeeds = new float[_ringPool.Count];
            _ringScales = new float[_ringPool.Count];
            _ringFrozenPos = new Vector3[_ringPool.Count];

            // 초기 오프셋/속도/스케일만 1회 설정(절대 리셋 X)
            for (int i = 0; i < _ringPool.Count; i++)
            {
                _ringOffsets[i] = Random.value; // 0~1 비율, 실제 길이는 나중에 totalLen과 곱
                _ringSpeeds[i] = Random.Range(owner.ringOrbitSpeedRange.x, owner.ringOrbitSpeedRange.y);
                _ringScales[i] = Random.Range(owner.ringScaleRange.x, owner.ringScaleRange.y);
            }

            _ringFrozen = false;
            _ringReady = false; // 경로만 아직 미생성
        }

        private void EnsureRingPath(P2PrincessSpriteTether owner)
        {
            if (_ringReady && _ringTotalLen > 1e-5f) return;

            var cols = target.GetComponentsInChildren<Collider2D>(true);
            var chosen = PTetherOutlineSampler.ChooseBest(cols);
            int quality = Mathf.Max(8, Mathf.RoundToInt(owner.ringSampleCount * owner.ringPathQualityMul));

            _ringPath = (chosen != null)
                ? PTetherOutlineSampler.BuildClosedPathCW(chosen, quality)
                : PTetherOutlineSampler.BuildBoundsCircleCW(target, quality);

            PTetherOutlineSampler.BuildCumulative(_ringPath, out _ringCum, out _ringTotalLen);

            // 0~1 비율 오프셋을 실제 arc 길이로 변환(최초 1회)
            if (!_ringReady && _ringTotalLen > 1e-5f)
            {
                for (int i = 0; i < _ringOffsets.Length; i++)
                    _ringOffsets[i] *= _ringTotalLen;
            }
            _ringReady = true;
        }

        private void TickRing(P2PrincessSpriteTether owner)
        {
            EnsureRingPath(owner);
            if (!_ringReady || _ringTotalLen <= 1e-5f) { foreach (var r in _ringPool) if (r.activeSelf) r.SetActive(false); return; }

            float dist = Vector3.Distance(_lastA, _lastB);
            bool shouldFreeze = (dist <= owner.nearBlueDistance);
            bool shouldUnfreeze = (dist > owner.nearBlueDistance + owner.ringUnfreezeMargin);

            // 상태 전환
            if (!_ringFrozen && shouldFreeze)
            {
                // 현재 위치를 고정값으로 스냅
                for (int i = 0; i < _ringPool.Count; i++)
                {
                    Vector3 tan;
                    _ringFrozenPos[i] = PTetherOutlineSampler.EvaluateAtArc(_ringPath, _ringCum, _ringTotalLen, _ringOffsets[i], out tan);
                }
                _ringFrozen = true;
            }
            else if (_ringFrozen && shouldUnfreeze)
            {
                _ringFrozen = false; // 다시 공전
            }

            // 알파(거리 비례), 컬러
            float closeness = (owner.searchRadius > 1e-5f) ? Mathf.Clamp01(1f - (dist / owner.searchRadius)) : 1f;
            float ringAlpha = Mathf.Lerp(owner.ringAlphaFar, owner.ringAlphaNear, Mathf.Pow(closeness, owner.ringAlphaExpo));
            bool nearBlue = shouldFreeze; // 블루 상태 = 고정 상태와 동일 임계

            float dt = Time.deltaTime;

            for (int i = 0; i < _ringPool.Count; i++)
            {
                var go = _ringPool[i];
                var sr = go.GetComponent<SpriteRenderer>();
                var flk = go.GetComponent<PTetherRingSparkle>();

                Vector3 pos, tangent;
                if (_ringFrozen)
                {
                    // 고정: 위치/회전 갱신 안 함(엣지에 붙여둠)
                    pos = _ringFrozenPos[i];
                    // 회전은 유지(변경 X)
                    tangent = Vector3.right;
                }
                else
                {
                    // 공전: 오프셋 적분(CW)
                    _ringOffsets[i] = (_ringOffsets[i] + _ringSpeeds[i] * dt) % _ringTotalLen;
                    pos = PTetherOutlineSampler.EvaluateAtArc(_ringPath, _ringCum, _ringTotalLen, _ringOffsets[i], out tangent);
                    // 마지막 위치도 갱신해둔다(다음에 freeze 전환 시 사용)
                    _ringFrozenPos[i] = pos;

                    if (owner.ringAlignToTangent && tangent.sqrMagnitude > 1e-6f)
                    {
                        float ang = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                        go.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                    }
                }

                go.transform.position = pos;
                go.transform.localScale = new Vector3(_ringScales[i], _ringScales[i], 1f);

                // 깜빡임(멀리선 느리게)
                float farInterval = 2.0f;
                float nearRand = Random.Range(owner.ringFlickerInterval.x, owner.ringFlickerInterval.y);
                float flickIntv = Mathf.Lerp(farInterval, nearRand, closeness);
                flk.SetFlicker(flickIntv);
                float flickMul = flk.GetFlickerMul();

                if (sr != null)
                {
                    var baseCol = nearBlue ? owner.nearBlueColor : owner.ringSparkleColor;
                    baseCol.a = Mathf.Clamp01(baseCol.a * ringAlpha * flickMul);
                    sr.color = baseCol;
                    sr.sortingLayerID = SortingLayer.NameToID(owner.sortingLayerName);
                    sr.sortingOrder = owner.sortingOrder + 30;
                }

                if (!go.activeSelf) go.SetActive(true);
            }
        }

        // utils
        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>(); if (c == null) c = go.AddComponent<T>(); return c;
        }
        private static GameObject NextInactive(List<GameObject> pool, ref int cursor)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                cursor = (cursor + 1) % pool.Count;
                if (!pool[cursor].activeSelf) return pool[cursor];
            }
            cursor = (cursor + 1) % pool.Count;
            return pool[cursor];
        }
        private static Sprite MakeWhite1x1(string name)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name };
            tex.SetPixel(0, 0, Color.white); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        private static GameObject MakeDefaultSpritePrefab(string goName, Color color)
        {
            var spr = MakeWhite1x1(goName + "_Spr");
            var go = new GameObject(goName);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = spr; sr.color = color;
            return go;
        }
    }
}

// ---------- Providers / Components ----------
sealed class PTetherLineProxy : MonoBehaviour
{
    public bool isAlive;
    public Vector3 A;
    public Vector3 B;
    public float closeness; // 0(멀다)~1(가깝다)
    public bool IsAlive() => isAlive;
    public Vector3 GetA() => A;
    public Vector3 GetB() => B;
    public float GetCloseness() => closeness;
}

sealed class PTetherSparkle : MonoBehaviour
{
    [SerializeField, Min(0.01f)] public float lifetime = 2f;
    private float _t; private SpriteRenderer _sr; private Color _base;
    void Awake() { _sr = GetComponent<SpriteRenderer>(); if (_sr != null) _base = _sr.color; }
    void OnEnable() => Replay();
    public void Replay() { _t = lifetime; if (_sr != null) _base = _sr.color; }
    void Update()
    {
        _t -= Time.deltaTime;
        if (_sr != null)
        {
            float k = Mathf.Clamp01(_t / lifetime);
            var c = _base; c.a = _base.a * k; _sr.color = c;
        }
        if (_t <= 0f) gameObject.SetActive(false);
    }
}

sealed class PTetherRingSparkle : MonoBehaviour
{
    private float _nextFlip; private float _interval = 0.5f; private bool _on = true;
    public void SetFlicker(float interval) { _interval = Mathf.Max(0.1f, interval); }
    public float GetFlickerMul() => _on ? 1f : 0.25f;
    void OnEnable() { _on = true; _nextFlip = Time.time + Random.Range(0.05f, _interval); }
    void Update()
    {
        if (Time.time >= _nextFlip)
        {
            _on = !_on;
            _nextFlip = Time.time + _interval;
        }
    }
}

// ---- Beam with trail + distance alpha + near-blue ----
sealed class PTetherLightBeam : MonoBehaviour
{
    private PTetherLineProxy _provider; private SpriteRenderer _sr;
    private float _t, _tStart, _speed, _amp, _waveLen, _fadeInFrac, _fadeOutFrac, _beamLen, _beamThick, _phase0;
    private Color _baseColor; private bool _inited;

    private List<GameObject> _trailPool; private int _trailCursor;
    private Transform _worldRoot;
    private float _trailLifetime; private Vector2 _trailIntervalRange; private Vector2 _trailScaleRange;
    private float _nextTrailTime;

    private float _alphaFar = 0.15f, _alphaNear = 1.0f, _alphaExpo = 1.0f;
    private float _nearColorDist = 1.5f;
    private Color _nearColor = new(0.45f, 0.75f, 1f, 1f);
    private Color _origColor;

    void Awake() { _sr = GetComponent<SpriteRenderer>(); }

    public void ConfigureTrail(List<GameObject> trailPool, ref int trailCursor, Transform worldRoot,
                               float trailLifetime, Vector2 intervalRange, Vector2 scaleRange)
    {
        _trailPool = trailPool; _trailCursor = trailCursor; _worldRoot = worldRoot;
        _trailLifetime = trailLifetime; _trailIntervalRange = intervalRange; _trailScaleRange = scaleRange;
        _nextTrailTime = 0f;
    }

    public void ConfigureDistanceAlpha(float far, float near, float expo)
    {
        _alphaFar = Mathf.Clamp01(far);
        _alphaNear = Mathf.Clamp01(near);
        _alphaExpo = Mathf.Max(0.1f, expo);
    }

    public void ConfigureNearColor(float dist, Color col)
    {
        _nearColorDist = Mathf.Max(0.001f, dist);
        _nearColor = col;
    }

    public void Begin(PTetherLineProxy provider, float startT, float speedWorldPerSec,
                      float amplitude, float wavelength, float beamLength, float beamThickness,
                      float fadeInFrac, float fadeOutFrac)
    {
        _provider = provider;
        _tStart = Mathf.Clamp01(startT); _t = _tStart;
        _speed = Mathf.Max(0.01f, speedWorldPerSec);
        _amp = Mathf.Max(0f, amplitude);
        _waveLen = Mathf.Max(0.01f, wavelength);
        _beamLen = Mathf.Max(0.02f, beamLength);
        _beamThick = Mathf.Max(0.02f, beamThickness);
        _fadeInFrac = Mathf.Clamp01(fadeInFrac);
        _fadeOutFrac = Mathf.Clamp01(fadeOutFrac);
        _phase0 = Random.Range(0f, Mathf.PI * 2f);

        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) { _sr.enabled = true; _baseColor = _sr.color; _origColor = _sr.color; transform.localScale = new Vector3(_beamLen, _beamThick, 1f); }
        _inited = true; TickVisual(Time.deltaTime * 0.001f);
    }

    void Update() => TickVisual(Time.deltaTime);

    private void TickVisual(float dt)
    {
        if (!_inited || _provider == null || !_provider.IsAlive()) { gameObject.SetActive(false); return; }

        Vector3 a = _provider.GetA(), b = _provider.GetB();
        Vector3 dir = (b - a); float L = dir.magnitude;
        if (L < 0.0001f) { gameObject.SetActive(false); return; }

        Vector3 dirN = dir / L; Vector3 perp = Vector3.Cross(dirN, Vector3.forward);

        _t += (_speed / L) * dt; if (_t >= 1f) { gameObject.SetActive(false); return; }

        float phase = (2f * Mathf.PI * (L * _t / _waveLen)) + _phase0;
        Vector3 pos = Vector3.Lerp(a, b, _t) + perp * (_amp * Mathf.Sin(phase));
        transform.position = pos;

        float angle = Mathf.Atan2(dirN.y, dirN.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        transform.localScale = new Vector3(_beamLen, _beamThick, 1f);

        if (_sr != null)
        {
            float p = Mathf.InverseLerp(_tStart, 1f, _t);
            float aIn = (_fadeInFrac > 0f) ? Mathf.Clamp01(p / _fadeInFrac) : 1f;
            float aOut = (_fadeOutFrac > 0f) ? Mathf.Clamp01((1f - p) / _fadeOutFrac) : 1f;
            float fadeMul = Mathf.Min(aIn, aOut);

            float close = Mathf.Clamp01(_provider.GetCloseness());
            float distMul = Mathf.Lerp(_alphaFar, _alphaNear, Mathf.Pow(close, _alphaExpo));

            bool nearBlue = (L <= _nearColorDist);
            Color src = nearBlue ? _nearColor : _origColor;

            var c = src; c.a = src.a * fadeMul * distMul; _sr.color = c;
        }

        // trail spawn
        if (_trailPool != null && Time.time >= _nextTrailTime)
        {
            var tgo = NextInactive(_trailPool, ref _trailCursor);
            if (tgo != null)
            {
                tgo.transform.SetParent(_worldRoot, true);
                tgo.transform.position = pos;
                tgo.transform.rotation = Quaternion.identity;

                float s = Random.Range(_trailScaleRange.x, _trailScaleRange.y);
                tgo.transform.localScale = new Vector3(s, s, 1f);

                var sp = tgo.GetComponent<PTetherSparkle>();
                if (sp != null) sp.lifetime = _trailLifetime;
                tgo.SetActive(true);
                if (sp != null) sp.Replay();
            }
            float itv = Random.Range(_trailIntervalRange.x, _trailIntervalRange.y);
            _nextTrailTime = Time.time + Mathf.Max(0.01f, itv);
        }
    }

    private static GameObject NextInactive(List<GameObject> pool, ref int cursor)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            cursor = (cursor + 1) % pool.Count;
            if (!pool[cursor].activeSelf) return pool[cursor];
        }
        cursor = (cursor + 1) % pool.Count;
        return pool[cursor];
    }
}

// ---------- Outline sampler & path utils (CW 보장/중복 정리본) ----------
static class PTetherOutlineSampler
{
    public static Collider2D ChooseBest(Collider2D[] cols)
    {
        if (cols == null || cols.Length == 0) return null;
        Collider2D best = null; int bestScore = -1;
        foreach (var c in cols)
        {
            if (c == null) continue;
            int score =
                (c is PolygonCollider2D) ? 5 :
                (c is CompositeCollider2D) ? 4 :
                (c is BoxCollider2D) ? 3 :
                (c is CircleCollider2D) ? 2 :
                (c is EdgeCollider2D) ? 1 : 0;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    public static Vector3[] BuildClosedPathCW(Collider2D col, int minPoints)
    {
        if (col is CircleCollider2D cc) return BuildCircleCW(cc, minPoints);
        if (col is BoxCollider2D bc) return BuildBoxCW(bc, Mathf.Max(4, minPoints));
        if (col is PolygonCollider2D pc) return BuildPolygonCW(pc, minPoints);
        if (col is CompositeCollider2D c) return BuildCompositeCW(c, minPoints);
        return BuildBoundsCircleCW(col.bounds, minPoints);
    }

    // BuildBoundsCircleCW 오버로드(2개)
    public static Vector3[] BuildBoundsCircleCW(Bounds b, int minPoints)
    {
        float r = Mathf.Max(b.extents.x, b.extents.y);
        return BuildCircleCW(b.center, r, Mathf.Max(8, minPoints));
    }
    public static Vector3[] BuildBoundsCircleCW(Transform target, int minPoints)
    {
        var cols = target.GetComponentsInChildren<Collider2D>(true);
        Bounds b;
        if (cols != null && cols.Length > 0)
        {
            b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
        }
        else
        {
            var rs = target.GetComponentsInChildren<Renderer>(true);
            if (rs != null && rs.Length > 0)
            {
                b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            }
            else b = new Bounds(target.position, Vector3.one);
        }
        return BuildBoundsCircleCW(b, minPoints);
    }

    public static void BuildCumulative(Vector3[] path, out float[] cum, out float total)
    {
        int n = path.Length;
        cum = new float[n]; total = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            total += Vector3.Distance(path[i], path[j]);
            cum[i] = total;
        }
    }

    public static Vector3 EvaluateAtArc(Vector3[] path, float[] cum, float totalLen, float s, out Vector3 tangent)
    {
        s = Mathf.Repeat(s, totalLen);
        int n = path.Length;
        float prevCum = 0f;

        for (int i = 0; i < n; i++)
        {
            float segEnd = cum[i];
            if (s <= segEnd)
            {
                int a = i, b = (i + 1) % n;
                float segLen = (i == 0 ? cum[0] : cum[i] - cum[i - 1]);
                float local = s - prevCum;
                float t = (segLen > 1e-6f) ? (local / segLen) : 0f;
                tangent = (path[b] - path[a]).normalized;
                return Vector3.Lerp(path[a], path[b], t);
            }
            prevCum = segEnd;
        }
        tangent = (path[1] - path[0]).normalized;
        return path[0];
    }

    private static Vector3[] BuildCircleCW(CircleCollider2D c, int minPoints)
    {
        float r = Mathf.Abs(c.radius) * Mathf.Max(Mathf.Abs(c.transform.lossyScale.x), Mathf.Abs(c.transform.lossyScale.y));
        Vector3 center = c.transform.TransformPoint(c.offset);
        return BuildCircleCW(center, r, minPoints);
    }
    private static Vector3[] BuildCircleCW(Vector3 center, float r, int minPoints)
    {
        int count = Mathf.Max(8, minPoints);
        var pts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float ang = (1f - (i / (float)count)) * Mathf.PI * 2f; // CW
            pts[i] = center + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * r;
        }
        return pts;
    }

    private static Vector3[] BuildBoxCW(BoxCollider2D b, int minPoints)
    {
        int perEdge = Mathf.Max(2, minPoints / 4);
        Vector2 size = Vector2.Scale(b.size, new Vector2(Mathf.Abs(b.transform.lossyScale.x), Mathf.Abs(b.transform.lossyScale.y)));
        Vector3 c = b.transform.TransformPoint(b.offset);
        Vector3 rx = b.transform.right * (size.x * 0.5f);
        Vector3 ry = b.transform.up * (size.y * 0.5f);

        Vector3 p0 = c + (rx + ry); // TR
        Vector3 p1 = c + (-rx + ry); // TL
        Vector3 p2 = c + (-rx - ry); // BL
        Vector3 p3 = c + (rx - ry); // BR

        var corners = new List<Vector3> { p0, p1, p2, p3 }; // CW
        return SubdivideLoop(corners, perEdge);
    }

    private static Vector3[] BuildPolygonCW(PolygonCollider2D p, int minPoints)
    {
        var pts = new List<Vector3>();
        if (p.pathCount > 0)
        {
            var arr = p.GetPath(0);
            for (int i = 0; i < arr.Length; i++)
                pts.Add(p.transform.TransformPoint(arr[i]));
        }
        if (pts.Count < 3) return BuildBoundsCircleCW(p.bounds, minPoints);
        if (SignedArea(pts) > 0f) pts.Reverse(); // CCW → CW
        return ResampleLoopByCount(pts, Mathf.Max(pts.Count, minPoints));
    }

    private static Vector3[] BuildCompositeCW(CompositeCollider2D c, int minPoints)
    {
        var all = new List<Vector3>();
        for (int path = 0; path < c.pathCount; path++)
        {
            int pc = c.GetPathPointCount(path);
            if (pc <= 0) continue;
            var buf = new Vector2[pc]; c.GetPath(path, buf);

            var pts = new List<Vector3>(pc);
            for (int i = 0; i < buf.Length; i++) pts.Add(c.transform.TransformPoint(buf[i]));
            if (pts.Count >= 3 && SignedArea(pts) > 0f) pts.Reverse();
            if (pts.Count > 0 && pts[0] != pts[^1]) pts.Add(pts[0]);

            var res = ResampleLoopByCount(pts, Mathf.Max(pts.Count, Mathf.RoundToInt(minPoints / Mathf.Max(1, c.pathCount))));
            all.AddRange(res);
        }
        if (all.Count < 3) return BuildBoundsCircleCW(c.bounds, minPoints);
        return all.ToArray();
    }

    private static float SignedArea(List<Vector3> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            Vector3 q = pts[(i + 1) % pts.Count];
            a += (double)p.x * q.y - (double)q.x * p.y;
        }
        return (float)(a * 0.5);
    }

    private static Vector3[] SubdivideLoop(List<Vector3> corners, int perEdge)
    {
        var outPts = new List<Vector3>();
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % corners.Count];
            for (int k = 0; k < perEdge; k++)
            {
                float t = k / (float)perEdge;
                outPts.Add(Vector3.Lerp(a, b, t));
            }
        }
        return outPts.ToArray();
    }

    private static Vector3[] ResampleLoopByCount(List<Vector3> inPts, int outCount)
    {
        if (inPts[0] != inPts[^1]) inPts.Add(inPts[0]);

        float total = 0f;
        for (int i = 0; i < inPts.Count - 1; i++) total += Vector3.Distance(inPts[i], inPts[i + 1]);
        outCount = Mathf.Max(3, outCount);

        var outPts = new Vector3[outCount];
        float step = total / outCount;
        float acc = 0f; int seg = 0; float segAcc = 0f;
        for (int i = 0; i < outCount; i++)
        {
            float target = step * i;
            while (seg < inPts.Count - 1 && segAcc + Vector3.Distance(inPts[seg], inPts[seg + 1]) < target)
            { segAcc += Vector3.Distance(inPts[seg], inPts[seg + 1]); seg++; }

            float segLen = Mathf.Max(1e-6f, Vector3.Distance(inPts[seg], inPts[seg + 1]));
            float t = (target - segAcc) / segLen;
            outPts[i] = Vector3.Lerp(inPts[seg], inPts[seg + 1], t);
        }
        return outPts;
    }
}
