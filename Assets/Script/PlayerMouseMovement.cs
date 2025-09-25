using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMouseMovement : MonoBehaviour
{
    public Rigidbody2D rb;
    public Animator rb2;
    public SmartCameraFollowByWall cameran;
    [Header("Layers")]
    [Header("Layer Names (auto-resolve)")]
    [SerializeField] private string groundLayerName = "Ground";
    [SerializeField] private string eventLayerName = "EventGround, OneWayGround";
    [SerializeField] private string trapLayerName = "Trap";
    [SerializeField] private string slimeLayerName = "Slime";
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string monsterLayerName = "Monster";

    [Header("Carry Drop/Throw Spawn")]
    [SerializeField] private float carryDropForward = 0.4f;   // 내려놓기 시, 바라보는 방향 앞으로 얼마나 둘지

    [Header("Auto Carry (Midair Catch)")]
    [SerializeField] private bool autoCatchEnabled = true;          // 오토 캐치 On/Off
    [SerializeField] private float autoCatchMinHeightAbove = 0.05f; // P2 발이 머리보다 최소 이만큼 높아야(두 번째 조건용)
    [SerializeField] private bool onlyCatchDuringThrowWindow = false; // true면 '던진 뒤(탄도창)'에만 캐치
    [SerializeField] private float autoCatchCooldown = 0.15f;       // 반복 캐치 튕김 방지
    [SerializeField] private float autoCatchBlockOnThrow = 0.2f; // 던진 직후 차단 시간
    private float autoCatchSuppressUntil = -1f;                  // 이 시각 전엔 오토캐치 금지
    private float nextAutoCatchAllowedAt = 0f;
    [Header("Auto Catch – Whole Body")]
    [SerializeField] private bool autoCatchUseWholeBody = true;           // 몸 전체 판정 사용
    [SerializeField] private Vector2 bodyCatchPadding = new(0.06f, 0.06f); // P1 바디 박스 확장량(여유)

    [Header("Camera Auto-Focus (to P1)")]
    [SerializeField] private bool autoForceViewToP1OnCatch = true;        // 캐치 시 카메라/시점 P1 강제
    [SerializeField] private UnityEngine.Events.UnityEvent onForceViewToP1; // (선택) 카메라 스크립트 훅
    [Header("Auto Carry Gate")]
    [SerializeField] private bool autoCatchRequireP2Descending = true;         // P2가 내려오는 중일 때만
    [SerializeField] private float autoCatchMaxHoriz = 0.60f;                  // 가로 허용 오차
    [SerializeField] private bool autoCatchDisallowIfBlockingCeiling = true;   // 사이에 지형/트랩 있으면 금지
    [SerializeField] private LayerMask autoCatchObstructionMask;               // Ground|Event|Trap 등

    [SerializeField] private bool autoCatchDisallowIfP1Busy = true;            // P1이 바쁠 땐 금지(공격/락 등)
    [SerializeField] private bool autoCatchDisallowIfP2Hidden = true;          // P2가 숨김상태면 금지

    [SerializeField] private string carryingBoolName = "carrying";             // 오토캐치 ON/OFF
    [SerializeField] private string carryEndTriggerName = "carryEnd";          // 캐리 해제 연출용 트리거(있으면 사용)
    [SerializeField] private string carryEndStateName = "CarryEnd";            // 없으면 이 상태로 크로스페이드
    
    [SerializeField] private int autoCatchCarryStartFrame = 6;

    [SerializeField] private string carryStateName = "Carry";

    [SerializeField] private AnimationClip carryClipRef;

    [SerializeField] private float reviveIFrame = 1.2f; // 부활 후 무적 시간
    private float _invincibleUntil = -1f;               // 무적 타이머
    public bool IsInvincible => Time.time < _invincibleUntil;

    private Vector3 _lastSafePos;
    public void SetCheckpoint(Vector3 pos) { _lastSafePos = pos; }
    public void SetCheckpoint(Transform t) { if (t) { _lastSafePos = t.position; } }


    [Header("Throw Start (Inspector Control)")]
    [Tooltip("던지기 시작 지연(초). 이 시간이 지난 뒤 보이면서 실제로 날아가기 시작합니다.")]
    [SerializeField] private float throwDelay = 0.25f;
    [Tooltip("월드 좌표로 지정할 수 있는 시작 위치. 설정되면 오프셋 대신 이 위치를 사용합니다.")]
    [SerializeField] private Transform throwStartWorldPoint;
    [Tooltip("P1의 현재 위치 기준 로컬 오프셋 (x는 좌/우 방향에 따라 자동으로 부호가 붙습니다).")]
    [SerializeField] private Vector2 throwStartLocalOffset = new(0.35f, 0.6f);
    [Tooltip("로컬 오프셋 X를 P1의 바라보는 방향 기준으로 좌/우 반전할지 여부")]
    [SerializeField] private bool throwStartUseFacing = true;
    [Header("Throw Combo Grace")]
    [SerializeField] private float throwComboGrace = 0.09f; // 60~120ms 권장
    private float throwComboPendingUntil = -1f;
    private bool throwComboPending = false;
    [Header("Throw Preview Colors")]
    [SerializeField] private Color previewSafeColor = new Color(1f, 0.2f, 0.2f, 0.95f); // 안전(빨강)
    [SerializeField] private Color previewHazardColor = new Color(0.2f, 0.6f, 1f, 0.95f); // 위험(파랑)
    [SerializeField] private string hazardLayerNames = "Trap, Monster, Monkill, MonAttack";
    private int hazardMask;

    private int groundMask, eventMask, trapMask, slimeMask;
    private LayerMask slimeLayerMask; // ContactFilter2D 용
    private int trapLayerIndex;
    public Player1HP dead;
    [SerializeField] private bool throwmanager = true;

    [SerializeField] private float slimeStickPush = 22f;
    [SerializeField] private float slimeNormalClamp = 20f;
    [SerializeField] private float carrySlideMaxFall = -11f;
    private int holdHorizSign = 0;                 // -1=L, +1=R, 0=none
    private float holdHorizChangedAt = -999f;
    private float holdUpChangedAt = -999f;

    [Header("Audio – Footsteps")]
    [SerializeField] private bool enableFootstepLoop = true;
    [SerializeField] private float footstepMinSpeed = 0.1f; // |vx|가 이값 이상일 때만 발소리
    private string _currentWalkLoop = null;                  // 현재 재생 중인 루프 이름(KnightWalk/PrincessWalk)
    private bool jumpchk;
    [Header("Audio – Landing")]
    [SerializeField] private float landSfxMinAirTime = 0.05f; // 너무 짧은 공중 구간(잡음) 무시
    private bool _leftGroundSinceLast = false;                // 지난번 이후 '땅을 떠난 적'이 있는가
    private float _leftGroundAt = -999f;                      // 마지막으로 땅을 떠난 시각

    [Header("Throw Preview (Sim)")]
    [SerializeField] private LineRenderer throwPreview;
    [SerializeField] private float throwPreviewDuration = 2.0f;
    [SerializeField] private int throwPreviewSteps = 60;

    [Header("Ceiling Slime (Head Stick)")]
    [SerializeField] private bool enableCeilingSlime = true;
    [SerializeField] private float ceilingStickMaxTime = 5f;      // 5초 유지
    [SerializeField] private float ceilingReleaseFade = 0.6f;     // 서서히 떨어지는 시간
    [SerializeField] private float ceilingKeepGap = 0.02f;        // 머리-천장 간격 유지
    [SerializeField] private float ceilingRestickBlock = 0.25f;   // 떨어진 직후 재부착 금지 시간

    private bool stickingToCeiling = false;
    private float ceilingStickStartTime = -1f;
    private float ceilingReleaseUntil = -1f;   // >0 이면 release 페이드 중
    private float ignoreCeilingUntil = -1f;    // 이 시각 전엔 머리로 다시 안 붙음
    private float lastCeilingY = 0f;           // 붙었던 천장 Y 캐시(유지용)

    [Header("P2 Laser Tether")]
    [SerializeField] private string boxLayerName = "Box";   // 연결 대상 레이어(쉼표로 여러 개 가능)
    [SerializeField] private float p2LaserMaxDist = 10f;    // 조준/히트 최대 거리
    [SerializeField] private float p2LaserBreakDist = 11.5f;// 이 거리 넘으면 자동 해제
    [SerializeField] private float p2PullForce = 20f;       // AddForce 끌어오기 힘
    [SerializeField] private float p2PullSpeed = 4.0f;      // 끌려오는 속도 상한
    [SerializeField] private float p2StopPullContactGap = 0.005f; // P2와 접촉 시 끌기 정지
    [SerializeField] private float p2LaserCooldown = 0.05f; // 토글 스팸 방지
    [SerializeField] private float p2LaserBoxCastHalfHeight = 0.35f; // 히트 편의 위해 얇은 BoxCast 높이
    [SerializeField] private LineRenderer p2LaserLine;      // (선택) 시각화 라인
    [SerializeField] private Transform p2LaserMuzzle;       // (선택) 레이저 시작 위치
                                                            // === P2 Wire Anchor ===
    [Header("P2 Wire Anchor")]
    [SerializeField] private string anchorLayerName = "WireAnchor"; // 선택: 레이어로도 구분하고 싶다면 사용
    private int anchorMask;
    private bool p2AnchorMode = false;       // true면 와이어 모드
    private Collider2D p2AnchorCol;          // 앵커 대상
    [SerializeField] private bool p2AnchorUseBoxBreakDist = true; // 박스 끊김 길이 재사용
    [SerializeField] private float p2AnchorMaxDist = 11.5f;       // 기본값(재사용 안할 때)

    [Header("P2 Laser Visual")]
    [SerializeField] private Sprite impactSprite;              // 끝점 반짝이(작은 원형 스프라이트 추천)
    [SerializeField] private float beamBaseWidth = 0.08f;      // 기본 굵기
    [SerializeField] private float beamPulseAmp = 0.12f;       // 굵기 펄스 진폭(비율)
    [SerializeField] private float beamPulseHz = 12f;          // 펄스 속도
    [SerializeField] private int beamSortingOrderOffset = 2;   // 플레이어보다 얼마나 위에 그릴지

    [Header("P2 Laser Stop-On-Release")]
    [SerializeField] private PhysicsMaterial2D boxHighFrictionMat; // friction=1, bounciness=0 권장(없으면 런타임 생성)
    [SerializeField] private float onReleaseExtraDrag = 10f;        // 해제 직후 드래그 임시 상승
    [SerializeField] private float onReleaseExtraAngDrag = 2f;
    [SerializeField] private float onReleaseDragDuration = 0.35f;   // 임시 드래그 유지 시간
    [SerializeField] private bool restoreMatAfter = true;           // 일정 시간 뒤 원복
    [SerializeField] private float restoreMatAfterSec = 0.6f;

    [Header("P2 Laser Auto-Connect")]
    [SerializeField] private float p2LaserAutoRadius = 10f;      // 자동 탐색 반경
    [SerializeField] private float p2LoSBoxWidth = 0.18f;        // 시야 박스캐스트 두께
    [SerializeField] private LayerMask p2ObstructionMask;        // 기본값: Ground|Event|Trap
    private readonly Collider2D[] p2BoxScanBuf = new Collider2D[32]; // NonAlloc 버퍼


    private List<(Collider2D col, PhysicsMaterial2D orig)> p2LastCols = new();
    private float p2PrevDrag = -1f, p2PrevAngDrag = -1f;
    private Coroutine p2RestoreCo;
    

    private Transform p2ImpactFx;    // 끝점 글로우
    private SpriteRenderer p2ImpactSr;
    private float p2BeamPulseTimer = 0f;


    private int boxMask;
    private bool p2LaserActive = false;
    private Collider2D p2TetherCol;

    [SerializeField] private float wallDetachGrace = 0.13f;
    [NonSerialized] public float ignoreSlimeUntil = -1f;
    public bool IsSlimeSuppressed => Time.time < ignoreSlimeUntil;

    [Header("F Pulse Object")]
    [SerializeField] private GameObject selectedObject;
    [SerializeField] private float fPulseDuration = 0.3f;
    private float fPulseOffAt = -1f;

    [Header("플레이어 ID 설정")]
    public SwapController.PlayerChar playerID;
    public SwapController swap;

    [Header("Carry Animation/Lock")]
    [SerializeField] private float carryLockDuration = 0.6f; // 기존 고정 잠금(백업용)
    [SerializeField] private string carryBoolName = "carry"; // Animator Bool 파라미터명

    [Header("Carry Start Rules")]
    [SerializeField] private bool requireGroundedForCarryStart = true;       // P1 접지 필수
    [SerializeField] private bool requireOtherGroundedForCarryStart = false; // P2 접지까지 필수

    [Header("Carry Timing (Anim-driven)")]
    [SerializeField] private bool useAnimDrivenCarry = true;
    [SerializeField] private float carryStartMinLock = 0.08f;
    [SerializeField] private float carryEndMinLock = 0.06f;
    [SerializeField] private float revealDelayOnDrop = 0.6f; // EXACT 0.6s
    private Coroutine _carryLockCo;
    private Coroutine _revealCo;       // P2 복귀 코루틴
    private Coroutine _throwResetCo;   // throw 종료 타이머
    private Coroutine _delayedThrowCo; // 던지기 지연 코루틴
                                       // === Throw Hold (Press & Hold to Aim) ===
    [Header("Throw Hold (Press & Hold to Aim)")]
    [SerializeField] private bool enableThrowHold = true;
    [SerializeField] private KeyCode throwHoldKeyMain = KeyCode.LeftShift;
    [SerializeField] private KeyCode throwHoldKeyAlt = KeyCode.RightShift;
    [SerializeField] private string throwStateName = "Throw";  // 1프레임 정지할 애니메이션 상태명 (없으면 Bool만 사용)
    [SerializeField] private bool throwHoldFreezeAnimator = true;
    [SerializeField] private float throwHoldMaxTime = 3.0f;   // (선택) 너무 오래 홀드하면 자동 던지기
    private bool throwHoldActive = false;
    private float throwHoldStartedAt = -1f;
    private float _prevAnimSpeed = 1f;

    [Header("Ray distances")]
    public float groundrayDistance = 1.3f;
    public float breakrayDistance = 1.4f;
    public float checkceilingtrap = 0.7f;

    [Header("Carry Cooldown")]
    [SerializeField] private float carryCooldown = 1.4f; // 해제 후 추가 쿨타임
    private float nextCarryAllowedAt = 1f;               // 다음 캐리 허용 시각

    [Header("Keyboard Movement (IWB-style)")]
    [SerializeField] public float moveSpeed = 9.5f;
    [SerializeField] private float accel = 180f;
    [SerializeField] private float decel = 220f;
    [SerializeField] private float airAccel = 130f;
    [SerializeField] private float airDecel = 150f;
    [SerializeField] private float jumpVelocity = 11.8f;
    [SerializeField] private float gravityScaleNormal = 3.2f;
    [SerializeField] public float gravityScaleFall = 5.0f;
    [SerializeField] private float cutJumpFactor = 0.45f;
    [SerializeField] private float maxFallSpeed = -28f;
    [SerializeField] private float coyoteTime = 0.06f;
    [SerializeField] private float jumpBuffer = 0.08f;

    [Header("Slime Friction Control")]
    [SerializeField] private PhysicsMaterial2D slimeNoFrictionMat;
    private PhysicsMaterial2D _originalMat;
    private bool _appliedNoFriction;

    [Header("Ceiling Slime (Head Stick)")]
    [SerializeField] private float headCheckDist = 0.12f;              // 머리 위 슬라임 감지 거리
    [SerializeField] private float ceilingStickDuration = 5f;           // 붙어있는 시간
    [SerializeField] private float ceilingReleaseBlendTime = 0.8f;      // 떨어질 때 부드럽게 전환
    [SerializeField] private float ceilingReleaseSlideMaxFall = -2.5f;  // 해제 직후 잠깐 천천히 낙하
    [SerializeField] private float ceilingAttachSkin = 0.01f;           // 천장에 스냅 붙일 때 여유

    private float ceilingStickUntil = -1f;

    [SerializeField] private bool allowAimUpInHold = true;            // 홀드 상태에서도 ↑ 던지기 허용
    [SerializeField] private Vector2 throwStartLocalOffsetUp = new(0f, 0.75f); // ↑ 던지기 스폰 오프셋(머리 위 중앙 추천)
    [SerializeField] private bool upThrowKeepsFacing = true;          // ↑ 던질 땐 회전 유지(=P2 방향 안 바꿈)


    [Header("Ground Snap")]
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private float snapProbe = 0.20f;
    [SerializeField] private float snapSkin = 0.02f;

    [Header("Jump Feel Tuning")]
    [SerializeField] private float minJumpHoldTime = 0.06f;
    [SerializeField] private float apexThreshold = 0.8f;
    [SerializeField] private float apexHangMultiplier = 0.7f;
    [SerializeField] private float gravitySmoothTime = 0.06f;

    [Header("Carry Throw (Ballistic)")]
    [SerializeField] private float carryThrowUpSpeed = 12f;
    [SerializeField] private float carryThrowSideSpeed = 8f;
    [SerializeField] private float carryThrowSeparation = 0.18f;
    [SerializeField] private float carryThrowBallisticMinTime = 1f;
    [SerializeField] private bool carryThrowHoldUntilGrounded = false;

    [Header("Carry Safety Check")]
    [SerializeField] private float releaseAheadCheckDist = 0.45f; // 앞쪽 벽 체크 거리
    [SerializeField] private float releaseGroundProbeDown = 2.0f; // 낭떠러지(아래) 체크 거리
    [SerializeField] private Vector2 releaseSpawnBoxSize = new(0.40f, 0.90f); // P2가 설 자리 박스 크기

    [Header("Slime Stick Tuning")]
    [SerializeField] private float slimeInwardHoldSpeed = 0.8f;
    [SerializeField] private float slimeInwardAccel = 35f;
    [SerializeField] private float wallSlideMaxFallCarrying = -12f;

    private bool ballisticThrowActive = false;
    private float ballisticThrowEndTime = -1f;
    private float lastJumpStartTime = -999f;
    private float swapSuppressUntil = -999f;
    private bool didCutThisJump = false;
    private float gravitySmoothVel = 0f;
    private int playerLayerIndexSelf;
    private int monsterLayerIndex; // 추가

    [Header("Extra Jumps")]
    [SerializeField] public int extraAirJumps = 1;
    private int airJumpsLeft = 0;

    [Header("Bounce Panels")]
    [SerializeField] private float bounceImpulseX = 12f;
    [SerializeField] private float bounceImpulseY = 15f;
    [SerializeField] private float inputLockAfterImpulse = 0.12f;
    [SerializeField] private float bounceProtectDuration = 0.06f;
    public float GravityScaleNormal => gravityScaleNormal;
    public float GravityScaleFall => gravityScaleFall;   // (있으면 편하니 같이)
    [Header("Slime Wall")]
    [SerializeField] private LayerMask slimeLayer;
    [SerializeField] private float wallCheckDist = 0.18f;
    [SerializeField] private float wallSlideMaxFall = -5.5f;
    [SerializeField] private float wallJumpHorizontal = 9.0f;
    [SerializeField] private float wallJumpVertical = 11.5f;
    [SerializeField] private bool requireSpaceForWallJump = false;
    [SerializeField] private bool resetAirJumpsOnWallJump = true;
    [Header("Wall Jump Input Lock")]
    [SerializeField] private float wallOppositeInputLock = 0.5f; // 0.5초 잠금
    private float oppositeInputLockUntil = -1f;
    private int oppositeInputLockedDir = 0; // -1 = Left(A/←)을 막음, +1 = Right(D/→)을 막음

    [Header("Slime Stick Grace")]
    [SerializeField] private float slimeStickAfterLeave = 0.3f;   // 떨어진 뒤 유지 시간
    private float lastSlimeTouchAt = -999f;                      // 마지막 접촉 시각
    private int lastSlimeSide = 0;  // -1 = 왼쪽 벽(법선 +X), +1 = 오른쪽 벽(법선 -X)

    [Header("Dive (Down Slam)")]
    [SerializeField] private float diveSpeed = -36f;
    [SerializeField] private float diveGravityScale = 7.5f;
    private bool isDiving = false;
    private bool throwAimUpLatched = false;
    [Header("Carry (P1 carries P2)")]
    public PlayerMouseMovement otherPlayer;
    public float carryOffsetY = 0.5f;
    public float carryPickupMaxGap = 0.15f;
    public bool carryset = false;   // 호환 유지
    public bool isCarrying = false;
    public bool isCarried = false;
    private Transform otherOriginalParent;

    [Header("Carry Gravity")]
    [SerializeField] private float carryGravityMul = 1.15f;
    [SerializeField] private float carryFallGravityMul = 1.25f;
    private float baseGravityNormal;
    private float baseGravityFall;

    [Tooltip("월점프 직후 같은 벽으로 재부착 금지 시간")]
    [SerializeField] private float wallRegrabBlock = 0.30f;
    private float wallRegrabUntil = -1f; // 이 시각 전까지 재부착 금지
                                         // -1 = 왼쪽벽(법선 +X), +1 = 오른쪽벽(법선 -X), 0 = 없음
    private int wallRegrabSide = 0;

    [Header("Health Setup")]
    [SerializeField] private int p1MaxHP = 2;
    [SerializeField] private int p2MaxHP = 1;

    private bool _sceneReloading = false; // 리로드 중복 방지

    [Header("Ground Check Fix")]
    [SerializeField] private float groundCheckSkin = 0.04f;
    [SerializeField] private float postJumpGroundIgnore = 0.06f;
    private float ignoreGroundUntil = -1f;

    [Header("Bounce Speed Tuning")]
    [SerializeField] private bool smoothBounce = true;
    [SerializeField] private float bounceTargetSpeed = 14f;
    [SerializeField] private float bounceRampTime = 0.12f;
    [SerializeField] private float bounceMaxSpeed = 18f;

    [Header("애니메이션")]
    private float janit = 0f;

    [Header("Attack")]
    [SerializeField] private float attackCooldown = 1.0f; // 쿨타임 1초
    [SerializeField] private float attackDuration = 0.5f; // 공격 애니 길이(자동 종료)
    [SerializeField] private float attackHitDelay = 0.2f;
    private float nextAttackTime = 0f; // 다음 사용 가능 시각
    private float attackEndTime = -1f; // 공격 종료 시각
    private bool attack = false;       // 공격 중 여부 (애니 bool과 동기화)
    private Coroutine _attackPulseCo; // 지연 코루틴 핸들
    public int maxHP = 5;
    public int currentHP;
    public bool IsDead { get; private set; } = false;

    [Header("Death Fall")]
    [SerializeField] private float deadHorizontalDamp = 6f;   // 사망 후 가로 감쇠 속도(0이면 감쇠 안 함)
    [SerializeField] private bool keepFallingOnDeath = true;  // 사망해도 계속 낙하

    [Header("Step Up (auto climb small ledges)")]
    [SerializeField] private bool enableStepUp = true;   // 자동 스텝업 On/Off
    [SerializeField] private float stepUpMax = 0.18f;    // 최대 올라탈 높이(0.1~0.2 추천)
    [SerializeField] private float stepForward = 0.10f;  // 발 앞쪽 탐색 거리
    [SerializeField] private float stepUpSkin = 0.01f;   // 살짝 더 올려 겹침 방지
    [SerializeField] private float stepOnlyWhenFallingVy = 0.05f; // 위로 점프중엔 스킵
                                                                  // --- 추가 파라미터 ---
    [SerializeField] private float seamFixProbe = 0.03f;  // 발 앞 세로면 탐색 폭
    [SerializeField] private float seamFixLift = 0.03f;  // 살짝 들어올릴 높이

    [Header("Step Up (Box gate)")]
    [SerializeField] private bool enableBoxGateForStepUp = true;
    [SerializeField] private float boxGateLowerY = 0.05f;   // 발밑 기준 낮은 레이 높이
    [SerializeField] private float boxGateUpperY = 0.28f;   // 높은 레이 높이(여기서 맞으면 '높은 박스')
    [SerializeField] private float boxGateDepth = 0.12f;   // 전방 탐색 깊이(=가로 길이)
    [SerializeField] private float boxGateThickness = 0.02f;// 레이 두께(얇은 띠)

    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private float rawX = 0f;
    private bool jumpHeld = false;
    private float inputLockUntil = -999f;

    private bool isBouncing = false;
    private float bounceProtectUntil = -999f;
    private bool inBounceFlight = false;
    private float bounceRampTimer = 0f;
    private Vector2 bounceTargetVel;
    private float bounceVxRef = 0f, bounceVyRef = 0f;
    private bool shouldPlayJump;

    private bool lefthold;
    private bool righthold;
    private bool prevSelected = false;
    private bool lockedall;

    private bool touchingLeftSlime, touchingRightSlime;
    private bool touchL_byCollision, touchR_byCollision;
    private bool touchL_byTrigger, touchR_byTrigger;

    private bool wasGrounded = false;

    public float dir = 1f;
    public bool dirseto = true;
    public bool chasize = true;
    public float dirsetofl = 1f;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!bodyCollider)
        {
            bodyCollider = GetComponent<Collider2D>();
            if (!bodyCollider) bodyCollider = GetComponentInChildren<Collider2D>();
        }
        if (!rb2) rb2 = GetComponent<Animator>();

        _originalMat = bodyCollider ? bodyCollider.sharedMaterial : null;

        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        maxHP = playerID == SwapController.PlayerChar.P1 ? p1MaxHP : p2MaxHP;
        currentHP = maxHP;

        Physics2D.queriesHitTriggers = true;

        baseGravityNormal = gravityScaleNormal;
        baseGravityFall = gravityScaleFall;
        _lastSafePos = transform.position;

        TryResolveSwap();
        ResolveLayerMasks();
        ApplyLayerIgnores();
        EnsureP2LaserLine();
        EnsureP2ImpactFx();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopFootstepLoop(); // 씬/오브젝트 비활성화 시 루프 정리
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) TryResolveSwap();
    }
