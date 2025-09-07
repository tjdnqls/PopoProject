using UnityEngine;

public class SmartCameraFollowByWall : MonoBehaviour
{
    public Transform target1;
    public Transform target2;
    public float followSpeed = 10f;
    public float rayDistance = 8f;
    public float raygroundDistance = 4f;
    public float yOffset = 3f;
    public LayerMask wallLayer;
    public LayerMask groundLayer;
    public SwapController.PlayerChar playerID; // Inspector에서 P1 or P2 지정
    public SwapController swap; // 인스펙터에서 직접 드래그 연결 (SwapController 오브젝트)
    public PlayerMouseMovement carry;
    public bool swapsup = true;
    public GameObject selectmark1;
    public GameObject selectmark2;
    public PlayerMouseMovement rb;

    private bool blockLeft, blockRight, blockUp;
    private Vector3 currentVelocity;
    [SerializeField] private float arrowRotationOffsetDeg = 0f;

    [SerializeField] private Color nearColor = new Color(1f, 0.78f, 0.06f, 1f); // 진한 노랑(Amber #FFC107)
    [SerializeField] private Color farColor = new Color(1f, 0.97f, 0.71f, 1f); // 연한 노랑
    [SerializeField] private float nearDistance = 3f;   // 이 이하이면 거의 nearColor/nearScale
    [SerializeField] private float farDistance = 25f;  // 이 이상이면 거의 farColor/farScale
    [SerializeField] private UnityEngine.UI.Graphic indicatorGraphic; // 화살표 UI(Image 등)

    [SerializeField] private GameObject Knight_UI;
    [SerializeField] private GameObject Princess_UI;

    // ===== Off-screen Indicator UI =====
    [SerializeField] private Camera cam;                    // 비워두면 자동으로 Camera.main 사용
    [SerializeField] private RectTransform canvasRect;      // Canvas의 RectTransform
    [SerializeField] private RectTransform offscreenIndicator; // 화면 가장자리에 붙을 아이콘(화살표)
    [SerializeField] private float edgePadding = 48f;       // 화면 가장자리로부터 여백
    [SerializeField] private bool showDistance = false;     // 원하시면 거리 텍스트도 표시
    [SerializeField] private TMPro.TextMeshProUGUI distanceText; // (선택) 거리 표시 텍스트

    // --- 경고 아이콘(빠른 페이드 인/아웃) ---
    [Header("Danger Warning")]
    [SerializeField] private LayerMask hazardMask;           // Trap | Bullet | Monster 포함
    [SerializeField] private float hazardCheckRadius = 3.0f; // 플레이어2 주변 체크 반경
    [SerializeField] private RectTransform warnIcon;         // 화살표 위에 배치할 경고 아이콘
    [SerializeField] private Vector2 warnScreenOffset = new Vector2(0f, 36f); // 화살표에서 위로 띄우기
    [SerializeField] private float warnBlinkSpeed = 6f;      // 빠르게 반짝
    [SerializeField] private float warnAlphaMin = 0.15f;
    [SerializeField] private float warnAlphaMax = 1f;
    [SerializeField] private float warnFadeOutSpeed = 8f;    // 위험이 사라질 때 빠르게 사라짐
    private CanvasGroup warnGroup;
    private readonly Collider2D[] _hazardHits = new Collider2D[8];

    // === 추가: 전환 제어 ===
    [Header("Tab 전환 이동")]
    [SerializeField] private bool disableWallGroundWhileTransit = true; // 전환 중 벽/바닥 차단 해제
    [SerializeField] private float transitArriveEps = 0.20f;            // 카메라 도착 판정(월드 유닛)
    [SerializeField] private float transitMaxDuration = 1.2f;           // 전환 타임아웃(초)
    [SerializeField] private float transitBoostFollowSpeed = 16f;       // 전환 중 임시 추종 속도
    private bool isTransit = false;
    private float transitUntil = 0f;
    private float originalFollowSpeed = 0f;


    // === 거리 기반 스케일 ===
    [Header("Indicator Scale by Distance")]
    [SerializeField] private float nearScale = 1.4f;   // 가까울 때 화살표 크기
    [SerializeField] private float farScale = 0.7f;    // 멀 때 화살표 크기
    [SerializeField, Tooltip("스케일 보간 속도(초당)")]
    private float scaleLerpSpeed = 12f;

