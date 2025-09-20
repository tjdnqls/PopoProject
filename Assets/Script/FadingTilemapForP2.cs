using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Collider2D))]
public class FadingTilemapForP2 : MonoBehaviour
{
    [Header("Targets")]
    public Transform player2Root;                     // 공주(P2)
    public Transform player1Root;                     // 옵션
    [Tooltip("있으면 콜라이더-콜라이더 최소거리로 표면거리 계산(겹침 포함 정확)")]
    public Collider2D player2MainCollider;            // 선택

    // ── 거리→속도: 콜라이더 '가장 가까운 면' 기준 ──
    [Header("Nearest-Surface Distance (meters)")]
    [Tooltip("면에서 이 거리까지를 0..1로 정규화(near=1, far=0)")]
    public float influenceMaxDistance = 6f;

    [Header("Distance Correction")]
    [Tooltip("정규화 전에 d' = max(0, d*Scale - Bias) 적용")]
    public float distanceBiasMeters = 0f;
    public float distanceScale = 1f;
    [Tooltip("정규화 t(0..1)를 속도 가중치로 보정(기본 선형)")]
    public AnimationCurve distanceToSpeedCurve = AnimationCurve.Linear(0, 0, 1, 1);

    // ── (선택) 시야 체크: 가려지면 '이탈'로 간주해 감소 ──
    [Header("Line of Sight (optional)")]
    public bool requireLineOfSight = false;
    public LayerMask losBlockMask;
    public float losSkin = 0.01f;

    // ── 속도 ──
    [Header("Rates (per second)")]
    public float chargeRateMin = 0.2f;   // 멀리
    public float chargeRateMax = 2.0f;   // 가까이
    public float drainRateMin = 0.1f;   // 가까이
    public float drainRateMax = 2.0f;   // 멀리

    // ── 임계치 ──
    [Header("Thresholds")]
    [Range(0f, 1f)] public float vanishThreshold = 0.995f; // 기본모드: ≥면 '사라짐(물리 off)'
    [Range(0f, 1f)] public float appearThreshold = 0.005f; // 기본모드: ≤면 '나타남(물리 on)'
    [Tooltip("접근/이탈 판정 데드존(m)")]
    public float epsilonDistance = 0.005f;

    // ── 시각효과(선/원) ──
    [Header("Guide Line")]
    [SerializeField] private bool showLine = true;
    [SerializeField] private Color lineColor = Color.yellow;                 // 기본(노랑)
    [SerializeField] private Color lineColorFull = new Color(0.2f, 0.65f, 1f); // Full(파랑)
    [SerializeField] private float lineMinWidth = 0.02f;
    [SerializeField] private float lineMaxWidth = 0.2f;

    [Header("Circle Indicator")]
    [SerializeField] private bool showCircle = true;
    [SerializeField] private float circleRadius = 0.5f;
    [SerializeField] private float ringThickness = 0.05f;
    [SerializeField] private Color ringColor = Color.white;
    [SerializeField] private Color fillColor = Color.yellow;                 // 기본(노랑)
    [SerializeField] private Color fillColorFull = new Color(0.2f, 0.65f, 1f); // Full(파랑)
    [Tooltip("테두리용 원 스프라이트(권장)")]
    [SerializeField] private Sprite circleSprite;
    [Tooltip("채움용(1x1 사각 추천, 비워도 자동 생성)")]
    [SerializeField] private Sprite fillSprite;

    // ── 본체 시각효과(게이지에 따라 노랗게 + 투명) ──
    [Header("Visual by Gauge (Object)")]
    public Color tintColor = new Color(1f, 0.92f, 0.2f);
    [Range(0f, 1f)] public float minAlphaAtFull = 0f; // 1→0
    public AnimationCurve visualCurve = AnimationCurve.Linear(0, 0, 1, 1);

    // ── Reverse 모드 ──
    [Header("Reverse Visibility")]
    [Tooltip("켜면: 평소 안보임(물리 off) → 게이지가 '가득' 찼을 때만 보임(물리 on)")]
    public bool reverseVisibility = false;
    [Tooltip("런타임 토글 키(없으면 None)")]
    public KeyCode reverseToggleKey = KeyCode.None;
    [Tooltip("리버스일 때, 가득 차기 전엔 완전 투명(0), 가득 차면 한 번에 보이게")]
    public bool reverseHardGate = true;
    [Tooltip("리버스일 때 보일 때의 알파(1=불투명)")]
    [Range(0f, 1f)] public float reverseAlphaAtFull = 1f;