#endif

    private int GetMaskFromCsv(string namesCsv)
    {
        if (string.IsNullOrWhiteSpace(namesCsv)) return 0;
        string[] parts = namesCsv.Split(',');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return LayerMask.GetMask(parts);
    }

    private void ResolveLayerMasks()
    {
        groundMask = GetMaskFromCsv(groundLayerName);
        eventMask = GetMaskFromCsv(eventLayerName);
        trapMask = GetMaskFromCsv(trapLayerName);
        slimeMask = GetMaskFromCsv(slimeLayerName);
        boxMask = GetMaskFromCsv(boxLayerName);
        anchorMask = GetMaskFromCsv(anchorLayerName);



        slimeLayerMask = slimeMask;
        trapLayerIndex = LayerMask.NameToLayer(trapLayerName.Trim());
        playerLayerIndexSelf = LayerMask.NameToLayer(playerLayerName.Trim());
        monsterLayerIndex = LayerMask.NameToLayer(monsterLayerName.Trim());

        if (groundMask == 0) Debug.LogWarning($"[Player] Ground layer(s) '{groundLayerName}' not found.");
        if (eventMask == 0) Debug.LogWarning($"[Player] Event layer(s) '{eventLayerName}' not found.");
        if (trapMask == 0) Debug.LogWarning($"[Player] Trap layer(s) '{trapLayerName}' not found.");
        if (slimeMask == 0) Debug.LogWarning($"[Player] Slime layer(s) '{slimeLayerName}' not found.");
        if (trapLayerIndex < 0) Debug.LogWarning($"[Player] Trap layer index for '{trapLayerName}' not found.");
        if (playerLayerIndexSelf < 0) Debug.LogWarning($"[Player] Player layer '{playerLayerName}' not found.");
        if (monsterLayerIndex < 0) Debug.LogWarning($"[Player] Monster layer '{monsterLayerName}' not found.");
        if (boxMask == 0) Debug.LogWarning($"[Player] Box layer(s) '{boxLayerName}' not found.");
        if (anchorMask == 0) { /* 레이어 안 쓸거면 경고 생략 가능 */ }

        if (p2ObstructionMask.value == 0)
        {
            // Ground|Event|Trap을 가림막으로 사용
            int mask = groundMask | eventMask | trapMask;
            p2ObstructionMask = mask;
        }
        hazardMask = GetMaskFromCsv(hazardLayerNames);
        if (hazardMask == 0)
        {
            hazardMask = trapMask;
            if (monsterLayerIndex >= 0) hazardMask |= (1 << monsterLayerIndex);
            int monKill = LayerMask.NameToLayer("Monkill");
            if (monKill >= 0) hazardMask |= (1 << monKill);
            int monAtk = LayerMask.NameToLayer("MonAttack");
            if (monAtk >= 0) hazardMask |= (1 << monAtk);
        }
    }

    //공격 중(P1) 입력락 여부
    private bool AttackLocksInput()
    {
        return attack && playerID == SwapController.PlayerChar.P1;
    }

    void Update()
    {
        void HideThrowPreview()
        {
            var tf = transform.Find("ThrowPreview");
            if (tf != null)
            {
                var lr = tf.GetComponent<LineRenderer>();
                if (lr) { lr.enabled = false; lr.positionCount = 0; }
            }
        }

        LineRenderer EnsureThrowPreview()
        {
            var tf = transform.Find("ThrowPreview");
            LineRenderer lr = null;

            if (tf == null)
            {
                var go = new GameObject("ThrowPreview");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();

                // 머티리얼/셰이더
                var shader = Shader.Find("Sprites/Default");
                lr.material = new Material(shader);

                lr.useWorldSpace = true;
                lr.textureMode = LineTextureMode.Stretch;
                lr.alignment = LineAlignment.View;
                lr.numCapVertices = 6;
                lr.numCornerVertices = 3;
                lr.widthMultiplier = 0.045f;

                // 정렬: 플레이어보다 위
                var sr = GetComponentInChildren<SpriteRenderer>();
                if (sr)
                {
                    lr.sortingLayerID = sr.sortingLayerID;
                    lr.sortingOrder = sr.sortingOrder + beamSortingOrderOffset + 1;
                }

                lr.startColor = new Color(1f, 0f, 0f, 0.95f);
                lr.endColor = new Color(1f, 0f, 0f, 0.95f);
            }
            else
            {
                lr = tf.GetComponent<LineRenderer>();
                if (!lr)
                {
                    lr = tf.gameObject.AddComponent<LineRenderer>();
                    var shader = Shader.Find("Sprites/Default");
                    lr.material = new Material(shader);
                }
            }
            return lr;
        }

        // 유틸: 포물선 그리기(홀드 중 매 프레임 호출)
        void DrawThrowPreview()
        {
            if (!isCarrying || otherPlayer == null || otherPlayer.rb == null) { HideThrowPreview(); return; }

            // 던지기 시작 위치 계산(DoThrowNowFromHold와 동일 로직)
            int facingSign = (transform.localScale.x >= 0f) ? +1 : -1;
            bool aimUp = allowAimUpInHold && (throwAimUpLatched || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));


            Vector3 startPos;
            if (throwStartWorldPoint != null)
            {
                startPos = throwStartWorldPoint.position;
            }
            else
            {
                if (aimUp)
                {
                    startPos = transform.position + new Vector3(0f, throwStartLocalOffsetUp.y, 0f);
                }
                else
                {
                    float xoff = throwStartLocalOffset.x * (throwStartUseFacing ? facingSign : 1f);
                    startPos = transform.position + new Vector3(xoff, throwStartLocalOffset.y, 0f);
                }
            }

            // 초기 속도 (DoThrowNowFromHold와 동일)
            Vector2 v0 = aimUp
                ? new Vector2(0f, carryThrowUpSpeed)
                : new Vector2(facingSign * carryThrowSideSpeed, carryThrowUpSpeed);

            // 중력 가속도 (Rigidbody2D 규칙 사용)
            Vector2 a = Physics2D.gravity * Mathf.Max(0.0001f, otherPlayer.rb.gravityScale);

            // 샘플링 파라미터
            const int maxSteps = 32;
            const float maxTime = 1.6f;
            float dt = maxTime / maxSteps;

            var lr = EnsureThrowPreview();
            if (!lr) return;

            int stopMask = groundMask | eventMask | trapMask;

            // 점들 계산
            Vector3 prev = startPos;
            List<Vector3> pts = new List<Vector3>(maxSteps + 1);
            pts.Add(startPos);

            float t = 0f;
            for (int i = 1; i <= maxSteps; i++)
            {
                t += dt;
                Vector2 p = (Vector2)startPos + v0 * t + 0.5f * a * (t * t);
                Vector3 curr = new Vector3(p.x, p.y, startPos.z);

                var hit = Physics2D.Linecast(prev, curr, stopMask);
                if (hit.collider)
                {
                    pts.Add(hit.point);
                    break;
                }

                pts.Add(curr);
                prev = curr;
            }

            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.enabled = true;
        }

        bool isSelected = (swap != null && swap.charSelect == playerID);
        bool suppressed = Time.time < swapSuppressUntil;

        bool attackLock = AttackLocksInput();
        bool locked = suppressed || Time.time < inputLockUntil || attackLock;
        lockedall = locked; // 전체 트랜지션 중에는 모든 입력/조작 봉인

        UpdateFootstepLoop(isSelected);

        if (SpiralBoxWipe.IsBusy || IsDead)
        {
            HideThrowPreview();
            rawX = 0f;
            jumpHeld = false;
            return;
        }

        if (prevSelected && !isSelected)
        {
            HideThrowPreview();
            ResetAnimStates();
        }
        prevSelected = isSelected;

        if (!isSelected)
        {
            HideThrowPreview();
            rawX = 0f;
            jumpHeld = false;
            return;
        }

        bool blockL = IsDirBlocked(-1);
        bool blockR = IsDirBlocked(+1);

        float left = (!locked && !blockL && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))) ? -1f : 0f;
        float right = (!locked && !blockR && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))) ? +1f : 0f;

        lefthold = (!locked && !blockL && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)));
        righthold = (!locked && !blockR && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)));

        rawX = Mathf.Clamp(left + right, -1f, 1f);

        bool jumpLocked = (suppressed || Time.time < inputLockUntil || attackLock);

        bool allowJumpNow = throwHoldActive ? !attackLock : !jumpLocked;

        if (allowJumpNow && Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;

        jumpHeld = allowJumpNow && Input.GetKey(KeyCode.Space);

        if (playerID == SwapController.PlayerChar.P1)
        {
            bool shiftDown = ThrowHoldDown();
            bool shiftUp = ThrowHoldUp();
            bool canToggleCarry = !locked && Time.time >= nextCarryAllowedAt;

            if (!isCarrying) throwComboPending = false;

            if (isCarrying && enableThrowHold)
            {
                if (throwComboPending)
                {
                    HideThrowPreview();

                    bool dirHeld =
                        Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
                        Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
                        Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                    bool shiftHeld = ThrowHoldHeld();

                    if (shiftHeld && dirHeld)
                    {
                        throwComboPending = false;
                        BeginThrowHold();
                        InitThrowHoldAimFromKeys();

                        DrawThrowPreview();
                        rawX = 0f;
                        return;
                    }

                    if (Time.time >= throwComboPendingUntil)
                    {
                        throwComboPending = false;
                        StopCarry(); // 내부에서 DROP/THROW 분기(현 상황은 DROP)
                        HideThrowPreview();
                        return;
                    }

                    // 보정 대기 중에는 다른 입력 잠깐 묶어줘(미끄럼/점프 방지)
                    rawX = 0f;
                    return;
                }

                // 이번 프레임에 쉬프트가 눌림
                if (shiftDown && canToggleCarry && !throwHoldActive)
                {
                    HideThrowPreview(); // 일단 감춰놓고

                    bool dirKeyHeld =
                        Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
                        Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
                        Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

                    if (dirKeyHeld)
                    {
                        // 쉬프트 + 방향키 동시 → 즉시 홀드 에임
                        BeginThrowHold();
                        InitThrowHoldAimFromKeys();
                        DrawThrowPreview();
                    }
                    else
                    {
                        // 쉬프트가 살짝 먼저면 여기로: 잠깐 기다렸다가 방향 들어오면 홀드, 아니면 DROP
                        throwComboPending = true;
                        throwComboPendingUntil = Time.time + throwComboGrace;
                    }
                    rawX = 0f;
                    return;
                }

                // 이미 홀드 중일 때(기존 동작 유지) + 프리뷰 표시
                if (throwHoldActive)
                {
                    // 수평: 마지막 입력 시간이 승리
                    if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
                    {
                        holdHorizSign = -1;
                        holdHorizChangedAt = Time.time;
                        ForceFaceSign(-1);
                    }
                    if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
                    {
                        holdHorizSign = +1;
                        holdHorizChangedAt = Time.time;
                        ForceFaceSign(+1);
                    }

                    // ↑: 마지막 입력 시간이 수평보다 최신이면 '위 에임'으로 해석
                    if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                        holdUpChangedAt = Time.time;

                    // (선택) ↓ 누르면 ↑ 해제 (원하지 않으면 이 3줄은 지워도 됨)
                    if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                        holdUpChangedAt = -999f;

                    // 자동 던지기/해제 기존 로직 그대로 유지
                    if (throwHoldMaxTime > 0f && Time.time - throwHoldStartedAt >= throwHoldMaxTime)
                    {
                        ReleaseThrowHoldAndThrow();
                        return;
                    }
                    if (ThrowHoldUp()) { ReleaseThrowHoldAndThrow(); return; }

                    // 입력 중 이동/점프 봉인은 기존대로
                    rawX = 0f;

                    // ★ 프리뷰 갱신 (실제 물리와 동기화된 포물선)
                    UpdateThrowPreview();
                    return;
                }
            }
            else
            {
                // 캐리 중이 아니면 보정 상태/프리뷰 리셋
                throwComboPending = false;
                HideThrowPreview();

                // (기존) 캐리 시작/종료 토글
                if (shiftDown && canToggleCarry)
                {
                    if (!isCarrying)
                    {
                        if (CanStartCarryNow()) TryStartCarryNow();
                    }
                    else
                    {
                        StopCarry();
                    }
                }
            }
        }

        // 홀드 중이 아니면 항상 프리뷰 감춤(안전장치)
        if (!throwHoldActive) HideThrowPreview();

        // Update() 안, 입력 처리들 아래 아무 위치에 추가
        if (!locked)
        {
            bool downPressed = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);

            // 천장에 붙어있을 때 아래키 => 즉시 해제+급강하
            if (downPressed && stickingToCeiling)
            {
                EndCeilingStick(forceDive: true);
            }
            // 일반 공중에서도 아래키로 급강하 시작
            else if (downPressed && !IsGroundedStrictSmall())
            {
                isDiving = true;
            }
        }

        // --- 공격 처리 & F Pulse ---
        if (selectedObject)
        {
            // 공격 시작: 쿨타임 체크
            if (Input.GetKeyDown(KeyCode.F) && !isCarrying && Time.time >= nextAttackTime)
            {
                SoundManager.Play("KnightAttack", transform); 
                StartAttack();
            }
            // 공격 종료: 시간 만료
            if (attack && Time.time >= attackEndTime)
            {
                EndAttack();
            }
            // 이펙트/히트박스 펄스 종료
            if (selectedObject.activeSelf && Time.time >= fPulseOffAt)
            {
                selectedObject.SetActive(false);
            }
        }

        bool grounded = IsGroundedStrictSmall();
        if (grounded)
        {
            lastGroundedTime = Time.time;
            airJumpsLeft = extraAirJumps;
        }

        // 방향 뒤집기
        if (rawX != 0f)
        {
            dir = rawX > 0 ? 1f : -1f;
            transform.localScale = new Vector3(dirseto ? dir : dir * 1f, dirsetofl, dirsetofl);
        }

        if(isCarrying == false && playerID == SwapController.PlayerChar.P1)
        {
            extraAirJumps = 1;
        }
        else if(isCarrying == false && playerID == SwapController.PlayerChar.P2)
        {
            extraAirJumps = 0;
        }
        // 천장 트랩
        CheckCeilingTrap();

        // 바닥 트랩 즉사
        var breakHit = IsBreak();
        if (breakHit.collider != null && breakHit.collider.CompareTag("Trap"))
        {
            SoundManager.Play("TrapDie", transform);
            return;
        }

        // (사이즈 프리셋)
        if (chasize)
        {
            dirseto = true;
            dir = Mathf.Sign(dir == 0 ? 1f : dir);
            dirsetofl = 1f;
            groundrayDistance = 1.3f;
            breakrayDistance = 1.4f;
            checkceilingtrap = 0.7f;
        }
        else
        {
            dirseto = false;
            dir = 1f;
            dirsetofl = 1f;
            groundrayDistance = 0.7f;
            breakrayDistance = 0.6f;
            checkceilingtrap = 0.35f;
        }

        // ==== 슬라임 접촉 상태 ====
        bool groundedForWall = IsGrounded();
        bool castL = !groundedForWall && TouchingSlimeSideCast(-1);
        bool castR = !groundedForWall && TouchingSlimeSideCast(+1);
        touchingLeftSlime = !groundedForWall && (castL || touchL_byCollision || touchL_byTrigger);
        touchingRightSlime = !groundedForWall && (castR || touchR_byCollision || touchR_byTrigger);

        if (touchingLeftSlime) { lastSlimeTouchAt = Time.time; lastSlimeSide = -1; }
        if (touchingRightSlime) { lastSlimeTouchAt = Time.time; lastSlimeSide = +1; }

        bool touchingGroundAny = IsGroundedStrictSmall() || IsGrounded();

        bool awayLeft = touchingRightSlime && (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow));
        bool awayRight = touchingLeftSlime && (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow));
        bool spaceOK = requireSpaceForWallJump ? Input.GetKey(KeyCode.Space) : true;

        if (!touchingGroundAny && spaceOK && (awayLeft || awayRight) && !(isCarrying || isCarried))
        {
            // 슬라임 재부착 억제(기존)
            ignoreSlimeUntil = Time.time + wallDetachGrace;

            // 월점프 속도 설정 (기존 코드 유지)
            Vector2 v2 = rb.linearVelocity;
            if (awayLeft) v2.x = -Mathf.Abs(wallJumpHorizontal);
            if (awayRight) v2.x = Mathf.Abs(wallJumpHorizontal);
            v2.y = wallJumpVertical;
            rb.linearVelocity = v2;

            int wallSideDir = awayLeft ? +1 : -1;
            oppositeInputLockUntil = Time.time + wallOppositeInputLock;
            oppositeInputLockedDir = wallSideDir;

            // (선택) 완전 확실히 끊고 싶으면 재부착 억제를 잠금시간과 동기화
            ignoreSlimeUntil = Mathf.Max(ignoreSlimeUntil, Time.time + wallOppositeInputLock);
        }

        // === Run 애니메이션 ===
        if (!lockedall && throwmanager &&
            (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
             Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)))
        {
            RunAni();
        }
        else
        {
            RunexitAni();
        }

        if (!locked && jumpHeld)
        {
            janit += Time.deltaTime;
            if (janit < 0.1f)
            {
                SoundManager.Play("Jump", transform);

                JumpAni();
            }
        }
        else
        {
            janit = 0;
        }
    }


    void FixedUpdate()
    {
        // === 사망했으면 물리 낙하만 처리 ===
        if (IsDead && keepFallingOnDeath && rb != null)
        {
            // 떨어질 때는 낙하용 중력 유지
            rb.gravityScale = gravityScaleFall;

            Vector2 dv = rb.linearVelocity;

            // 수평은 서서히 감쇠(자연스러운 정지)
            if (deadHorizontalDamp > 0f)
                dv.x = Mathf.MoveTowards(dv.x, 0f, deadHorizontalDamp * Time.fixedDeltaTime);

            // 최대 낙하 속도 하한 유지
            if (dv.y < maxFallSpeed) dv.y = maxFallSpeed;

            rb.linearVelocity = dv;
            return; // 나머지 이동/점프/슬라임 로직 전부 우회
        }
        // 던지기 홀드 중에는 수평 속도를 빠르게 0으로 → 미끄럼 방지
        if (throwHoldActive)
        {
            var vv = rb.linearVelocity;
            vv.x = Mathf.MoveTowards(vv.x, 0f, decel * Time.fixedDeltaTime * 2f);
            rb.linearVelocity = vv;
        }

        bool groundedStrict = IsGroundedStrictSmall();

        Vector2 v = rb.linearVelocity;
        bool grounded = groundedStrict;

        // === Ceiling Slime: 감지 & 유지 ===
        RaycastHit2D upHit = default;
        bool canCeilingStick = enableCeilingSlime && !isCarrying && !isCarried && Time.time >= ignoreCeilingUntil;
        bool headTouchesSlime = false;

        // 머리 위 슬라임 감지(안전하게 out 초기화)
        if (canCeilingStick)
            headTouchesSlime = TouchingSlimeCeilingCast(out upHit);

        // 아직 안 붙어있고 머리로 닿았으면 붙기 시작
        if (!stickingToCeiling && headTouchesSlime)
        {
            StartCeilingStick(upHit);
        }

        // 붙어있는 동안 유지/해제 판정
        if (stickingToCeiling)
        {
            bool downHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool timeUp = (Time.time - ceilingStickStartTime) >= ceilingStickMaxTime;
            bool lostContact = !headTouchesSlime;

            // ① 아래키 즉시 급강하
            if (downHeld) { EndCeilingStick(forceDive: true); }

            // ② 시간 만료/접촉 끊김 → 서서히 해제
            else if (timeUp || lostContact) { BeginCeilingRelease(); }

            // ★ 붙어있는 프레임: Y축 고정(미끄러짐 제거), 좌우는 정상 이동
            if (stickingToCeiling)
            {
                rb.gravityScale = 0f; // 중력 0 → 마찰 발생 원인 제거
                v.y = 0f;

                // 머리-천장 간격 유지(살짝 스냅)
                float targetCeilY = (upHit.collider ? upHit.point.y : lastCeilingY);
                float targetTopY = targetCeilY - ceilingKeepGap;
                float top = bodyCollider.bounds.max.y;
                float deltaY = targetTopY - top;
                if (Mathf.Abs(deltaY) > 0.0005f)
                    rb.position = rb.position + new Vector2(0f, deltaY);
            }
        }

        // 해제 페이드 중이면 중력을 서서히 복원
        if (!stickingToCeiling && ceilingReleaseUntil > 0f)
        {
            if (Time.time < ceilingReleaseUntil)
            {
                float t = 1f - ((ceilingReleaseUntil - Time.time) / ceilingReleaseFade);
                rb.gravityScale = Mathf.Lerp(0f, baseGravityFall, t);
            }
            else
            {
                rb.gravityScale = baseGravityFall;
                ceilingReleaseUntil = -1f;
            }
        }


        bool minTimeNotPassed = Time.time < ballisticThrowEndTime;
        bool ballistic = ballisticThrowActive && (minTimeNotPassed || (carryThrowHoldUntilGrounded && !groundedStrict));
        if (ballisticThrowActive && !ballistic) ballisticThrowActive = false;

        // --- 목표 속도 & 가감속 ---
        if (AttackLocksInput())
        {
            // ★ 공격 중(P1)에는 가로속도를 빠르게 0으로 수렴 → 좌우이동 불가 보장
            v.x = Mathf.MoveTowards(v.x, 0f, decel * Time.fixedDeltaTime * 2f);
        }
        else if (ballistic)
        {
            if (Mathf.Abs(rawX) > 0.01f)
            {
                float targetX = rawX * moveSpeed;
                float a = airAccel;
                v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
            }
        }
        else
        {
            float targetX = rawX * moveSpeed;
            float a = grounded
                ? (Mathf.Sign(targetX) == Mathf.Sign(v.x) ? accel : decel)
                : (Mathf.Abs(targetX) > Mathf.Abs(v.x) ? airAccel : airDecel);
            v.x = Mathf.MoveTowards(v.x, targetX, a * Time.fixedDeltaTime);
        }

        // --- 점프 처리 ---
        bool buffered = (Time.time - lastJumpPressedTime) <= jumpBuffer;
        bool canCoyote = (Time.time - lastGroundedTime) <= coyoteTime;

        // ★ 공격 중(P1)에는 점프 생성 금지
        if (!ballistic && !AttackLocksInput())
        {
            if (buffered && (canCoyote || airJumpsLeft > 0))
            {
                v.y = jumpVelocity;
                lastJumpStartTime = Time.time;
                didCutThisJump = false;

                if (!canCoyote && !grounded)
                {
                    // FX: 더블점프 이펙트 (발밑에서, 스케일 x4)
                    if (bodyCollider)
                    {
                        var b = bodyCollider.bounds;
                        Vector3 feet = new Vector3(b.center.x, b.min.y, transform.position.z);
                        SoundManager.Play("DoubleJump", transform);
                        FX.Play("doubleJumpe", feet + Vector3.down * 0.06f, 10f);
                    }

                    // 공중점프 1회 소모
                    airJumpsLeft = Mathf.Max(airJumpsLeft - 1, 0);
                }

                lastJumpPressedTime = -999f;
                lastGroundedTime = -999f;
                ignoreGroundUntil = Time.time + postJumpGroundIgnore;
            }
        }

        // --- 내려찍기 / 컷점프(비활성) ---
        if (isDiving && !grounded)
        {
            rb.gravityScale = diveGravityScale;
            v.y = Mathf.Min(v.y, diveSpeed);
        }
        else if (stickingToCeiling)
        {
            // 천장에 붙어있는 동안엔 중력/낙하 정지
            rb.gravityScale = 0f;
            if (v.y < 0f) v.y = 0f;

            // 지정 시간이 지나면 해제 시작(부드럽게 떨어짐 구간 세팅)
            if (Time.time >= ceilingStickUntil)
            {
                stickingToCeiling = false;
                ceilingReleaseUntil = Time.time + ceilingReleaseBlendTime;
            }
        }
        else
        {
            float desiredGravity = (v.y < -0.01f) ? baseGravityFall : baseGravityNormal;
            if (isCarrying) desiredGravity *= (v.y < -0.01f) ? carryFallGravityMul : carryGravityMul;
            if (!groundedStrict && Mathf.Abs(v.y) <= apexThreshold)
                desiredGravity = Mathf.Min(desiredGravity, baseGravityNormal * apexHangMultiplier);
            rb.gravityScale = Mathf.SmoothDamp(rb.gravityScale, desiredGravity, ref gravitySmoothVel, gravitySmoothTime);
        }

        if (v.y < maxFallSpeed) v.y = maxFallSpeed;

        bool groundedAny = groundedStrict || IsGrounded();
        bool touchingSlimeNow = !groundedAny && (touchingLeftSlime || touchingRightSlime);

        SetFrictionless(touchingSlimeNow);

        // === SLIME STICK / SLIDE ==
        bool onSlimeRaw = !groundedAny && (touchingLeftSlime || touchingRightSlime);

        // (기존) 아주 작은 턱 오르기
        TryStepUpSmallLedge(rawX);

        // 벽을 계속 밀고 있으면 수평입력 억제
        bool allowStick = !IsSlimeSuppressed && !(isCarrying || isCarried);
        bool pressingIntoWall =
            allowStick && onSlimeRaw &&
            ((touchingLeftSlime && rawX < -0.01f) || (touchingRightSlime && rawX > 0.01f));
        if (pressingIntoWall)
        {
            rawX = 0f;
            if (v.y > 0f) v.y = 0f;
        }

        if (allowStick && onSlimeRaw)
        {
            Vector2 wallNormal =
                touchingLeftSlime ? Vector2.right :
                touchingRightSlime ? Vector2.left : Vector2.zero;

            if (wallNormal != Vector2.zero)
            {
                rb.AddForce(-wallNormal * slimeStickPush, ForceMode2D.Force);

                float vn = Vector2.Dot(v, wallNormal);
                if (vn > 0f)
                {
                    float cut = Mathf.Min(vn, slimeNormalClamp);
                    v -= wallNormal * cut;
                }

                if (v.y < wallSlideMaxFall) v.y = wallSlideMaxFall;
            }
        }
        else if (onSlimeRaw && (isCarrying || isCarried))
        {
            if (v.y < carrySlideMaxFall) v.y = carrySlideMaxFall;
        }


        // === 천장 해제 직후 잠깐 천천히 낙하(부드러운 이탈감) ===
        if (Time.time < ceilingReleaseUntil)
        {
            if (v.y < ceilingReleaseSlideMaxFall) v.y = ceilingReleaseSlideMaxFall;
        }

        FixVerticalSeam(rawX);
        if (playerID == SwapController.PlayerChar.P2 && p2LaserActive && p2AnchorMode && p2AnchorCol)
        {
            ApplyAnchorConstraint(ref v);
        }
        rb.linearVelocity = v;
        rb.linearVelocity = v;
        // --- P2 레이저 물리 끌어오기 ---
        

        bool groundedThisFrame = groundedStrict;
        if (wasGrounded && !groundedThisFrame)
        {
            _leftGroundSinceLast = true;
            _leftGroundAt = Time.time;
        }
        if (!wasGrounded && groundedThisFrame)
        {
            JumpedAni();
            wallRegrabUntil = -1f;
            wallRegrabSide = 0;

            // ▼ 현재 조작 중인 캐릭터인지 확인
            bool isSelectedNow = (swap != null && swap.charSelect == playerID);

            // ▼ 착지 사운드: 반드시 '한번 떠난 뒤' 착지했을 때만, 그리고 현재 조작 캐릭터만
            if (isSelectedNow && _leftGroundSinceLast && (Time.time - _leftGroundAt) >= landSfxMinAirTime)
            {
                // 캐릭터에 맞는 키로 1회 재생
                if (playerID == SwapController.PlayerChar.P1)
                    SoundManager.Play("KnightJumpAnd", transform);
                else
                    SoundManager.Play("PrincessJumpAnd", transform);

                _leftGroundSinceLast = false; // 소모
            }

            // ▼ 추가: P2가 착지한 그 프레임에 ground=false
            if (playerID == SwapController.PlayerChar.P2 && rb2)
                rb2.SetBool("ground", false);

            // P2가 착지하면 throwed 해제
            if (playerID == SwapController.PlayerChar.P2 && rb2)
            {
                rb2.SetBool("throwed", false);
            }

            if (isDiving)
            {
                var hit = IsBreak();
                if (hit.collider != null && hit.collider.CompareTag("Breakable"))
                    Destroy(hit.collider.gameObject);
            }
            isDiving = false;
            ballisticThrowActive = false;
        }

        wasGrounded = groundedThisFrame;
        touchL_byTrigger = touchR_byTrigger = false;

        // --- Midair Auto-Catch (P1 only) ---
        if (playerID == SwapController.PlayerChar.P1 &&
            autoCatchEnabled &&
            !isCarrying &&
            otherPlayer != null &&
            Time.time >= nextAutoCatchAllowedAt &&
            Time.time >= autoCatchSuppressUntil)   // ← 던진 직후 0.2s 차단
        {
            TryAutoCatchMidair();
        }

    }



    /* ===================== 충돌/트리거에서 슬라임 판정 보강 ===================== */
    void OnCollisionStay2D(Collision2D col)
    {

        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;

        for (int i = 0; i < col.contactCount; i++)
        {
            var n = col.GetContact(i).normal;
            if (n.x > 0.35f) touchL_byCollision = true;
            if (n.x < -0.35f) touchR_byCollision = true;
        }
    }

    void OnCollisionExit2D(Collision2D col)
    {

        if (!IsInLayerMask(col.collider.gameObject.layer, slimeLayerMask)) return;
        touchL_byCollision = false;
        touchR_byCollision = false;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        float ox = other.bounds.center.x;
        float px = transform.position.x;
        if (ox > px) touchR_byTrigger = true;
        else touchL_byTrigger = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        float ox = other.bounds.center.x;
        float px = transform.position.x;
        if (ox > px) touchR_byTrigger = false;
        else touchL_byTrigger = false;
    }

    private static bool IsInLayerMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

    /* ===================== 바운스/트랩 등 ===================== */
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Trap")) return;

        if (collision.collider.CompareTag("BounceLeftUp"))
        {
            Bounce(new Vector2(-bounceImpulseX, bounceImpulseY));
            return;
        }
        if (collision.collider.CompareTag("BounceRightUp"))
        {
            Bounce(new Vector2(+bounceImpulseX, bounceImpulseY));
            return;
        }
    }

    private void TryStartCarryNow()
    {
        if (Time.time < nextCarryAllowedAt) return;
        if (!CanStartCarryNow()) return; // 공중 시전 차단 핵심

        if (otherPlayer == null || isCarrying || bodyCollider == null || otherPlayer.bodyCollider == null) return;

        var d = Physics2D.Distance(bodyCollider, otherPlayer.bodyCollider);
        bool closeEnough = d.isOverlapped || d.distance <= carryPickupMaxGap;
        if (!closeEnough)
        {
            Debug.Log($"[Carry] too far: overlapped={d.isOverlapped}, dist={d.distance:F3}, need<={carryPickupMaxGap:F3}");
            return;
        }
        StartCarry();
    }
    private void SoftUnlockCarryWindow(float extra = 0.25f)
    {
        nextCarryAllowedAt = Mathf.Max(nextCarryAllowedAt, Time.time + extra);
    }

    // 캐리 시작 가능 여부 게이트
    private bool CanStartCarryNow()
    {
        if (isCarrying || isCarried) return false;
        if (requireGroundedForCarryStart && !IsGroundedStrictSmall()) return false;

        if (otherPlayer == null) return false;
        if (requireOtherGroundedForCarryStart && !otherPlayer.IsGroundedStrictSmall_Public()) return false;

        if (ballisticThrowActive) return false;
        return true;
    }

    private void TryResolveSwap()
    {
        if (swap != null) return;
        var go = GameObject.FindWithTag("Swap");
        if (go != null)
        {
            swap = go.GetComponent<SwapController>();
            if (swap == null) Debug.LogWarning("[Player] Tag 'Swap' 오브젝트에 SwapController 컴포넌트가 없습니다.", go);
        }
        else
        {
            Debug.LogWarning("[Player] 태그 'Swap' 오브젝트를 씬에서 찾지 못했습니다.");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryResolveSwap();
        ApplyLayerIgnores();
    }

    private void Bounce(Vector2 impulse)
    {
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(impulse, ForceMode2D.Impulse);

        isBouncing = true;
        bounceProtectUntil = Time.time + bounceProtectDuration;
        inputLockUntil = Time.time + inputLockAfterImpulse;
    }

    // P2 렌더러 토글(자식까지 전부)
    public void SetOtherPlayerVisible(bool visible)
    {
        if (!otherPlayer) return;
        var rends = otherPlayer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) rends[i].enabled = visible;
    }

    // P2를 delay 뒤에 다시 보이게 (정확히 0.6초: Real-time 기준)
    private IEnumerator RevealOtherAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        SetOtherPlayerVisible(true);
    }

    private IEnumerator ResetThrowAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (rb2) rb2.SetBool("carry", false);
        if (rb2) rb2.SetBool("carrying", false);
        if (rb2) rb2.SetBool("throw", false);
    }

    private void StartCarry()
    {
        if (otherPlayer == null || isCarrying) return;

        gravityScaleFall = 6.0f;
        extraAirJumps = 0;
        isCarrying = true;
        carryset = true;

        otherPlayer.rb.linearVelocity = Vector2.zero;
        otherPlayer.rb.simulated = false;
        otherPlayer.isCarried = true;
        if (rb2) rb2.SetBool("ground", true);
        otherOriginalParent = otherPlayer.transform.parent;

        otherPlayer.transform.SetParent(this.transform, true);
        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

        SetOtherPlayerVisible(false);

        // 애니 기반 입력잠금 시작
        BeginCarryStartLock();
        if (rb2) rb2.SetBool("carry", true);
        lockedall = true;

        if (_revealCo != null)
        {
            StopCoroutine(_revealCo);
            _revealCo = null;
            lockedall = false;
        }
        ForceViewToP1IfNeeded();
    }

    private void StopCarry()
    {
        HideThrowPreview();
        if (otherPlayer == null || !isCarrying) return;

        // ====== 기본 상태/입력 해석 ======
        gravityScaleFall = 4.0f;
        moveSpeed = 7.0f;
        extraAirJumps = 1;

        int horizSign = 0;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizSign = +1;
        else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizSign = -1;
        bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

        int facingSign = (transform.localScale.x >= 0f) ? +1 : -1;
        int spawnSign = (horizSign != 0) ? horizSign : facingSign;
        int dirSign = spawnSign >= 0 ? +1 : -1;

        bool anyDir = (horizSign != 0) || upHeld; // false=DROP, true=THROW

        // ====== 스폰 좌표 미리 계산 ======
        Vector3 dropSpawnOffset = new(spawnSign * carryDropForward, carryOffsetY + carryThrowSeparation, 1f);
        Vector3 dropPos = transform.position + dropSpawnOffset;

        Vector3 throwPos;
        if (throwStartWorldPoint != null) throwPos = throwStartWorldPoint.position;
        else
        {
            float xoff = throwStartLocalOffset.x;
            if (throwStartUseFacing) xoff *= spawnSign;
            throwPos = transform.position + new Vector3(xoff, throwStartLocalOffset.y, 0f);
        }

        // ====== 드롭 전용 안전 검사 ======
        if (!anyDir) // DROP only
        {
            // 규칙 1: 접지 중에만 드롭 허용
            if (!IsGroundedStrictSmall())
            {
                Debug.Log("[Carry] Drop blocked: not grounded.");
                return;
            }
            // 규칙 2: 앞벽/낭떠러지/스폰영역 막힘 중 하나라도 있으면 드롭 금지
            bool wallAhead = HasWallAhead(dirSign);
            bool cliffAhead = !HasGroundBelow(new Vector2(dropPos.x, transform.position.y));
            bool areaBlocked = IsAreaBlocked((Vector2)dropPos);
            if (wallAhead || cliffAhead || areaBlocked)
            {
                Debug.Log($"[Carry] Drop blocked: wallAhead={wallAhead}, cliffAhead={cliffAhead}, areaBlocked={areaBlocked}");
                return;
            }
        }
        otherPlayer.transform.SetParent(otherOriginalParent, worldPositionStays: true);

        if (!anyDir)
        {
            // ---- DROP: 0.6초 뒤 보이기 ----
            otherPlayer.transform.position = dropPos;
            if (rb2) rb2.SetBool("ground", false);
            otherPlayer.rb.simulated = true;
            otherPlayer.isCarried = false;
            otherPlayer.rb.linearVelocity = Vector2.zero;
            otherPlayer.ballisticThrowActive = false;
            extraAirJumps = 1;
            int faceToP1 = (transform.position.x > otherPlayer.transform.position.x) ? +1 : -1;
            otherPlayer.ForceFaceSign(faceToP1);

            isCarrying = false;
            carryset = false;

            // 애니 상태 정리
            if (rb2)
            {
                extraAirJumps = 1;
                AnimatorSetBoolSafe(rb2, carryBoolName, false);      // 수동 캐리일 수도 있으니 OFF
                AnimatorSetBoolSafe(rb2, carryingBoolName, false);   //  오토캐치 OFF
                if (!string.IsNullOrEmpty(carryEndTriggerName))      // 트리거가 있으면 사용
                    AnimatorSetTriggerSafe(rb2, carryEndTriggerName);
                else if (!string.IsNullOrEmpty(carryEndStateName))   // 없으면 상태로 강제 전이
                    rb2.CrossFadeInFixedTime(carryEndStateName, 0.05f, 0, 0f);
            }
            BeginCarryEndLock();

            // P2는 0.6s 뒤 보이기(기존 로직 유지)
            if (_revealCo != null) StopCoroutine(_revealCo);
            _revealCo = StartCoroutine(RevealOtherAfter(revealDelayOnDrop));

            Debug.Log("[Carry] DROP");
            return;
        }

        // ---- THROW: 지연 후 보이면서 비행 시작 ----
        otherPlayer.transform.position = throwPos;
        otherPlayer.rb.simulated = false; // 던지기 시작 순간까지 비활성
        otherPlayer.isCarried = false;
        SetOtherPlayerVisible(false);

        Vector2 initialVelocity = upHeld
            ? new Vector2(0f, carryThrowUpSpeed)
            : new Vector2(spawnSign * carryThrowSideSpeed, carryThrowUpSpeed);

        autoCatchSuppressUntil = Time.time + autoCatchBlockOnThrow;
        
        isCarrying = false;
        carryset = false;
        // 애니 상태 정리(던질 땐 즉시 해제 연출)
        if (rb2)
        {
            AnimatorSetBoolSafe(rb2, carryBoolName, false);
            AnimatorSetBoolSafe(rb2, carryingBoolName, false);    // ★ 오토캐치 OFF
            if (!string.IsNullOrEmpty(carryEndTriggerName))
                AnimatorSetTriggerSafe(rb2, carryEndTriggerName);
            else if (!string.IsNullOrEmpty(carryEndStateName))
                rb2.CrossFadeInFixedTime(carryEndStateName, 0.05f, 0, 0f);
        }

        // 지면/공중에 따라 throw / jumpthrow 선택
        bool groundedNow = IsGroundedStrictSmall();
        if (rb2)
        {
            
            rb2.SetBool("throw", false);
            rb2.SetBool("run", false);
            rb2.SetBool("throw", true);
            rb2.SetBool("throwed", true);
        }

        throwmanager = false;

        BeginCarryEndLock();

        if (playerID == SwapController.PlayerChar.P1)
        {
            extraAirJumps = 1;
            CancelCarryLock();
            inputLockUntil = Time.time + 0.45f;

            if (_throwResetCo != null) StopCoroutine(_throwResetCo);
            _throwResetCo = StartCoroutine(ResetThrowAfter(0.6f));
        }

        otherPlayer.ForceFaceSign(spawnSign);

        if (_delayedThrowCo != null) StopCoroutine(_delayedThrowCo);
        _delayedThrowCo = StartCoroutine(DelayedThrow(throwDelay, initialVelocity));

        Debug.Log($"[Carry] THROW scheduled after {throwDelay:F2}s: startPos={throwPos}, vel={initialVelocity}, grounded={groundedNow}");
    }

    private IEnumerator DelayedThrow(float delay, Vector2 initialVelocity)
    {
        // 정확한 실시간 지연(타임스케일 영향 없음)
        yield return new WaitForSecondsRealtime(delay);

        // 보이게
        SetOtherPlayerVisible(true);

        // 물리 켜고 실제 비행 시작
        if (otherPlayer != null && otherPlayer.rb != null)
        {
            SoundManager.Play("KnightThrow", transform);
            otherPlayer.rb.simulated = true;

            // 던지기 시작 애니 플래그 (P2)

            if (otherPlayer.rb2) otherPlayer.rb2.SetBool("throwe", true);
            if (otherPlayer.rb2) otherPlayer.rb2.SetBool("throwed", true);

            // 탄도 유지 플래그/타이밍 세팅
            otherPlayer.ballisticThrowActive = true;
            otherPlayer.ballisticThrowEndTime = Time.time + otherPlayer.carryThrowBallisticMinTime;
            otherPlayer.didCutThisJump = true;
            otherPlayer.lastJumpStartTime = Time.time;
            otherPlayer.ignoreGroundUntil = Time.time + otherPlayer.postJumpGroundIgnore;

            // 초기 속도 적용
            otherPlayer.rb.linearVelocity = initialVelocity;
            throwmanager = true;
        }

        _delayedThrowCo = null;
    }

    // 1차

    private void ApplyLayerIgnores()
    {
        if (playerLayerIndexSelf >= 0)
        {
            // 자기 자신끼리 충돌 무시
            Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, playerLayerIndexSelf, true);

            // Player vs Monster 충돌 무시
            if (monsterLayerIndex >= 0)
                Physics2D.IgnoreLayerCollision(playerLayerIndexSelf, monsterLayerIndex, true);
        }
    }

    public void TakeDamage(int dmg = 1)
    {
        if (IsDead || _sceneReloading) return;
        if (IsInvincible) return; // 부활 무적 중이면 무시
        int amount = Mathf.Max(1, dmg);
        currentHP = Mathf.Max(0, currentHP - amount);
        Debug.Log($"플레이어 HP: {currentHP}");
        if (currentHP <= 0) Die();
    }

    public void SuppressInputFor(float seconds, bool zeroHorizontalVelocity = true)
    {
        swapSuppressUntil = Time.time + Mathf.Max(0f, seconds);
        rawX = 0f;
        lastJumpPressedTime = -999f;
        ResetAnimStates();
        if (zeroHorizontalVelocity && rb) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    private void ResetAnimStates()
    {
        if (!rb2) return;
        rb2.SetBool("run", false);
        rb2.SetBool("jump", false);
        rb2.SetBool("hurt", false);
        rb2.SetBool("jumped", false);
        rb2.SetBool("attack", false);
        attack = false;
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (playerID == SwapController.PlayerChar.P2)
        {
            if (_sceneReloading) return;
            _sceneReloading = true;

            //  DS3 스타일 연출 + 리로드
            SpiralBoxWipe.Run(SceneManager.GetActiveScene().name);
            return;
        }

        // ▼ 사망 후에도 낙하하도록 물리 상태 보장
        if (rb)
        {
            rb.simulated = true; // 혹시 꺼졌을 경우 대비
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = gravityScaleFall; // 하강 중 중력
        }
    }
    private void RunAni()
    {
        
        if (rb2) rb2.SetBool("run", true);
        
        lefthold = false;
        righthold = false;
    }
    private void RunexitAni()
    {
        if (rb2) rb2.SetBool("run", false);
    }
    private void JumpAni()
    {
        if (rb2) rb2.SetBool("jump", true);
        jumpchk = true;
    }
    private void JumpedAni()
    {
        if (rb2)
        {
          
            rb2.SetBool("jump", false);
            rb2.SetBool("jumped", true);
        }
    }
    // === 공격 시작/종료 ===
    private void StartAttack()
    {
        attack = true;
        if (rb2) rb2.SetBool("attack", true);

        rawX = 0f;
        jumpHeld = false;
        lastJumpPressedTime = -999f;

        if (_attackPulseCo != null) StopCoroutine(_attackPulseCo);
        _attackPulseCo = StartCoroutine(ActivateSelectedAfterDelay(attackHitDelay));

        attackEndTime = Time.time + attackDuration; // 공격 애니 자동 종료
        nextAttackTime = Time.time + attackCooldown; // 쿨타임 시작
    }

    private void EndAttack()
    {
        attack = false;
        if (rb2) rb2.SetBool("attack", false);

        // 지연 코루틴 취소 및 안전 비활성화
        if (_attackPulseCo != null)
        {
            StopCoroutine(_attackPulseCo);
            _attackPulseCo = null;
        }
        if (selectedObject) selectedObject.SetActive(false); // 펄스 타이머와 무관하게 끔
    }

    private IEnumerator ActivateSelectedAfterDelay(float delay)
    {
        // 혹시 켜져있다면 먼저 끄기
        if (selectedObject) selectedObject.SetActive(false);

        float until = Time.time + Mathf.Max(0f, delay);
        // 공격이 유지되는 동안만 대기
        while (Time.time < until)
        {
            if (!attack) yield break; // 공격이 중간에 끝나면 취소
            yield return null;
        }

        if (!attack) yield break;

        if (selectedObject)
        {
            selectedObject.SetActive(true);
            fPulseOffAt = Time.time + fPulseDuration; // 기존 펄스 타이머 그대로 사용
        }

        _attackPulseCo = null;
    }

    // === 애니 기반 잠금 유틸 ===
    private void CancelCarryLock()
    {
        if (_carryLockCo != null)
        {
            StopCoroutine(_carryLockCo);
            _carryLockCo = null;
        }
    }

    private float GetCurrentClipLengthSec(int layer = 0)
    {
        if (!rb2) return 0f;
        var clips = rb2.GetCurrentAnimatorClipInfo(layer);
        if (clips != null && clips.Length > 0 && clips[0].clip)
        {
            float speed = rb2.speed;
            if (speed < 0.05f) speed = 1f; // ★ 방어: 느리면 1배로 간주
            return clips[0].clip.length / Mathf.Max(0.0001f, speed);
        }
        return 0f;
    }

    private void TryStepUpSmallLedge(float dirInput)
    {
        if (!enableStepUp) return;
        if (!bodyCollider) return;
        if (Mathf.Abs(dirInput) < 0.01f) return;

        // 위로 상승 중일 땐 스킵(계단 탈 때만)
        if (rb && rb.linearVelocity.y > stepOnlyWhenFallingVy) return;

        Bounds b = bodyCollider.bounds;
        int sign = dirInput > 0f ? +1 : -1;

        bool allowBoxSurface = false;
        if (enableBoxGateForStepUp && boxMask != 0)
        {
            float cx = b.center.x + sign * (b.extents.x + boxGateDepth * 0.5f);
            Vector2 size = new Vector2(boxGateDepth, boxGateThickness);

            Vector2 lowCenter = new Vector2(cx, b.min.y + boxGateLowerY);
            Vector2 upCenter = new Vector2(cx, b.min.y + boxGateUpperY);

            bool lowHit = Physics2D.OverlapBox(lowCenter, size, 0f, boxMask);
            bool upHit = Physics2D.OverlapBox(upCenter, size, 0f, boxMask);

#if UNITY_EDITOR
            // 디버그 시각화
            Debug.DrawLine(lowCenter + new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                           lowCenter + new Vector2(size.x * 0.5f, -size.y * 0.5f),
                           lowHit ? Color.cyan : Color.gray, 0f);
            Debug.DrawLine(lowCenter + new Vector2(size.x * 0.5f, -size.y * 0.5f),
                           lowCenter + new Vector2(size.x * 0.5f, size.y * 0.5f),
                           lowHit ? Color.cyan : Color.gray, 0f);
            Debug.DrawLine(lowCenter + new Vector2(size.x * 0.5f, size.y * 0.5f),
                           lowCenter + new Vector2(-size.x * 0.5f, size.y * 0.5f),
                           lowHit ? Color.cyan : Color.gray, 0f);
            Debug.DrawLine(lowCenter + new Vector2(-size.x * 0.5f, size.y * 0.5f),
                           lowCenter + new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                           lowHit ? Color.cyan : Color.gray, 0f);

            Debug.DrawLine(upCenter + new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                           upCenter + new Vector2(size.x * 0.5f, -size.y * 0.5f),
                           upHit ? Color.blue : Color.gray, 0f);
            Debug.DrawLine(upCenter + new Vector2(size.x * 0.5f, -size.y * 0.5f),
                           upCenter + new Vector2(size.x * 0.5f, size.y * 0.5f),
                           upHit ? Color.blue : Color.gray, 0f);
            Debug.DrawLine(upCenter + new Vector2(size.x * 0.5f, size.y * 0.5f),
                           upCenter + new Vector2(-size.x * 0.5f, size.y * 0.5f),
                           upHit ? Color.blue : Color.gray, 0f);
            Debug.DrawLine(upCenter + new Vector2(-size.x * 0.5f, size.y * 0.5f),
                           upCenter + new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                           upHit ? Color.blue : Color.gray, 0f);
#endif
            if (upHit) return;                // 높다 → 금지
            allowBoxSurface = lowHit && !upHit;
        }

        // ===== 발 앞쪽에서 '올라탈 표면' 높이 측정 =====
        float feetY = b.min.y + 0.01f;
        Vector2 rayOrigin = new Vector2(
            b.center.x + sign * (b.extents.x + stepForward),
            feetY + stepUpMax
        );
        float rayLen = stepUpMax + 0.06f;

        //Box 표면도 허용해야 Box 위로 올라간다
        int surfaceMask = groundMask | eventMask | (allowBoxSurface ? boxMask : 0);

        RaycastHit2D down = Physics2D.Raycast(rayOrigin, Vector2.down, rayLen, surfaceMask);
#if UNITY_EDITOR
        Debug.DrawRay(rayOrigin, Vector2.down * rayLen, down ? Color.yellow : Color.gray, 0f);
#endif
        if (!down) return;

        float climb = down.point.y - feetY;
        if (climb <= 0f || climb > stepUpMax) return;

        // 머리/몸통 간섭 체크
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = surfaceMask | trapMask
        };
        RaycastHit2D[] buf = new RaycastHit2D[2];
        int hitCount = bodyCollider.Cast(Vector2.up, filter, buf, climb + stepUpSkin);
        if (hitCount > 0) return;

        // 안전 상승
        if (rb)
            rb.position = rb.position + new Vector2(0f, climb + stepUpSkin);
        else
            transform.position += new Vector3(0f, climb + stepUpSkin, 0f);
    }

    // PlayerMouseMovement 내부 (아래 메서드 아무 곳에 추가)
    public void ResetJumpStateOnRevive(bool assumeGrounded = true, int restoreExtraJumpsTo = 1)
    {
        // 캐리/상태 잔여치 정리
        isCarrying = false;
        isCarried = false;
        ballisticThrowActive = false;
        isDiving = false;
        stickingToCeiling = false;

        // 2단 점프 회복
        extraAirJumps = restoreExtraJumpsTo;   // 기본 1개로 복구(프로젝트 값에 맞게 바꾸세요)
                                               // 내부 카운터도 채움(grounded 간주)
        if (assumeGrounded)
        {
            var now = Time.time;
            lastGroundedTime = now;
            ignoreGroundUntil = -1f;
        }
        // 속도 정리
        if (rb) rb.linearVelocity = Vector2.zero;
    }

    private void ApplyAnchorConstraint(ref Vector2 v)
    {
        // 앵커 중심과 플레이어 몸 중심
        Vector2 anchor = p2AnchorCol.bounds.center;
        Vector2 center = bodyCollider.bounds.center;

        float max = p2AnchorUseBoxBreakDist ? p2LaserBreakDist : p2AnchorMaxDist;

        // LaserAnchor가 길이 오버라이드하면 적용
        var la = p2AnchorCol.GetComponentInParent<LaserAnchor>();
        if (la && la.maxDistanceOverride > 0f) max = la.maxDistanceOverride;

        Vector2 r = center - anchor;
        float dist = r.magnitude;
        if (dist <= max + 0.0001f) return;

        Vector2 dir = r / dist;

        // 1) 바깥쪽 속도 성분 제거(더 멀어지지 않게)
        float outV = Vector2.Dot(v, dir);
        if (outV > 0f) v -= dir * outV;

        // 2) 위치 스냅(원주 위로)
        Vector2 targetCenter = anchor + dir * max;
        Vector2 delta = targetCenter - center;
        rb.position += delta; // bounds.center 기준 보정
    }

    private void FixVerticalSeam(float dirInput)
    {
        if (!bodyCollider) return;
        if (!IsGroundedStrictSmall()) return;
        if (Mathf.Abs(dirInput) < 0.01f) return;

        Bounds b = bodyCollider.bounds;
        int sign = dirInput > 0 ? +1 : -1;

        // 발 앞, 아주 얇은 세로면이 있는지 검사
        Vector2 seamCenter = new Vector2(
            b.center.x + sign * (b.extents.x + seamFixProbe * 0.5f),
            b.min.y + 0.02f
        );
        Vector2 seamSize = new Vector2(seamFixProbe, 0.04f);

        bool hasVerticalFace = Physics2D.OverlapBox(seamCenter, seamSize, 0f, groundMask);
        if (!hasVerticalFace) return;

        // 현재 발 아래가 실제 바닥이면(공중 아님) '이음매'로 간주, 살짝 들어올림
        Vector2 feetCenter = new Vector2(b.center.x, b.min.y - 0.01f);
        Vector2 feetSize = new Vector2(b.size.x * 0.9f, 0.02f);
        bool grounded = Physics2D.OverlapBox(feetCenter, feetSize, 0f, groundMask);
        if (grounded)
        {
            rb.position = rb.position + new Vector2(0f, seamFixLift);
        }
    }
    private bool IsDirBlocked(int dir)
    {
        return Time.time < oppositeInputLockUntil && oppositeInputLockedDir == dir;
    }

    private IEnumerator LockForAnimation(float minLockSeconds, bool zeroHorizontalVelocity)
    {
        float t0 = Time.time;
        inputLockUntil = Mathf.Max(inputLockUntil, t0 + 0.0001f);

        if (zeroHorizontalVelocity && rb) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        yield return null; // 상태 전이 후 길이 측정

        float clipLen = useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f;
        float lockFor = Mathf.Max(minLockSeconds, clipLen);
        inputLockUntil = Time.time + lockFor;

        while (Time.time < inputLockUntil) yield return null;
        _carryLockCo = null;
    }

    private void BeginCarryStartLock()
    {
        CancelCarryLock();
        _carryLockCo = StartCoroutine(LockForAnimation(carryStartMinLock, true));
        float planned = Time.time + Mathf.Max(carryStartMinLock, useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f);
        nextCarryAllowedAt = Mathf.Max(nextCarryAllowedAt, planned);
    }

    private void BeginCarryEndLock()
    {
        CancelCarryLock();
        _carryLockCo = StartCoroutine(LockForAnimation(carryEndMinLock, true));
        float planned = Time.time + Mathf.Max(carryEndMinLock, useAnimDrivenCarry ? GetCurrentClipLengthSec() : 0f) + carryCooldown;
        nextCarryAllowedAt = Mathf.Max(nextCarryAllowedAt, planned);
    }

    private void SetThrowPreviewColor(bool hazardous)
    {
        if (!throwPreview) return;
        Color c = hazardous ? previewHazardColor : previewSafeColor;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(c.a, 1f) }
        );
        throwPreview.colorGradient = g;
    }

    private void EnsureP2LaserLine()
    {
        if (p2LaserLine != null) return;

        var go = new GameObject("P2LaserLine");
        go.transform.SetParent(transform, false);
        p2LaserLine = go.AddComponent<LineRenderer>();

        // 머티리얼 없이도 또렷하게: 기본 Unlit 느낌
        var shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        p2LaserLine.sharedMaterial = mat;

        p2LaserLine.textureMode = LineTextureMode.Stretch;
        p2LaserLine.numCapVertices = 8;
        p2LaserLine.numCornerVertices = 3;
        p2LaserLine.alignment = LineAlignment.View;
        p2LaserLine.positionCount = 2;
        p2LaserLine.enabled = false;

        // 파란빛 그라디언트
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(new Color(0.25f, 0.75f, 1f, 1f), 0f),  // 밝은 파랑
            new GradientColorKey(new Color(0.05f, 0.35f, 1f, 1f), 1f)   // 진한 파랑
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(0.85f, 0f),
            new GradientAlphaKey(0.85f, 1f)
            }
        );
        p2LaserLine.colorGradient = g;

        // 기본 굵기 곡선(중앙 살짝 두껍게)
        var w = new AnimationCurve(
            new Keyframe(0f, 1.0f),
            new Keyframe(0.15f, 1.2f),
            new Keyframe(0.85f, 1.2f),
            new Keyframe(1f, 0.8f)
        );
        p2LaserLine.widthCurve = w;
        p2LaserLine.widthMultiplier = beamBaseWidth;

        // 정렬: 플레이어 스프라이트보다 위
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            p2LaserLine.sortingLayerID = sr.sortingLayerID;
            p2LaserLine.sortingOrder = sr.sortingOrder + beamSortingOrderOffset;
        }
    }

    private void EnsureP2ImpactFx()
    {
        if (p2ImpactFx != null || impactSprite == null) return;

        var fx = new GameObject("P2LaserImpact");
        fx.transform.SetParent(transform, false);
        p2ImpactSr = fx.AddComponent<SpriteRenderer>();
        p2ImpactSr.sprite = impactSprite;
        p2ImpactSr.color = new Color(0.35f, 0.75f, 1f, 0.9f); // 푸른 글로우

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            p2ImpactSr.sortingLayerID = sr.sortingLayerID;
            p2ImpactSr.sortingOrder = sr.sortingOrder + beamSortingOrderOffset + 1;
        }

        p2ImpactFx = fx.transform;
        p2ImpactFx.gameObject.SetActive(false);
    }


    // 애니메이션 이벤트 훅(선택 사용)
    public void AE_CarryStart_Begin() { }
    public void AE_CarryStart_End() { inputLockUntil = Time.time; }
    public void AE_CarryEnd_Begin() { }
    public void AE_CarryEnd_End() { inputLockUntil = Time.time; }

    private bool IsGroundedStrictSmall()
    {
        if (Time.time < ignoreGroundUntil) return false;
        if (!bodyCollider) return false;

        Bounds b = bodyCollider.bounds;
        float skin = Mathf.Max(0.005f, groundCheckSkin);
        Vector2 boxCenter = new(b.center.x, b.min.y + skin * 0.5f);
        Vector2 boxSize = new(Mathf.Max(0.02f, b.size.x * 0.9f), skin);

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, groundMask);

