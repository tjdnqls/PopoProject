using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시작~끝 기준으로 '공주/플레이어1' 진행도를 바/미니맵에 표시.
/// 공주 주변에 Monster/Trap 레이어 감지 시 공주 마커(그리고 선택적으로 미니맵 점) 페이드 인/아웃.
/// </summary>
public class PrincessProgressUI : MonoBehaviour
{
    [Header("World References")]
    [SerializeField] private Transform princess;
    [SerializeField] private Transform player1;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;

    [Header("Progress Bar UI (1D)")]
    [SerializeField] private RectTransform barRect;           // 가로 바 영역
    [SerializeField] private RectTransform markerPrincess;    // 공주 마커
    [SerializeField] private RectTransform markerPlayer1;     // P1 마커
    [SerializeField] private Image fillImage;                 // (선택) 채우기 이미지(Filled-Horizontal 권장)

    [Header("MiniMap UI (2D, Optional)")]
    [SerializeField] private RectTransform miniMapRect;       // 미니맵 박스
    [SerializeField] private RectTransform dotPrincess;       // 공주 점
    [SerializeField] private RectTransform dotPlayer1;        // P1 점
    [Tooltip("시작/끝 AABB에 여유 패딩(월드 유닛)")]
    [SerializeField] private float worldPadding = 1f;

    [Header("Smoothing")]
    [Tooltip("UI 보간 시간(초). 0이면 즉시 반영")]
    [SerializeField] private float smoothTime = 0.08f;
    private float _tPrincessSmoothed; // 0~1
    private float _tP1Smoothed;       // 0~1

    // ================== Danger Blink ==================
    [Header("Danger Blink (Princess)")]
    [Tooltip("위험 감지 레이어(예: Monster, Trap)")]
    [SerializeField] private LayerMask dangerMask;
    [Tooltip("공주 주변 위험 감지 반경(월드 유닛)")]
    [SerializeField] private float dangerRadius = 2.0f;
    [Tooltip("페이드 깜빡임 속도(주기/초)")]
    [SerializeField] private float blinkSpeed = 4.0f;
    [Tooltip("깜빡임 알파 최소/최대")]
    [SerializeField] private float blinkMinAlpha = 0.35f;
    [SerializeField] private float blinkMaxAlpha = 1.0f;
    [Tooltip("미니맵 점도 같이 깜빡일지")]
    [SerializeField] private bool blinkMiniMapDotAlso = true;
    [Tooltip("UnscaledTime 사용(일시정지 중에도 깜빡임 유지)")]
    [SerializeField] private bool useUnscaledTime = true;

    // 캐시/상태
    private CanvasGroup _cgMarkerPrincess;
    private Graphic[] _gfxMarkerPrincess;

    private CanvasGroup _cgDotPrincess;
    private Graphic[] _gfxDotPrincess;

    private float _blinkPhase;         // 누적 위상
    private bool _dangerNow;           // 이번 프레임 위험 감지
    private bool _dangerPrev;          // 이전 프레임 위험 감지

    // NonAlloc 캐시
    private readonly Collider2D[] _dangerHits = new Collider2D[8];

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color gizmoColor = new Color(1f, 0.6f, 0.2f, 1f);

    void Reset()
    {
        if (!princess) princess = GameObject.Find("Princess")?.transform;
        if (!player1) player1 = GameObject.Find("Player1")?.transform;
    }

    void Awake()
    {
        // 공주 마커 그래픽 캐시
        if (markerPrincess)
        {
            markerPrincess.TryGetComponent(out _cgMarkerPrincess);
            _gfxMarkerPrincess = markerPrincess.GetComponentsInChildren<Graphic>(true);
        }
        // 미니맵 점 그래픽 캐시
        if (dotPrincess)
        {
            dotPrincess.TryGetComponent(out _cgDotPrincess);
            _gfxDotPrincess = dotPrincess.GetComponentsInChildren<Graphic>(true);
        }
    }