    [Header("Collision/Render")]
    [Tooltip("충돌용 콜라이더 오브젝트(선택). 비우면 자신의 Collider2D on/off")]
    public GameObject collGO;

    // 내부 캐시
    private Collider2D wallCol;
    private TilemapRenderer tmRenderer;
    private Tilemap tilemap;
    private SpriteRenderer[] spriteRenderers;
    private Renderer[] otherRenderers;

    private LineRenderer linkLine;    // P2 ↔ 콜라이더 중앙
    private LineRenderer ringLine;    // 흰 링
    private SpriteMask circleMask;    // 원형 마스크
    private SpriteRenderer fillSR;    // 노란/파란 채움
    private Transform indicatorRoot;  // 중앙 고정

    [SerializeField, Range(0f, 1f)] private float charge01 = 0f;
    private bool isPresent = true;                     // '물리 on' 상태
    private const int ringSegments = 48;

    // 거리 변화 추적
    private float prevSurfaceDist = -1f;
    private bool hasPrevDist = false;

    // 비주얼 원본 색상 저장
    private Color tilemapBaseColor = Color.white;
    private Color[] spriteBaseColors;
    private Color[] otherBaseColors; // _Color 지원 머터리얼만

    // Full 컬러 스위치 상태(노랑↔파랑)
    [Header("Full Color Switch")]
    [Range(0f, 1f)] public float fullColorThreshold = 0.995f; // 기본은 vanishThreshold와 동일
    [Range(0f, 0.2f)] public float fullColorHysteresis = 0.02f;
    private bool _lastFullVisual = false;

    void Awake()
    {
        wallCol = GetComponent<Collider2D>();
        tmRenderer = GetComponent<TilemapRenderer>();
        tilemap = GetComponent<Tilemap>();

        // 자신(자식 제외)의 렌더러만 제어
        spriteRenderers = GetComponents<SpriteRenderer>();
        var all = GetComponents<Renderer>();
        var others = new List<Renderer>();
        foreach (var r in all) if (r is not SpriteRenderer && r is not TilemapRenderer) others.Add(r);
        otherRenderers = others.ToArray();

        if (!player2MainCollider && player2Root)
            player2MainCollider = player2Root.GetComponentInChildren<Collider2D>();

        CacheBaseVisuals();
        SetupLine();
        SetupCircle();

        ApplyPresentState(true, affectRenderers: false); // 렌더는 항상 on, 물리만 토글
        if (Mathf.Approximately(fullColorThreshold, 0f)) fullColorThreshold = vanishThreshold;

        ValidateParams();
        SyncPresenceWithMode(); // 리버스 초기상태 반영
    }

    void OnValidate() => ValidateParams();

    private void ValidateParams()
    {
        if (influenceMaxDistance < 0.01f) influenceMaxDistance = 0.01f;
        if (distanceScale <= 0f) distanceScale = 1f;
        if (distanceBiasMeters < 0f) distanceBiasMeters = 0f;
        if (vanishThreshold <= appearThreshold)
            vanishThreshold = Mathf.Clamp01(Mathf.Max(appearThreshold + 0.05f, 0.2f));
        if (epsilonDistance < 0f) epsilonDistance = 0f;
        if (fullColorThreshold <= 0f) fullColorThreshold = vanishThreshold;
    }