    // 내부 캐시
    private Vector3 indicatorBaseScale = Vector3.one; // 인디케이터 원본 스케일
    private float currentScale = 1f;                  // 현재 배율(1=기본)

    private void Awake()
    {
        if (!cam) cam = Camera.main;
        originalFollowSpeed = followSpeed;

        if (offscreenIndicator)
        {
            if (!indicatorGraphic)
                indicatorGraphic = offscreenIndicator.GetComponent<UnityEngine.UI.Graphic>();
            offscreenIndicator.pivot = new Vector2(0.5f, 0.5f);
            offscreenIndicator.anchorMin = offscreenIndicator.anchorMax = new Vector2(0.5f, 0.5f);

            indicatorBaseScale = offscreenIndicator.localScale;
            currentScale = 1f;
        }

        if (warnIcon)
        {
            warnGroup = warnIcon.GetComponent<CanvasGroup>();
            if (!warnGroup) warnGroup = warnIcon.gameObject.AddComponent<CanvasGroup>();
            warnGroup.alpha = 0f;
            warnIcon.gameObject.SetActive(false);
        }
    }


    private void Reset()
    {
        Knight_UI = gameObject;
        Princess_UI = gameObject;
    }

    void Start()
    {
        selectmark2.SetActive(false);
        selectmark1.SetActive(true);
    }

    void Update()
    {
        Vector3 targetPos1 = target1.position;
        Vector3 targetPos2 = target2.position;
        Vector3 cameraPos = transform.position;

        // === 변경: 전환 중이면 벽/바닥 차단을 해제 ===
        if (isTransit && disableWallGroundWhileTransit)
        {
            blockLeft = blockRight = blockUp = false;
        }
        else
        {
            blockLeft = Physics2D.Raycast(cameraPos, Vector2.left, rayDistance, wallLayer);
            blockRight = Physics2D.Raycast(cameraPos, Vector2.right, rayDistance, wallLayer);

            RaycastHit2D hitUpRaw = Physics2D.Raycast(cameraPos, Vector2.up, raygroundDistance, groundLayer);
            blockUp = hitUpRaw.collider != null && hitUpRaw.collider.tag != "OneWay";
        }

        float targetX = cameraPos.x;
        float targetY = cameraPos.y;

        Transform focus = swapsup ? target1 : target2;

        // === 탭 입력: 전환 시작 시점에서 전환 상태 On + 속도 부스트 ===
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (carry.carryset == false)
            {
                if (!swapsup)
                {
                    Knight_UI.SetActive(true);
                    Princess_UI.SetActive(false);
                    selectmark2.SetActive(false);
                    selectmark1.SetActive(true);
                    swapsup = true;
                }
                else
                {
                    Knight_UI.SetActive(false);
                    Princess_UI.SetActive(true);
                    selectmark2.SetActive(true);
                    selectmark1.SetActive(false);
                    swapsup = false;
                }

                // === 전환 시작 ===
                isTransit = true;
                transitUntil = Time.unscaledTime + transitMaxDuration;
                originalFollowSpeed = Mathf.Approximately(originalFollowSpeed, 0f) ? followSpeed : originalFollowSpeed;
                followSpeed = Mathf.Max(followSpeed, transitBoostFollowSpeed);
            }
        }

        if (swapsup)
        {
            if (!blockLeft && target1.position.x < cameraPos.x) targetX = target1.position.x;
            else if (!blockRight && target1.position.x > cameraPos.x) targetX = target1.position.x;

            float desiredY = target1.position.y + yOffset;
            if (!blockUp && desiredY > cameraPos.y) targetY = desiredY;
            else if (desiredY < cameraPos.y) targetY = desiredY;
        }
        else
        {
            if (!blockLeft && target2.position.x < cameraPos.x) targetX = target2.position.x;
            else if (!blockRight && target2.position.x > cameraPos.x) targetX = target2.position.x;

            float desiredY = target2.position.y + yOffset;
            if (!blockUp && desiredY > cameraPos.y) targetY = desiredY;
            else if (desiredY < cameraPos.y) targetY = desiredY;
        }

        Vector3 desiredPosition = new Vector3(targetX, targetY, cameraPos.z);
        transform.position = Vector3.SmoothDamp(cameraPos, desiredPosition, ref currentVelocity, 1f / followSpeed);