#if UNITY_EDITOR
        Color c = hit ? Color.green : Color.red;
        Debug.DrawLine(new Vector2(boxCenter.x - boxSize.x * 0.5f, boxCenter.y),
                       new Vector2(boxCenter.x + boxSize.x * 0.5f, boxCenter.y), c, 0f, false);
#endif
        return hit != null;
    }

    // 외부에서 쓸 수 있도록 래퍼
    public bool IsGroundedStrictSmall_Public() => IsGroundedStrictSmall();
    private bool CanEnterAutoCatchGate_Body(Bounds p1Body, Bounds p2Body)
    {
        if (Time.time < autoCatchSuppressUntil) return false;
        if (Time.time < nextAutoCatchAllowedAt) return false;
        if (otherPlayer == null || otherPlayer.rb == null) return false;

        // 던지기 딜레이 중이면 금지
        if (!otherPlayer.rb.simulated) return false;

        // P1이 바쁜 상태(공격/입력락/컷씬 등)면 금지
        if (autoCatchDisallowIfP1Busy && (AttackLocksInput() || lockedall || SpiralBoxWipe.IsBusy)) return false;

        // P2가 숨김 상태면 금지
        if (autoCatchDisallowIfP2Hidden && !AnyRendererVisible(otherPlayer.gameObject)) return false;

        // 던진 뒤 창만 허용 옵션
        if (onlyCatchDuringThrowWindow && !(otherPlayer.ballisticThrowActive && !otherPlayer.IsGroundedStrictSmall_Public()))
            return false;

        // 내려오는 중만 허용 옵션
        if (autoCatchRequireP2Descending && otherPlayer.rb.linearVelocity.y > -0.01f) return false;

        // 사이에 지형/트랩 끼임 금지(옵션)
        if (autoCatchDisallowIfBlockingCeiling)
        {
            Vector2 from = p2Body.center;
            Vector2 to = p1Body.center;
            Vector2 dir = to - from;
            float dist = dir.magnitude;
            if (dist > 0.001f)
            {
                dir /= dist;
                float width = Mathf.Max(0.10f, Mathf.Min(p1Body.size.x, p2Body.size.x) * 0.5f);
                var hit = Physics2D.BoxCast(from, new Vector2(width, 0.08f), 0f, dir, dist, autoCatchObstructionMask);
#if UNITY_EDITOR
                Debug.DrawLine(from, to, hit ? Color.red : Color.green, 0f);
#endif
                if (hit.collider) return false;
            }
        }

        return true;
    }



    private void TryAutoCatchMidair()
    {
        if (otherPlayer == null || bodyCollider == null || otherPlayer.bodyCollider == null) return;

        // 던지기 딜레이 중(물리 off)면 금지
        if (otherPlayer.rb != null && !otherPlayer.rb.simulated) return;

        // THROW 전용 옵션이면 창 유효성 확인
        bool throwWindow = otherPlayer.ballisticThrowActive && !otherPlayer.IsGroundedStrictSmall_Public();
        if (onlyCatchDuringThrowWindow && !throwWindow) return;

        // === 몸 전체 박스(여유 포함)로 판정 ===
        Bounds b1 = bodyCollider.bounds;               // P1 몸
        Bounds b2 = otherPlayer.bodyCollider.bounds;   // P2 몸

        // P1의 바디 박스를 약간 확장해서 캐치 윈도우 완화
        Bounds catchBox = new Bounds(b1.center, b1.size + new Vector3(bodyCatchPadding.x, bodyCatchPadding.y, 0f));
        bool intersects = catchBox.Intersects(b2);
        if (!intersects) return;

        // 게이트(최소한의 안전 조건만) 통과 확인
        if (!CanEnterAutoCatchGate_Body(b1, b2)) return;

        // 스냅 캐치 & 시점 P1 강제
        SnapCatchOtherMidair();
        ForceViewToP1IfNeeded();

        nextAutoCatchAllowedAt = Time.time + autoCatchCooldown;
    }


    private void ForceViewToP1IfNeeded()
    {
        if (!autoForceViewToP1OnCatch) return;

        bool switched = false;
        if (swap != null)
        {
            // 시점이 P2면 P1로 전환
            if (swap.charSelect != SwapController.PlayerChar.P1)
            {
                try { swap.charSelect = SwapController.PlayerChar.P1; switched = true; }
                catch { /* 읽기전용이면 UnityEvent로 처리 */ }
            }
        }

        if (!switched)
        {
            // 카메라 컨트롤러에 직접 바인딩된 훅 호출(선택)
            onForceViewToP1?.Invoke();
        }
    }
    // 애니 없이 곧장 '캐리 상태' 세팅 (StartCarry와 유사하지만 락/모션 없음)
    private void SnapCatchOtherMidair()
    {
        if (otherPlayer == null) return;

        otherPlayer.rb.linearVelocity = Vector2.zero;
        otherPlayer.rb.simulated = false;
        otherPlayer.isCarried = true;
        otherPlayer.ballisticThrowActive = false;
        extraAirJumps = 0;
        if (rb2) rb2.SetBool("ground", true);

        if (otherPlayer.rb2)
        {
            AnimatorSetBoolSafe(otherPlayer.rb2, "throwe", false);
            AnimatorSetBoolSafe(otherPlayer.rb2, "throwed", false);
        }

        SetOtherPlayerVisible(false);

        otherOriginalParent = otherPlayer.transform.parent;
        otherPlayer.transform.SetParent(this.transform, true);
        otherPlayer.transform.position = transform.position + new Vector3(0f, carryOffsetY, 0f);

        isCarrying = true;
        carryset = true;

        // ★ 오토캐치: 'carrying'만 On
        if (rb2)
        {
            AnimatorSetBoolSafe(rb2, carryingBoolName, true);
            // 수동 캐리용 'carry'는 건드리지 않음
        }
    }

    private IEnumerator RestoreBoxAfterDelay(Transform root, Rigidbody2D rb2d)
    {
        yield return new WaitForSeconds(onReleaseDragDuration);

        // 드래그 원래값 복원
        if (rb2d != null)
        {
            if (p2PrevDrag >= 0f) rb2d.linearDamping = p2PrevDrag;
            if (p2PrevAngDrag >= 0f) rb2d.angularDamping = p2PrevAngDrag;
        }

        // 마찰은 조금 더 유지했다가 원복
        if (restoreMatAfter)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, restoreMatAfterSec - onReleaseDragDuration));
            for (int i = 0; i < p2LastCols.Count; i++)
            {
                var (col, orig) = p2LastCols[i];
                if (col) col.sharedMaterial = orig;
            }
            p2LastCols.Clear();
        }

        p2PrevDrag = p2PrevAngDrag = -1f;
        p2RestoreCo = null;
    }

    private void SetFrictionless(bool on)
    {
        if (!bodyCollider) return;

        if (on)
        {
            if (!_appliedNoFriction)
            {
                if (!slimeNoFrictionMat)
                {
                    slimeNoFrictionMat = new PhysicsMaterial2D("Runtime_NoFric");
                    slimeNoFrictionMat.friction = 0f;
                    slimeNoFrictionMat.bounciness = 0f;
                }
                bodyCollider.sharedMaterial = slimeNoFrictionMat;
                _appliedNoFriction = true;
            }
        }
        else
        {
            if (_appliedNoFriction)
            {
                bodyCollider.sharedMaterial = _originalMat;
                _appliedNoFriction = false;
            }
        }
    }

    private bool ThrowHoldDown() => Input.GetKeyDown(throwHoldKeyMain) || Input.GetKeyDown(throwHoldKeyAlt);
    private bool ThrowHoldHeld() => Input.GetKey(throwHoldKeyMain) || Input.GetKey(throwHoldKeyAlt);
    private bool ThrowHoldUp() => Input.GetKeyUp(throwHoldKeyMain) || Input.GetKeyUp(throwHoldKeyAlt);

    private void FreezeThrowAnimAtFirstFrame()
    {
        if (!rb2) return;
        _prevAnimSpeed = rb2.speed;

        // 던지기 상태로 즉시 진입 후 정지
        if (!string.IsNullOrEmpty(throwStateName))
            rb2.CrossFadeInFixedTime(throwStateName, 0.05f, 0, 0f);
        rb2.SetBool("throw", true);    // 기존 파이프라인과 호환
        if (throwHoldFreezeAnimator) rb2.speed = 0f;
    }

    private void RestoreAnimatorSpeed()
    {
        if (!rb2) return;
        rb2.speed = Mathf.Approximately(_prevAnimSpeed, 0f) ? 1f : _prevAnimSpeed;
    }
    private void BeginThrowHold()
    {
        if (!isCarrying || otherPlayer == null) return;

        ResetHoldAimState();          // 래치 초기화
        EnsureThrowPreviewLine();     // 프리뷰 라인 준비
        DisableOtherPreviewLines();   // 예전 라인 OFF
        throwHoldActive = true;
        throwHoldStartedAt = Time.time;

        rawX = 0f;                    // 수평만 고정(미끄럼 방지)
                                      // 기존: jumpHeld = false;  → 삭제
                                      // 기존: lastJumpPressedTime = -999f; → 삭제

        FreezeThrowAnimAtFirstFrame();

        if (throwPreview) throwPreview.enabled = true; // 프리뷰 ON
    }



    private void ReleaseThrowHoldAndThrow()
    {
        HideThrowPreview();
        if (!throwHoldActive) return;
        throwHoldActive = false;

        RestoreAnimatorSpeed();

        // ★ 프리뷰 끄기
        if (throwPreview) throwPreview.enabled = false;

        DoThrowNowFromHold();
        SoftUnlockCarryWindow(0.25f);
    }


    // StopCarry()의 THROW 분기를 그대로 재현(드롭 분기 없이 강제 던지기)
    private void DoThrowNowFromHold()
    {
        if (otherPlayer == null || !isCarrying) return;

        // ↑ 입력 체크 (홀드 해제 순간 기준)
        bool aimUp = HoldAimUpActive();


        // ====== 공통 상태 해제 ======
        otherPlayer.transform.SetParent(otherOriginalParent, worldPositionStays: true);

        // 스폰 위치 계산
        int facingSign = HoldHorizSignOrFacing();

        Vector3 throwPos;
        if (throwStartWorldPoint != null)
        {
            throwPos = throwStartWorldPoint.position; // 월드 포인트가 있으면 그대로 사용
        }
        else
        {
            if (aimUp)
            {
                // ↑ 던지기: X는 중앙, Y는 머리 위로
                throwPos = transform.position + new Vector3(0f, throwStartLocalOffsetUp.y, 0f);
            }
            else
            {
                float xoff = throwStartLocalOffset.x * (throwStartUseFacing ? facingSign : 1f);
                throwPos = transform.position + new Vector3(xoff, throwStartLocalOffset.y, 0f);
            }
        }

        otherPlayer.transform.position = throwPos;
        otherPlayer.rb.simulated = false;  // 지연 시작 전까지 Off
        otherPlayer.isCarried = false;
        SetOtherPlayerVisible(false);

        // 초기 속도
        Vector2 initialVelocity = aimUp
            ? new Vector2(0f, carryThrowUpSpeed)                                   // ↑ 던지기
            : new Vector2(facingSign * carryThrowSideSpeed, carryThrowUpSpeed);    // 좌/우 대각

        // 던진 직후 오토캐치 차단
        autoCatchSuppressUntil = Time.time + autoCatchBlockOnThrow;

        // P1 애니/상태 정리
        isCarrying = false;
        carryset = false;

        if (rb2)
        {
            AnimatorSetBoolSafe(rb2, carryBoolName, false);
            AnimatorSetBoolSafe(rb2, carryingBoolName, false);
            if (!string.IsNullOrEmpty(carryEndTriggerName)) AnimatorSetTriggerSafe(rb2, carryEndTriggerName);
            else if (!string.IsNullOrEmpty(carryEndStateName)) rb2.CrossFadeInFixedTime(carryEndStateName, 0.05f, 0, 0f);

            rb2.SetBool("run", false);
            rb2.SetBool("throw", true);
            rb2.SetBool("hurt", false);
            rb2.SetBool("throwed", true);
        }

        throwmanager = false;

        BeginCarryEndLock();
        CancelCarryLock();
        inputLockUntil = Time.time + 0.45f;

        if (_throwResetCo != null) StopCoroutine(_throwResetCo);
        _throwResetCo = StartCoroutine(ResetThrowAfter(0.6f));

        // P2 방향 정렬: 던지기는 회전 유지
        if (!aimUp || !upThrowKeepsFacing)
        {
            otherPlayer.ForceFaceSign(facingSign);
        }
        // else: 회전 유지(아무 것도 안 함)

        // 실제 발사 스케줄
        if (_delayedThrowCo != null) StopCoroutine(_delayedThrowCo);
        _delayedThrowCo = StartCoroutine(DelayedThrow(throwDelay, initialVelocity));
    }

    // 머리 위 슬라임 감지
    private bool TouchingSlimeCeilingCast(out RaycastHit2D upHit)
    {
        upHit = default;
        if (!bodyCollider) return false;

        // 1) Collider.Cast로 머리 위 3~4cm 체크
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = slimeLayerMask, useTriggers = true };
        RaycastHit2D[] hits = new RaycastHit2D[2];
        int cnt = bodyCollider.Cast(Vector2.up, filter, hits, 0.04f);
        if (cnt > 0) { upHit = hits[0]; return true; }

        // 2) 보강: 아주 얇은 박스로 한 번 더
        Bounds b = bodyCollider.bounds;
        Vector2 size = new Vector2(b.size.x * 0.9f, 0.06f);
        Vector2 center = new Vector2(b.center.x, b.max.y + size.y * 0.5f);
        var col = Physics2D.OverlapBox(center, size, 0f, slimeMask);