    void LateUpdate()
    {
        // 런타임 키 토글(선택)
        if (reverseToggleKey != KeyCode.None && Input.GetKeyDown(reverseToggleKey))
        {
            reverseVisibility = !reverseVisibility;
            SyncPresenceWithMode();
        }

        Transform target = player2Root ? player2Root : player1Root;
        if (!target)
        {
            UpdateUI(false, Vector3.zero, 0f);
            UpdateVisualByGauge();
            return;
        }

        Vector3 center = GetCenter();

        // 1) 표면까지 최단거리
        float surfaceDist = GetSurfaceDistance((Vector2)target.position);

        // 2) 거리 보정 → 정규화 tNear: near(1) ← far(0)
        float adj = Mathf.Max(0f, surfaceDist * distanceScale - distanceBiasMeters);
        float tNear = Mathf.Clamp01(1f - adj / influenceMaxDistance);

        // 3) LOS(선택). 가려지면 '이탈'로 취급
        bool hasLOS = !requireLineOfSight || HasLineOfSight(center, target.position);

        // 4) 접근/이탈 판정
        float dt = Time.deltaTime;
        float delta = 0f;
        bool approaching = false, leaving = false;

        if (!hasPrevDist) { prevSurfaceDist = surfaceDist; hasPrevDist = true; }
        else
        {
            delta = prevSurfaceDist - surfaceDist; // +면 접근, -면 이탈
            if (delta > epsilonDistance) approaching = true;
            if (delta < -epsilonDistance) leaving = true;
            prevSurfaceDist = surfaceDist;
        }

        // 5) 범위/LOS 충족 여부
        bool inRange = (adj < influenceMaxDistance - 1e-4f);
        bool canInfluence = inRange && hasLOS;

        // ───────── 규칙 확정: 범위 안+접근↑ / 범위 안+이탈↓ / 범위 밖↓ ─────────
        if (canInfluence)
        {
            if (approaching)
            {
                float w = Mathf.Clamp01(distanceToSpeedCurve.Evaluate(tNear)); // near일수록 큼
                float rate = Mathf.Lerp(chargeRateMin, chargeRateMax, w);
                charge01 += rate * dt;
            }
            else if (leaving)
            {
                float nearInv = 1f - tNear; // near=1 → 0, far=0 → 1
                float rate = Mathf.Lerp(drainRateMin, drainRateMax, Mathf.Clamp01(nearInv));
                charge01 -= rate * dt;
            }
            // else: 정지 → 유지
        }
        else
        {
            // ★ 범위 밖이면 접근 중이어도 '무조건 감소'
            float far01 = 1f - Mathf.Clamp01(tNear);
            float rate = Mathf.Lerp(drainRateMin, drainRateMax, far01);
            charge01 -= rate * dt;
        }

        charge01 = Mathf.Clamp01(charge01);

        // ── 물리 상태 전이(모드별 반전) ──
        if (!reverseVisibility)
        {
            if (isPresent && charge01 >= vanishThreshold) ApplyPresentState(false, affectRenderers: false);
            else if (!isPresent && charge01 <= appearThreshold) ApplyPresentState(true, affectRenderers: false);
        }
        else // Reverse: 가득 차야 '나타남(물리 on)'
        {
            if (!isPresent && charge01 >= vanishThreshold) ApplyPresentState(true, affectRenderers: false);
            else if (isPresent && charge01 <= appearThreshold) ApplyPresentState(false, affectRenderers: false);
        }

        // 게이지에 따른 본체 색/알파 연출(리버스 대응)
        UpdateVisualByGauge();

        // Full 컬러 스위치(선·게이지 노랑↔파랑)
        bool wantFull = _lastFullVisual
                        ? (charge01 >= fullColorThreshold - fullColorHysteresis)
                        : (charge01 >= fullColorThreshold);
        UpdateFullVisual(wantFull);

        // ── 가시성 규칙: 게이지 UI는 진행 중이면 보이되, 선은 '관여 가능'일 때만 ──
        bool showGaugeUI = canInfluence || (charge01 > 0f && charge01 < 1f);
        bool showLineNow = canInfluence;             // ★ 범위/LOS 충족시에만 선 표시

        UpdateUI(showGaugeUI, center, Mathf.Lerp(lineMinWidth, lineMaxWidth, tNear));

        // 라인 토글/좌표는 여기서만!
        if (showLine && linkLine)
        {
            linkLine.enabled = showLineNow;
            if (showLineNow)
            {
                linkLine.SetPosition(0, target.position);
                linkLine.SetPosition(1, center); // 콜라이더 중앙
            }
        }
    }

    // ── 콜라이더 '면'까지의 최단거리 ──
    private float GetSurfaceDistance(Vector2 p2World)
    {
        if (!wallCol) return Mathf.Infinity;

        if (player2MainCollider)
        {
            var d = Physics2D.Distance(player2MainCollider, wallCol); // 겹치면 음수
            return Mathf.Max(0f, d.distance);
        }
        else
        {
            Vector2 closest = wallCol.ClosestPoint(p2World);
            return Vector2.Distance(closest, p2World);
        }
    }

    private Vector3 GetCenter() => wallCol ? (Vector3)wallCol.bounds.center : transform.position;

