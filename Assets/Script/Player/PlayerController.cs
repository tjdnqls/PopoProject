using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 플레이어 시스템의 중앙 제어 클래스
/// 모든 플레이어 모듈들을 관리하고 조정하는 역할
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("Player Configuration")]
    [SerializeField] private SwapController.PlayerChar playerID = SwapController.PlayerChar.P1;
    [SerializeField] private int p1MaxHP = 2;
    [SerializeField] private int p2MaxHP = 1;

    [Header("Component References")]
    public Rigidbody2D rb;
    public Animator animator;
    public Collider2D bodyCollider;

    [Header("Other Player Reference")]
    public PlayerController otherPlayer;

    // === State Management ===
    [SerializeField] private PlayerState playerState = new PlayerState();
    public PlayerState State => playerState;

    // === Module References ===
    private Dictionary<System.Type, MonoBehaviour> modules = new Dictionary<System.Type, MonoBehaviour>();

    // === Swap Controller Reference ===
    public SwapController swap;

    // === Properties ===
    public SwapController.PlayerChar PlayerID => playerID;
    public bool IsSelected 
    {
        get
        {
            if (!swap) 
            {
                bool defaultSelected = playerID == SwapController.PlayerChar.P1;
                // Debug.Log($"[{playerID}] No SwapController, default selected: {defaultSelected}");
                return defaultSelected;
            }
            bool selected = swap.charSelect == playerID;
            // Debug.Log($"[{playerID}] SwapController.charSelect={swap.charSelect}, selected: {selected}");
            return selected;
        }
    }
    public Vector3 LastSafePosition { get; private set; }

    // === Unity Lifecycle ===
    
    void Awake()
    {
        InitializeComponents();
        InitializeState();
        RegisterModules();
        SetupEventListeners();
    }

    void Start()
    {
        TryResolveSwapController();
        TryResolveOtherPlayer();
        
        // 게임 시작 시 입력 잠금 강제 해제
        ForceUnlockInput();
        SetLastSafePosition(transform.position);
    }

    void OnEnable()
    {
        SetupEventListeners();
    }

    void Update()
    {
        // 디버깅: U 키로 입력 잠금 해제
        if (Input.GetKeyDown(KeyCode.U))
        {
            ForceUnlockInput();
        }
    }

    void OnDisable()
    {
        RemoveEventListeners();
    }

    void OnDestroy()
    {
        PlayerEvents.ClearAllEvents();
    }

    // === Initialization ===
    
    private void InitializeComponents()
    {
        // Auto-find components if not assigned
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!animator) animator = GetComponent<Animator>();
        if (!bodyCollider) 
        {
            bodyCollider = GetComponent<Collider2D>();
            if (!bodyCollider) bodyCollider = GetComponentInChildren<Collider2D>();
        }

        // Ensure we have required components
        if (!rb)
        {
            Debug.LogError($"[PlayerController] Rigidbody2D component not found on {gameObject.name}! Please add a Rigidbody2D component.");
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        
        if (!bodyCollider)
        {
            Debug.LogError($"[PlayerController] Collider2D component not found on {gameObject.name}! Please add a Collider2D component.");
        }

        // Configure Rigidbody2D
        if (rb)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
        
        // 플레이어 간 충돌 방지 설정
        SetupPlayerCollisionIgnore();

        // Enable trigger queries
        Physics2D.queriesHitTriggers = true;
    }

    private void InitializeState()
    {
        // Set max HP based on player ID
        int maxHP = playerID == SwapController.PlayerChar.P1 ? p1MaxHP : p2MaxHP;
        playerState.maxHP = maxHP;
        playerState.currentHP = maxHP;
        
        // Initialize facing direction
        playerState.facingDirection = 1;
        
        // Initialize last grounded time
        playerState.lastGroundedTime = Time.time;
        
        // 입력 상태 초기화 (입력 잠금 해제)
        playerState.inputLocked = false;
        playerState.inputLockUntil = -999f;
        playerState.isDead = false;
        
        Debug.Log($"[{playerID}] State initialized: inputLocked={playerState.inputLocked}, isDead={playerState.isDead}");
    }

    private void RegisterModules()
    {
        // This will be expanded as we add more modules
        // For now, we'll register any existing modules
        var allModules = GetComponents<MonoBehaviour>();
        foreach (var module in allModules)
        {
            if (module != this && module != null)
            {
                modules[module.GetType()] = module;
            }
        }
    }

    private void SetupEventListeners()
    {
        // Listen to important events for coordination
        PlayerEvents.OnPlayerDied += HandlePlayerDeath;
        PlayerEvents.OnPlayerRevived += HandlePlayerRevive;
        PlayerEvents.OnHealthChanged += HandleHealthChanged;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnPlayerDied -= HandlePlayerDeath;
        PlayerEvents.OnPlayerRevived -= HandlePlayerRevive;
        PlayerEvents.OnHealthChanged -= HandleHealthChanged;
    }

    // === Module Management ===
    
    public T GetModule<T>() where T : MonoBehaviour
    {
        if (modules.TryGetValue(typeof(T), out MonoBehaviour module))
        {
            return module as T;
        }
        return null;
    }

    public void RegisterModule<T>(T module) where T : MonoBehaviour
    {
        modules[typeof(T)] = module;
    }

    public void UnregisterModule<T>() where T : MonoBehaviour
    {
        modules.Remove(typeof(T));
    }

    // === Collision Setup ===
    
    private void SetupPlayerCollisionIgnore()
    {
        // 다른 플레이어와의 충돌 방지
        StartCoroutine(SetupCollisionIgnoreDelayed());
    }
    
    private System.Collections.IEnumerator SetupCollisionIgnoreDelayed()
    {
        // 다른 PlayerController가 초기화될 때까지 대기
        yield return new WaitForSeconds(0.1f);
        
        var allPlayers = FindObjectsOfType<PlayerController>();
        foreach (var otherPlayer in allPlayers)
        {
            if (otherPlayer != this && otherPlayer.bodyCollider && bodyCollider)
            {
                // 플레이어 간 충돌 무시
                Physics2D.IgnoreCollision(bodyCollider, otherPlayer.bodyCollider, true);
                Debug.Log($"[{playerID}] Ignoring collision with {otherPlayer.playerID}");
            }
        }
    }
    
    // === External References ===
    
    private void TryResolveSwapController()
    {
        if (swap != null) return;
        
        // Tag로 찾기
        var swapObject = GameObject.FindWithTag("Swap");
        if (swapObject != null)
        {
            swap = swapObject.GetComponent<SwapController>();
            if (swap == null)
            {
                Debug.LogWarning($"[PlayerController] Tag 'Swap' object found but no SwapController component.", swapObject);
            }
        }
        else
        {
            // Tag로 못 찾으면 타입으로 찾기
            swap = FindObjectOfType<SwapController>();
            if (swap == null)
            {
                Debug.LogWarning("[PlayerController] No SwapController found in scene.");
            }
        }
        
        // SwapController에 이 PlayerController 등록
        if (swap != null)
        {
            if (playerID == SwapController.PlayerChar.P1)
            {
                swap.p1Controller = this;
            }
            else if (playerID == SwapController.PlayerChar.P2)
            {
                swap.p2Controller = this;
            }
        }
    }

    private void TryResolveOtherPlayer()
    {
        if (otherPlayer != null) return;

        var allPlayers = FindObjectsOfType<PlayerController>();
        foreach (var player in allPlayers)
        {
            if (player != this && player.playerID != this.playerID)
            {
                otherPlayer = player;
                break;
            }
        }

        if (otherPlayer == null)
        {
            Debug.LogWarning("[PlayerController] Could not find other player in scene.");
        }
    }

    // === State Management ===
    
    public void SetLastSafePosition(Vector3 position)
    {
        LastSafePosition = position;
    }

    public void TeleportToSafePosition()
    {
        if (rb)
        {
            rb.position = LastSafePosition;
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            transform.position = LastSafePosition;
        }
    }

    // === Event Handlers ===
    
    private void HandlePlayerDeath()
    {
        Debug.Log($"[PlayerController] Player {playerID} died.");
        // Handle death logic here (respawn, game over, etc.)
    }

    private void HandlePlayerRevive()
    {
        Debug.Log($"[PlayerController] Player {playerID} revived.");
        // Handle revive logic here
    }

    private void HandleHealthChanged(int newHealth)
    {
        Debug.Log($"[PlayerController] Player {playerID} health changed to {newHealth}");
    }

    // === Utility Methods ===
    
    public void ForceFaceDirection(int direction)
    {
        direction = direction >= 0 ? 1 : -1;
        var localScale = transform.localScale;
        transform.localScale = new Vector3(Mathf.Abs(localScale.x) * direction, localScale.y, localScale.z);
        playerState.facingDirection = direction;
    }

    public void LockInput(float duration)
    {
        playerState.SetInputLocked(true, duration);
    }

    public void UnlockInput()
    {
        playerState.SetInputLocked(false);
    }
    
    public void ForceUnlockInput()
    {
        playerState.inputLocked = false;
        playerState.inputLockUntil = -999f;
        Debug.Log($"[{playerID}] Input forcefully unlocked!");
    }

    // === Debug ===
    
    [ContextMenu("Log Player State")]
    public void LogPlayerState()
    {
        playerState.LogState();
    }

    void OnValidate()
    {
        // Ensure player ID is valid
        if (playerID != SwapController.PlayerChar.P1 && playerID != SwapController.PlayerChar.P2)
        {
            playerID = SwapController.PlayerChar.P1;
        }
    }
}
