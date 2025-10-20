using UnityEngine;
using System.Collections;

/// <summary>
/// 플레이어의 전투 시스템 (공격, 데미지, 체력, 사망 등)
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Attack Settings")]
    [SerializeField] private float attackCooldown = 1.0f;
    [SerializeField] private float attackDuration = 0.5f;
    [SerializeField] private float attackHitDelay = 0.2f;
    [SerializeField] private GameObject attackHitbox;

    [Header("Health Settings")]
    [SerializeField] private float reviveIFrameTime = 1.2f;
    [SerializeField] private float damageKnockback = 5f;

    [Header("Death Settings")]
    [SerializeField] private float deathHorizontalDamp = 6f;
    [SerializeField] private bool keepFallingOnDeath = true;

    [Header("F Pulse Object")]
    [SerializeField] private GameObject selectedObject;
    [SerializeField] private float fPulseDuration = 0.3f;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;
    private PlayerInputHandler inputHandler;
    private Rigidbody2D rb;

    // === Combat State ===
    private float nextAttackTime = 0f;
    private float attackEndTime = -1f;
    private float invincibleUntil = -1f;
    private float fPulseOffAt = -1f;
    private Coroutine attackPulseCo;
    private bool sceneReloading = false;

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
        
        SetupEventListeners();
        InitializeCombat();
    }

    void Update()
    {
        UpdateAttackInput();
        UpdateAttackState();
        UpdateInvincibility();
        UpdateFPulse();
    }

    void FixedUpdate()
    {
        UpdateDeathPhysics();
    }

    void OnDestroy()
    {
        RemoveEventListeners();
    }

    // === Initialization ===
    
    private void InitializeCombat()
    {
        // Initialize health based on player ID
        int maxHP = playerController.PlayerID == SwapController.PlayerChar.P1 ? 2 : 1;
        playerState.maxHP = maxHP;
        playerState.SetHealth(maxHP);
    }

    private void SetupEventListeners()
    {
        PlayerEvents.OnPlayerDied += HandlePlayerDied;
        PlayerEvents.OnPlayerRevived += HandlePlayerRevived;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnPlayerDied -= HandlePlayerDied;
        PlayerEvents.OnPlayerRevived -= HandlePlayerRevived;
    }

    // === Attack System ===
    
    private void UpdateAttackInput()
    {
        if (!inputHandler) return;
        if (!playerController.IsSelected) return;
        if (playerState.isDead) return;

        // Only P1 can attack
        if (playerController.PlayerID != SwapController.PlayerChar.P1) return;

        if (inputHandler.GetAttackPressed() && CanAttack())
        {
            StartAttack();
            inputHandler.ConsumeAttackInput();
        }
    }

    private void UpdateAttackState()
    {
        // Auto-end attack after duration
        if (playerState.isAttacking && Time.time >= attackEndTime)
        {
            EndAttack();
        }
    }

    private bool CanAttack()
    {
        return playerState.CanAttack() && Time.time >= nextAttackTime;
    }

    private void StartAttack()
    {
        playerState.SetAttacking(true);
        
        // Set timing
        attackEndTime = Time.time + attackDuration;
        nextAttackTime = Time.time + attackCooldown;

        // Lock input during attack
        playerController.LockInput(attackDuration);

        // Stop horizontal movement
        if (rb)
        {
            Vector2 velocity = rb.linearVelocity;
            velocity.x = 0f;
            rb.linearVelocity = velocity;
        }

        // Start delayed hitbox activation
        if (attackPulseCo != null) StopCoroutine(attackPulseCo);
        attackPulseCo = StartCoroutine(ActivateHitboxAfterDelay(attackHitDelay));

        // Trigger animation
        PlayerEvents.TriggerAnimationBoolChanged("attack", true);
    }

    private void EndAttack()
    {
        playerState.SetAttacking(false);

        // Stop delayed hitbox coroutine
        if (attackPulseCo != null)
        {
            StopCoroutine(attackPulseCo);
            attackPulseCo = null;
        }

        // Deactivate hitbox
        if (selectedObject) selectedObject.SetActive(false);

        // Trigger animation
        PlayerEvents.TriggerAnimationBoolChanged("attack", false);
    }

    private IEnumerator ActivateHitboxAfterDelay(float delay)
    {
        // Wait for delay
        float endTime = Time.time + delay;
        while (Time.time < endTime)
        {
            if (!playerState.isAttacking) yield break; // Attack was cancelled
            yield return null;
        }

        // Activate hitbox if still attacking
        if (playerState.isAttacking && selectedObject)
        {
            selectedObject.SetActive(true);
            fPulseOffAt = Time.time + fPulseDuration;
        }

        attackPulseCo = null;
    }

    // === Damage System ===
    
    public void TakeDamage(int damage, Vector2 knockbackDirection = default)
    {
        if (playerState.isDead) return;
        if (IsInvincible()) return;

        // Apply damage
        int newHealth = playerState.currentHP - damage;
        playerState.SetHealth(newHealth);

        // Apply knockback
        if (knockbackDirection != Vector2.zero && rb)
        {
            rb.AddForce(knockbackDirection * damageKnockback, ForceMode2D.Impulse);
        }

        // Start invincibility frames
        SetInvincible(reviveIFrameTime);

        // Trigger hurt animation
        PlayerEvents.TriggerAnimationBoolChanged("hurt", true);

        // Play hurt sound
        PlayerEvents.TriggerSoundRequested("PlayerHurt");

        Debug.Log($"Player {playerController.PlayerID} took {damage} damage. Health: {playerState.currentHP}/{playerState.maxHP}");
    }

    public void Heal(int amount)
    {
        if (playerState.isDead) return;

        int newHealth = Mathf.Min(playerState.currentHP + amount, playerState.maxHP);
        playerState.SetHealth(newHealth);

        Debug.Log($"Player {playerController.PlayerID} healed {amount}. Health: {playerState.currentHP}/{playerState.maxHP}");
    }

    // === Invincibility System ===
    
    private void UpdateInvincibility()
    {
        // Update invincible state
        bool wasInvincible = playerState.isInvincible;
        playerState.isInvincible = Time.time < invincibleUntil;

        // Clear hurt animation when invincibility ends
        if (wasInvincible && !playerState.isInvincible)
        {
            PlayerEvents.TriggerAnimationBoolChanged("hurt", false);
        }
    }

    public bool IsInvincible()
    {
        return playerState.isInvincible;
    }

    public void SetInvincible(float duration)
    {
        invincibleUntil = Time.time + duration;
        playerState.isInvincible = true;
    }

    // === Death System ===
    
    private void UpdateDeathPhysics()
    {
        if (!playerState.isDead) return;
        if (!rb) return;

        // Apply horizontal damping when dead
        if (deathHorizontalDamp > 0f)
        {
            Vector2 velocity = rb.linearVelocity;
            velocity.x = Mathf.MoveTowards(velocity.x, 0f, deathHorizontalDamp * Time.fixedDeltaTime);
            rb.linearVelocity = velocity;
        }

        // Stop falling if configured
        if (!keepFallingOnDeath)
        {
            Vector2 velocity = rb.linearVelocity;
            if (velocity.y < 0f) velocity.y = 0f;
            rb.linearVelocity = velocity;
            rb.gravityScale = 0f;
        }
    }

    public void Die()
    {
        if (playerState.isDead) return;

        playerState.isDead = true;
        
        // Stop all attacks
        if (playerState.isAttacking)
        {
            EndAttack();
        }

        // Lock all input
        playerController.LockInput(float.MaxValue);

        // Trigger death animation
        PlayerEvents.TriggerAnimationBoolChanged("dead", true);

        // Play death sound
        PlayerEvents.TriggerSoundRequested("PlayerDeath");

        Debug.Log($"Player {playerController.PlayerID} died!");
    }

    public void Revive(Vector3? position = null)
    {
        if (!playerState.isDead) return;

        // Stop scene reloading if in progress
        sceneReloading = false;

        // Restore health
        playerState.SetHealth(playerState.maxHP);

        // Reset physics
        if (rb)
        {
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 3.2f; // Normal gravity
        }

        // Teleport if position specified
        if (position.HasValue)
        {
            if (rb) rb.position = position.Value;
            else transform.position = position.Value;
        }

        // Reset states
        playerController.UnlockInput();
        playerState.isDiving = false;
        playerState.ballisticThrowActive = false;

        // Set invincibility
        SetInvincible(reviveIFrameTime);

        // Trigger revive animation
        PlayerEvents.TriggerAnimationBoolChanged("dead", false);

        // Play revive sound
        PlayerEvents.TriggerSoundRequested("PlayerRevive");

        Debug.Log($"Player {playerController.PlayerID} revived!");
    }

    // === F Pulse System ===
    
    private void UpdateFPulse()
    {
        // Auto-deactivate F pulse object
        if (selectedObject && selectedObject.activeSelf && Time.time >= fPulseOffAt)
        {
            selectedObject.SetActive(false);
        }
    }

    // === Event Handlers ===
    
    private void HandlePlayerDied()
    {
        Die();
    }

    private void HandlePlayerRevived()
    {
        // Additional revive logic if needed
    }

    // === Public Interface ===
    
    public bool IsAttacking()
    {
        return playerState.isAttacking;
    }

    public bool IsDead()
    {
        return playerState.isDead;
    }

    public int GetCurrentHealth()
    {
        return playerState.currentHP;
    }

    public int GetMaxHealth()
    {
        return playerState.maxHP;
    }

    public float GetAttackCooldownRemaining()
    {
        return Mathf.Max(0f, nextAttackTime - Time.time);
    }

    public bool AttackLocksInput()
    {
        return playerState.isAttacking && playerController.PlayerID == SwapController.PlayerChar.P1;
    }

    // === Debug ===
    
    void OnGUI()
    {
        if (!Debug.isDebugBuild) return;
        if (!playerController.IsSelected) return;

        GUILayout.BeginArea(new Rect(430, 10, 200, 120));
        GUILayout.Label($"Player {playerController.PlayerID} Combat:");
        GUILayout.Label($"Health: {playerState.currentHP}/{playerState.maxHP}");
        GUILayout.Label($"Attacking: {playerState.isAttacking}");
        GUILayout.Label($"Invincible: {playerState.isInvincible}");
        GUILayout.Label($"Dead: {playerState.isDead}");
        GUILayout.Label($"Attack CD: {GetAttackCooldownRemaining():F1}s");
        GUILayout.EndArea();
    }
}