    // ── LOS 체크 ──
    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector2 dir = (to - from);
        float len = dir.magnitude;
        if (len <= losSkin) return true;
        var hit = Physics2D.Raycast((Vector2)from, dir / len, len - losSkin, losBlockMask);
        return hit.collider == null;
    }

    // ── 물리 on/off (렌더는 항상 on) ──
    private void ApplyPresentState(bool present, bool affectRenderers)
    {
        isPresent = present;

        // 충돌만 임계치로 on/off
        if (collGO != null) collGO.SetActive(present);
        else if (wallCol) wallCol.enabled = present;

        // 렌더러는 항상 on으로 두고 알파/색만 보간(팝인X)
        if (affectRenderers)
        {
            if (tmRenderer) tmRenderer.enabled = present;
            if (spriteRenderers != null) foreach (var sr in spriteRenderers) if (sr) sr.enabled = present;
            if (otherRenderers != null) foreach (var r in otherRenderers) if (r) r.enabled = present;
        }

        if (indicatorRoot) indicatorRoot.gameObject.SetActive(true);
    }

    // ── 게이지에 따른 색/알파 보간(리버스 대응) ──
    private void UpdateVisualByGauge()
    {
        if (!reverseVisibility)
        {
            float f = Mathf.Clamp01(visualCurve.Evaluate(charge01));   // 0..1

            // Tilemap
            if (tilemap)
            {
                Color rgb = Color.Lerp(new Color(tilemapBaseColor.r, tilemapBaseColor.g, tilemapBaseColor.b, 1f),
                                       new Color(tintColor.r, tintColor.g, tintColor.b, 1f), f);
                float a = Mathf.Lerp(tilemapBaseColor.a, minAlphaAtFull, f);
                rgb.a = a;
                tilemap.color = rgb;
            }
            else if (tmRenderer)
            {
                var mpb = new MaterialPropertyBlock();
                tmRenderer.GetPropertyBlock(mpb);
                Color baseC = tilemapBaseColor;
                Color rgb = Color.Lerp(new Color(baseC.r, baseC.g, baseC.b, 1f),
                                       new Color(tintColor.r, tintColor.g, tintColor.b, 1f), f);
                float a = Mathf.Lerp(baseC.a, minAlphaAtFull, f);
                rgb.a = a;
                mpb.SetColor("_Color", rgb);
                tmRenderer.SetPropertyBlock(mpb);
            }

            // SpriteRenderer들
            if (spriteRenderers != null)
            {
                for (int i = 0; i < spriteRenderers.Length; i++)
                {
                    var sr = spriteRenderers[i];
                    if (!sr) continue;
                    Color baseC = (spriteBaseColors != null && i < spriteBaseColors.Length) ? spriteBaseColors[i] : Color.white;
                    Color rgb = Color.Lerp(new Color(baseC.r, baseC.g, baseC.b, 1f),
                                           new Color(tintColor.r, tintColor.g, tintColor.b, 1f), f);
                    float a = Mathf.Lerp(baseC.a, minAlphaAtFull, f);
                    rgb.a = a;
                    sr.color = rgb;
                }
            }

            // 기타 Renderer(MeshRenderer 등)
            if (otherRenderers != null)
            {
                for (int i = 0; i < otherRenderers.Length; i++)
                {
                    var r = otherRenderers[i];
                    if (!r) continue;
                    Color baseC = (otherBaseColors != null && i < otherBaseColors.Length) ? otherBaseColors[i] : Color.white;
                    var mpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    Color rgb = Color.Lerp(new Color(baseC.r, baseC.g, baseC.b, 1f),
                                           new Color(tintColor.r, tintColor.g, tintColor.b, 1f), f);
                    float a = Mathf.Lerp(baseC.a, minAlphaAtFull, f);
                    rgb.a = a;
                    mpb.SetColor("_Color", rgb);
                    r.SetPropertyBlock(mpb);
                }
            }
        }
        else
        {
            // 리버스: 가득 차기 전엔 완전 투명(하드 게이트), 가득 차면 한 번에 보임
            bool full = charge01 >= vanishThreshold;
            float alpha = full ? reverseAlphaAtFull : 0f;

            // Tilemap
            if (tilemap)
            {
                Color c = tilemapBaseColor;
                c.a = alpha;
                tilemap.color = c;
            }
            else if (tmRenderer)
            {
                var mpb = new MaterialPropertyBlock();
                tmRenderer.GetPropertyBlock(mpb);
                Color baseC = tilemapBaseColor;
                baseC.a = alpha;
                mpb.SetColor("_Color", baseC);
                tmRenderer.SetPropertyBlock(mpb);
            }

            // SpriteRenderer들
            if (spriteRenderers != null)
            {
                for (int i = 0; i < spriteRenderers.Length; i++)
                {
                    var sr = spriteRenderers[i];
                    if (!sr) continue;
                    Color c = (spriteBaseColors != null && i < spriteBaseColors.Length) ? spriteBaseColors[i] : Color.white;
                    c.a = alpha;
                    sr.color = c;
                }
            }

            // 기타 Renderer(MeshRenderer 등)
            if (otherRenderers != null)
            {
                for (int i = 0; i < otherRenderers.Length; i++)
                {
                    var r = otherRenderers[i];
                    if (!r) continue;
                    var mpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    Color c = (otherBaseColors != null && i < otherBaseColors.Length) ? otherBaseColors[i] : Color.white;
                    c.a = alpha;
                    mpb.SetColor("_Color", c);
                    r.SetPropertyBlock(mpb);
                }
            }
        }
    }

    private void CacheBaseVisuals()
    {
        // Tilemap 원본
        if (tilemap) tilemapBaseColor = tilemap.color;
        else if (tmRenderer && tmRenderer.sharedMaterial && tmRenderer.sharedMaterial.HasProperty("_Color"))
            tilemapBaseColor = tmRenderer.sharedMaterial.color;
        else tilemapBaseColor = Color.white;

        // SpriteRenderer 원본
        if (spriteRenderers != null)
        {
            spriteBaseColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
                spriteBaseColors[i] = spriteRenderers[i] ? spriteRenderers[i].color : Color.white;
        }

        // 기타 Renderer 원본(_Color)
        if (otherRenderers != null)
        {
            otherBaseColors = new Color[otherRenderers.Length];
            for (int i = 0; i < otherRenderers.Length; i++)
            {
                var r = otherRenderers[i];
                if (!r) { otherBaseColors[i] = Color.white; continue; }
                if (r.sharedMaterial && r.sharedMaterial.HasProperty("_Color"))
                    otherBaseColors[i] = r.sharedMaterial.color;
                else
                    otherBaseColors[i] = Color.white;
            }
        }
    }

    private void UpdateUI(bool enable, Vector3 center, float lineWidth)
    {
        if (indicatorRoot) indicatorRoot.position = center;

        // 링
        if (showCircle && ringLine)
        {
            ringLine.enabled = enable;
            ringLine.startWidth = ringThickness;
            ringLine.endWidth = ringThickness;
            RebuildRing(center);
        }

        // 채움(아래→위, 진행률=charge01)
        if (showCircle && fillSR && circleMask)
        {
            float diameter = circleRadius * 2f;

            if (circleMask.sprite != null)
            {
                float maskWorldW = circleMask.sprite.rect.width / circleMask.sprite.pixelsPerUnit;
                float scale = (maskWorldW > 0.0001f) ? diameter / maskWorldW : 1f;
                circleMask.transform.localScale = new Vector3(scale, scale, 1f);
            }

            float targetW = diameter;
            float targetH = diameter * charge01;

            Vector2 baseSize = fillSR.sprite
                ? fillSR.sprite.rect.size / fillSR.sprite.pixelsPerUnit
                : new Vector2(1f, 1f);

            float sx = targetW / Mathf.Max(0.0001f, baseSize.x);
            float sy = targetH / Mathf.Max(0.0001f, baseSize.y);
            fillSR.transform.localScale = new Vector3(sx, sy, 1f);

            float y = -circleRadius + targetH * 0.5f;
            fillSR.transform.localPosition = new Vector3(0f, y, 0f);

            fillSR.enabled = enable;
        }

        // 라인의 enable/disable은 LateUpdate에서만
        if (showLine && linkLine)
        {
            linkLine.startWidth = lineWidth;
            linkLine.endWidth = lineWidth;
        }
    }

    // ── Full/Normal 전환 시에만 선·채움 색 교체 ──
    private void UpdateFullVisual(bool isFull)
    {
        if (_lastFullVisual == isFull) return;
        _lastFullVisual = isFull;

        if (linkLine)
        {
            var lc = isFull ? lineColorFull : lineColor;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(lc, 0f), new GradientColorKey(lc, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            linkLine.colorGradient = g;
        }

        if (fillSR)
        {
            var c = isFull ? fillColorFull : fillColor;
            c.a = 1f; // 마스크 절두
            fillSR.color = c;
        }
    }

    // ── 세팅 ──
    private void SetupLine()
    {
        if (!showLine) return;

        linkLine = GetComponent<LineRenderer>();
        if (!linkLine) linkLine = gameObject.AddComponent<LineRenderer>();
        linkLine.useWorldSpace = true;
        linkLine.positionCount = 2;
        linkLine.material = new Material(Shader.Find("Sprites/Default"));

        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(lineColor, 0f), new GradientColorKey(lineColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        linkLine.colorGradient = g;

        if (tmRenderer)
        {
            linkLine.sortingLayerID = tmRenderer.sortingLayerID;
            linkLine.sortingOrder = tmRenderer.sortingOrder + 3; // 타일 위 최상
        }
    }

    private void SetupCircle()
    {
        if (!showCircle) return;

        indicatorRoot = new GameObject("CircleIndicatorRoot").transform;
        indicatorRoot.SetParent(transform, false);

        // 링
        var ringGO = new GameObject("Ring");
        ringGO.transform.SetParent(indicatorRoot, false);
        ringLine = ringGO.AddComponent<LineRenderer>();
        ringLine.useWorldSpace = true;
        ringLine.positionCount = ringSegments + 1;
        ringLine.loop = false;
        ringLine.material = new Material(Shader.Find("Sprites/Default"));
        ringLine.startColor = ringColor;
        ringLine.endColor = ringColor;
        if (tmRenderer)
        {
            ringLine.sortingLayerID = tmRenderer.sortingLayerID;
            ringLine.sortingOrder = tmRenderer.sortingOrder + 2;
        }

        // 마스크 + 채움
        var maskGO = new GameObject("CircleMask");
        maskGO.transform.SetParent(indicatorRoot, false);
        circleMask = maskGO.AddComponent<SpriteMask>();
        circleMask.sprite = circleSprite;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(indicatorRoot, false);
        fillSR = fillGO.AddComponent<SpriteRenderer>();
        fillSR.sprite = fillSprite ? fillSprite : Texture2D.whiteTexture.ToSprite();
        fillSR.color = fillColor; // 초기 노랑
        fillSR.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        if (tmRenderer)
        {
            fillSR.sortingLayerID = tmRenderer.sortingLayerID;
            fillSR.sortingOrder = tmRenderer.sortingOrder + 1;
        }

        // 마스크가 fill을 확실히 포함하도록 정렬 범위 지정
        circleMask.isCustomRangeActive = true;
        circleMask.backSortingLayerID = fillSR.sortingLayerID;
        circleMask.frontSortingLayerID = fillSR.sortingLayerID;
        circleMask.backSortingOrder = fillSR.sortingOrder - 1;
        circleMask.frontSortingOrder = fillSR.sortingOrder + 1;

        RebuildRing(GetCenter());
    }

    private void RebuildRing(Vector3 center)
    {
        if (!ringLine) return;
        for (int i = 0; i <= ringSegments; i++)
        {
            float a = (i / (float)ringSegments) * Mathf.PI * 2f;
            var p = new Vector3(center.x + Mathf.Cos(a) * circleRadius,
                                center.y + Mathf.Sin(a) * circleRadius,
                                center.z);
            ringLine.SetPosition(i, p);
        }
        ringLine.startWidth = ringThickness;
        ringLine.endWidth = ringThickness;
    }

    // ── 모드 전환 시 물리 상태 동기화 ──
    private void SyncPresenceWithMode()
    {
        bool shouldPresent = !reverseVisibility
            ? (charge01 < vanishThreshold)
            : (charge01 >= vanishThreshold);
        if (shouldPresent != isPresent)
            ApplyPresentState(shouldPresent, affectRenderers: false);
    }
}

// ── Texture2D → Sprite 헬퍼 ──
public static class SpriteExtensions
{
    private static Sprite _cachedWhite;
    public static Sprite ToSprite(this Texture2D tex, float ppu = 100f)
    {
        if (!tex) return null;
        if (ReferenceEquals(tex, Texture2D.whiteTexture))
        {
            if (_cachedWhite == null)
            {
                var w = Texture2D.whiteTexture;
                _cachedWhite = Sprite.Create(w, new Rect(0, 0, w.width, w.height), new Vector2(0.5f, 0.5f), ppu);
            }
            return _cachedWhite;
        }
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
    }
}
