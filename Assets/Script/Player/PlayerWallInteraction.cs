using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어의 벽 상호작용 시스템 (슬라임 벽, 천장 붙기, 벽 점프 등)
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerWallInteraction : MonoBehaviour
{
    [Header("Wall Detection")]
    [SerializeField] private LayerMask slimeLayerMask = -1;
    [SerializeField] private float wallCheckDistance = 0.18f;
    [SerializeField] private PhysicsMaterial2D slimeNoFrictionMat;

    [Header("Wall Slide")]
    [SerializeField] private float wallSlideMaxFall = -5.5f;
    [SerializeField] private float wallSlideMaxFallCarrying = -12f;
    [SerializeField] private float slimeStickPush = 22f;
    [SerializeField] private float slimeNormalClamp = 20f;

    [Header("Wall Jump")]
    [SerializeField] private float wallJumpHorizontal = 9.0f;
    [SerializeField] private float wallJumpVertical = 11.5f;
    [SerializeField] private float wallOppositeInputLock = 0.5f;
    [SerializeField] private float wallRegrabBlock = 0.30f;
    [SerializeField] private bool resetAirJumpsOnWallJump = true;

    [Header("Ceiling Stick")]
    [SerializeField] private bool enableCeilingSlime = true;
    [SerializeField] private float ceilingStickMaxTime = 5f;
    [SerializeField] private float ceilingReleaseFade = 0.6f;
    [SerializeField] private float ceilingKeepGap = 0.02f;
    [SerializeField] private float ceilingRestickBlock = 0.25f;
    [SerializeField] private float headCheckDistance = 0.12f;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;
    private PlayerInputHandler inputHandler;
    private PlayerMovement movement;
    private Rigidbody2D rb;
    private Collider2D bodyCollider;

    // === Wall State ===
    private bool touchingLeftSlime = false;
    private bool touchingRightSlime = false;
    private bool touchL_byCollision = false, touchR_byCollision = false;
    private bool touchL_byTrigger = false, touchR_byTrigger = false;
    private float lastSlimeTouchAt = -999f;
    private int lastSlimeSide = 0; // -1 = left, +1 = right
    private float slimeStickAfterLeave = 0.3f;

    // === Wall Jump State ===
    private float oppositeInputLockUntil = -1f;
    private int oppositeInputLockedDir = 0;
    private float wallRegrabUntil = -1f;
    private int wallRegrabSide = 0;

    // === Ceiling State ===
    private bool stickingToCeiling = false;
    private float ceilingStickStartTime = -1f;
    private float ceilingReleaseUntil = -1f;
    private float ignoreCeilingUntil = -1f;
    private float lastCeilingY = 0f;

    // === Friction State ===
    private PhysicsMaterial2D originalMaterial;
    private bool appliedNoFriction = false;

    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
        rb = playerController.rb;
        bodyCollider = playerController.bodyCollider;
        
        originalMaterial = bodyCollider ? bodyCollider.sharedMaterial : null;
    }

    void Start()
    {
        playerController.RegisterModule(this);
        inputHandler = playerController.GetModule<PlayerInputHandler>();
        movement = playerController.GetModule<PlayerMovement>();
        
        SetupEventListeners();
    }

    void FixedUpdate()
    {
        UpdateWallDetection();
        UpdateWallInteraction();
        UpdateCeilingStick();
        UpdateFriction();
    }

    void OnDestroy()
    {
        RemoveEventListeners();
    }

    // === Event Setup ===
    
    private void SetupEventListeners()
    {
        PlayerEvents.OnJumped += HandleJumped;
        PlayerEvents.OnLanded += HandleLanded;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnJumped -= HandleJumped;
        PlayerEvents.OnLanded -= HandleLanded;
    }

    // === Wall Detection ===
    
    private void UpdateWallDetection()
    {
        // Update collision-based detection
        touchingLeftSlime = touchL_byCollision || touchL_byTrigger;
        touchingRightSlime = touchR_byCollision || touchR_byTrigger;

        // Additional cast-based detection
        bool leftCast = TouchingSlimeSideCast(-1);
        bool rightCast = TouchingSlimeSideCast(1);

        touchingLeftSlime = touchingLeftSlime || leftCast;
        touchingRightSlime = touchingRightSlime || rightCast;

        // Update slime touch timing
        if (touchingLeftSlime || touchingRightSlime)
        {
            lastSlimeTouchAt = Time.time;
            lastSlimeSide = touchingLeftSlime ? -1 : 1;
        }

        // Grace period for wall interaction after leaving wall
        bool inGracePeriod = (Time.time - lastSlimeTouchAt) <= slimeStickAfterLeave;
        bool canUseGrace = !playerState.isGrounded && inGracePeriod;

        // Update state
        bool wasWallContact = playerState.touchingLeftWall || playerState.touchingRightWall;
        playerState.touchingLeftWall = touchingLeftSlime || (canUseGrace && lastSlimeSide == -1);
        playerState.touchingRightWall = touchingRightSlime || (canUseGrace && lastSlimeSide == 1);

        // Trigger events if wall contact changed
        bool nowWallContact = playerState.touchingLeftWall || playerState.touchingRightWall;
        if (wasWallContact != nowWallContact)
        {
            PlayerEvents.TriggerWallContactChanged(nowWallContact);
        }
    }

    private bool TouchingSlimeSideCast(int sign)
    {
        if (!bodyCollider) return false;

        Vector2 direction = (sign < 0) ? Vector2.left : Vector2.right;

        // Cast-based detection
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = slimeLayerMask,
            useTriggers = true
        };

        RaycastHit2D[] hits = new RaycastHit2D[2];
        int count = bodyCollider.Cast(direction, filter, hits, 0.03f);
        if (count > 0) return true;

        // Box-based detection as backup
        Bounds bounds = bodyCollider.bounds;
        float padX = 0.04f;
        Vector2 size = new Vector2(0.12f, bounds.size.y * 0.8f);
        Vector2 center = (Vector2)bounds.center + new Vector2(sign * (bounds.extents.x + size.x * 0.5f + padX), 0f);

        bool boxHit = Physics2D.OverlapBox(center, size, 0f, slimeLayerMask);

        return boxHit;
    }

    // === Wall Interaction ===
    
    private void UpdateWallInteraction()
    {
        if (playerState.isDead) return;

        bool onWall = !playerState.isGrounded && (playerState.touchingLeftWall || playerState.touchingRightWall);
        bool allowStick = !IsSlimeSuppressed() && !(playerState.isCarrying || playerState.isCarried);

        if (allowStick && onWall)
        {
            HandleWallStick();
            HandleWallSlide();
            HandleWallJump();
        }
    }

    private void HandleWallStick()
    {
        // Prevent horizontal input into wall
        bool pressingIntoWall = 
            (playerState.touchingLeftWall && playerState.horizontalInput < -0.01f) ||
            (playerState.touchingRightWall && playerState.horizontalInput > 0.01f);

        if (pressingIntoWall)
        {
            // Stop horizontal movement when pressing into wall
            Vector2 velocity = rb.linearVelocity;
            if (velocity.y > 0f) velocity.y = 0f; // Also stop upward movement
            
            // Apply wall stick force
            Vector2 wallNormal = playerState.touchingLeftWall ? Vector2.right : Vector2.left;
            rb.AddForce(-wallNormal * slimeStickPush, ForceMode2D.Force);

            // Clamp velocity away from wall
            float normalVelocity = Vector2.Dot(velocity, wallNormal);
            if (normalVelocity > 0f)
            {
                float clampedVelocity = Mathf.Min(normalVelocity, slimeNormalClamp);
                velocity -= wallNormal * clampedVelocity;
            }

            rb.linearVelocity = velocity;
        }
    }

    private void HandleWallSlide()
    {
        Vector2 velocity = rb.linearVelocity;
        float maxFall = playerState.isCarrying || playerState.isCarried ? wallSlideMaxFallCarrying : wallSlideMaxFall;
        
        if (velocity.y < maxFall)
        {
            velocity.y = maxFall;
            rb.linearVelocity = velocity;
        }
    }

    private void HandleWallJump()
    {
        if (!inputHandler) return;
        if (!inputHandler.GetJumpPressed()) return;
        if (Time.time < wallRegrabUntil && 
            ((wallRegrabSide == -1 && playerState.touchingLeftWall) || 
             (wallRegrabSide == 1 && playerState.touchingRightWall)))
        {
            return; // Prevent immediate re-grab
        }

        PerformWallJump();
    }

    private void PerformWallJump()
    {
        // Determine jump direction
        int jumpDirection = playerState.touchingLeftWall ? 1 : -1; // Jump away from wall
        
        // Apply wall jump velocity
        Vector2 jumpVelocity = new Vector2(jumpDirection * wallJumpHorizontal, wallJumpVertical);
        rb.linearVelocity = jumpVelocity;

        // Lock opposite input temporarily
        oppositeInputLockUntil = Time.time + wallOppositeInputLock;
        oppositeInputLockedDir = -jumpDirection;

        // Prevent immediate re-grab
        wallRegrabUntil = Time.time + wallRegrabBlock;
        wallRegrabSide = jumpDirection == 1 ? -1 : 1; // The wall we just left

        // Reset air jumps if enabled
        if (resetAirJumpsOnWallJump && movement)
        {
            // This would need to be implemented in PlayerMovement
            // movement.ResetAirJumps();
        }

        // Update facing direction
        playerController.ForceFaceDirection(jumpDirection);

        // Trigger events
        PlayerEvents.TriggerWallJumped();
        PlayerEvents.TriggerJumped();

        // Consume jump input
        inputHandler.ConsumeJumpInput();
    }

    // === Ceiling Stick System ===
    
    private void UpdateCeilingStick()
    {
        if (!enableCeilingSlime) return;
        if (playerState.isCarrying || playerState.isCarried) return;
        if (Time.time < ignoreCeilingUntil) return;

        bool headTouchesSlime = TouchingSlimeCeilingCast(out RaycastHit2D upHit);

        // Start ceiling stick
        if (!stickingToCeiling && headTouchesSlime)
        {
            StartCeilingStick(upHit);
        }

        // Handle ceiling stick
        if (stickingToCeiling)
        {
            HandleCeilingStickLogic(headTouchesSlime, upHit);
        }

        // Handle ceiling release fade
        if (!stickingToCeiling && ceilingReleaseUntil > 0f)
        {
            HandleCeilingReleaseFade();
        }
    }

    private bool TouchingSlimeCeilingCast(out RaycastHit2D upHit)
    {
        upHit = default;
        if (!bodyCollider) return false;

        // Cast-based detection
        var filter = new ContactFilter2D 
        { 
            useLayerMask = true, 
            layerMask = slimeLayerMask, 
            useTriggers = true 
        };
        
        RaycastHit2D[] hits = new RaycastHit2D[2];
        int count = bodyCollider.Cast(Vector2.up, filter, hits, 0.04f);
        if (count > 0) 
        { 
            upHit = hits[0]; 
            return true; 
        }

        // Box-based backup detection
        Bounds bounds = bodyCollider.bounds;
        Vector2 size = new Vector2(bounds.size.x * 0.9f, 0.06f);
        Vector2 center = new Vector2(bounds.center.x, bounds.max.y + size.y * 0.5f);
        
        var collider = Physics2D.OverlapBox(center, size, 0f, slimeLayerMask);
        if (collider)
        {
            upHit = Physics2D.Raycast(new Vector2(bounds.center.x, bounds.max.y), Vector2.up, 0.08f, slimeLayerMask);
            return true;
        }

        return false;
    }

    private void StartCeilingStick(RaycastHit2D upHit)
    {
        stickingToCeiling = true;
        ceilingStickStartTime = Time.time;
        lastCeilingY = upHit.collider ? upHit.point.y : transform.position.y + 2f;
        
        PlayerEvents.TriggerCeilingStickChanged(true);
    }

    private void HandleCeilingStickLogic(bool headTouchesSlime, RaycastHit2D upHit)
    {
        bool downHeld = inputHandler && (inputHandler.GetDivePressed() || Input.GetKey(KeyCode.S));
        bool timeUp = (Time.time - ceilingStickStartTime) >= ceilingStickMaxTime;
        bool lostContact = !headTouchesSlime;

        if (downHeld)
        {
            // Immediate dive
            EndCeilingStick(true);
        }
        else if (timeUp || lostContact)
        {
            // Gradual release
            BeginCeilingRelease();
        }
        else
        {
            // Maintain ceiling stick
            MaintainCeilingPosition(upHit);
        }
    }

    private void MaintainCeilingPosition(RaycastHit2D upHit)
    {
        rb.gravityScale = 0f;
        
        Vector2 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        // Maintain gap from ceiling
        float targetCeilY = upHit.collider ? upHit.point.y : lastCeilingY;
        float targetTopY = targetCeilY - ceilingKeepGap;
        float currentTopY = bodyCollider.bounds.max.y;
        float deltaY = targetTopY - currentTopY;
        
        if (Mathf.Abs(deltaY) > 0.0005f)
        {
            rb.position += new Vector2(0f, deltaY);
        }
    }

    private void BeginCeilingRelease()
    {
        stickingToCeiling = false;
        ceilingReleaseUntil = Time.time + ceilingReleaseFade;
        PlayerEvents.TriggerCeilingStickChanged(false);
    }

    private void EndCeilingStick(bool forceDive = false)
    {
        stickingToCeiling = false;
        ceilingReleaseUntil = -1f;
        ignoreCeilingUntil = Time.time + ceilingRestickBlock;
        
        if (forceDive && movement)
        {
            playerState.isDiving = true;
            // Apply dive velocity through movement system
        }
        
        PlayerEvents.TriggerCeilingStickChanged(false);
    }

    private void HandleCeilingReleaseFade()
    {
        if (Time.time < ceilingReleaseUntil)
        {
            // Gradually restore gravity
            float t = 1f - ((ceilingReleaseUntil - Time.time) / ceilingReleaseFade);
            rb.gravityScale = Mathf.Lerp(0f, 3.2f, t); // Use normal gravity scale
        }
        else
        {
            rb.gravityScale = 3.2f;
            ceilingReleaseUntil = -1f;
        }
    }

    // === Friction Management ===
    
    private void UpdateFriction()
    {
        bool shouldBeFrictionless = !playerState.isGrounded && (playerState.touchingLeftWall || playerState.touchingRightWall);
        SetFrictionless(shouldBeFrictionless);
    }

    private void SetFrictionless(bool frictionless)
    {
        if (!bodyCollider) return;

        if (frictionless && !appliedNoFriction)
        {
            if (!slimeNoFrictionMat)
            {
                slimeNoFrictionMat = new PhysicsMaterial2D("Runtime_NoFriction");
                slimeNoFrictionMat.friction = 0f;
                slimeNoFrictionMat.bounciness = 0f;
            }
            bodyCollider.sharedMaterial = slimeNoFrictionMat;
            appliedNoFriction = true;
        }
        else if (!frictionless && appliedNoFriction)
        {
            bodyCollider.sharedMaterial = originalMaterial;
            appliedNoFriction = false;
        }
    }

    // === Collision Events ===
    
    void OnCollisionStay2D(Collision2D collision)
    {
        if (!IsInLayerMask(collision.collider.gameObject.layer, slimeLayerMask)) return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            var normal = collision.GetContact(i).normal;
            if (normal.x > 0.35f) touchL_byCollision = true;
            if (normal.x < -0.35f) touchR_byCollision = true;
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (!IsInLayerMask(collision.collider.gameObject.layer, slimeLayerMask)) return;
        touchL_byCollision = false;
        touchR_byCollision = false;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        
        float otherX = other.bounds.center.x;
        float playerX = transform.position.x;
        
        if (otherX > playerX) touchR_byTrigger = true;
        else touchL_byTrigger = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsInLayerMask(other.gameObject.layer, slimeLayerMask)) return;
        
        float otherX = other.bounds.center.x;
        float playerX = transform.position.x;
        
        if (otherX > playerX) touchR_byTrigger = false;
        else touchL_byTrigger = false;
    }

    // === Event Handlers ===
    
    private void HandleJumped()
    {
        // Reset wall regrab when jumping normally
        wallRegrabUntil = -1f;
        wallRegrabSide = 0;
    }

    private void HandleLanded()
    {
        // Reset wall jump locks when landing
        oppositeInputLockUntil = -1f;
        oppositeInputLockedDir = 0;
        wallRegrabUntil = -1f;
        wallRegrabSide = 0;
    }

    // === Utility ===
    
    private bool IsSlimeSuppressed()
    {
        // This would be implemented based on game-specific logic
        return false;
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    public bool IsDirBlocked(int direction)
    {
        return Time.time < oppositeInputLockUntil && oppositeInputLockedDir == direction;
    }

    // === Public Interface ===
    
    public bool IsOnWall()
    {
        return playerState.touchingLeftWall || playerState.touchingRightWall;
    }

    public bool IsStickingToCeiling()
    {
        return stickingToCeiling;
    }

    public void SetSlimeLayerMask(LayerMask mask)
    {
        slimeLayerMask = mask;
    }
}
