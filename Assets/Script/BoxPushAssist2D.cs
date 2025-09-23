using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class BoxPushAssist2D : MonoBehaviour
{
    [Header("Player Refs")]
    [SerializeField] private Rigidbody2D playerRb;      // 자동 할당
    [SerializeField] private Collider2D playerCol;      // 자동 할당(자식까지 탐색)
    [Tooltip("있으면 푸시 중 moveSpeed를 낮춥니다.")]
    [SerializeField] private PlayerMouseMovement playerMove; // 선택

    [Header("Layers (names)")]
    [SerializeField] private string boxLayerName = "Box";
    [SerializeField] private string groundLayerNames = "Ground, EventGround, OneWayGround";

    [Header("Push Feel")]
    [Tooltip("푸시 중 플레이어의 moveSpeed")]
    [SerializeField] private float pushPlayerMoveSpeed = 3.0f;
    [Tooltip("박스 동행 최대 속도(과속 안전캡)")]
    [SerializeField] private float maxPushSpeed = 3.5f;
    [Tooltip("붙기 시작 최소 플레이어 속도")]
    [SerializeField] private float engageMinPlayerSpeed = 0.15f;
    [Tooltip("접촉이 잠깐 끊겨도 유지해줄 유예(초)")]
    [SerializeField] private float contactKeepAlive = 0.06f;

    [Header("Cancel Conditions")]
    [SerializeField] private bool cancelWhenPlayerAirborne = true;
    [SerializeField] private bool cancelWhenBoxOffGround = true;
    [Tooltip("붙기 시작 시 접지 필수 여부(P2 접지 판정이 불안하면 끄세요)")]
    [SerializeField] private bool requireGroundedToEngage = true;

    [Header("Audio")]
    [SerializeField] private string pushLoopName = "StonePush0";

    // 현재 재생 중인 루프 이름 & 대상(중복 스타트/타겟 전환 관리)
    private string _activePushLoop = null;
    private Transform _activeLoopTarget = null;

    [Header("Debug")]
    [SerializeField] private bool debug = false;

    // ───────── Internals ─────────
    private int boxMask;
    private int groundMask;

    private Rigidbody2D currentBox;
    private Collider2D currentBoxCol;
    private int pushDir = 0; // -1(left) +1(right)
    private float lastContactTime = -999f;
    private bool isActive = false;

    // Δx 추적용
    private float prevPlayerX;
    private float cachedPlayerMoveSpeed = -1f; // <0이면 미사용

    void Awake()
    {
        AutoWire();
        ResolveMasks();
    }

    void Reset() { AutoWire(); ResolveMasks(); }

    void OnValidate()
    {
        AutoWire(); ResolveMasks();
        maxPushSpeed = Mathf.Max(0.1f, maxPushSpeed);
        pushPlayerMoveSpeed = Mathf.Max(0.1f, pushPlayerMoveSpeed);
        engageMinPlayerSpeed = Mathf.Max(0f, engageMinPlayerSpeed);
        contactKeepAlive = Mathf.Clamp(contactKeepAlive, 0f, 0.25f);
    }

    void AutoWire()
    {
        if (!playerRb) playerRb = GetComponent<Rigidbody2D>();
        if (!playerCol)
        {
            playerCol = GetComponent<Collider2D>();
            if (!playerCol) playerCol = GetComponentInChildren<Collider2D>(true); // 자식까지 탐색
        }
        if (!playerMove) playerMove = GetComponent<PlayerMouseMovement>(); // 있으면 자동
    }

    void ResolveMasks()
    {
        boxMask = NameToMask(boxLayerName);
        groundMask = NamesToMask(groundLayerNames);
    }

    static int NameToMask(string nameOrEmpty)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmpty)) return 0;
        int layer = LayerMask.NameToLayer(nameOrEmpty.Trim());
        return (layer < 0) ? 0 : (1 << layer);
    }

    static int NamesToMask(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return 0;
        int mask = 0;
        foreach (var p in csv.Split(','))
        {
            var n = p.Trim();
            if (n.Length == 0) continue;
            int layer = LayerMask.NameToLayer(n);
            if (layer >= 0) mask |= (1 << layer);
        }
        return mask;
    }

    // Rigidbody2D 기준으로 접지 체크(부착된 모든 콜라이더 포함)
    bool PlayerGrounded() => playerRb ? playerRb.IsTouchingLayers(groundMask) : true;
    bool BoxGrounded() => currentBox ? currentBox.IsTouchingLayers(groundMask) : false;

    void Engage(Rigidbody2D boxRb, Collider2D boxCol, int dirToBox)
    {
        currentBox = boxRb;
        currentBoxCol = boxCol;
        pushDir = dirToBox;
        isActive = true;

        // Δx 기준 설정
        prevPlayerX = playerRb ? playerRb.position.x : transform.position.x;

        // 플레이어 속도 낮추기(있을 때만)
        if (playerMove != null)
        {
            cachedPlayerMoveSpeed = playerMove.moveSpeed;
            playerMove.moveSpeed = pushPlayerMoveSpeed;
        }

        StartPushSfx();

        if (debug) Debug.Log($"[BoxPushAssist2D] engage -> {currentBox.name}, dir={pushDir}");
    }

    void Release()
    {
        if (debug && isActive) Debug.Log("[BoxPushAssist2D] release");

        StopPushSfx();

        isActive = false;
        currentBox = null;
        currentBoxCol = null;
        pushDir = 0;

        // 속도 원복
        if (playerMove != null && cachedPlayerMoveSpeed >= 0f)
        {
            playerMove.moveSpeed = cachedPlayerMoveSpeed;
            cachedPlayerMoveSpeed = -1f;
        }
    }

    void FixedUpdate()
    {
        if (!isActive) return;

        // 접촉 유예 종료 → 해제
        if (Time.time - lastContactTime > contactKeepAlive) { Release(); return; }

        // 반대 이동/정지/공중/박스미접지 → 해제
        float pvx = playerRb ? playerRb.linearVelocity.x : 0f;
        if (Mathf.Abs(pvx) < 0.0001f || Mathf.Sign(pvx) != pushDir) { Release(); return; }
        if (cancelWhenPlayerAirborne && !PlayerGrounded()) { Release(); return; }
        if (cancelWhenBoxOffGround && !BoxGrounded()) { Release(); return; }

        if (!currentBox) { Release(); return; }

        // 플레이어 Δx만큼 박스 MovePosition (가로만)
        float playerX = playerRb ? playerRb.position.x : transform.position.x;
        float dx = playerX - prevPlayerX;          // 이번 Fixed 스텝에서 플레이어가 움직인 양
        prevPlayerX = playerX;

        // 같은 방향일 때만 적용
        if (Mathf.Abs(dx) > 0.00001f && Mathf.Sign(dx) == pushDir)
        {
            float maxDx = maxPushSpeed * Time.fixedDeltaTime; // 안전캡
            float move = Mathf.Clamp(dx, -maxDx, maxDx);
            Vector2 next = currentBox.position + new Vector2(move, 0f);
            currentBox.MovePosition(next);
        }
    }

    void OnCollisionStay2D(Collision2D c)
    {
        // Box 레이어만 관심
        if ((boxMask & (1 << c.collider.gameObject.layer)) == 0) return;

        // 활성 중에는 '현재 밀고 있는 박스'와의 접촉일 때만 keepAlive 연장
        if (!isActive || c.collider == currentBoxCol)
            lastContactTime = Time.time;

        // ① 항상: 접촉 즉시 박스 가로 속도 0(관성 제거)
        if (c.rigidbody != null)
        {
            var v = c.rigidbody.linearVelocity;
            if (Mathf.Abs(v.x) > 0.0001f) { v.x = 0f; c.rigidbody.linearVelocity = v; }
        }

        // ② 아직 비활성 → 조건 맞으면 Engage
        if (!isActive)
        {
            if (!playerRb) return;

            float pvx = playerRb.linearVelocity.x;
            if (requireGroundedToEngage && !PlayerGrounded()) return; // 접지 필수 옵션
            if (Mathf.Abs(pvx) < engageMinPlayerSpeed) return;

            // 박스가 플레이어의 어느 쪽에 있는지 → 전진 중인지 확인
            float dx = c.transform.position.x - transform.position.x;
            int dirToBox = dx >= 0f ? +1 : -1;
            if (Mathf.Sign(pvx) != dirToBox) return;

            if (c.rigidbody == null) return; // Rigidbody2D 없는 대상 제외

            Engage(c.rigidbody, c.collider, dirToBox);
        }
    }

    void OnCollisionExit2D(Collision2D c)
    {
        if (!isActive) return;
        if (currentBoxCol == c.collider)
        {
            // 접촉 유예 타이머로 유지, 즉시 끊지는 않음
        }
    }

    private void StartPushSfx()
    {
        if (!currentBox) return;

        // 같은 박스에서 이미 재생 중이면 무시
        if (_activePushLoop == pushLoopName && _activeLoopTarget == currentBox.transform) return;

        // 이전 루프가 남아 있으면 정리(다른 박스에서 넘어온 경우 등)
        StopPushSfx();

        // 박스 Transform 기준으로 루프 시작(플레이어가 아니라 박스에서 재생)
        SoundManager.StartLoop(pushLoopName, currentBox.transform);
        _activePushLoop = pushLoopName;
        _activeLoopTarget = currentBox.transform;

        if (debug) Debug.Log($"[BoxPushAssist2D] SFX start on {currentBox.name}");
    }

    private void StopPushSfx()
    {
        if (string.IsNullOrEmpty(_activePushLoop)) return;

        SoundManager.StopLoop(_activePushLoop, graceful: true);
        _activePushLoop = null;
        _activeLoopTarget = null;

        if (debug) Debug.Log("[BoxPushAssist2D] SFX stop");
    }

    void OnDisable() { StopPushSfx(); }
    void OnDestroy() { StopPushSfx(); }

    void OnDrawGizmosSelected()
    {
        if (!debug) return;
        if (isActive && currentBox)
        {
            Gizmos.color = Color.cyan;
            Bounds b;
            if (currentBoxCol != null) b = currentBoxCol.bounds;
            else
            {
                var anyCol = currentBox.GetComponent<Collider2D>();
                if (!anyCol) { Gizmos.DrawWireSphere(currentBox.worldCenterOfMass, 0.25f); return; }
                b = anyCol.bounds;
            }
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
