# PlayerMouseMovement 모듈화 시스템 사용 가이드

## 개요

기존의 거대한 `PlayerMouseMovement.cs` 파일을 여러 개의 모듈로 분리하여 유지보수성과 확장성을 향상시킨 시스템입니다.

## 모듈 구조

### 1. 핵심 시스템
- **PlayerController.cs** - 중앙 제어 시스템
- **PlayerState.cs** - 상태 관리
- **PlayerEvents.cs** - 이벤트 시스템

### 2. 입력 및 물리
- **PlayerInputHandler.cs** - 입력 처리
- **PlayerMovement.cs** - 이동 및 점프
- **PlayerGroundDetection.cs** - 지면 감지

### 3. 고급 기능
- **PlayerWallInteraction.cs** - 벽 상호작용 (슬라임, 천장 붙기)
- **PlayerCarrySystem.cs** - 캐리 및 던지기 시스템
- **PlayerCombat.cs** - 전투 시스템

### 4. 보조 시스템
- **PlayerAnimationController.cs** - 애니메이션 관리
- **PlayerAudioController.cs** - 오디오 관리

## 설정 방법

### 1. 기본 설정

1. 기존 `PlayerMouseMovement` 컴포넌트를 제거합니다.
2. `PlayerController`를 플레이어 GameObject에 추가합니다.
3. 필요한 모듈들을 같은 GameObject에 추가합니다.

```csharp
// 필수 모듈
- PlayerController
- PlayerInputHandler
- PlayerMovement
- PlayerGroundDetection

// 선택적 모듈 (필요에 따라)
- PlayerWallInteraction
- PlayerCarrySystem
- PlayerCombat
- PlayerAnimationController
- PlayerAudioController
```

### 2. 컴포넌트 참조 설정

PlayerController에서 다음 참조들을 설정해주세요:

```csharp
public class PlayerController : MonoBehaviour
{
    [Header("Component References")]
    public Rigidbody2D rb;              // 자동 탐지됨
    public Animator animator;           // 자동 탐지됨
    public Collider2D bodyCollider;     // 자동 탐지됨
    
    [Header("Other Player Reference")]
    public PlayerController otherPlayer; // 수동 설정 필요
    
    public SwapController swap;         // 자동 탐지됨
}
```

### 3. 레이어 마스크 설정

각 모듈에서 필요한 레이어 마스크들을 설정해주세요:

- **PlayerGroundDetection**: groundMask, eventMask
- **PlayerWallInteraction**: slimeLayerMask
- **PlayerCarrySystem**: 특별한 레이어 설정 불필요

## 이벤트 시스템 사용법

### 이벤트 구독

```csharp
void Start()
{
    // 이벤트 구독
    PlayerEvents.OnJumped += HandleJumped;
    PlayerEvents.OnLanded += HandleLanded;
    PlayerEvents.OnCarryStateChanged += HandleCarryChanged;
}

void OnDestroy()
{
    // 이벤트 구독 해제
    PlayerEvents.OnJumped -= HandleJumped;
    PlayerEvents.OnLanded -= HandleLanded;
    PlayerEvents.OnCarryStateChanged -= HandleCarryChanged;
}
```

### 이벤트 발생

```csharp
// 이벤트 발생
PlayerEvents.TriggerJumped();
PlayerEvents.TriggerCarryStateChanged(true);
PlayerEvents.TriggerSoundRequested("JumpSound");
```

## 상태 관리 사용법

### 상태 읽기

```csharp
PlayerState state = playerController.State;

bool isGrounded = state.isGrounded;
bool isCarrying = state.isCarrying;
Vector2 velocity = state.velocity;
int health = state.currentHP;
```

### 상태 변경

```csharp
// 상태 변경 (이벤트 자동 발생)
state.SetGrounded(true);
state.SetCarrying(false);
state.SetHealth(3);
state.Jump();
```

## 모듈 간 통신

### 다른 모듈 참조

```csharp
public class MyCustomModule : MonoBehaviour
{
    private PlayerController playerController;
    private PlayerMovement movement;
    private PlayerInputHandler inputHandler;
    
    void Start()
    {
        playerController = GetComponent<PlayerController>();
        
        // 다른 모듈 참조
        movement = playerController.GetModule<PlayerMovement>();
        inputHandler = playerController.GetModule<PlayerInputHandler>();
    }
}
```

