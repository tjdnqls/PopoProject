using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using static UnityEngine.GraphicsBuffer;

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
    public Player1HP dead;
    public Player2HP deade;
    public bool deadcount;

    // ---- 내부 상태 ----
    private bool blockLeft, blockRight, blockUp; // 디버그용 표시(실제 이동은 BoxCast로 제한)
    private Vector3 currentVelocity;
    [SerializeField] private float arrowRotationOffsetDeg = 0f;

    // === Auto select P1 when carry starts ===
    [Header("Auto Select to P1 on Carry")]
    [SerializeField] private bool autoSelectP1OnCarry = true;
    private bool wasCarrying = false; // 캐리 상태 상승엣지 감지용

    [SerializeField] private Color nearColor = new Color(1f, 0.78f, 0.06f, 1f); // 진한 노랑(Amber #FFC107)
    [SerializeField] private Color farColor = new Color(1f, 0.97f, 0.71f, 1f); // 연한 노랑
    [SerializeField] private float nearDistance = 3f;   // 이 이하이면 거의 nearColor/nearScale
    [SerializeField] private float farDistance = 25f;  // 이 이상이면 거의 farColor/farScale
    [SerializeField] private UnityEngine.UI.Graphic indicatorGraphic; // 화살표 UI(Image 등)

    [SerializeField] private GameObject Knight_UI;
    [SerializeField] private GameObject Princess_UI;

    // ===== Off-screen Indicator UI =====
    [SerializeField] private Camera cam;                       // 비워두면 자동으로 Camera.main 사용
    [SerializeField] private RectTransform canvasRect;         // Canvas의 RectTransform
    [SerializeField] private RectTransform offscreenIndicator; // 화면 가장자리에 붙을 아이콘(화살표)
    [SerializeField] private float edgePadding = 48f;          // 화면 가장자리로부터 여백
    [SerializeField] private bool showDistance = false;        // 원하면 거리 텍스트 표시
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

    // === 전환 제어 ===
    [Header("Tab 전환 이동")]
    [SerializeField] private bool disableWallGroundWhileTransit = true; // 전환 중 벽/바닥 차단 해제(옵션)
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

    // ====== BoxCast Confiner(끼임 방지) ======
    [Header("BoxCast Confiner (anti-stuck)")]
    [SerializeField, Tooltip("카메라가 통과 가능한 ‘상자’로 가정하고, 이동 경로를 BoxCast로 제한합니다.")]
    private bool useBoxCastConfiner = true;

    [SerializeField, Tooltip("충돌면과의 최소 이격(스킨). 너무 작으면 살짝 겹치고, 너무 크면 목적지에 못 닿습니다.")]
    private float confinerSkin = 0.08f;

    [SerializeField, Tooltip("BoxCast 크기를 이만큼 축소해서 불필요한 접촉을 줄입니다.")]
    private float boxShrink = 0.02f;

    [SerializeField, Tooltip("같은 자리에서 다섯 프레임 이상 ‘꿈쩍’도 못하면 잠깐 콘파이너 해제(탈출용).")]
    private int stuckFramesToForgive = 5;

    [SerializeField, Tooltip("탈출용으로 콘파이너를 잠깐 끄는 시간(초).")]
    private float forgiveSeconds = 0.15f;

    private float forgiveUntil = 0f;
    private int stuckFrameCounter = 0;
    private Vector3 lastAppliedPos;

    private LayerMask ConfinerMask => wallLayer | groundLayer;

    // ====== Swap SFX (OneShot 전환음 전용) ======
    [Header("Swap SFX (OneShot)")]
    [SerializeField] private bool useSoundManagerOneShot = true;     // SoundManager의 PlayOneShot 사용 여부
    [SerializeField] private string knightSwapSfxKey = "KnightChenge";
    [SerializeField] private string princessSwapSfxKey = "PrincessChenge";
    [SerializeField] private AudioClip knightSwapClip;               // SoundManager 미사용 시 사용
    [SerializeField] private AudioClip princessSwapClip;             // SoundManager 미사용 시 사용
    [SerializeField] private float minSwapSfxInterval = 0.2f;        // 중복 이벤트 디바운스 간격

    private bool _lastIsP1Focus;     // 직전 프레임의 시점 대상(P1=true/P2=false)
    private float _lastSwapSfxTime = -999f;
    private AudioSource _swapAudio;  // 로컬 OneShot 재생용

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

        // Swap OneShot용 로컬 AudioSource(백업 경로)
        if (!useSoundManagerOneShot)
        {
            _swapAudio = GetComponent<AudioSource>();
            if (_swapAudio == null) _swapAudio = gameObject.AddComponent<AudioSource>();
            _swapAudio.playOnAwake = false;
            _swapAudio.loop = false;
            _swapAudio.spatialBlend = 0f; // UI 성격이면 0, 3D면 1로 변경
        }

        lastAppliedPos = transform.position;
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
        wasCarrying = (carry != null && carry.isCarrying);

        // SFX: 시작 시 베이스라인 저장(즉시 재생 방지)
        _lastIsP1Focus = swapsup; // swapsup == true면 P1, false면 P2
    }

    void Update()
    {
        if (!cam) cam = Camera.main;
        Vector3 cameraPos = transform.position;

        // ===== 목표 선택 =====
        if (SpiralBoxWipe.IsBusy && deade.IsDead == true)
            swapsup = false;

        if (Input.GetKeyDown(KeyCode.Tab) && dead.Dead == false && !SpiralBoxWipe.IsBusy)
        {
            if (carry.isCarrying == false)
            {
                swapsup = !swapsup;
                // 전환 시작
                isTransit = true;
                transitUntil = Time.unscaledTime + transitMaxDuration;
                originalFollowSpeed = Mathf.Approximately(originalFollowSpeed, 0f) ? followSpeed : originalFollowSpeed;
                followSpeed = Mathf.Max(followSpeed, transitBoostFollowSpeed);
                // 전환음은 프레임 말미에서 일괄 감지
            }
        }

        if (autoSelectP1OnCarry && carry != null)
        {
            bool nowCarrying = carry.isCarrying;

            // P2 시점(swapsup == false)일 때 캐리가 '시작'되면 강제 전환
            if (nowCarrying && !wasCarrying && !swapsup)
            {
                ForceToP1();
            }
            wasCarrying = nowCarrying;
        }
        Transform focus = swapsup ? target1 : target2;

        // ===== UI 표기 =====
        if (swapsup)
        {
            Knight_UI.SetActive(true);
            Princess_UI.SetActive(false);
            selectmark2.SetActive(false);
            selectmark1.SetActive(true);
        }
        else
        {
            Knight_UI.SetActive(false);
            Princess_UI.SetActive(true);
            selectmark2.SetActive(true);
            selectmark1.SetActive(false);
        }

        // ===== 디버그 레이(표시용) =====
        {
            blockLeft = Physics2D.Raycast(cameraPos, Vector2.left, rayDistance, wallLayer);
            blockRight = Physics2D.Raycast(cameraPos, Vector2.right, rayDistance, wallLayer);
            var hitUpRaw = Physics2D.Raycast(cameraPos, Vector2.up, raygroundDistance, groundLayer);
            blockUp = hitUpRaw.collider != null && hitUpRaw.collider.tag != "OneWay";
        }

        // ===== 목표 위치 =====
        float targetX = focus.position.x;
        float desiredY = focus.position.y + yOffset;
        float targetY = desiredY;

        Vector3 desired = new Vector3(targetX, targetY, cameraPos.z);

        // ===== 부드러운 추종 =====
        Vector3 smooth = Vector3.SmoothDamp(cameraPos, desired, ref currentVelocity, 1f / followSpeed);

        // ===== 전환 중 콘파이너 우회 여부 =====
        bool bypassConfiner = isTransit && disableWallGroundWhileTransit;

        // ===== BoxCast Confiner =====
        Vector3 nextPos = smooth;
        if (useBoxCastConfiner && !bypassConfiner)
        {
            nextPos = ConfineMoveByBoxCast(cameraPos, smooth);
        }

        // ===== 전환 종료 판정 =====
        if (isTransit)
        {
            float remain = Vector2.Distance(new Vector2(nextPos.x, nextPos.y),
                                            new Vector2(desired.x, desired.y));
            bool arrived = remain <= transitArriveEps || currentVelocity.sqrMagnitude <= 0.0001f;
            bool timedOut = Time.unscaledTime >= transitUntil;

            if (arrived || timedOut)
            {
                isTransit = false;
                followSpeed = originalFollowSpeed; // 속도 원복
            }
        }

        // ===== 실제 위치 반영 =====
        transform.position = nextPos;

        // ===== 끼임 탈출 =====
        if (useBoxCastConfiner && !bypassConfiner)
        {
            float moved = (nextPos - lastAppliedPos).sqrMagnitude;
            float wantMove = (smooth - cameraPos).sqrMagnitude;
            if (moved < 1e-6f && wantMove > 0.0004f)
            {
                stuckFrameCounter++;
                if (stuckFrameCounter >= stuckFramesToForgive)
                {
                    forgiveUntil = Time.unscaledTime + forgiveSeconds;
                    stuckFrameCounter = 0;
                }
            }
            else
            {
                stuckFrameCounter = 0;
            }

            if (Time.unscaledTime < forgiveUntil)
            {
                transform.position = smooth;
            }
        }

        lastAppliedPos = transform.position;

        // ===== 카메라 쉐이커 보정 =====
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

        // ===== 오프스크린 인디케이터 =====
        Transform self = swapsup ? target1 : target2;
        Transform other = swapsup ? target2 : target1;
        UpdateOffscreenIndicator(other, self);

        // ===== 전환음: 딱 한 번만 OneShot =====
        PlayChaseSwapSfxIfChanged();
    }

    // === BoxCast 기반 이동 제한(축 분리) ===
    private Vector3 ConfineMoveByBoxCast(Vector3 from, Vector3 to)
    {
        if (!cam) cam = Camera.main;

        Vector2 half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
        Vector2 boxSize = new Vector2(Mathf.Max(0.01f, (half.x * 2f) - boxShrink * 2f),
                                      Mathf.Max(0.01f, (half.y * 2f) - boxShrink * 2f));

        Vector3 pos = from;
        Vector3 delta = to - from;

        // 1) X축 이동
        float dx = delta.x;
        if (Mathf.Abs(dx) > 1e-5f)
        {
            Vector2 dir = dx > 0 ? Vector2.right : Vector2.left;
            float dist = Mathf.Abs(dx);

            if (TryBoxCastFiltered((Vector2)pos, boxSize, dir, dist, ConfinerMask, ignoreOneWay: false, out RaycastHit2D hitX))
            {
                float allow = Mathf.Max(0f, hitX.distance - confinerSkin);
                pos.x += Mathf.Sign(dx) * allow;
            }
            else
            {
                pos.x += dx;
            }
        }

        // 2) Y축 이동
        float dy = delta.y;
        if (Mathf.Abs(dy) > 1e-5f)
        {
            Vector2 dir = dy > 0 ? Vector2.up : Vector2.down;
            float dist = Mathf.Abs(dy);

            bool ignoreOneWay = dy > 0f; // 위로 갈 때는 OneWay는 무시(천장으로 취급하지 않음)
            if (TryBoxCastFiltered((Vector2)pos, boxSize, dir, dist, ConfinerMask, ignoreOneWay, out RaycastHit2D hitY))
            {
                float allow = Mathf.Max(0f, hitY.distance - confinerSkin);
                pos.y += Mathf.Sign(dy) * allow;
            }
            else
            {
                pos.y += dy;
            }
        }

        return pos;
    }

    // BoxCast 결과에서 OneWay/Trigger 무시(필요 시)
    private bool TryBoxCastFiltered(Vector2 origin, Vector2 size, Vector2 dir, float dist,
                                    LayerMask mask, bool ignoreOneWay, out RaycastHit2D hitOut)
    {
        if (!ignoreOneWay)
        {
            hitOut = Physics2D.BoxCast(origin, size, 0f, dir, dist, mask);
            return hitOut.collider != null;
        }

        var hits = Physics2D.BoxCastAll(origin, size, 0f, dir, dist, mask);
        float best = float.MaxValue;
        RaycastHit2D bestHit = new RaycastHit2D();
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            if (h.collider.isTrigger) continue;
            if (h.collider.CompareTag("OneWay")) continue; // 위로 이동 중엔 무시
            if (h.distance < best)
            {
                best = h.distance;
                bestHit = h;
            }
        }
        hitOut = best < float.MaxValue ? bestHit : new RaycastHit2D();
        return hitOut.collider != null;
    }

    private void ForceToP1()
    {
        // 카메라 대상 전환
        swapsup = true;

        // 부드러운 전환 시작(현재 전환 로직 재사용)
        isTransit = true;
        transitUntil = Time.unscaledTime + transitMaxDuration;
        originalFollowSpeed = Mathf.Approximately(originalFollowSpeed, 0f) ? followSpeed : originalFollowSpeed;
        followSpeed = Mathf.Max(followSpeed, transitBoostFollowSpeed);

        // SwapController도 함께 갱신(있을 때)
        if (swap != null)
            swap.charSelect = SwapController.PlayerChar.P1;

        // 전환 즉시 SFX 변화 반영(외부 호출 대비)
        PlayChaseSwapSfxIfChanged();
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

        Vector3 vp = cam.WorldToViewportPoint(otherTarget.position);

        bool inFront = vp.z > 0f;
        bool onScreen = inFront && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
        if (onScreen)
        {
            offscreenIndicator.gameObject.SetActive(false);
            if (warnIcon) warnIcon.gameObject.SetActive(false);
            return;
        }
        offscreenIndicator.gameObject.SetActive(true);

        Vector2 v2 = new Vector2(vp.x, vp.y);
        Vector2 center = new Vector2(0.5f, 0.5f);
        if (!inFront) v2 = center - (v2 - center);

        Vector2 dirFromCenter = (v2 - center).normalized;
        if (dirFromCenter.sqrMagnitude < 1e-6f) dirFromCenter = Vector2.right;

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

        float dxMin = Mathf.Abs(edgeVP.x - minX);
        float dxMax = Mathf.Abs(edgeVP.x - maxX);
        float dyMin = Mathf.Abs(edgeVP.y - minY);
        float dyMax = Mathf.Abs(edgeVP.y - maxY);
        float best = Mathf.Min(Mathf.Min(dxMin, dxMax), Mathf.Min(dyMin, dyMax));
        if (best == dxMin) edgeVP.x = minX;
        else if (best == dxMax) edgeVP.x = maxX;
        else if (best == dyMin) edgeVP.y = minY;
        else edgeVP.y = maxY;

        Vector2 dirFromEdgeToTarget = (v2 - edgeVP).normalized;
        if (dirFromEdgeToTarget.sqrMagnitude < 1e-6f) dirFromEdgeToTarget = dirFromCenter;
        float angle = Mathf.Atan2(dirFromEdgeToTarget.y, dirFromEdgeToTarget.x) * Mathf.Rad2Deg + arrowRotationOffsetDeg;

        Vector2 screenPos = new Vector2(edgeVP.x * Screen.width, edgeVP.y * Screen.height);
        Canvas canvas = canvasRect.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? (canvas.worldCamera ? canvas.worldCamera : cam) : null;

        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, uiCam, out local);
        offscreenIndicator.anchoredPosition = local;
        offscreenIndicator.rotation = Quaternion.Euler(0f, 0f, angle);

        if (otherTarget && selfTarget)
        {
            float dist = Vector2.Distance(selfTarget.position, otherTarget.position);
            float closeness = 1f - Mathf.InverseLerp(nearDistance, farDistance, dist);

            if (indicatorGraphic)
                indicatorGraphic.color = Color.Lerp(farColor, nearColor, closeness);

            float targetScale = Mathf.Lerp(farScale, nearScale, closeness);
            currentScale = Mathf.Lerp(currentScale, targetScale, Time.unscaledDeltaTime * scaleLerpSpeed);
            offscreenIndicator.localScale = indicatorBaseScale * currentScale;

            if (showDistance && distanceText)
                distanceText.text = Mathf.RoundToInt(dist).ToString();
        }

        if (warnIcon)
        {
            bool danger = IsDangerNear(otherTarget.position);
            warnIcon.anchoredPosition = offscreenIndicator.anchoredPosition + warnScreenOffset;

            if (danger)
            {
                if (!warnIcon.gameObject.activeSelf) warnIcon.gameObject.SetActive(true);
                if (warnGroup == null) warnGroup = warnIcon.GetComponent<CanvasGroup>();
                if (warnGroup == null) warnGroup = warnIcon.gameObject.AddComponent<CanvasGroup>();
                float tBlink = Mathf.PingPong(Time.unscaledTime * warnBlinkSpeed, 1f);
                warnGroup.alpha = Mathf.Lerp(warnAlphaMin, warnAlphaMax, tBlink);
            }
            else
            {
                if (warnGroup != null)
                {
                    warnGroup.alpha = Mathf.MoveTowards(warnGroup.alpha, 0f, Time.unscaledDeltaTime * warnFadeOutSpeed);
                    if (warnGroup.alpha <= 0.01f && warnIcon.gameObject.activeSelf)
                        warnIcon.gameObject.SetActive(false);
                }
            }
        }
    }

    private bool IsDangerNear(Vector2 center)
    {
        return Physics2D.OverlapCircle(center, hazardCheckRadius, hazardMask) != null;
    }

    // === 전환 시에만 OneShot을 1회 재생(루프 사용 금지) ===
    private void PlayChaseSwapSfxIfChanged()
    {
        bool nowIsP1 = swapsup; // true: P1, false: P2
        if (nowIsP1 != _lastIsP1Focus)
        {
            if (Time.unscaledTime - _lastSwapSfxTime >= minSwapSfxInterval)
            {
                if (useSoundManagerOneShot)
                {
                    // 프로젝트의 사운드 매니저에 맞춰 함수명을 조정하세요.
                    SoundManager.Play(nowIsP1 ? knightSwapSfxKey : princessSwapSfxKey, transform);
                }
                else
                {
                    var clip = nowIsP1 ? knightSwapClip : princessSwapClip;
                    if (clip != null)
                    {
                        if (_swapAudio == null) _swapAudio = gameObject.AddComponent<AudioSource>();
                        _swapAudio.PlayOneShot(clip);
                    }
                }
                _lastSwapSfxTime = Time.unscaledTime;
            }

            _lastIsP1Focus = nowIsP1;
        }
    }
}
