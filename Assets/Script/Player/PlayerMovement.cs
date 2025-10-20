using UnityEngine;

/// <summary>
/// 플레이어의 기본 이동 및 점프 시스템을 처리하는 클래스
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 9.5f;
    [SerializeField] private float acceleration = 180f;
    [SerializeField] private float deceleration = 220f;
    [SerializeField] private float airAcceleration = 130f;
    [SerializeField] private float airDeceleration = 150f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpVelocity = 16f;
    [SerializeField] private int p1ExtraAirJumps = 1; // P1만 더블점프 가능
    [SerializeField] private int p2ExtraAirJumps = 0; // P2는 더블점프 불가
    [SerializeField] private float coyoteTime = 0.15f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [SerializeField] private float cutJumpFactor = 0.5f;
    [SerializeField] private float minJumpHoldTime = 0.1f;

    [Header("Gravity Settings")]
    [SerializeField] private float gravityScaleNormal = 3.2f;
    [SerializeField] private float gravityScaleFall = 5.0f;
    [SerializeField] private float maxFallSpeed = -28f;
    [SerializeField] private float apexThreshold = 0.8f;
    [SerializeField] private float apexHangMultiplier = 0.7f;
    [SerializeField] private float gravitySmoothTime = 0.06f;

    [Header("Dive Settings")]
    [SerializeField] private float diveSpeed = -36f;
    [SerializeField] private float diveGravityScale = 7.5f;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;
    private PlayerInputHandler inputHandler;
    private PlayerGroundDetection groundDetection;
    private Rigidbody2D rb;

    // === Movement State ===
    private float gravitySmoothVel = 0f;
    private bool didCutThisJump = false;
    private float lastJumpStartTime = -999f;
    private int airJumpsLeft = 0;

    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
        rb = playerController.rb;
    }

    void Start()
    {
        playerController.RegisterModule(this);
        inputHandler = playerController.GetModule<PlayerInputHandler>();
        groundDetection = playerController.GetModule<PlayerGroundDetection>();
        
        SetupEventListeners();
        InitializeMovement();
    }

    void FixedUpdate()
    {
        if (playerState.isDead) return;
        if (!rb) return; // Safety check

        // 선택된 플레이어만 이동 업데이트
        if (playerController.IsSelected)
        {
            UpdateMovement();
            UpdateJump();
            UpdateGravity();
            UpdateDive();
        }
        else
        {
            // 비선택 플레이어는 중력만 적용
            UpdateGravity();
        }
        
        // Apply final velocity
        playerState.SetVelocity(rb.linearVelocity);
    }

    void OnDestroy()
    {
        RemoveEventListeners();
    }

    // === Initialization ===
    
    private void InitializeMovement()
    {
        // 플레이어별 에어 점프 횟수 설정
        int maxAirJumps = playerController.PlayerID == SwapController.PlayerChar.P1 
            ? p1ExtraAirJumps 
            : p2ExtraAirJumps;
            
        airJumpsLeft = maxAirJumps;
        playerState.airJumpsLeft = airJumpsLeft;
        
        Debug.Log($"[{playerController.PlayerID}] Air jumps initialized: {airJumpsLeft}");
    }

    private void SetupEventListeners()
    {
        PlayerEvents.OnGroundedChanged += HandleGroundedChanged;
        PlayerEvents.OnLanded += HandleLanded;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnGroundedChanged -= HandleGroundedChanged;
        PlayerEvents.OnLanded -= HandleLanded;
    }

    // === Movement Update ===
    
    private void UpdateMovement()
    {
        if (!playerState.CanMove()) return;
        if (!inputHandler) return;
        if (!rb) return; // Safety check

        Vector2 input = inputHandler.GetMovementInput();
        float targetVelocityX = input.x * moveSpeed;
        
        // Choose acceleration based on grounded state and input direction
        float accel = GetCurrentAcceleration(targetVelocityX);
        
        // Apply movement
        Vector2 velocity = rb.linearVelocity;
        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocityX, accel * Time.fixedDeltaTime);
        rb.linearVelocity = velocity;

        // Update facing direction
        if (Mathf.Abs(input.x) > 0.01f)
        {
            playerController.ForceFaceDirection(input.x > 0 ? 1 : -1);
        }
    }

    private float GetCurrentAcceleration(float targetVelocityX)
    {
        if (!rb) return acceleration; // Safety fallback
        
        bool grounded = playerState.isGrounded;
        float currentVelocityX = rb.linearVelocity.x;
        
        if (grounded)
        {
            // Ground acceleration/deceleration
            return Mathf.Sign(targetVelocityX) == Mathf.Sign(currentVelocityX) ? acceleration : deceleration;
        }
        else
        {
            // Air acceleration/deceleration
            return Mathf.Abs(targetVelocityX) > Mathf.Abs(currentVelocityX) ? airAcceleration : airDeceleration;
        }
    }

    // === Jump System ===
    
    private void UpdateJump()
    {
        if (!playerState.CanJump()) return;
        if (!inputHandler) return;

        // Check for jump input
        bool jumpPressed = inputHandler.GetJumpPressed() || inputHandler.IsJumpBuffered();
        bool jumpHeld = inputHandler.GetJumpHeld();

        // Attempt jump
        if (jumpPressed && CanPerformJump())
        {
            PerformJump();
            inputHandler.ConsumeJumpInput();
        }

        // Handle jump cut (variable height)
        HandleJumpCut(jumpHeld);
    }

    private bool CanPerformJump()
    {
        // 지면에 있으면 점프 가능
        if (playerState.isGrounded) return true;
        
        // 코요테 타임 내에 있으면 점프 가능
        if (IsInCoyoteTime()) return true;
        
        // 공중에서는 에어 점프 횟수가 남아있어야 점프 가능
        if (airJumpsLeft > 0)
        {
            Debug.Log($"[{playerController.PlayerID}] Can air jump: {airJumpsLeft} left");
            return true;
        }
        
        Debug.Log($"[{playerController.PlayerID}] Cannot jump: grounded={playerState.isGrounded}, coyote={IsInCoyoteTime()}, airJumps={airJumpsLeft}");
        return false;
    }
    
    private bool IsInCoyoteTime()
    {
        return !playerState.isGrounded && 
               (Time.time - playerState.lastGroundedTime) <= coyoteTime;
    }

    private void PerformJump()
    {
        if (!rb) return; // Safety check
        
        // 점프 타입 결정
        bool isGroundJump = playerState.isGrounded;
        bool isCoyoteJump = IsInCoyoteTime();
        bool isAirJump = !isGroundJump && !isCoyoteJump;
        
        // 에어 점프인데 횟수가 없으면 점프 불가
        if (isAirJump && airJumpsLeft <= 0)
        {
            return;
        }
        
        // 점프 실행
        Vector2 velocity = rb.linearVelocity;
        velocity.y = jumpVelocity;
        rb.linearVelocity = velocity;

        // Track jump state
        lastJumpStartTime = Time.time;
        didCutThisJump = false;
        playerState.Jump();

        // 에어 점프일 때 횟수 차감
        if (isAirJump)
        {
            airJumpsLeft = Mathf.Max(0, airJumpsLeft - 1);
            playerState.airJumpsLeft = airJumpsLeft;
            
            Debug.Log($"[{playerController.PlayerID}] Air jump used! Remaining: {airJumpsLeft}");
            
            // Play double jump effect
            PlayDoubleJumpEffect();
        }
        else
        {
            Debug.Log($"[{playerController.PlayerID}] Ground/Coyote jump performed");
        }

        // Reset ground timing
        playerState.lastGroundedTime = -999f;
        
        // Ignore ground briefly after jump
        if (groundDetection)
        {
            groundDetection.SetIgnoreGroundUntil(Time.time + 0.06f);
        }
    }

    private void HandleJumpCut(bool jumpHeld)
    {
        if (!rb) return; // Safety check
        
        // Only cut jump if we're still in the jump and haven't cut it yet
        if (didCutThisJump) return;
        if (Time.time - lastJumpStartTime < minJumpHoldTime) return;
        if (rb.linearVelocity.y <= 0f) return;

        // Cut jump if not holding jump button
        if (!jumpHeld)
        {
            Vector2 velocity = rb.linearVelocity;
            velocity.y *= cutJumpFactor;
            rb.linearVelocity = velocity;
            didCutThisJump = true;
        }
    }

    private void PlayDoubleJumpEffect()
    {
        // Play double jump sound
        PlayerEvents.TriggerSoundRequested("DoubleJump");
        
        // Play double jump visual effect
        if (playerController.bodyCollider)
        {
            var bounds = playerController.bodyCollider.bounds;
            Vector3 feetPosition = new Vector3(bounds.center.x, bounds.min.y, transform.position.z);
            FX.Play("doubleJumpe", feetPosition + Vector3.down * 0.06f, 10f);
        }
    }

    // === Gravity System ===
    
    private void UpdateGravity()
    {
        if (!rb) return; // Safety check
        if (playerState.isDiving) return; // Dive handles its own gravity
        
        Vector2 velocity = rb.linearVelocity;
        
        // Determine desired gravity scale
        float desiredGravity = velocity.y < -0.01f ? gravityScaleFall : gravityScaleNormal;
        
        // Apply apex hang effect
        if (!playerState.isGrounded && Mathf.Abs(velocity.y) <= apexThreshold)
        {
            desiredGravity = Mathf.Min(desiredGravity, gravityScaleNormal * apexHangMultiplier);
        }
        
        // Smooth gravity transition
        rb.gravityScale = Mathf.SmoothDamp(rb.gravityScale, desiredGravity, ref gravitySmoothVel, gravitySmoothTime);
        
        // Clamp fall speed
        if (velocity.y < maxFallSpeed)
        {
            velocity.y = maxFallSpeed;
            rb.linearVelocity = velocity;
        }
    }

    // === Dive System ===
    
    private void UpdateDive()
    {
        if (!inputHandler) return;
        if (!rb) return; // Safety check

        // Start dive
        if (inputHandler.GetDivePressed() && !playerState.isGrounded && !playerState.isDiving)
        {
            StartDive();
        }

        // Handle dive physics
        if (playerState.isDiving && !playerState.isGrounded)
        {
            rb.gravityScale = diveGravityScale;
            Vector2 velocity = rb.linearVelocity;
            velocity.y = Mathf.Min(velocity.y, diveSpeed);
            rb.linearVelocity = velocity;
        }
    }

    private void StartDive()
    {
        if (!rb) return; // Safety check
        
        playerState.isDiving = true;
        Vector2 velocity = rb.linearVelocity;
        velocity.y = diveSpeed;
        rb.linearVelocity = velocity;
    }

    // === Event Handlers ===
    
    private void HandleGroundedChanged(bool grounded)
    {
        if (grounded)
        {
            // 착지 시 에어 점프 리셋 (플레이어별)
            int maxAirJumps = playerController.PlayerID == SwapController.PlayerChar.P1 
                ? p1ExtraAirJumps 
                : p2ExtraAirJumps;
                
            airJumpsLeft = maxAirJumps;
            playerState.airJumpsLeft = airJumpsLeft;
            
            Debug.Log($"[{playerController.PlayerID}] Landed! Air jumps reset to: {airJumpsLeft}");
            
            // Stop diving
            if (playerState.isDiving)
            {
                playerState.isDiving = false;
            }
        }
    }

    private void HandleLanded()
    {
        // Reset jump state
        didCutThisJump = false;
        playerState.isJumping = false;
    }

    // === Public Interface ===
    
    public void SetMoveSpeed(float speed)
    {
        moveSpeed = speed;
    }

    public void SetJumpVelocity(float velocity)
    {
        jumpVelocity = velocity;
    }

    public void AddVelocity(Vector2 additionalVelocity)
    {
        if (!rb) return;
        rb.linearVelocity += additionalVelocity;
    }

    public void SetVelocity(Vector2 newVelocity)
    {
        if (!rb) return;
        rb.linearVelocity = newVelocity;
    }

    public Vector2 GetVelocity()
    {
        return rb ? rb.linearVelocity : Vector2.zero;
    }

    // === Debug ===
    
    void OnGUI()
    {
        if (!Debug.isDebugBuild) return;
        if (!playerController.IsSelected) return;
        if (!rb) return;

        GUILayout.BeginArea(new Rect(220, 10, 200, 150));
        GUILayout.Label($"Player {playerController.PlayerID} Movement:");
        GUILayout.Label($"Velocity: {rb.linearVelocity:F2}");
        GUILayout.Label($"Grounded: {playerState.isGrounded}");
        int maxAirJumps = playerController.PlayerID == SwapController.PlayerChar.P1 
            ? p1ExtraAirJumps 
            : p2ExtraAirJumps;
        GUILayout.Label($"Air Jumps: {airJumpsLeft}/{maxAirJumps}");
        GUILayout.Label($"Diving: {playerState.isDiving}");
        GUILayout.Label($"Gravity Scale: {rb.gravityScale:F2}");
        GUILayout.EndArea();
    }
}