        // === 전환 종료 판정: 충분히 가까워졌거나 타임아웃 ===
        if (isTransit)
        {
            float remain = Vector2.Distance(new Vector2(transform.position.x, transform.position.y),
                                            new Vector2(desiredPosition.x, desiredPosition.y));

            bool arrived = remain <= transitArriveEps || currentVelocity.sqrMagnitude <= 0.0001f;
            bool timedOut = Time.unscaledTime >= transitUntil;

            if (arrived || timedOut)
            {
                isTransit = false;
                followSpeed = originalFollowSpeed; // 속도 원복
                                                   // 이후 프레임부터 다시 벽/바닥 차단 레이가 동작
            }
        }

        // === 쉐이크 보정 ===
        if (CameraShaker.Exists)
        {
            var s = CameraShaker.Instance;
            transform.position += (Vector3)s.CurrentOffset;
            if (Mathf.Abs(s.CurrentAngleZ) > 0.0001f)
                transform.rotation = Quaternion.Euler(0f, 0f, s.CurrentAngleZ);
            else
                transform.rotation = Quaternion.identity;
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }

        Transform self = swapsup ? target1 : target2;
        Transform other = swapsup ? target2 : target1;
        UpdateOffscreenIndicator(other, self);
    }


    void OnDrawGizmos()
    {
        Vector3 cameraPos = transform.position;

        RaycastHit2D hitLeft = Physics2D.Raycast(cameraPos, Vector2.left, rayDistance, wallLayer);
        Gizmos.color = hitLeft.collider ? Color.blue : Color.red;
        Gizmos.DrawLine(cameraPos, cameraPos + Vector3.left * rayDistance);

        RaycastHit2D hitRight = Physics2D.Raycast(cameraPos, Vector2.right, rayDistance, wallLayer);
        Gizmos.color = hitRight.collider ? Color.blue : Color.red;
        Gizmos.DrawLine(cameraPos, cameraPos + Vector3.right * rayDistance);

        RaycastHit2D hitUp = Physics2D.Raycast(cameraPos, Vector2.up, raygroundDistance, groundLayer);
        Gizmos.color = hitUp.collider && hitUp.collider.tag != "OneWay" ? Color.blue : Color.red;
        Gizmos.DrawLine(cameraPos, cameraPos + Vector3.up * raygroundDistance);
    }

    private void UpdateOffscreenIndicator(Transform otherTarget, Transform selfTarget)
    {
        if (!offscreenIndicator || !canvasRect || !otherTarget) return;
        if (!cam) cam = Camera.main;
        if (!cam) return;

        // 1) 대상의 뷰포트 좌표
        Vector3 vp = cam.WorldToViewportPoint(otherTarget.position);

        // 2) 화면 안이면 화살표/경고 숨김
        bool inFront = vp.z > 0f;
        bool onScreen = inFront && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
        if (onScreen)
        {
            offscreenIndicator.gameObject.SetActive(false);
            if (warnIcon) warnIcon.gameObject.SetActive(false);
            // 필요 시 스케일 원복:
            // offscreenIndicator.localScale = indicatorBaseScale;
            return;
        }
        offscreenIndicator.gameObject.SetActive(true);

        // 3) 카메라 뒤 → 반사
        Vector2 v2 = new Vector2(vp.x, vp.y);
        Vector2 center = new Vector2(0.5f, 0.5f);
        if (!inFront) v2 = center - (v2 - center);

        // 4) 방향
        Vector2 dirFromCenter = (v2 - center).normalized;
        if (dirFromCenter.sqrMagnitude < 1e-6f) dirFromCenter = Vector2.right;

        // 5) 경계와 교차 (패딩 반영)
        float padX = edgePadding / Screen.width;
        float padY = edgePadding / Screen.height;
        float minX = padX, maxX = 1f - padX;
        float minY = padY, maxY = 1f - padY;

        float t = float.PositiveInfinity;
        if (Mathf.Abs(dirFromCenter.x) > 1e-6f)
        {
            float tx1 = (minX - center.x) / dirFromCenter.x;
            float tx2 = (maxX - center.x) / dirFromCenter.x;
            if (tx1 > 0) t = Mathf.Min(t, tx1);
            if (tx2 > 0) t = Mathf.Min(t, tx2);
        }
        if (Mathf.Abs(dirFromCenter.y) > 1e-6f)
        {
            float ty1 = (minY - center.y) / dirFromCenter.y;
            float ty2 = (maxY - center.y) / dirFromCenter.y;
            if (ty1 > 0) t = Mathf.Min(t, ty1);
            if (ty2 > 0) t = Mathf.Min(t, ty2);
        }
        if (!float.IsFinite(t) || t <= 0) t = 0.001f;

        Vector2 edgeVP = center + dirFromCenter * t;

        // 스냅 (수치 오차 방지)
        float dxMin = Mathf.Abs(edgeVP.x - minX);
        float dxMax = Mathf.Abs(edgeVP.x - maxX);
        float dyMin = Mathf.Abs(edgeVP.y - minY);
        float dyMax = Mathf.Abs(edgeVP.y - maxY);
        float best = Mathf.Min(Mathf.Min(dxMin, dxMax), Mathf.Min(dyMin, dyMax));
        if (best == dxMin) edgeVP.x = minX;
        else if (best == dxMax) edgeVP.x = maxX;
        else if (best == dyMin) edgeVP.y = minY;
        else edgeVP.y = maxY;

        // 각도 (화살표 끝이 타겟 향함)
        Vector2 dirFromEdgeToTarget = (v2 - edgeVP).normalized;
        if (dirFromEdgeToTarget.sqrMagnitude < 1e-6f) dirFromEdgeToTarget = dirFromCenter;
        float angle = Mathf.Atan2(dirFromEdgeToTarget.y, dirFromEdgeToTarget.x) * Mathf.Rad2Deg + arrowRotationOffsetDeg;

        // 뷰포트→스크린→캔버스 좌표
        Vector2 screenPos = new Vector2(edgeVP.x * Screen.width, edgeVP.y * Screen.height);
        Canvas canvas = canvasRect.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? (canvas.worldCamera ? canvas.worldCamera : cam) : null;

        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, uiCam, out local);
        offscreenIndicator.anchoredPosition = local;
        offscreenIndicator.rotation = Quaternion.Euler(0f, 0f, angle);

        // --- 거리 기반 색 & 스케일 보간 ---
        if (otherTarget && selfTarget)
        {
            float dist = Vector2.Distance(selfTarget.position, otherTarget.position);
            float closeness = 1f - Mathf.InverseLerp(nearDistance, farDistance, dist); // 0(멀다)~1(가깝다)

            // 색 보간
            if (indicatorGraphic)
                indicatorGraphic.color = Color.Lerp(farColor, nearColor, closeness);

            // 스케일 보간: 가까울수록 nearScale, 멀수록 farScale
            float targetScale = Mathf.Lerp(farScale, nearScale, closeness);
            currentScale = Mathf.Lerp(currentScale, targetScale, Time.unscaledDeltaTime * scaleLerpSpeed);
            offscreenIndicator.localScale = indicatorBaseScale * currentScale;

            // 거리 텍스트(선택)
            if (showDistance && distanceText)
                distanceText.text = Mathf.RoundToInt(dist).ToString();
        }

        // --- 위험 감지 & 경고 아이콘 페이드 ---
        if (warnIcon)
        {
            bool danger = IsDangerNear(otherTarget.position);
            // 화살표 위치 기준으로 위쪽에 고정
            warnIcon.anchoredPosition = offscreenIndicator.anchoredPosition + warnScreenOffset;

            if (danger)
            {
                if (!warnIcon.gameObject.activeSelf) warnIcon.gameObject.SetActive(true);
                float tBlink = Mathf.PingPong(Time.unscaledTime * warnBlinkSpeed, 1f); // 빠르게 반짝
                warnGroup.alpha = Mathf.Lerp(warnAlphaMin, warnAlphaMax, tBlink);
            }
            else
            {
                // 위험 없으면 빠르게 사라지고 비활성
                warnGroup.alpha = Mathf.MoveTowards(warnGroup.alpha, 0f, Time.unscaledDeltaTime * warnFadeOutSpeed);
                if (warnGroup.alpha <= 0.01f && warnIcon.gameObject.activeSelf)
                    warnIcon.gameObject.SetActive(false);
            }
        }
    }

    private bool IsDangerNear(Vector2 center)
    {
        // 트랩/불릿/몬스터 레이어에 속한 콜라이더가 하나라도 반경 내에 있으면 true
        return Physics2D.OverlapCircle(center, hazardCheckRadius, hazardMask) != null;
    }
}
