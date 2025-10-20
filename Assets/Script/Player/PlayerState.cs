using UnityEngine;

/// <summary>
/// 플레이어의 현재 상태를 관리하는 클래스
/// 모든 모듈에서 공유되는 상태 정보를 중앙 집중식으로 관리
/// </summary>
[System.Serializable]
public class PlayerState
{
    [Header("Movement State")]
    public bool isGrounded = false;
    public bool wasGrounded = false;
    public Vector2 velocity = Vector2.zero;
    public float horizontalInput = 0f;
    public int facingDirection = 1; // 1 = right, -1 = left

    [Header("Jump State")]
    public bool isJumping = false;
    public bool jumpHeld = false;
    public int airJumpsLeft = 0;
    public float lastJumpTime = -999f;
    public float lastGroundedTime = -999f;

    [Header("Carry State")]
    public bool isCarrying = false;
    public bool isCarried = false;
    public bool ballisticThrowActive = false;

    [Header("Combat State")]
    public int currentHP = 5;
    public int maxHP = 5;
    public bool isDead = false;
    public bool isInvincible = false;
    public bool isAttacking = false;

    [Header("Wall Interaction State")]
    public bool touchingLeftWall = false;
    public bool touchingRightWall = false;
    public bool stickingToCeiling = false;
    public bool isDiving = false;

    [Header("Input State")]
    public bool inputLocked = false;
    public float inputLockUntil = -999f;

    [Header("Animation State")]
    public bool isRunning = false;
    public bool isJumpingAnim = false;
    public bool isCarryingAnim = false;
    public bool isAttackingAnim = false;

    // === State Change Methods ===
    
    public void SetGrounded(bool grounded)
    {
        if (isGrounded != grounded)
        {
            wasGrounded = isGrounded;
            isGrounded = grounded;
            PlayerEvents.TriggerGroundedChanged(grounded);
            
            if (grounded && !wasGrounded)
            {
                PlayerEvents.TriggerLanded();
                lastGroundedTime = Time.time;
            }
        }
    }

    public void SetVelocity(Vector2 newVelocity)
    {
        if (velocity != newVelocity)
        {
            velocity = newVelocity;
            PlayerEvents.TriggerVelocityChanged(velocity);
        }
    }

    public void SetHorizontalInput(float input)
    {
        if (Mathf.Abs(horizontalInput - input) > 0.01f)
        {
            horizontalInput = input;
            PlayerEvents.TriggerMovementChanged(new Vector2(input, 0f));
            
            // Update facing direction
            if (Mathf.Abs(input) > 0.01f)
            {
                facingDirection = input > 0 ? 1 : -1;
            }
        }
    }

    public void SetCarrying(bool carrying)
    {
        if (isCarrying != carrying)
        {
            isCarrying = carrying;
            PlayerEvents.TriggerCarryStateChanged(carrying);
            
            if (carrying)
                PlayerEvents.TriggerCarryStarted();
            else
                PlayerEvents.TriggerCarryEnded();
        }
    }

    public void SetHealth(int health)
    {
        health = Mathf.Clamp(health, 0, maxHP);
        if (currentHP != health)
        {
            currentHP = health;
            PlayerEvents.TriggerHealthChanged(currentHP);
            
            if (currentHP <= 0 && !isDead)
            {
                isDead = true;
                PlayerEvents.TriggerPlayerDied();
            }
        }
    }

    public void SetAttacking(bool attacking)
    {
        if (isAttacking != attacking)
        {
            isAttacking = attacking;
            
            if (attacking)
                PlayerEvents.TriggerAttackStarted();
            else
                PlayerEvents.TriggerAttackEnded();
        }
    }

    public void SetInputLocked(bool locked, float duration = 0f)
    {
        inputLocked = locked;
        if (locked && duration > 0f)
        {
            inputLockUntil = Time.time + duration;
        }
        PlayerEvents.TriggerInputLocked(locked);
    }

    public void Jump()
    {
        isJumping = true;
        lastJumpTime = Time.time;
        PlayerEvents.TriggerJumped();
    }

    public void Revive()
    {
        isDead = false;
        currentHP = maxHP;
        isInvincible = true;
        PlayerEvents.TriggerPlayerRevived();
        PlayerEvents.TriggerHealthChanged(currentHP);
    }

    // === Utility Methods ===
    
    public bool IsInputAllowed()
    {
        // 시간 기반 입력 잠금이 만료되면 자동 해제
        if (inputLocked && Time.time >= inputLockUntil)
        {
            inputLocked = false;
            Debug.Log($"Input lock automatically released at time {Time.time}");
        }
        
        bool allowed = !inputLocked && !isDead;
        
        // 디버깅 로그
        if (!allowed)
        {
            Debug.Log($"Input blocked: inputLocked={inputLocked}, lockUntil={inputLockUntil}, currentTime={Time.time}, isDead={isDead}");
        }
        
        return allowed;
    }

    public bool CanJump()
    {
        return IsInputAllowed() && !isAttacking && (isGrounded || airJumpsLeft > 0);
    }

    public bool CanMove()
    {
        return IsInputAllowed() && !isCarried;
    }

    public bool CanAttack()
    {
        return IsInputAllowed() && !isAttacking && !isCarrying && !isCarried;
    }

    public bool CanCarry()
    {
        return IsInputAllowed() && !isCarrying && !isCarried && !isAttacking;
    }

    // === Debug Methods ===
    
    public void LogState()
    {
        Debug.Log($"PlayerState - Grounded: {isGrounded}, Carrying: {isCarrying}, HP: {currentHP}/{maxHP}, InputLocked: {inputLocked}");
    }
}