    void LateUpdate()
    {
        // ===== 진행도 계산 =====
        if (!startPoint || !endPoint) return;

        Vector2 s = startPoint.position;
        Vector2 e = endPoint.position;
        Vector2 v = e - s;
        float len = v.magnitude;
        if (len <= 1e-5f)
        {
            UpdateBarUI(0f, 0f);
            UpdateMiniMapUI(Vector2.zero, Vector2.zero, s, e);
            return;
        }

        Vector2 dir = v / len;

        float tPrincess = 0f, tP1 = 0f;
        Vector2 pPos = s, p1Pos = s;

        if (princess)
        {
            pPos = princess.position;
            float projP = Mathf.Clamp(Vector2.Dot(pPos - s, dir), 0f, len);
            tPrincess = projP / len;
        }
        if (player1)
        {
            p1Pos = player1.position;
            float proj1 = Mathf.Clamp(Vector2.Dot(p1Pos - s, dir), 0f, len);
            tP1 = proj1 / len;
        }

        // 스무딩
        if (smoothTime > 0f)
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(1e-4f, smoothTime));
            _tPrincessSmoothed = Mathf.Lerp(_tPrincessSmoothed, tPrincess, k);
            _tP1Smoothed = Mathf.Lerp(_tP1Smoothed, tP1, k);
        }
        else
        {
            _tPrincessSmoothed = tPrincess;
            _tP1Smoothed = tP1;
        }

        // UI 반영
        UpdateBarUI(_tPrincessSmoothed, _tP1Smoothed);
        UpdateMiniMapUI(pPos, p1Pos, s, e);

        // ===== 위험 감지 & 깜빡임 =====
        UpdateDangerState();
        UpdateBlinkVisuals();
    }

    // ---------------- Progress Bar (피벗 보정: 항상 왼쪽→오른쪽) ----------------
    private void UpdateBarUI(float tPrincess, float tP1)
    {
        tPrincess = Mathf.Clamp01(tPrincess);
        tP1 = Mathf.Clamp01(tP1);

        if (barRect)
        {
            float w = barRect.rect.width;
            float leftX = -w * barRect.pivot.x;
            float rightX = w * (1f - barRect.pivot.x);

            if (markerPrincess)
            {
                var pos = markerPrincess.anchoredPosition;
                pos.x = Mathf.Lerp(leftX, rightX, tPrincess);
                markerPrincess.anchoredPosition = pos;
            }
            if (markerPlayer1)
            {
                var pos = markerPlayer1.anchoredPosition;
                pos.x = Mathf.Lerp(leftX, rightX, tP1);
                markerPlayer1.anchoredPosition = pos;
            }
        }

        if (fillImage)
        {
            if (fillImage.type != Image.Type.Filled) fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = tPrincess; // 공주 진행도로 채움(필요시 바꾸세요)
        }
    }

    // ---------------- MiniMap (피벗 보정: 항상 왼쪽-아래 기준) ----------------
    private void UpdateMiniMapUI(Vector2 princessWorld, Vector2 p1World, Vector2 s, Vector2 e)
    {
        if (!miniMapRect) return;

        Vector2 min = new Vector2(Mathf.Min(s.x, e.x), Mathf.Min(s.y, e.y)) - Vector2.one * worldPadding;
        Vector2 max = new Vector2(Mathf.Max(s.x, e.x), Mathf.Max(s.y, e.y)) + Vector2.one * worldPadding;

        float w = miniMapRect.rect.width;
        float h = miniMapRect.rect.height;
        float leftUI = -w * miniMapRect.pivot.x;
        float bottomUI = -h * miniMapRect.pivot.y;

        if (dotPrincess)
        {
            float nx = Mathf.InverseLerp(min.x, max.x, princessWorld.x);
            float ny = Mathf.InverseLerp(min.y, max.y, princessWorld.y);
            var pos = dotPrincess.anchoredPosition;
            pos.x = leftUI + nx * w;
            pos.y = bottomUI + ny * h;
            dotPrincess.anchoredPosition = pos;
        }

        if (dotPlayer1)
        {
            float nx = Mathf.InverseLerp(min.x, max.x, p1World.x);
            float ny = Mathf.InverseLerp(min.y, max.y, p1World.y);
            var pos = dotPlayer1.anchoredPosition;
            pos.x = leftUI + nx * w;
            pos.y = bottomUI + ny * h;
            dotPlayer1.anchoredPosition = pos;
        }
    }

    // ---------------- Danger detection + Blink ----------------
    private void UpdateDangerState()
    {
        _dangerPrev = _dangerNow;

        if (!princess)
        {
            _dangerNow = false;
            return;
        }

        int count = Physics2D.OverlapCircleNonAlloc(princess.position, dangerRadius, _dangerHits, dangerMask);
        _dangerNow = (count > 0);
        if (!_dangerPrev && _dangerNow)
        {
            // 막 위험 진입 시 위상 초기화로 즉시 눈에 띄게
            _blinkPhase = 0f;
        }
    }

    private void UpdateBlinkVisuals()
    {
        if (_dangerNow)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _blinkPhase += Mathf.Max(0f, blinkSpeed) * dt * Mathf.PI * 2f; // rad/s
            float s = 0.5f * (1f + Mathf.Sin(_blinkPhase)); // 0~1
            float alpha = Mathf.Lerp(blinkMinAlpha, blinkMaxAlpha, s);

            // 공주 바 마커
            ApplyAlphaToTarget(markerPrincess, _cgMarkerPrincess, _gfxMarkerPrincess, alpha);

            // (옵션) 미니맵 점
            if (blinkMiniMapDotAlso)
                ApplyAlphaToTarget(dotPrincess, _cgDotPrincess, _gfxDotPrincess, alpha);
        }
        else
        {
            // 위험 없음: 알파 복구
            ApplyAlphaToTarget(markerPrincess, _cgMarkerPrincess, _gfxMarkerPrincess, 1f);
            if (blinkMiniMapDotAlso)
                ApplyAlphaToTarget(dotPrincess, _cgDotPrincess, _gfxDotPrincess, 1f);
        }
    }

    private static void ApplyAlphaToTarget(RectTransform target, CanvasGroup cg, Graphic[] gfxList, float a)
    {
        if (!target) return;

        if (cg)
        {
            cg.alpha = a;
            return;
        }

        if (gfxList != null && gfxList.Length > 0)
        {
            for (int i = 0; i < gfxList.Length; i++)
            {
                var g = gfxList[i];
                if (!g) continue;
                var c = g.color;
                c.a = a;
                g.color = c;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (drawGizmos && princess)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(princess.position, dangerRadius);
        }

        if (!drawGizmos || !startPoint || !endPoint) return;
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(startPoint.position, 0.15f);
        Gizmos.DrawSphere(endPoint.position, 0.15f);
        Gizmos.DrawLine(startPoint.position, endPoint.position);
    }
}
