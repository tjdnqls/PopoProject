using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 플레이어의 애니메이션을 관리하는 클래스
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAnimationController : MonoBehaviour
{
    [Header("Animation Parameters")]
    [SerializeField] private string runBoolName = "run";
    [SerializeField] private string jumpBoolName = "jump";
    [SerializeField] private string jumpedBoolName = "jumped";
    [SerializeField] private string groundBoolName = "ground";
    [SerializeField] private string carryBoolName = "carry";
    [SerializeField] private string carryingBoolName = "carrying";
    [SerializeField] private string throwBoolName = "throw";
    [SerializeField] private string throwedBoolName = "throwed";
    [SerializeField] private string attackBoolName = "attack";
    [SerializeField] private string hurtBoolName = "hurt";
    [SerializeField] private string deadBoolName = "dead";

    [Header("Animation States")]
    [SerializeField] private string carryStateName = "Carry";
    [SerializeField] private string carryEndStateName = "CarryEnd";
    [SerializeField] private string throwStateName = "Throw";

    [Header("Animation Triggers")]
    [SerializeField] private string carryEndTriggerName = "carryEnd";

    [Header("Settings")]
    [SerializeField] private float runSpeedThreshold = 0.1f;
    [SerializeField] private bool useAnimationEvents = true;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;
    private Animator animator;

    // === Animation State ===
    private Dictionary<string, bool> lastBoolStates = new Dictionary<string, bool>();
    private bool wasGrounded = false;
    private bool wasRunning = false;
    private bool wasJumping = false;
    private bool wasCarrying = false;
    private bool wasAttacking = false;

    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
        animator = playerController.animator;
    }

    void Start()
    {
        playerController.RegisterModule(this);
        SetupEventListeners();
        InitializeAnimationStates();
    }

    void Update()
    {
        // 선택된 플레이어만 애니메이션 업데이트
        if (playerController.IsSelected)
        {
            UpdateAnimationStates();
        }
        else
        {
            // 비선택 플레이어는 기본 애니메이션으로 설정
            SetIdleAnimation();
        }
    }

    void OnDestroy()
    {
        RemoveEventListeners();
    }

    // === Initialization ===
    
    private void SetupEventListeners()
    {
        PlayerEvents.OnGroundedChanged += HandleGroundedChanged;
        PlayerEvents.OnJumped += HandleJumped;
        PlayerEvents.OnLanded += HandleLanded;
        PlayerEvents.OnCarryStateChanged += HandleCarryStateChanged;
        PlayerEvents.OnCarryStarted += HandleCarryStarted;
        PlayerEvents.OnCarryEnded += HandleCarryEnded;
        PlayerEvents.OnThrowStarted += HandleThrowStarted;
        PlayerEvents.OnAttackStarted += HandleAttackStarted;
        PlayerEvents.OnAttackEnded += HandleAttackEnded;
        PlayerEvents.OnHealthChanged += HandleHealthChanged;
        PlayerEvents.OnPlayerDied += HandlePlayerDied;
        PlayerEvents.OnPlayerRevived += HandlePlayerRevived;
        PlayerEvents.OnAnimationBoolChanged += HandleAnimationBoolChanged;
        PlayerEvents.OnAnimationTriggered += HandleAnimationTriggered;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnGroundedChanged -= HandleGroundedChanged;
        PlayerEvents.OnJumped -= HandleJumped;
        PlayerEvents.OnLanded -= HandleLanded;
        PlayerEvents.OnCarryStateChanged -= HandleCarryStateChanged;
        PlayerEvents.OnCarryStarted -= HandleCarryStarted;
        PlayerEvents.OnCarryEnded -= HandleCarryEnded;
        PlayerEvents.OnThrowStarted -= HandleThrowStarted;
        PlayerEvents.OnAttackStarted -= HandleAttackStarted;
        PlayerEvents.OnAttackEnded -= HandleAttackEnded;
        PlayerEvents.OnHealthChanged -= HandleHealthChanged;
        PlayerEvents.OnPlayerDied -= HandlePlayerDied;
        PlayerEvents.OnPlayerRevived -= HandlePlayerRevived;
        PlayerEvents.OnAnimationBoolChanged -= HandleAnimationBoolChanged;
        PlayerEvents.OnAnimationTriggered -= HandleAnimationTriggered;
    }

    private void InitializeAnimationStates()
    {
        if (!animator) return;

        // Initialize all boolean parameters to false
        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(jumpedBoolName, false);
        SetAnimationBoolSafe(groundBoolName, true); // Usually start grounded
        SetAnimationBoolSafe(carryBoolName, false);
        SetAnimationBoolSafe(carryingBoolName, false);
        SetAnimationBoolSafe(throwBoolName, false);
        SetAnimationBoolSafe(throwedBoolName, false);
        SetAnimationBoolSafe(attackBoolName, false);
        SetAnimationBoolSafe(hurtBoolName, false);
        SetAnimationBoolSafe(deadBoolName, false);

        // Set initial speed
        animator.speed = 1f;
    }

    // === Animation State Updates ===
    
    private void SetIdleAnimation()
    {
        // 비선택 플레이어를 위한 기본 애니메이션
        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(attackBoolName, false);
        // 기본 상태로 설정
        SetAnimationBoolSafe(groundBoolName, true);
    }
    
    private void UpdateAnimationStates()
    {
        if (!animator) return;

        UpdateRunAnimation();
        UpdateGroundAnimation();
        UpdateCarryAnimation();
        UpdateAttackAnimation();
    }

    private void UpdateRunAnimation()
    {
        // Determine if should be running based on horizontal velocity
        bool shouldRun = Mathf.Abs(playerState.velocity.x) > runSpeedThreshold && 
                        playerState.isGrounded && 
                        !playerState.isAttacking &&
                        !playerState.isDead;

        if (shouldRun != wasRunning)
        {
            SetAnimationBoolSafe(runBoolName, shouldRun);
            wasRunning = shouldRun;
            playerState.isRunning = shouldRun;
        }
    }

    private void UpdateGroundAnimation()
    {
        if (playerState.isGrounded != wasGrounded)
        {
            // 선택된 플레이어만 애니메이션 업데이트
            if (playerController.IsSelected)
            {
                SetAnimationBoolSafe(runBoolName, playerState.isRunning);
                SetAnimationBoolSafe(groundBoolName, playerState.isGrounded);
                SetAnimationBoolSafe(jumpBoolName, !playerState.isGrounded && playerState.velocity.y > 0.1f);
                SetAnimationBoolSafe(carryingBoolName, playerState.isCarrying);
                SetAnimationBoolSafe(attackBoolName, playerState.isAttacking);
                SetAnimationBoolSafe(deadBoolName, playerState.isDead);
            }
            wasGrounded = playerState.isGrounded;
        }
    }

    private void UpdateCarryAnimation()
    {
        if (playerState.isCarrying != wasCarrying)
        {
            SetAnimationBoolSafe(carryingBoolName, playerState.isCarrying);
            wasCarrying = playerState.isCarrying;
            playerState.isCarryingAnim = playerState.isCarrying;
        }
    }

    private void UpdateAttackAnimation()
    {
        if (playerState.isAttacking != wasAttacking)
        {
            SetAnimationBoolSafe(attackBoolName, playerState.isAttacking);
            wasAttacking = playerState.isAttacking;
            playerState.isAttackingAnim = playerState.isAttacking;
        }
    }

    // === Event Handlers ===
    
    private void HandleGroundedChanged(bool grounded)
    {
        SetAnimationBoolSafe(groundBoolName, grounded);
        
        // Special case for P2 landing
        if (grounded && playerController.PlayerID == SwapController.PlayerChar.P2)
        {
            SetAnimationBoolSafe(groundBoolName, false); // Immediately set to false for P2
            SetAnimationBoolSafe(throwedBoolName, false);
        }
    }

    private void HandleJumped()
    {
        SetAnimationBoolSafe(jumpBoolName, true);
        playerState.isJumpingAnim = true;
    }

    private void HandleLanded()
    {
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(jumpedBoolName, true);
        playerState.isJumpingAnim = false;
    }

    private void HandleCarryStateChanged(bool isCarrying)
    {
        SetAnimationBoolSafe(carryingBoolName, isCarrying);
    }

    private void HandleCarryStarted()
    {
        SetAnimationBoolSafe(carryBoolName, true);
        SetAnimationBoolSafe(carryingBoolName, true);
    }

    private void HandleCarryEnded()
    {
        SetAnimationBoolSafe(carryBoolName, false);
        SetAnimationBoolSafe(carryingBoolName, false);
        
        // Trigger carry end animation if available
        if (!string.IsNullOrEmpty(carryEndTriggerName))
        {
            SetAnimationTriggerSafe(carryEndTriggerName);
        }
        else if (!string.IsNullOrEmpty(carryEndStateName))
        {
            CrossFadeToState(carryEndStateName, 0.05f);
        }
    }

    private void HandleThrowStarted()
    {
        SetAnimationBoolSafe(throwBoolName, true);
        SetAnimationBoolSafe(throwedBoolName, true);
        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(hurtBoolName, false);
    }

    private void HandleAttackStarted()
    {
        SetAnimationBoolSafe(attackBoolName, true);
    }

    private void HandleAttackEnded()
    {
        SetAnimationBoolSafe(attackBoolName, false);
    }

    private void HandleHealthChanged(int newHealth)
    {
        // Animation logic based on health could go here
    }

    private void HandlePlayerDied()
    {
        SetAnimationBoolSafe(deadBoolName, true);
        
        // Stop all other animations
        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(attackBoolName, false);
        SetAnimationBoolSafe(carryBoolName, false);
        SetAnimationBoolSafe(throwBoolName, false);
    }

    private void HandlePlayerRevived()
    {
        SetAnimationBoolSafe(deadBoolName, false);
        SetAnimationBoolSafe(hurtBoolName, false);
        
        // Reset other animation states
        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(jumpedBoolName, false);
        SetAnimationBoolSafe(throwBoolName, false);
        SetAnimationBoolSafe(throwedBoolName, false);
        SetAnimationBoolSafe(carryBoolName, false);
        SetAnimationBoolSafe(carryingBoolName, false);
        SetAnimationBoolSafe(attackBoolName, false);
        
        // Restore normal speed
        if (animator) animator.speed = 1f;
    }

    private void HandleAnimationBoolChanged(string paramName, bool value)
    {
        SetAnimationBoolSafe(paramName, value);
    }

    private void HandleAnimationTriggered(string triggerName)
    {
        SetAnimationTriggerSafe(triggerName);
    }

    // === Animation Control Methods ===
    
    public void SetAnimationBool(string paramName, bool value)
    {
        SetAnimationBoolSafe(paramName, value);
    }

    public void SetAnimationTrigger(string triggerName)
    {
        SetAnimationTriggerSafe(triggerName);
    }

    public void CrossFadeToState(string stateName, float transitionDuration = 0.1f, int layer = 0)
    {
        if (!animator) return;
        
        try
        {
            animator.CrossFadeInFixedTime(stateName, transitionDuration, layer, 0f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerAnimationController] Failed to crossfade to state '{stateName}': {e.Message}");
        }
    }

    public void SetAnimatorSpeed(float speed)
    {
        if (animator)
        {
            animator.speed = Mathf.Max(0f, speed);
        }
    }

    public float GetAnimatorSpeed()
    {
        return animator ? animator.speed : 1f;
    }

    public float GetCurrentClipLength(int layer = 0)
    {
        if (!animator) return 0f;
        
        var clips = animator.GetCurrentAnimatorClipInfo(layer);
        if (clips != null && clips.Length > 0 && clips[0].clip)
        {
            float speed = animator.speed;
            if (speed < 0.05f) speed = 1f; // Prevent division by near-zero
            return clips[0].clip.length / Mathf.Max(0.0001f, speed);
        }
        return 0f;
    }

    // === Safe Animation Methods ===
    
    private void SetAnimationBoolSafe(string paramName, bool value)
    {
        if (!animator || string.IsNullOrEmpty(paramName)) return;
        
        if (HasAnimatorParameter(paramName, AnimatorControllerParameterType.Bool))
        {
            // Only set if value changed to reduce unnecessary calls
            if (!lastBoolStates.ContainsKey(paramName) || lastBoolStates[paramName] != value)
            {
                animator.SetBool(paramName, value);
                lastBoolStates[paramName] = value;
            }
        }
    }

    private void SetAnimationTriggerSafe(string triggerName)
    {
        if (!animator || string.IsNullOrEmpty(triggerName)) return;
        
        if (HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(triggerName);
        }
    }

    private bool HasAnimatorParameter(string paramName, AnimatorControllerParameterType paramType)
    {
        if (!animator || string.IsNullOrEmpty(paramName)) return false;
        
        foreach (var param in animator.parameters)
        {
            if (param.name == paramName && param.type == paramType)
            {
                return true;
            }
        }
        return false;
    }

    // === Animation Events (called from Animator) ===
    
    public void AE_CarryStart_Begin()
    {
        if (useAnimationEvents)
        {
            Debug.Log("Animation Event: Carry Start Begin");
        }
    }

    public void AE_CarryStart_End()
    {
        if (useAnimationEvents)
        {
            playerController.UnlockInput();
            Debug.Log("Animation Event: Carry Start End");
        }
    }

    public void AE_CarryEnd_Begin()
    {
        if (useAnimationEvents)
        {
            Debug.Log("Animation Event: Carry End Begin");
        }
    }

    public void AE_CarryEnd_End()
    {
        if (useAnimationEvents)
        {
            playerController.UnlockInput();
            Debug.Log("Animation Event: Carry End End");
        }
    }

    public void AE_Attack_Hit()
    {
        if (useAnimationEvents)
        {
            Debug.Log("Animation Event: Attack Hit");
            // This could trigger damage dealing logic
        }
    }

    public void AE_Footstep()
    {
        if (useAnimationEvents)
        {
            PlayerEvents.TriggerFootstepRequested();
        }
    }

    // === Public Interface ===
    
    public bool IsAnimationPlaying(string stateName, int layer = 0)
    {
        if (!animator) return false;
        
        var stateInfo = animator.GetCurrentAnimatorStateInfo(layer);
        return stateInfo.IsName(stateName);
    }

    public float GetAnimationNormalizedTime(int layer = 0)
    {
        if (!animator) return 0f;
        
        var stateInfo = animator.GetCurrentAnimatorStateInfo(layer);
        return stateInfo.normalizedTime;
    }

    public void ResetAllAnimationStates()
    {
        if (!animator) return;

        SetAnimationBoolSafe(runBoolName, false);
        SetAnimationBoolSafe(jumpBoolName, false);
        SetAnimationBoolSafe(jumpedBoolName, false);
        SetAnimationBoolSafe(carryBoolName, false);
        SetAnimationBoolSafe(carryingBoolName, false);
        SetAnimationBoolSafe(throwBoolName, false);
        SetAnimationBoolSafe(throwedBoolName, false);
        SetAnimationBoolSafe(attackBoolName, false);
        SetAnimationBoolSafe(hurtBoolName, false);
        
        animator.speed = 1f;
        
        lastBoolStates.Clear();
    }

    // === Debug ===
    
    void OnGUI()
    {
        if (!Debug.isDebugBuild) return;
        if (!playerController.IsSelected) return;

        GUILayout.BeginArea(new Rect(850, 10, 200, 200));
        GUILayout.Label($"Player {playerController.PlayerID} Animation:");
        GUILayout.Label($"Running: {wasRunning}");
        GUILayout.Label($"Grounded: {wasGrounded}");
        GUILayout.Label($"Jumping: {playerState.isJumpingAnim}");
        GUILayout.Label($"Carrying: {wasCarrying}");
        GUILayout.Label($"Attacking: {wasAttacking}");
        GUILayout.Label($"Speed: {GetAnimatorSpeed():F2}");
        if (animator)
        {
            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            GUILayout.Label($"State: {stateInfo.shortNameHash}");
            GUILayout.Label($"Time: {stateInfo.normalizedTime:F2}");
        }
        GUILayout.EndArea();
    }
}