#if UNITY_EDITOR
        Color c = col ? Color.green : Color.red;
        Debug.DrawLine(new Vector2(center.x - size.x * 0.5f, center.y),
                       new Vector2(center.x + size.x * 0.5f, center.y), c, 0f, false);
#endif

        if (col)
        {
            // 점만 필요하니 간단 레이로 보정
            upHit = Physics2D.Raycast(new Vector2(b.center.x, b.max.y), Vector2.up, 0.08f, slimeMask);
            return true;
        }
        return false;
    }
    public void OnRevivedSafe()
    {
        IsDead = false;                 // 해당 프로퍼티가 private set이면 이 메서드 내부에선 접근 가능
        lockedall = false;
        rawX = 0f;
        jumpHeld = false;
        inputLockUntil = -999f;
        swapSuppressUntil = -999f;

        rb.gravityScale = gravityScaleNormal;
        ResetAnimStates();
        if (rb2)
        {
            rb2.SetBool("death", false);
            rb2.SetBool("hurt", false);
            rb2.SetBool("throw", false);
            rb2.SetBool("throwed", false);
        }
    }
    private void StartCeilingStick(RaycastHit2D upHit)
    {
        stickingToCeiling = true;
        ceilingStickStartTime = Time.time;
        ceilingReleaseUntil = -1f;

        lastCeilingY = upHit.collider ? upHit.point.y : bodyCollider.bounds.max.y + 0.02f;

        // 중력 끄고 Y속도 0으로(슬라이드 방지)
        rb.gravityScale = 0f;
        var v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        // 간격 맞춰 스냅
        float targetTop = lastCeilingY - ceilingKeepGap;
        float top = bodyCollider.bounds.max.y;
        float dy = targetTop - top;
        if (Mathf.Abs(dy) > 0.0005f)
            rb.position = rb.position + new Vector2(0f, dy);
    }

    // 던지기 홀드 진입 직후, 현재 눌려있는 키로 초깃값 래치
    void InitThrowHoldAimFromKeys()
    {
        bool w = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool a = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool d = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

        if (allowAimUpInHold && w)
        {
            // 위 에임 우선
            throwAimUpLatched = true;
            holdUpChangedAt = Time.time;
            // 수평은 그대로 두되, 필요시 기본값을 유지
        }
        else
        {
            // 위 에임 해제하고 수평 결정
            throwAimUpLatched = false;

            if (a ^ d) // 둘 중 하나만 눌렸을 때
            {
                holdHorizSign = a ? -1 : +1;
                holdHorizChangedAt = Time.time;
                ForceFaceSign(holdHorizSign);
            }
            else
            {
                // 둘 다 미입력 또는 동시 입력이면 현 바라보는 방향 유지
                holdHorizSign = (transform.localScale.x >= 0f) ? +1 : -1;
            }
        }
    }

    private void BeginCeilingRelease()
    {
        if (!stickingToCeiling) return;
        stickingToCeiling = false;

        // 0 → baseGravityFall로 부드럽게 복원
        ceilingReleaseUntil = Time.time + ceilingReleaseFade;

        // 같은 프레임 재부착 방지 & 살짝 아래로 밀어줌
        ignoreCeilingUntil = Time.time + ceilingRestickBlock;
        var v = rb.linearVelocity;
        v.y = Mathf.Min(v.y, -0.1f);
        rb.linearVelocity = v;
    }

    private void EndCeilingStick(bool forceDive)
    {
        stickingToCeiling = false;
        ceilingReleaseUntil = -1f;
        ignoreCeilingUntil = Time.time + ceilingRestickBlock;

        if (forceDive)
        {
            // 즉시 급강하
            rb.gravityScale = diveGravityScale;
            var v = rb.linearVelocity;
            v.y = Mathf.Min(v.y, diveSpeed);
            rb.linearVelocity = v;
            isDiving = true;
        }
        else
        {
            // 즉시 해제(자연 낙하)
            rb.gravityScale = baseGravityFall;
            var v = rb.linearVelocity;
            v.y = Mathf.Min(v.y, -0.1f);
            rb.linearVelocity = v;
        }
    }

    private void StopFootstepLoop()
    {
        if (!string.IsNullOrEmpty(_currentWalkLoop))
        {
            SoundManager.StopLoop(_currentWalkLoop, graceful: true);
            _currentWalkLoop = null;
        }
    }

    private void UpdateFootstepLoop(bool isSelected)
    {
        if (!enableFootstepLoop || rb == null)
        {
            StopFootstepLoop();
            return;
        }

        // “재생해야 하는가?” 조건
        bool grounded = IsGroundedStrictSmall();
        float speedX = Mathf.Abs(rb.linearVelocity.x);
        bool shouldPlay =
            isSelected &&                           // 내가 현재 조작 대상일 때만
            !IsDead &&                              // 사망 중 아님
            !SpiralBoxWipe.IsBusy &&                // 컷씬/전환 중 아님
            grounded &&                             // 땅 위
            speedX >= footstepMinSpeed &&           // 실제 수평속도 존재
            !throwHoldActive &&                     // 던지기 에임 중 아님
            !attack;                                // 공격 중 아님

        // 플레이어 캐릭터별 루프 이름
        string desiredLoop = shouldPlay
            ? (playerID == SwapController.PlayerChar.P1 ? "KnightWalk" : "PrincessWalk")
            : null;

        // 상태 변화에만 반응 (중복 호출로 인한 누적 방지)
        if (_currentWalkLoop != desiredLoop)
        {
            // 1) 이전 루프 정리
            if (!string.IsNullOrEmpty(_currentWalkLoop))
                SoundManager.StopLoop(_currentWalkLoop, graceful: true);

            _currentWalkLoop = null;

            // 2) 새 루프 시작
            if (!string.IsNullOrEmpty(desiredLoop))
            {
                SoundManager.StartLoop(desiredLoop, transform);
                _currentWalkLoop = desiredLoop;
            }
        }
    }


    // 마지막 입력 기준으로 ↑ 에임 활성인지 판정
    private bool HoldAimUpActive() => allowAimUpInHold && (holdUpChangedAt > holdHorizChangedAt);

    // 수평 방향(없으면 현재 바라보는 방향)
    private int HoldHorizSignOrFacing()
    {
        if (holdHorizSign != 0) return holdHorizSign;
        return (transform.localScale.x >= 0f) ? +1 : -1;
    }

    private void ResetHoldAimState()
    {
        holdHorizSign = 0;
        holdHorizChangedAt = holdUpChangedAt = -999f;
    }

    // 한 번만 쓰는 프리뷰 라인 확보(중복 있으면 제거/비활성)
    private void EnsureThrowPreviewLine()
    {
        // 1) 같은 이름 라인 찾기(있으면 재사용, 여러 개면 하나만 남기고 제거)
        LineRenderer found = null;
        var all = GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var lr = all[i];
            if (lr == p2LaserLine) continue; // P2 레이저 라인은 건드리지 않음
            if (lr.gameObject.name == "ThrowPreviewLine")
            {
                if (found == null) found = lr;
                else Destroy(lr.gameObject); // 중복 제거
            }
        }

        // 2) 없으면 새로 생성
        if (!found)
        {
            var go = new GameObject("ThrowPreviewLine");
            go.transform.SetParent(transform, false);
            found = go.AddComponent<LineRenderer>();

            var shader = Shader.Find("Sprites/Default");
            found.sharedMaterial = new Material(shader);

            found.useWorldSpace = true;
            found.textureMode = LineTextureMode.Stretch;
            found.numCapVertices = 8;
            found.numCornerVertices = 3;
            found.alignment = LineAlignment.View;
            found.widthMultiplier = 0.05f;

            // 기본 색상: 안전(빨강)
            SetThrowPreviewColor(false);

            // 정렬: 플레이어 스프라이트보다 위
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                found.sortingLayerID = sr.sortingLayerID;
                found.sortingOrder = sr.sortingOrder + 3;
            }
        }

        throwPreview = found;
        throwPreview.positionCount = 0;
        throwPreview.enabled = false;

        // 3) 혹시 남아있는 다른 Throw/Preview 라인은 끄기(이름 기반)
        DisableOtherPreviewLines();
    }

    // 다른(예전) 프리뷰 라인들 끄기
    private void DisableOtherPreviewLines()
    {
        var all = GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var lr = all[i];
            if (lr == throwPreview || lr == p2LaserLine) continue;
            string n = lr.gameObject.name;
            if (n.Contains("Throw") || n.Contains("Preview"))
                lr.enabled = false;
        }
    }


    // 프리뷰 숨기기(던질 때/홀드 종료 때 호출)
    private void HideThrowPreview()
    {
        if (throwPreview)
        {
            throwPreview.positionCount = 0;
            throwPreview.enabled = false;
        }
    }

    private void UpdateThrowPreview()
    {
        EnsureThrowPreviewLine();
        if (!throwPreview) return;

        int sign = HoldHorizSignOrFacing();
        bool aimUp = HoldAimUpActive();

        // 스폰 위치(실제 던지기와 동일 규칙)
        Vector3 start;
        if (throwStartWorldPoint != null) start = throwStartWorldPoint.position;
        else if (aimUp) start = transform.position + new Vector3(0f, throwStartLocalOffsetUp.y, 0f);
        else
        {
            float xoff = throwStartLocalOffset.x * (throwStartUseFacing ? sign : 1f);
            start = transform.position + new Vector3(xoff, throwStartLocalOffset.y, 0f);
        }

        // 초기 속도(실제와 동일)
        Vector2 v0 = aimUp
            ? new Vector2(0f, carryThrowUpSpeed)
            : new Vector2(sign * carryThrowSideSpeed, carryThrowUpSpeed);

        RenderThrowPreview_Sim((Vector2)start, v0, throwPreviewDuration, throwPreviewSteps);
    }

    private void RenderThrowPreview_Sim(Vector2 start, Vector2 v0, float duration, int steps)
    {
        EnsureThrowPreviewLine();
        var line = throwPreview;
        if (!line) return;

        // 기본은 안전색
        SetThrowPreviewColor(false);

        int total = Mathf.Max(2, steps);
        line.positionCount = total;

        // 충돌 마스크: Ground/Event/Trap 에 닿으면 끊기
        int collideMask = groundMask | eventMask | trapMask;

        // 캐스트 반경: P2의 바디 크기 기반(없으면 기본 0.12)
        float radius = 0.12f;
        if (otherPlayer && otherPlayer.bodyCollider)
        {
            var ext = otherPlayer.bodyCollider.bounds.extents;
            radius = Mathf.Min(ext.x, ext.y) * 0.9f;
            radius = Mathf.Clamp(radius, 0.05f, 0.25f);
        }

        // 실제 던져지는 애(P2)의 물리 파라미터로 시뮬
        PlayerMouseMovement target = otherPlayer ? otherPlayer : this;

        Vector2 p = start;
        Vector2 v = v0;
        float dt = Mathf.Max(0.005f, duration / (total - 1));

        float gScale = (target.rb ? target.rb.gravityScale : 1f);
        float gNormal = target.baseGravityNormal;
        float gFall = target.baseGravityFall;
        float apexTh = target.apexThreshold;
        float apexMul = target.apexHangMultiplier;
        float smoothT = target.gravitySmoothTime;
        float vFallMin = target.maxFallSpeed;
        float smoothVel = 0f;

        for (int i = 0; i < total; i++)
        {
            line.SetPosition(i, p);

            // 중력 스텝(실제 로직과 동일)
            float desired = (v.y < -0.01f) ? gFall : gNormal;
            if (Mathf.Abs(v.y) <= apexTh) desired = Mathf.Min(desired, gNormal * apexMul);
            gScale = Mathf.SmoothDamp(gScale, desired, ref smoothVel, smoothT, Mathf.Infinity, dt);

            // 다음 위치 예측
            Vector2 vNext = v + Physics2D.gravity * gScale * dt;
            if (vNext.y < vFallMin) vNext.y = vFallMin;
            Vector2 pNext = p + vNext * dt;

            // p → pNext 구간 충돌 캐스트(원-캐스트)
            Vector2 delta = pNext - p;
            float dist = delta.magnitude;
            if (dist > 0.0001f)
            {
                var hit = Physics2D.CircleCast(p, radius, delta / dist, dist, collideMask);
                if (hit.collider)
                {
                    Vector2 endPoint = hit.point;

                    // 착지 지점에 '위험 레이어'가 있나?
                    bool hazardAtEnd =
                        ((hazardMask & (1 << hit.collider.gameObject.layer)) != 0) ||
                        Physics2D.OverlapCircle(endPoint, Mathf.Max(0.06f, radius * 0.8f), hazardMask);

                    line.SetPosition(i, endPoint);
                    line.positionCount = i + 1;
                    line.enabled = true;

                    SetThrowPreviewColor(hazardAtEnd);
                    return;
                }
            }

            // 갱신
            v = vNext;
            p = pNext;
        }

        // 충돌 없이 끝난 경우: 마지막 지점 주변에 위험 레이어가 있는지 체크
        bool hazardFinal = Physics2D.OverlapCircle(p, Mathf.Max(0.06f, radius * 0.8f), hazardMask);
        SetThrowPreviewColor(hazardFinal);
        line.enabled = true;
    }

    public void ForceReviveAt(Vector3 worldPos)
    {
        if (!IsDead) return; // 죽어있을 때만

        // 코루틴/연출 중이면 정리
        StopAllCoroutines();

        IsDead = false;
        _sceneReloading = false;
        currentHP = maxHP;

        // 물리/위치 초기화
        if (rb)
        {
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = gravityScaleNormal;
            rb.position = worldPos;      // 정확한 텔레포트
        }
        else
        {
            transform.position = worldPos;
        }

        // 상태 리셋
        inputLockUntil = Time.time + 0.1f;
        swapSuppressUntil = Time.time;
        ignoreGroundUntil = Time.time;
        ballisticThrowActive = false;
        isDiving = false;
        stickingToCeiling = false;
        ceilingReleaseUntil = -1f;
        throwHoldActive = false;
        autoCatchSuppressUntil = -1f;

        // 애니 초기화
        ResetAnimStates();
        if (rb2)
        {
            rb2.speed = 1f;
            rb2.SetBool("run", false);
            rb2.SetBool("jump", false);
            rb2.SetBool("jumped", false);
            rb2.SetBool("throw", false);
            rb2.SetBool("throwed", false);
            rb2.SetBool("carry", false);
            rb2.SetBool("carrying", false);
            rb2.SetBool("hurt", false);
            if (AnimatorHasParam(rb2, "dead", AnimatorControllerParameterType.Bool)) rb2.SetBool("dead", false);
        }

        // 부활 무적
        _invincibleUntil = Time.time + reviveIFrame;

        // 선택: 시점 P1로 강제 전환
        if (playerID == SwapController.PlayerChar.P1) ForceViewToP1IfNeeded();
    }

    private static bool AnimatorHasParam(Animator a, string name, AnimatorControllerParameterType t)
    {
        if (!a || string.IsNullOrEmpty(name)) return false;
        foreach (var p in a.parameters) if (p.name == name && p.type == t) return true;
        return false;
    }
    private static void AnimatorSetBoolSafe(Animator a, string name, bool v)
    {
        if (AnimatorHasParam(a, name, AnimatorControllerParameterType.Bool)) a.SetBool(name, v);
    }
    private static void AnimatorSetTriggerSafe(Animator a, string name)
    {
        if (AnimatorHasParam(a, name, AnimatorControllerParameterType.Trigger)) a.SetTrigger(name);
    }
    private static bool AnyRendererVisible(GameObject go)
    {
        if (!go) return false;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) if (rends[i].enabled) return true;
        return false;
    }
    private bool TouchingSlimeSideCast(int sign)
    {
        if (!bodyCollider) return false;

        Vector2 dir = (sign < 0) ? Vector2.left : Vector2.right;

        ContactFilter2D filter = new();
        filter.useLayerMask = true;
        filter.SetLayerMask(slimeLayerMask);
        filter.useTriggers = true;

        RaycastHit2D[] hits = new RaycastHit2D[2];
        int count = bodyCollider.Cast(dir, filter, hits, 0.03f);
        if (count > 0) return true;

        Bounds b = bodyCollider.bounds;
        float padX = 0.04f;
        Vector2 size = new(0.12f, b.size.y * 0.8f);
        Vector2 center = (Vector2)b.center + new Vector2(sign * (b.extents.x + size.x * 0.5f + padX), 0f);

        bool boxHit = Physics2D.OverlapBox(center, size, 0f, slimeMask);

#if UNITY_EDITOR
        Color c = (count > 0 || boxHit) ? Color.green : Color.red;
        Debug.DrawLine(center + Vector2.up * size.y * 0.5f,
                       center - Vector2.up * size.y * 0.5f, c, 0f, false);
#endif
        return boxHit;
    }

    // 앞에 벽(지형)이 있는지 — Ground만 벽으로 판단(OneWay/Event는 통과 가능이라 보통 벽 아님)
    private bool HasWallAhead(int dirSign)
    {
        if (!bodyCollider) return false;
        var b = bodyCollider.bounds;

        Vector2 origin = new(b.center.x + dirSign * (b.extents.x + 0.02f), b.center.y);
        Vector2 size = new(Mathf.Max(0.12f, b.size.x * 0.6f), Mathf.Max(0.20f, b.size.y * 0.6f));
        float dist = Mathf.Max(0.05f, releaseAheadCheckDist);

        // Ground 레이어만 벽으로 판정
        int wallMask = groundMask;
        var hit = Physics2D.BoxCast(origin, size, 0f, new Vector2(dirSign, 0f), dist, wallMask);

#if UNITY_EDITOR
        Debug.DrawRay(origin, Vector2.right * dirSign * dist, hit.collider ? Color.yellow : Color.gray, 0f);
#endif
        return hit.collider != null;
    }

    // 해당 위치 아래에 '설 수 있는' 지면이 있는지 — Ground 또는 Event(원웨이 포함) 허용
    private bool HasGroundBelow(Vector2 worldPos)
    {
        int groundOrEvent = groundMask | eventMask;
        Vector2 start = new(worldPos.x, worldPos.y + 0.1f); // 살짝 위에서 아래로
        var hit = Physics2D.Raycast(start, Vector2.down, Mathf.Max(0.05f, releaseGroundProbeDown), groundOrEvent);

#if UNITY_EDITOR
        Debug.DrawRay(start, Vector2.down * releaseGroundProbeDown, hit.collider ? Color.green : Color.red, 0f);
#endif
        return hit.collider != null;
    }

    // 해당 스폰 지점이 지형으로 막혀 있는지(겹침) — Ground/Event 모두 막힘으로 판단
    private bool IsAreaBlocked(Vector2 center)
    {
        int blockMask = groundMask | eventMask;
        var hit = Physics2D.OverlapBox(center, releaseSpawnBoxSize, 0f, blockMask);

#if UNITY_EDITOR
        Color c = hit ? Color.red : Color.green;
        Debug.DrawLine(center + Vector2.left * 0.2f, center + Vector2.right * 0.2f, c, 0f);
#endif
        return hit != null;
    }

    /* ===================== 레이 감지 유지 ===================== */
    public bool IsGrounded()
    {
        float rayDistance = groundrayDistance;

        Vector2 center = transform.position + Vector3.down * 0.2f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        bool centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, groundMask);
        bool leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, groundMask);
        bool rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, groundMask);

        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit ? Color.green : Color.red);
        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit ? Color.green : Color.red);
        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit ? Color.green : Color.red);

        return centerHit || leftHit || rightHit;
    }

    public RaycastHit2D IsBreak()
    {
        float rayDistance = breakrayDistance;

        Vector2 center = transform.position + Vector3.down * 0.2f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.down, rayDistance, eventMask);
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.down, rayDistance, eventMask);
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.down, rayDistance, eventMask);

        Debug.DrawRay(center, Vector2.down * rayDistance, centerHit.collider ? Color.cyan : Color.gray);
        Debug.DrawRay(left, Vector2.down * rayDistance, leftHit.collider ? Color.cyan : Color.gray);
        Debug.DrawRay(right, Vector2.down * rayDistance, rightHit.collider ? Color.cyan : Color.gray);

        if (centerHit.collider != null) return centerHit;
        if (leftHit.collider != null) return leftHit;
        if (rightHit.collider != null) return rightHit;
        return new RaycastHit2D();
    }

    private void CheckCeilingTrap()
    {
        float rayDistance = checkceilingtrap;

        Vector2 center = transform.position + Vector3.up * 0.5f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.up, rayDistance, trapMask);
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.up, rayDistance, trapMask);
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.up, rayDistance, trapMask);

        Debug.DrawRay(center, Vector2.up * rayDistance, centerHit.collider ? Color.magenta : Color.gray);
        Debug.DrawRay(left, Vector2.up * rayDistance, leftHit.collider ? Color.magenta : Color.gray);
        Debug.DrawRay(right, Vector2.up * rayDistance, rightHit.collider ? Color.magenta : Color.gray);

        bool hitTrap =
            (centerHit.collider && ((trapLayerIndex >= 0 && centerHit.collider.gameObject.layer == trapLayerIndex) || centerHit.collider.CompareTag("Trap"))) ||
            (leftHit.collider && ((trapLayerIndex >= 0 && leftHit.collider.gameObject.layer == trapLayerIndex) || leftHit.collider.CompareTag("Trap"))) ||
            (rightHit.collider && ((trapLayerIndex >= 0 && rightHit.collider.gameObject.layer == trapLayerIndex) || rightHit.collider.CompareTag("Trap")));
        _ = hitTrap; // 현재는 시각 디버그만
    }

    // === 유틸: 스프라이트 좌우 강제 (항상 '자기 자신'만 수정) ===
    public void ForceFaceSign(int sign)
    {
        sign = sign >= 0 ? 1 : -1;
        var ls = transform.localScale;
        transform.localScale = new Vector3(Mathf.Abs(ls.x) * sign, ls.y, ls.z);
        dir = sign;
    }
}