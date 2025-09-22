using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SpiralBoxWipe : MonoBehaviour
{
    public static SpiralBoxWipe Instance { get; private set; }
    public static bool IsBusy { get; private set; }

    // ================== UI ==================
    [Header("UI")]
    [SerializeField] private bool pixelPerfect = true;
    [SerializeField] private TMP_FontAsset youDiedFont;
    [SerializeField] private string youDiedText = "YOU DIED";
    [SerializeField] private Color youDiedColor = new(1f, 0.25f, 0.2f, 1f);
    [SerializeField] private float youDiedFadeIn = 2.0f;
    [SerializeField] private AnimationCurve youDiedFadeCurve = null; // null=EaseInOut
    [SerializeField] private Vector2 textScaleRange = new(0.94f, 1.0f);
    [SerializeField] private float youDiedYOffset = 60f;
    [SerializeField] private float youDiedDelay = 7f; // 시작 7초 뒤 등장

    [Header("AnyKey")]
    [SerializeField] private bool allowAnyKeySkip = true;
    [SerializeField] private string anyKeyText = "Press any key to restart";
    [SerializeField] private float anyKeyBottomMargin = 48f;
    [SerializeField] private float anyKeyFadeIn = 0.8f;

    // ================== Fast Blackout ==================
    [Header("Fast Blackout (Except P2)")]
    [SerializeField] private float blackoutDuration = 0.12f;            // 매우 빠르게
    [SerializeField] private bool useSortingBlackout = true;            // (새) 레이어 방식으로 블랙아웃

    // ================== Camera ==================
    [Header("Camera Zoom & Focus")]
    [SerializeField] private bool enableCameraZoom = true;
    [SerializeField] private float zoomDuration = 5.0f;                 // 슬로모 5초와 동일
    [SerializeField] private AnimationCurve zoomEase = null;            // null=EaseInOut
    [SerializeField] private float targetOrthoSize = 3.5f;
    [SerializeField] private float targetFOV = 35f;
    [SerializeField] private Vector2 zoomFocusOffsetWorld = new(0f, -0.6f); // P2에서 살짝 아래

    [Header("Camera Lock / Anti-jitter")]
    [SerializeField] private bool lockCameraUntilEnd = true;
    [SerializeField] private bool disableCinemachineBrain = true;
    [SerializeField] private bool pixelSnapDuringLock = false;
    [SerializeField] private int assumedPPU = 32;

    // ================== Slow Motion ==================
    [Header("Slow Motion")]
    [SerializeField] private bool enableSlowMo = true;
    [SerializeField] private float slowMoScale = 0.25f;
    [SerializeField] private float slowMoEaseIn = 0.08f;
    [SerializeField] private float slowMoRecover = 0.2f;

    // ================== Spotlight Beam (World) ==================
    [Header("Spotlight Beam (World, behind P2)")]
    [SerializeField] private float beamLength = 5f;          // 월드 길이(Y)
    [SerializeField] private float beamBottomRadius = 1.6f;  // 바닥 반지름
    [SerializeField] private float beamTopOffset = 1.0f;     // 꼭짓점이 P2 위로
    [SerializeField, Range(0f, 1f)] private float beamOpacity = 0.9f;
    [SerializeField] private float beamFadeIn = 0.15f;
    [SerializeField] private bool flipBeamY = true;          // 상하 반전 보정
    [SerializeField] private int beamTexW = 256, beamTexH = 512;
    [SerializeField] private int beamPPU = 100;

    // ================== Sorting Override ==================
    [Header("Death Sorting Override (SpriteRenderer)")]
    [Tooltip("사망 연출 동안 사용할 정렬 레이어 이름 (최상위 전용 레이어를 권장, 예: 'Overlay' / 'Death')")]
    [SerializeField] private string deathSortingLayerName = "Default";
    [Tooltip("검은 배경 SR의 Sorting Order (낮을수록 뒤)")]
    [SerializeField] private int deathBlackOrder = 32758;
    [Tooltip("하얀 스포트라이트 SR의 Sorting Order (블랙보다 앞, P2보다 뒤)")]
    [SerializeField] private int deathBeamOrder = 32759;
    [Tooltip("P2의 Sorting Order (가장 앞)")]
    [SerializeField] private int deathP2Order = 32760;

    // ================== Runtime ==================
    Canvas _canvas;
    TMP_Text _label;
    TMP_Text _anyKey;

    Transform _p2;
    Camera _cam;
    Behaviour _cmBrain;

    // 카메라 오버라이드 상태
    bool _camOverrideActive;
    Vector3 _camStartPos, _camEndPos;
    float _camStartOrtho, _camEndOrtho;
    float _camStartFov, _camEndFov;
    float _camT, _camDur;
    AnimationCurve _camCurve;

    bool _lockCamActive;
    Vector3 _lockPos;
    float? _lockOrtho, _lockFov;

    // 빔 / 블랙커버
    GameObject _beamGO;
    SpriteRenderer _beamSR;
    Sprite _beamSprite;

    GameObject _blackGO;
    SpriteRenderer _blackSR;
    Sprite _blackSpriteUnit; // 1x1 유닛 스프라이트

    // P2 렌더러 백업
    struct SRBak { public SpriteRenderer r; public int layerId; public int order; }
    readonly List<SRBak> _p2SRBackup = new();

    System.Action<Scene, LoadSceneMode> _onSceneLoadedHandler;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildCanvas();
        HideUI();

        _onSceneLoadedHandler = OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        youDiedFadeCurve ??= AnimationCurve.EaseInOut(0, 0, 1, 1);
        zoomEase ??= AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    void OnDestroy()
    {
        if (_onSceneLoadedHandler != null) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _cam = null; _cmBrain = null;
        FindP2();
    }

    // --------------- UI ---------------
    void BuildCanvas()
    {
        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.GetComponent<Canvas>();
        _canvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        canvas.pixelPerfect = pixelPerfect;

        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // YOU DIED
        var textGO = new GameObject("YouDied", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(_canvas.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, youDiedYOffset);
        trt.sizeDelta = new Vector2(1600, 320);

        _label = textGO.GetComponent<TextMeshProUGUI>();
        if (youDiedFont) _label.font = youDiedFont;
        _label.text = youDiedText;
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 180;
        _label.color = new Color(youDiedColor.r, youDiedColor.g, youDiedColor.b, 0f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;
        _label.fontMaterial = new Material(_label.fontMaterial);
        _label.outlineWidth = 0.35f;
        _label.outlineColor = new Color(0, 0, 0, 0.95f);

        // AnyKey (하단 중앙)
        var anyGO = new GameObject("AnyKey", typeof(RectTransform), typeof(TextMeshProUGUI));
        anyGO.transform.SetParent(_canvas.transform, false);
        var art = anyGO.GetComponent<RectTransform>();
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0f);
        art.pivot = new Vector2(0.5f, 0f);
        art.anchoredPosition = new Vector2(0f, anyKeyBottomMargin);
        art.sizeDelta = new Vector2(1600, 180);

        _anyKey = anyGO.GetComponent<TextMeshProUGUI>();
        _anyKey.text = anyKeyText;
        _anyKey.alignment = TextAlignmentOptions.Center;
        _anyKey.fontSize = 48;
        _anyKey.color = new Color(1f, 1f, 1f, 0f);
        SetAlpha(_anyKey, 0f);
        _anyKey.textWrappingMode = TextWrappingModes.NoWrap;
        _anyKey.overflowMode = TextOverflowModes.Overflow;
        _anyKey.raycastTarget = false;
    }

    void HideUI()
    {
        if (_label) SetAlpha(_label, 0f);
        if (_anyKey) SetAlpha(_anyKey, 0f);
        if (_canvas) _canvas.enabled = true;
        DestroyBeam();
        DestroyBlackCover();
        _p2SRBackup.Clear();
    }

    void SetAlpha(TMP_Text t, float a) { var c = t.color; c.a = a; t.color = c; }

    // --------------- Target / Camera ---------------
    void FindP2()
    {
        _p2 = null;
        var players = FindObjectsByType<PlayerMouseMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
            if (players[i].playerID == SwapController.PlayerChar.P2) { _p2 = players[i].transform; break; }
        if (_p2 == null)
        {
            var ts = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in ts)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("p2") || n.Contains("player2")) { _p2 = t; break; }
            }
        }
    }

    Camera FindBestCamera()
    {
        var cam = Camera.main;
        if (cam != null && cam.enabled) return cam;

        var cams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Camera best = null; float bestDepth = float.NegativeInfinity;
        foreach (var c in cams)
            if (c && c.enabled && c.gameObject.activeInHierarchy && c.targetDisplay == 0 && c.depth >= bestDepth)
            { best = c; bestDepth = c.depth; }
        return best ?? cam;
    }

    Behaviour FindCinemachineBrain(Camera cam)
    {
        if (!cam) return null;
        var comps = cam.GetComponents<Behaviour>();
        foreach (var b in comps)
            if (b && b.GetType().Name == "CinemachineBrain") return b;
        return null;
    }

    // --------------- LateUpdate: Camera only here (anti-jitter) ---------------
    void LateUpdate()
    {
        if (_camOverrideActive && _cam)
        {
            float k = (_camDur <= 0f) ? 1f : Mathf.Clamp01(_camT / _camDur);
            if (_camCurve != null) k = _camCurve.Evaluate(k);

            Vector3 pos = Vector3.Lerp(_camStartPos, _camEndPos, k);
            if (_cam.orthographic)
                _cam.orthographicSize = Mathf.Lerp(_camStartOrtho, _camEndOrtho, k);
            else
                _cam.fieldOfView = Mathf.Lerp(_camStartFov, _camEndFov, k);

            if (pixelSnapDuringLock) pos = PixelSnap(pos, _cam);
            _cam.transform.position = pos;

            // 블랙 커버는 화면 크기에 맞춰 매 프레임 스케일 갱신(줌에 대응)
            if (_blackSR) UpdateBlackCoverTransform();

            _camT += Time.unscaledDeltaTime;
            if (_camT >= _camDur)
            {
                _camOverrideActive = false;

                if (lockCameraUntilEnd)
                {
                    _lockPos = _cam.transform.position;
                    _lockOrtho = _cam.orthographic ? _cam.orthographicSize : (float?)null;
                    _lockFov = !_cam.orthographic ? _cam.fieldOfView : (float?)null;
                    _lockCamActive = true;
                }
            }
        }

        if (_lockCamActive && _cam)
        {
            Vector3 pos = _lockPos;
            if (pixelSnapDuringLock) pos = PixelSnap(pos, _cam);
            _cam.transform.position = pos;
            if (_lockOrtho.HasValue && _cam.orthographic) _cam.orthographicSize = _lockOrtho.Value;
            if (_lockFov.HasValue && !_cam.orthographic) _cam.fieldOfView = _lockFov.Value;

            if (_blackSR) UpdateBlackCoverTransform();
        }
    }

    Vector3 PixelSnap(Vector3 pos, Camera cam)
    {
        int ppu = assumedPPU;
        var ppc = cam.GetComponent("PixelPerfectCamera");
        if (ppc != null)
        {
            var ppuProp = ppc.GetType().GetProperty("assetsPPU");
            if (ppuProp != null) ppu = (int)ppuProp.GetValue(ppc);
        }
        float unitsPerPixel = 1f / Mathf.Max(1, ppu);
        pos.x = Mathf.Round(pos.x / unitsPerPixel) * unitsPerPixel;
        pos.y = Mathf.Round(pos.y / unitsPerPixel) * unitsPerPixel;
        return pos;
    }

    // --------------- Public API ---------------
    public static void Run(string sceneName)
    {
        Ensure();
        Instance.StopAllCoroutines();
        Instance.StartCoroutine(Instance.PlayRoutine(sceneName));
    }
    public static void RunActiveScene() => Run(SceneManager.GetActiveScene().name);
    public static void Ensure()
    {
        if (!Instance) new GameObject("YouDiedWipe").AddComponent<SpiralBoxWipe>();
    }

    // --------------- Main Routine ---------------
    IEnumerator PlayRoutine(string sceneName)
    {
        IsBusy = true;

        _cam = FindBestCamera();
        if (disableCinemachineBrain) _cmBrain = FindCinemachineBrain(_cam);
        if (_cmBrain && disableCinemachineBrain) _cmBrain.enabled = false;

        // 초기 UI
        SetAlpha(_label, 0f);
        SetAlpha(_anyKey, 0f);

        FindP2();

        // === 1) 순간 블랙(Except P2) : 레이어 블랙아웃 & P2 최전방 ===
        if (useSortingBlackout)
        {
            BringP2ToFront();
            CreateOrUpdateBlackCover();     // 검은 SR 생성(카메라 자식)
            // 빠른 페이드
            float t = 0f;
            while (t < blackoutDuration)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, blackoutDuration));
                var c = _blackSR.color; c.a = k; _blackSR.color = c;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            { var c = _blackSR.color; c.a = 1f; _blackSR.color = c; }
        }

        // === 2) 스포트라이트 콘(P2 뒤) 생성 & 빠른 페이드인 ===
        CreateOrUpdateBeam();
        yield return StartCoroutine(FadeInBeam());

        // === 3) 카메라 5초 줌 + 포커스(살짝 아래) / 슬로모 5초 ===
        if (enableCameraZoom && _cam)
        {
            Vector3 startPos = _cam.transform.position;
            Vector3 endPos = startPos;
            if (_p2) endPos = new Vector3(_p2.position.x + zoomFocusOffsetWorld.x,
                                          _p2.position.y + zoomFocusOffsetWorld.y,
                                          startPos.z);

            _camOverrideActive = true;
            _camT = 0f; _camDur = zoomDuration; _camCurve = zoomEase;
            _camStartPos = startPos; _camEndPos = endPos;

            if (_cam.orthographic)
            { _camStartOrtho = _cam.orthographicSize; _camEndOrtho = Mathf.Max(0.01f, targetOrthoSize); }
            else
            { _camStartFov = _cam.fieldOfView; _camEndFov = Mathf.Clamp(targetFOV, 1f, 179f); }

            if (enableSlowMo)
                yield return StartCoroutine(SlowMoFor(duration: zoomDuration, targetScale: slowMoScale, easeIn: slowMoEaseIn, recover: slowMoRecover));
            else
            {
                float w = 0f; while (w < zoomDuration) { w += Time.unscaledDeltaTime; yield return null; }
            }
        }

        // === 4) (시작 후 7초) 게임오버 텍스트 천천히 뜨기 ===
        float elapsed = 0f;
        while (elapsed < Mathf.Max(0f, youDiedDelay - blackoutDuration)) { elapsed += Time.unscaledDeltaTime; yield return null; }

        float tf = 0f;
        _label.rectTransform.localScale = Vector3.one * textScaleRange.x;
        while (tf < youDiedFadeIn)
        {
            float k = Mathf.Clamp01(tf / Mathf.Max(0.0001f, youDiedFadeIn));
            k = youDiedFadeCurve != null ? youDiedFadeCurve.Evaluate(k) : k;
            SetAlpha(_label, k);
            _label.rectTransform.localScale = Vector3.Lerp(Vector3.one * textScaleRange.x, Vector3.one * textScaleRange.y, k);
            tf += Time.unscaledDeltaTime;
            yield return null;
        }
        SetAlpha(_label, 1f);

        // === 5) 텍스트가 모두 뜬 뒤 AnyKey 등장 ===
        yield return StartCoroutine(FadeInAnyKey());

        // === 입력/자동 재시작 대기 ===
        float waitAuto = 0f;
        while (true)
        {
            if (allowAnyKeySkip && Input.anyKeyDown) break;
            // 자동 재시작 쓰고 싶으면 외부에서 처리(또는 여기서 시간 체크 추가)
            yield return null;
        }

        // 정리
        _lockCamActive = false;
        if (_cmBrain && disableCinemachineBrain) _cmBrain.enabled = true;

        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        HideUI();
        IsBusy = false;
    }

    // --------------- Helpers ---------------
    IEnumerator SlowMoFor(float duration, float targetScale, float easeIn, float recover)
    {
        float origScale = Time.timeScale;
        float origFixed = Time.fixedDeltaTime;

        float t = 0f;
        while (t < easeIn)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, easeIn));
            Time.timeScale = Mathf.Lerp(origScale, targetScale, k);
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Time.timeScale = targetScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        float hold = 0f; while (hold < duration) { hold += Time.unscaledDeltaTime; yield return null; }

        t = 0f;
        while (t < recover)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, recover));
            Time.timeScale = Mathf.Lerp(targetScale, 1f, k);
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    IEnumerator FadeInAnyKey()
    {
        float t = 0f;
        while (t < anyKeyFadeIn)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, anyKeyFadeIn));
            SetAlpha(_anyKey, k);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        SetAlpha(_anyKey, 1f);
    }

    // ---------- Spotlight Beam ----------
    void CreateOrUpdateBeam()
    {
        if (_p2 == null) return;

        if (_beamGO == null)
        {
            _beamGO = new GameObject("SpotlightBeam", typeof(SpriteRenderer));
            _beamGO.transform.SetParent(_p2, worldPositionStays: false);
            _beamSR = _beamGO.GetComponent<SpriteRenderer>();
            _beamSR.color = new Color(1f, 1f, 1f, 0f); // 나중에 페이드인
            _beamSprite = GenerateBeamSprite(beamTexW, beamTexH, beamOpacity);
            _beamSR.sprite = _beamSprite;
            _beamSR.drawMode = SpriteDrawMode.Simple;
        }

        // 정렬: 같은 레이어, 블랙 앞 / P2 뒤
        int layerId = SortingLayer.NameToID(deathSortingLayerName);
        _beamSR.sortingLayerID = layerId;
        _beamSR.sortingOrder = deathBeamOrder;

        // 상하 반전 보정
        _beamSR.flipY = flipBeamY;

        // 꼭짓점이 P2 위쪽이 되게
        _beamGO.transform.localPosition = new Vector3(0f, beamTopOffset, 0f);

        // 길이/폭 스케일 (Pivot이 top-center라 Y가 길이, X가 아래쪽 지름)
        float spriteH = _beamSR.sprite.rect.height / (float)beamPPU;
        float spriteW = _beamSR.sprite.rect.width / (float)beamPPU;
        float scaleY = beamLength / Mathf.Max(0.0001f, spriteH);
        float scaleX = (beamBottomRadius * 2f) / Mathf.Max(0.0001f, spriteW);
        _beamGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    IEnumerator FadeInBeam()
    {
        if (_beamSR == null) yield break;
        float t = 0f;
        while (t < beamFadeIn)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, beamFadeIn));
            var c = _beamSR.color; c.a = k * beamOpacity; _beamSR.color = c;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        var c2 = _beamSR.color; c2.a = beamOpacity; _beamSR.color = c2;
    }

    void DestroyBeam()
    {
        if (_beamGO != null) Destroy(_beamGO);
        _beamGO = null; _beamSR = null; _beamSprite = null;
    }

    Sprite GenerateBeamSprite(int w, int h, float opacity)
    {
        // Pivot: Top center(0.5, 1) → 꼭짓점이 로컬 위쪽
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        var cols = new Color32[w * h];

        float capY = 0.22f;
        float capRX = 0.5f;
        float feather = 2.5f / w;

        for (int yy = 0; yy < h; yy++)
        {
            float y = (yy + 0.5f) / h;                   // 0(top)~1(bottom)
            float halfTri = Mathf.Pow(y, 0.7f) * 0.5f;
            float halfCap = 0f;
            if (y > 1f - capY)
            {
                float ny = (y - (1f - capY)) / capY;     // 0~1
                halfCap = capRX * Mathf.Sqrt(Mathf.Max(0f, 1f - (ny - 1f) * (ny - 1f)));
            }
            float half = Mathf.Max(halfTri, halfCap);
            float baseAlpha = Mathf.Clamp01(Mathf.Pow(y, 1.2f)) * opacity;

            for (int xx = 0; xx < w; xx++)
            {
                float x = (xx + 0.5f) / w;
                float nx = (x - 0.5f);
                float distEdge = (half - Mathf.Abs(nx));
                float a = Mathf.Clamp01(distEdge / Mathf.Max(1e-5f, feather));
                a = Mathf.SmoothStep(0f, 1f, a) * baseAlpha;
                cols[yy * w + xx] = new Color(a, a, a, a);
            }
        }
        tex.SetPixels32(cols);
        tex.Apply(false, false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), beamPPU);
        return sprite;
    }

    // ---------- Black Cover ----------
    void CreateOrUpdateBlackCover()
    {
        if (_cam == null) _cam = FindBestCamera();

        if (_blackGO == null)
        {
            _blackGO = new GameObject("DeathBlackCover", typeof(SpriteRenderer));
            _blackGO.transform.SetParent(_cam.transform, worldPositionStays: false);
            _blackSR = _blackGO.GetComponent<SpriteRenderer>();
            _blackSR.color = new Color(0f, 0f, 0f, 0f); // 알파는 페이드로
            _blackSR.sortingLayerID = SortingLayer.NameToID(deathSortingLayerName);
            _blackSR.sortingOrder = deathBlackOrder;

            if (_blackSpriteUnit == null)
            {
                var t = new Texture2D(1, 1, TextureFormat.ARGB32, false, true);
                t.SetPixel(0, 0, Color.white);
                t.Apply(false, false);
                _blackSpriteUnit = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f); // 1유닛
            }
            _blackSR.sprite = _blackSpriteUnit;
        }

        UpdateBlackCoverTransform();
    }

    void UpdateBlackCoverTransform()
    {
        if (_cam == null || _blackSR == null) return;

        if (_cam.orthographic)
        {
            float h = _cam.orthographicSize * 2f;
            float w = h * _cam.aspect;
            _blackGO.transform.localPosition = new Vector3(0f, 0f, 1f);
            _blackGO.transform.localRotation = Quaternion.identity;
            _blackGO.transform.localScale = new Vector3(w, h, 1f);
        }
        else
        {
            // near + d 만큼 앞에 평면을 놓고 화면 크기에 맞게 스케일
            float d = Mathf.Max(0.5f, _cam.nearClipPlane + 0.5f);
            float h = 2f * d * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * _cam.aspect;
            _blackGO.transform.localPosition = new Vector3(0f, 0f, d);
            _blackGO.transform.localRotation = Quaternion.identity;
            _blackGO.transform.localScale = new Vector3(w, h, 1f);
        }
    }

    void DestroyBlackCover()
    {
        if (_blackGO != null) Destroy(_blackGO);
        _blackGO = null; _blackSR = null;
    }

    // ---------- Bring P2 to Front ----------
    void BringP2ToFront()
    {
        if (_p2 == null) return;

        _p2SRBackup.Clear();
        var all = _p2.GetComponentsInChildren<SpriteRenderer>(true);
        int layerId = SortingLayer.NameToID(deathSortingLayerName);

        // 백업 & 올리기
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            _p2SRBackup.Add(new SRBak { r = r, layerId = r.sortingLayerID, order = r.sortingOrder });
            r.sortingLayerID = layerId;
            r.sortingOrder = deathP2Order + i; // 여러 SR이 있으면 상대 순서 유지
        }
    }
}
