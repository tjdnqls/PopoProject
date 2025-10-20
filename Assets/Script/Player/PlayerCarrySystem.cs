using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어 캐리 시스템 - P1이 P2를 캐리하는 기능
/// PlayerMouseMovement.cs의 캐리 시스템과 호환되도록 설계
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerCarrySystem : MonoBehaviour
{
    [Header("Carry Settings")]
    [SerializeField] private KeyCode carryKey = KeyCode.Mouse1;
    [SerializeField] private float carryRange = 1.5f;
    [SerializeField] private float carryOffsetY = 0.5f;
    [SerializeField] private float carryPickupMaxGap = 0.15f;
    [SerializeField] private float carryCooldown = 1.4f;
    
    [Header("Carry Requirements")]
    [SerializeField] private bool requireGroundedForCarryStart = true;
    [SerializeField] private bool requireOtherGroundedForCarryStart = false;
    
    [Header("Carry Animation")]
    [SerializeField] private string carryBoolName = "carry";
    [SerializeField] private string carryingBoolName = "carrying";
    [SerializeField] private float carryLockDuration = 0.0f; // 입력 잠금 비활성화
    
    [Header("Throw Settings")]
    [SerializeField] private float carryThrowUpSpeed = 12f;
    [SerializeField] private float carryThrowSideSpeed = 8f;
    [SerializeField] private float throwDelay = 0.25f;
    [SerializeField] private Vector2 throwStartLocalOffset = new Vector2(0.35f, 0.6f);
    [SerializeField] private Vector2 throwStartLocalOffsetUp = new Vector2(0f, 0.75f);
    
    [Header("Carry End Lock")]
    [SerializeField] private float carryEndLockDuration = 0.5f; // 캐리 종료 시 P1 이동 잠금 시간
    [SerializeField] private float throwEndLockDuration = 0.3f; // 던지기 시 P1 이동 잠금 시간
    
    [Header("Auto Catch")]
    [SerializeField] private bool autoCatchEnabled = true;
    [SerializeField] private float autoCatchMinHeightAbove = 0.05f;
    [SerializeField] private float autoCatchCooldown = 0.15f;
    [SerializeField] private float autoCatchMaxHoriz = 0.60f;
    
    // === Component References ===
    private PlayerController playerController;
    private PlayerInputHandler inputHandler;
    private PlayerController otherPlayer;
    private PlayerMouseMovement legacyMovement; // 기존 시스템과의 호환성
    // private Rigidbody2D rb; // P1의 Rigidbody는 사용하지 않으므로 제거
    private Animator animator;
    
    // === Carry State ===
    public bool IsCarrying { get; private set; } = false;
    public bool IsCarried { get; private set; } = false;
    private Transform otherOriginalParent;
    private float nextCarryAllowedAt = 0f;
    private float nextAutoCatchAllowedAt = 0f;
    
    // === Throw State ===
    private bool throwHoldActive = false;
    private bool throwAimUpLatched = false;
    private Coroutine carryLockCoroutine;
    private Coroutine delayedThrowCoroutine;
    
    // === Carry End Lock ===
    private bool carryEndLockActive = false;
    private Coroutine carryEndLockCoroutine;
    
    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        inputHandler = GetComponent<PlayerInputHandler>();
        // rb = GetComponent<Rigidbody2D>(); // P1의 Rigidbody 참조 제거
        animator = GetComponent<Animator>();
        
        // 기존 PlayerMouseMovement와의 호환성을 위해 참조
        legacyMovement = GetComponent<PlayerMouseMovement>();
    }
    
    void Start()
    {
        // P1만 초기화 수행
        if (playerController.PlayerID != SwapController.PlayerChar.P1)
        {
            enabled = false;
            return;
        }
        
        // PlayerController에 모듈 등록
        playerController.RegisterModule(this);
        
        // 다른 플레이어 찾기
        FindOtherPlayer();
        
        // 기존 시스템과 동기화 (조심스럽게)
        // SyncWithLegacySystem(); // 일단 비활성화
    }
    
    void Update()
    {
        // P1만 캐리 시스템 사용 가능
        if (playerController.PlayerID != SwapController.PlayerChar.P1)
        {
            // P2인 경우 스크립트 비활성화
            enabled = false;
            return;
        }
            
        // 선택된 플레이어만 입력 처리
        if (!playerController.IsSelected)
            return;
        
        // 캐리 종료 잠금 중에도 위치 업데이트는 계속
        UpdateCarryPosition();
        
        // 캐리 종료 잠금 중이 아니면 입력 처리
        if (!carryEndLockActive)
        {
            HandleCarryInput();
            HandleThrowInput();
            HandleAutoCatch();
        }
    }
    
    // === Player Finding ===
    
    private void FindOtherPlayer()
    {
        if (otherPlayer != null) return;
        
        var allPlayers = FindObjectsOfType<PlayerController>();
        foreach (var player in allPlayers)
        {
            if (player != playerController && player.PlayerID != playerController.PlayerID)
            {
                otherPlayer = player;
                break;
            }
        }
        
        if (otherPlayer == null)
        {
            Debug.LogWarning("[PlayerCarrySystem] Could not find other player!");
        }
    }
    
    // === Legacy System Sync ===
    
    private void SyncWithLegacySystem()
    {
        if (legacyMovement == null) return;
        
        // 기존 시스템과의 충돌 방지를 위해 일단 비활성화
        // 필요시에만 선택적으로 동기화
        
        /*
        // 기존 시스템의 상태와 동기화
        IsCarrying = legacyMovement.isCarrying;
        IsCarried = legacyMovement.isCarried;
        
        // 기존 시스템의 otherPlayer 설정
        if (legacyMovement.otherPlayer == null && otherPlayer != null)
        {
            var otherLegacy = otherPlayer.GetComponent<PlayerMouseMovement>();
            if (otherLegacy != null)
            {
                legacyMovement.otherPlayer = otherLegacy;
                otherLegacy.otherPlayer = legacyMovement;
            }
        }
        */
    }
    
    // === Input Handling ===
    
    private void HandleCarryInput()
    {
        if (inputHandler == null) return;
        
        // 캐리 종료 잠금 중에는 입력 처리 안 함
        if (carryEndLockActive) return;
        
        bool carryPressed = inputHandler.GetCarryPressed();
        
        if (carryPressed && CanToggleCarry())
        {
            if (!IsCarrying)
            {
                TryStartCarry();
            }
            else
            {
                StopCarryWithLock(); // 잠금과 함께 종료
            }
        }
    }
    
    private void HandleThrowInput()
    {
        if (!IsCarrying || inputHandler == null) return;
        
        // 캐리 종료 잠금 중에는 던지기 입력 처리 안 함
        if (carryEndLockActive) return;
        
        bool aimUp = inputHandler.GetAimUpHeld();
        bool carryHeld = Input.GetKey(carryKey);
        
        // 던지기 에임 처리
        if (carryHeld && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || aimUp))
        {
            if (!throwHoldActive)
            {
                BeginThrowHold();
            }
            throwAimUpLatched = aimUp;
        }
        else if (throwHoldActive)
        {
            // 홀드 해제 시 던지기 실행
            ExecuteThrowWithLock(); // 잠금과 함께 던지기
        }
    }
    
    // === Carry Logic ===
    
    private bool CanToggleCarry()
    {
        if (Time.time < nextCarryAllowedAt) return false;
        if (playerController.State.inputLocked) return false;
        if (carryEndLockActive) return false; // 캐리 종료 잠금 중에는 비활성
        
        return true;
    }
    
    private bool CanStartCarry()
    {
        if (IsCarrying || IsCarried) return false;
        if (otherPlayer == null) return false;
        
        // 거리 체크
        float distance = Vector2.Distance(transform.position, otherPlayer.transform.position);
        if (distance > carryRange) return false;
        
        // 접지 조건 체크
        if (requireGroundedForCarryStart)
        {
            var groundDetection = GetComponent<PlayerGroundDetection>();
            if (groundDetection != null && !groundDetection.IsGrounded()) return false;
        }
        
        if (requireOtherGroundedForCarryStart)
        {
            var otherGroundDetection = otherPlayer.GetComponent<PlayerGroundDetection>();
            if (otherGroundDetection != null && !otherGroundDetection.IsGrounded()) return false;
        }
        
        return true;
    }
    
    public void TryStartCarry()
    {
        if (!CanStartCarry()) return;
        
        StartCarry();
    }
    
    private void StartCarry()
    {
        if (otherPlayer == null || IsCarrying) return;
        
        IsCarrying = true;
        
        // 다른 플레이어의 캐리 시스템에도 알림
        var otherCarrySystem = otherPlayer.GetComponent<PlayerCarrySystem>();
        if (otherCarrySystem != null)
        {
            otherCarrySystem.SetCarried(true);
        }
        
        // 부모 설정 - 물리 계산 방해를 위해 부모 설정 제거
        otherOriginalParent = otherPlayer.transform.parent;
        // otherPlayer.transform.SetParent(transform); // 부모 설정 제거
        
        // 물리 설정 - P2를 kinematic으로 만들어 물리 영향 제거
        var otherRb = otherPlayer.GetComponent<Rigidbody2D>();
        if (otherRb != null)
        {
            otherRb.isKinematic = true;
            otherRb.linearVelocity = Vector2.zero; // 속도 초기화
        }
        
        // 애니메이션 설정
        if (animator != null)
        {
            animator.SetBool(carryBoolName, true);
            animator.SetBool(carryingBoolName, true);
        }
        
        // 기존 시스템과 동기화
        SyncCarryStateToLegacy(true);
        
        // 입력 잠금 제거 - P1이 캐리 중에도 자유롭게 움직이도록
        // if (carryLockCoroutine != null) StopCoroutine(carryLockCoroutine);
        // carryLockCoroutine = StartCoroutine(CarryLockCoroutine());
        
        // 쿨다운 설정
        nextCarryAllowedAt = Time.time + carryCooldown;
        
        Debug.Log($"[PlayerCarrySystem] {playerController.PlayerID} started carrying {otherPlayer.PlayerID}");
    }
    
    public void StopCarry()
    {
        if (!IsCarrying || otherPlayer == null) return;
        
        IsCarrying = false;
        
        // 다른 플레이어의 캐리 상태 해제
        var otherCarrySystem = otherPlayer.GetComponent<PlayerCarrySystem>();
        if (otherCarrySystem != null)
        {
            otherCarrySystem.SetCarried(false);
        }
        
        // 부모 복원 (이미 부모 설정을 안 했으므로 불필요)
        // otherPlayer.transform.SetParent(otherOriginalParent);
        
        // 물리 복원
        var otherRb = otherPlayer.GetComponent<Rigidbody2D>();
        if (otherRb != null)
        {
            otherRb.isKinematic = false;
            // 내려놓을 때 약간의 속도 초기화
            otherRb.linearVelocity = new Vector2(0f, -1f);
        }
        
        // 위치 조정 (앞쪽에 내려놓기)
        Vector3 dropPosition = transform.position + new Vector3(playerController.State.facingDirection * 0.4f, 0f, 0f);
        otherPlayer.transform.position = dropPosition;
        
        // 애니메이션 설정
        if (animator != null)
        {
            animator.SetBool(carryBoolName, false);
            animator.SetBool(carryingBoolName, false);
        }
        
        // 기존 시스템과 동기화
        SyncCarryStateToLegacy(false);
        
        // 쿨다운 설정
        nextCarryAllowedAt = Time.time + carryCooldown;
        
        Debug.Log($"[PlayerCarrySystem] {playerController.PlayerID} stopped carrying {otherPlayer.PlayerID}");
    }
    
    // 잠금과 함께 캐리 종료
    public void StopCarryWithLock()
    {
        if (!IsCarrying) return;
        
        // 입력 잠금 시작
        StartCarryEndLock(carryEndLockDuration);
        
        // 캐리 종료
        StopCarry();
    }
    
    public void SetCarried(bool carried)
    {
        IsCarried = carried;
        
        // 기존 시스템과 동기화
        if (legacyMovement != null)
        {
            legacyMovement.isCarried = carried;
        }
    }
    
    // === Throw Logic ===
    
    private void BeginThrowHold()
    {
        throwHoldActive = true;
        
        // 애니메이션 정지 (에임 상태)
        if (animator != null)
        {
            animator.speed = 0f;
        }
    }
    
    private void ExecuteThrow()
    {
        if (!IsCarrying || otherPlayer == null) return;
        
        throwHoldActive = false;
        
        // 애니메이션 재개
        if (animator != null)
        {
            animator.speed = 1f;
        }
        
        // 지연 던지기 시작
        if (delayedThrowCoroutine != null) StopCoroutine(delayedThrowCoroutine);
        delayedThrowCoroutine = StartCoroutine(DelayedThrowCoroutine());
    }
    
    // 잠금과 함께 던지기 실행
    private void ExecuteThrowWithLock()
    {
        if (!IsCarrying || otherPlayer == null) return;
        
        // 입력 잠금 시작
        StartCarryEndLock(throwEndLockDuration);
        
        // 던지기 실행
        ExecuteThrow();
    }
    
    private IEnumerator DelayedThrowCoroutine()
    {
        yield return new WaitForSeconds(throwDelay);
        
        if (!IsCarrying || otherPlayer == null) yield break;
        
        // 던지기 시작 위치 계산
        Vector3 throwStartPos = CalculateThrowStartPosition();
        
        // 캐리 해제
        StopCarry();
        
        // P2를 던지기 위치로 이동
        otherPlayer.transform.position = throwStartPos;
        
        // 던지기 속도 계산
        Vector2 throwVelocity = CalculateThrowVelocity();
        
        // P2에게 속도 적용
        var otherRb = otherPlayer.GetComponent<Rigidbody2D>();
        if (otherRb != null)
        {
            otherRb.linearVelocity = throwVelocity;
        }
        
        Debug.Log($"[PlayerCarrySystem] Threw {otherPlayer.PlayerID} with velocity {throwVelocity}");
    }
    
    private Vector3 CalculateThrowStartPosition()
    {
        int facingSign = playerController.State.facingDirection;
        
        if (throwAimUpLatched)
        {
            return transform.position + new Vector3(0f, throwStartLocalOffsetUp.y, 0f);
        }
        else
        {
            float xOffset = throwStartLocalOffset.x * facingSign;
            return transform.position + new Vector3(xOffset, throwStartLocalOffset.y, 0f);
        }
    }
    
    private Vector2 CalculateThrowVelocity()
    {
        int facingSign = playerController.State.facingDirection;
        
        if (throwAimUpLatched)
        {
            return new Vector2(0f, carryThrowUpSpeed);
        }
        else
        {
            return new Vector2(facingSign * carryThrowSideSpeed, carryThrowUpSpeed);
        }
    }
    
    // === Position Update ===
    
    private void UpdateCarryPosition()
    {
        if (!IsCarrying || otherPlayer == null) return;
        
        // P2를 P1 위에 위치시키기 - 부드럽게 이동
        Vector3 targetPosition = transform.position + new Vector3(0f, carryOffsetY, 0f);
        
        // 직접 위치 설정 대신 Rigidbody를 통해 이돔
        var otherRb = otherPlayer.GetComponent<Rigidbody2D>();
        if (otherRb != null && otherRb.isKinematic)
        {
            otherRb.MovePosition(targetPosition);
        }
        else
        {
            // kinematic이 아닌 경우 직접 설정
            otherPlayer.transform.position = targetPosition;
        }
    }
    
    // === Auto Catch ===
    
    private void HandleAutoCatch()
    {
        if (!autoCatchEnabled || IsCarrying || IsCarried) return;
        if (Time.time < nextAutoCatchAllowedAt) return;
        if (otherPlayer == null) return;
        
        // P2가 공중에 있고 P1 위에 있는지 체크
        if (ShouldAutoCatch())
        {
            StartCarry();
            nextAutoCatchAllowedAt = Time.time + autoCatchCooldown;
        }
    }
    
    private bool ShouldAutoCatch()
    {
        if (otherPlayer == null) return false;
        
        Vector2 p1Pos = transform.position;
        Vector2 p2Pos = otherPlayer.transform.position;
        
        // P2가 P1보다 위에 있는지
        if (p2Pos.y <= p1Pos.y + autoCatchMinHeightAbove) return false;
        
        // 가로 거리 체크
        float horizDist = Mathf.Abs(p2Pos.x - p1Pos.x);
        if (horizDist > autoCatchMaxHoriz) return false;
        
        // P2가 내려오는 중인지
        var otherRb = otherPlayer.GetComponent<Rigidbody2D>();
        if (otherRb != null && otherRb.linearVelocity.y > 0f) return false;
        
        return true;
    }
    
    // === Legacy System Sync ===
    
    private void SyncCarryStateToLegacy(bool carrying)
    {
        // 기존 시스템과의 충돌 방지를 위해 비활성화
        // 필요시에만 활성화
        
        /*
        if (legacyMovement == null) return;
        
        legacyMovement.isCarrying = carrying;
        
        if (otherPlayer != null)
        {
            var otherLegacy = otherPlayer.GetComponent<PlayerMouseMovement>();
            if (otherLegacy != null)
            {
                otherLegacy.isCarried = carrying;
            }
        }
        */
    }
    
    // === Carry End Lock ===
    
    private void StartCarryEndLock(float duration)
    {
        if (carryEndLockCoroutine != null) StopCoroutine(carryEndLockCoroutine);
        carryEndLockCoroutine = StartCoroutine(CarryEndLockCoroutine(duration));
    }
    
    private IEnumerator CarryEndLockCoroutine(float duration)
    {
        carryEndLockActive = true;
        
        // PlayerController를 통해 입력 잠금
        playerController.LockInput(duration);
        
        Debug.Log($"[PlayerCarrySystem] P1 movement locked for {duration} seconds during carry end");
        
        yield return new WaitForSeconds(duration);
        
        carryEndLockActive = false;
        playerController.UnlockInput();
        
        Debug.Log($"[PlayerCarrySystem] P1 movement unlocked after carry end");
    }
    
    // === Coroutines ===
    
    private IEnumerator CarryLockCoroutine()
    {
        // 입력 잠금을 아예 제거 - P1이 자유롭게 움직이도록
        // 애니메이션만 설정하고 입력은 잠글지 않음
        yield return null; // 빈 코루틴
    }
    
    // === Public Interface ===
    
    public bool CanCarry()
    {
        return CanStartCarry();
    }
    
    public PlayerController GetCarriedPlayer()
    {
        return IsCarrying ? otherPlayer : null;
    }
    
    public PlayerController GetCarryingPlayer()
    {
        if (!IsCarried) return null;
        
        var allPlayers = FindObjectsOfType<PlayerController>();
        foreach (var player in allPlayers)
        {
            var carrySystem = player.GetComponent<PlayerCarrySystem>();
            if (carrySystem != null && carrySystem.IsCarrying && carrySystem.GetCarriedPlayer() == playerController)
            {
                return player;
            }
        }
        
        return null;
    }
    
    // === Debug ===
    
    void OnDrawGizmosSelected()
    {
        // 캐리 범위 표시
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, carryRange);
        
        // 오토 캐치 범위 표시
        if (autoCatchEnabled)
        {
            Gizmos.color = Color.green;
            Vector3 boxCenter = transform.position + new Vector3(0f, autoCatchMinHeightAbove, 0f);
            Vector3 boxSize = new Vector3(autoCatchMaxHoriz * 2f, 1f, 0f);
            Gizmos.DrawWireCube(boxCenter, boxSize);
        }
    }
}