### 모듈 등록

```csharp
void Start()
{
    playerController.RegisterModule(this);
}
```

## 커스텀 모듈 추가

### 1. 새 모듈 클래스 생성

```csharp
[RequireComponent(typeof(PlayerController))]
public class MyCustomPlayerModule : MonoBehaviour
{
    private PlayerController playerController;
    private PlayerState playerState;
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
    }
    
    void Start()
    {
        playerController.RegisterModule(this);
        SetupEventListeners();
    }
    
    private void SetupEventListeners()
    {
        PlayerEvents.OnJumped += HandleJumped;
    }
    
    private void HandleJumped()
    {
        // 커스텀 로직
    }
}
```

### 2. 이벤트 추가 (필요시)

PlayerEvents.cs에 새 이벤트 추가:

```csharp
public static event Action<float> OnCustomEvent;
public static void TriggerCustomEvent(float value) => OnCustomEvent?.Invoke(value);
```

## 성능 최적화 팁

### 1. 이벤트 사용 최적화
- 불필요한 이벤트 구독 피하기
- OnDestroy에서 반드시 구독 해제하기
- 자주 발생하는 이벤트는 최소한으로 사용

### 2. 모듈 비활성화
```csharp
// 불필요한 모듈 비활성화
GetComponent<PlayerWallInteraction>().enabled = false;
```

### 3. 조건부 업데이트
```csharp
void Update()
{
    if (!playerController.IsSelected) return; // 선택된 플레이어만 업데이트
    
    // 업데이트 로직
}
```

## 디버깅 가이드

### 1. 상태 확인
```csharp
[ContextMenu("Log Player State")]
public void LogPlayerState()
{
    playerController.State.LogState();
}
```

### 2. 이벤트 로깅
```csharp
void Start()
{
    PlayerEvents.OnJumped += () => Debug.Log("Player jumped!");
}
```

### 3. GUI 디버그 정보
각 모듈은 OnGUI()에서 디버그 정보를 표시합니다. Debug 빌드에서만 활성화됩니다.

## 마이그레이션 가이드

### 기존 코드에서 새 시스템으로

#### Before (기존):
```csharp
PlayerMouseMovement player = GetComponent<PlayerMouseMovement>();
bool isGrounded = player.IsGrounded();
player.Jump();
```

#### After (새 시스템):
```csharp
PlayerController player = GetComponent<PlayerController>();
bool isGrounded = player.State.isGrounded;
PlayerEvents.TriggerJumped();
```

## 문제 해결

### 자주 발생하는 문제들

1. **모듈이 서로를 찾지 못함**
   - PlayerController.RegisterModule()이 호출되었는지 확인
   - Start()에서 GetModule()을 호출하는지 확인

2. **이벤트가 발생하지 않음**
   - 이벤트 구독이 올바른지 확인
   - PlayerEvents.Trigger...() 메서드 사용 확인

3. **애니메이션이 작동하지 않음**
   - Animator 컴포넌트가 있는지 확인
   - 애니메이션 파라미터 이름이 일치하는지 확인

4. **사운드가 재생되지 않음**
   - SoundManager가 씬에 있는지 확인
   - 사운드 파일 이름이 올바른지 확인

## 확장 예제

### 새로운 능력 추가 (예: 대시)

```csharp
[RequireComponent(typeof(PlayerController))]
public class PlayerDashSystem : MonoBehaviour
{
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1f;
    
    private PlayerController playerController;
    private PlayerInputHandler inputHandler;
    private float nextDashTime = 0f;
    
    void Start()
    {
        playerController = GetComponent<PlayerController>();
        inputHandler = playerController.GetModule<PlayerInputHandler>();
        playerController.RegisterModule(this);
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift) && CanDash())
        {
            StartDash();
        }
    }
    
    private bool CanDash()
    {
        return Time.time >= nextDashTime && 
               playerController.State.IsInputAllowed();
    }
    
    private void StartDash()
    {
        // 대시 로직 구현
        nextDashTime = Time.time + dashCooldown;
        PlayerEvents.TriggerSoundRequested("DashSound");
    }
}
```

이 가이드를 참고하여 모듈화된 플레이어 시스템을 효과적으로 사용하고 확장할 수 있습니다.
