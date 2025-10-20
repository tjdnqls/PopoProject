using UnityEngine;

/// <summary>
/// 플레이어의 지면 감지 및 관련 물리 상호작용을 처리하는 클래스
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerGroundDetection : MonoBehaviour
{
    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.04f;
    [SerializeField] private float groundCheckSkin = 0.04f;
    [SerializeField] private LayerMask groundMask = -1;
    [SerializeField] private LayerMask eventMask = -1;

    [Header("Step Up System")]
    [SerializeField] private bool enableStepUp = true;
    [SerializeField] private float stepUpMaxHeight = 0.18f;
    [SerializeField] private float stepForwardDistance = 0.10f;
    [SerializeField] private float stepUpSkin = 0.01f;
    [SerializeField] private float stepOnlyWhenFallingVy = 0.05f;

    [Header("Seam Fix")]
    [SerializeField] private float seamFixProbe = 0.03f;
    [SerializeField] private float seamFixLift = 0.03f;

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = true;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;
    private Rigidbody2D rb;
    private Collider2D bodyCollider;

    // === Ground State ===
    private bool wasGroundedLastFrame = false;
    private float ignoreGroundUntil = -1f;
    private float postJumpGroundIgnore = 0.06f;

    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
        rb = playerController.rb;
        bodyCollider = playerController.bodyCollider;
    }

    void Start()
    {
        playerController.RegisterModule(this);
        SetupEventListeners();
    }

    void FixedUpdate()
    {
        UpdateGroundDetection();
        HandleStepUp();
        HandleSeamFix();
    }

    void OnDestroy()
    {
        RemoveEventListeners();
    }

    // === Event Setup ===
    
    private void SetupEventListeners()
    {
        PlayerEvents.OnJumped += HandleJumped;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnJumped -= HandleJumped;
    }

    // === Ground Detection ===
    
    private void UpdateGroundDetection()
    {
        bool currentlyGrounded = CheckGrounded();
        
        // Update state if changed
        if (playerState.isGrounded != currentlyGrounded)
        {
            playerState.SetGrounded(currentlyGrounded);
        }

        wasGroundedLastFrame = currentlyGrounded;
    }

    public bool CheckGrounded()
    {
        if (Time.time < ignoreGroundUntil) return false;
        if (!bodyCollider) return false;

        return CheckGroundedStrict() || CheckGroundedRaycast();
    }

    private bool CheckGroundedStrict()
    {
        Bounds bounds = bodyCollider.bounds;
        float skin = Mathf.Max(0.005f, groundCheckSkin);
        
        Vector2 boxCenter = new Vector2(bounds.center.x, bounds.min.y + skin * 0.5f);
        Vector2 boxSize = new Vector2(Mathf.Max(0.02f, bounds.size.x * 0.9f), skin);

        Collider2D hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, groundMask | eventMask);

        if (showDebugRays)
        {
            Color debugColor = hit ? Color.green : Color.red;
            Debug.DrawLine(
                new Vector2(boxCenter.x - boxSize.x * 0.5f, boxCenter.y),
                new Vector2(boxCenter.x + boxSize.x * 0.5f, boxCenter.y),
                debugColor, 0f, false
            );
        }

        return hit != null;
    }

    private bool CheckGroundedRaycast()
    {
        Vector2 center = transform.position + Vector3.down * 0.2f;
        Vector2 left = center + Vector2.left * 0.1f;
        Vector2 right = center + Vector2.right * 0.1f;

        bool centerHit = Physics2D.Raycast(center, Vector2.down, groundCheckDistance, groundMask | eventMask);
        bool leftHit = Physics2D.Raycast(left, Vector2.down, groundCheckDistance, groundMask | eventMask);
        bool rightHit = Physics2D.Raycast(right, Vector2.down, groundCheckDistance, groundMask | eventMask);

        if (showDebugRays)
        {
            Debug.DrawRay(center, Vector2.down * groundCheckDistance, centerHit ? Color.green : Color.red);
            Debug.DrawRay(left, Vector2.down * groundCheckDistance, leftHit ? Color.green : Color.red);
            Debug.DrawRay(right, Vector2.down * groundCheckDistance, rightHit ? Color.green : Color.red);
        }

        return centerHit || leftHit || rightHit;
    }

    // === Step Up System ===
    
    private void HandleStepUp()
    {
        if (!enableStepUp) return;
        if (Mathf.Abs(playerState.horizontalInput) < 0.01f) return;

        TryStepUp(playerState.horizontalInput);
    }

    private void TryStepUp(float dirInput)
    {
        if (!bodyCollider) return;
        
        // Skip if moving upward (jumping)
        if (rb && rb.linearVelocity.y > stepOnlyWhenFallingVy) return;

        Bounds bounds = bodyCollider.bounds;
        int sign = dirInput > 0f ? 1 : -1;

        // Check for step surface ahead
        float feetY = bounds.min.y + 0.01f;
        Vector2 rayOrigin = new Vector2(
            bounds.center.x + sign * (bounds.extents.x + stepForwardDistance),
            feetY + stepUpMaxHeight
        );
        float rayLength = stepUpMaxHeight + 0.06f;

        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, groundMask | eventMask);

        if (showDebugRays)
        {
            Debug.DrawRay(rayOrigin, Vector2.down * rayLength, hit ? Color.yellow : Color.gray, 0f);
        }

        if (!hit) return;

        float climbHeight = hit.point.y - feetY;
        if (climbHeight <= 0f || climbHeight > stepUpMaxHeight) return;

        // Check for head clearance
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = groundMask | eventMask
        };

        RaycastHit2D[] buffer = new RaycastHit2D[2];
        int hitCount = bodyCollider.Cast(Vector2.up, filter, buffer, climbHeight + stepUpSkin);
        
        if (hitCount > 0) return; // Head would hit something

        // Perform step up
        Vector2 stepUpOffset = new Vector2(0f, climbHeight + stepUpSkin);
        if (rb)
            rb.position += stepUpOffset;
        else
            transform.position += (Vector3)stepUpOffset;
    }

    // === Seam Fix ===
    
    private void HandleSeamFix()
    {
        if (!bodyCollider) return;
        if (!playerState.isGrounded) return;
        if (Mathf.Abs(playerState.horizontalInput) < 0.01f) return;

        FixVerticalSeam(playerState.horizontalInput);
    }

    private void FixVerticalSeam(float dirInput)
    {
        Bounds bounds = bodyCollider.bounds;
        int sign = dirInput > 0 ? 1 : -1;

        // Check for thin vertical face ahead
        Vector2 seamCenter = new Vector2(
            bounds.center.x + sign * (bounds.extents.x + seamFixProbe * 0.5f),
            bounds.min.y + 0.02f
        );
        Vector2 seamSize = new Vector2(seamFixProbe, 0.04f);

        bool hasVerticalFace = Physics2D.OverlapBox(seamCenter, seamSize, 0f, groundMask);
        if (!hasVerticalFace) return;

        // Check if we're actually on ground
        Vector2 feetCenter = new Vector2(bounds.center.x, bounds.min.y - 0.01f);
        Vector2 feetSize = new Vector2(bounds.size.x * 0.9f, 0.02f);
        bool grounded = Physics2D.OverlapBox(feetCenter, feetSize, 0f, groundMask);

        if (grounded && rb)
        {
            rb.position += new Vector2(0f, seamFixLift);
        }
    }

    // === Event Handlers ===
    
    private void HandleJumped()
    {
        ignoreGroundUntil = Time.time + postJumpGroundIgnore;
    }

    // === Public Interface ===
    
    public bool IsGrounded()
    {
        return playerState.isGrounded;
    }

    public bool WasGroundedLastFrame()
    {
        return wasGroundedLastFrame;
    }

    public void SetIgnoreGroundUntil(float time)
    {
        ignoreGroundUntil = time;
    }

    // === Layer Mask Setup ===
    
    public void SetGroundMask(LayerMask mask)
    {
        groundMask = mask;
    }

    public void SetEventMask(LayerMask mask)
    {
        eventMask = mask;
    }
}
