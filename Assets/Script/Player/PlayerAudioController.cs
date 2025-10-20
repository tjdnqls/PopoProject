using UnityEngine;

/// <summary>
/// 플레이어의 오디오 시스템을 관리하는 클래스
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAudioController : MonoBehaviour
{
    [Header("Footstep Settings")]
    [SerializeField] private bool enableFootstepLoop = true;
    [SerializeField] private float footstepMinSpeed = 0.1f;
    [SerializeField] private string knightWalkSoundName = "KnightWalk";
    [SerializeField] private string princessWalkSoundName = "PrincessWalk";

    [Header("Jump/Landing Settings")]
    [SerializeField] private float landSfxMinAirTime = 0.05f;
    [SerializeField] private string knightJumpSoundName = "KnightJumpAnd";
    [SerializeField] private string princessJumpSoundName = "PrincessJumpAnd";
    [SerializeField] private string doubleJumpSoundName = "DoubleJump";

    [Header("Combat Settings")]
    [SerializeField] private string attackSoundName = "PlayerAttack";
    [SerializeField] private string hurtSoundName = "PlayerHurt";
    [SerializeField] private string deathSoundName = "PlayerDeath";
    [SerializeField] private string reviveSoundName = "PlayerRevive";

    [Header("Carry Settings")]
    [SerializeField] private string carryStartSoundName = "CarryStart";
    [SerializeField] private string carryEndSoundName = "CarryEnd";
    [SerializeField] private string throwSoundName = "PlayerThrow";

    [Header("Wall Interaction Settings")]
    [SerializeField] private string wallJumpSoundName = "WallJump";
    [SerializeField] private string ceilingStickSoundName = "CeilingStick";

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;

    // === Audio State ===
    private string currentWalkLoop = null;
    private bool leftGroundSinceLast = false;
    private float leftGroundAt = -999f;
    private bool wasRunning = false;

    // === Unity Lifecycle ===
    
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

    void Update()
    {
        UpdateFootstepLoop();
    }

    void OnDestroy()
    {
        RemoveEventListeners();
        StopFootstepLoop();
    }

    // === Event Setup ===
    
    private void SetupEventListeners()
    {
        PlayerEvents.OnGroundedChanged += HandleGroundedChanged;
        PlayerEvents.OnJumped += HandleJumped;
        PlayerEvents.OnLanded += HandleLanded;
        PlayerEvents.OnAttackStarted += HandleAttackStarted;
        PlayerEvents.OnCarryStarted += HandleCarryStarted;
        PlayerEvents.OnCarryEnded += HandleCarryEnded;
        PlayerEvents.OnThrowStarted += HandleThrowStarted;
        PlayerEvents.OnWallJumped += HandleWallJumped;
        PlayerEvents.OnCeilingStickChanged += HandleCeilingStickChanged;
        PlayerEvents.OnSoundRequested += HandleSoundRequested;
        PlayerEvents.OnFootstepRequested += HandleFootstepRequested;
        PlayerEvents.OnPlayerDied += HandlePlayerDied;
        PlayerEvents.OnPlayerRevived += HandlePlayerRevived;
    }

    private void RemoveEventListeners()
    {
        PlayerEvents.OnGroundedChanged -= HandleGroundedChanged;
        PlayerEvents.OnJumped -= HandleJumped;
        PlayerEvents.OnLanded -= HandleLanded;
        PlayerEvents.OnAttackStarted -= HandleAttackStarted;
        PlayerEvents.OnCarryStarted -= HandleCarryStarted;
        PlayerEvents.OnCarryEnded -= HandleCarryEnded;
        PlayerEvents.OnThrowStarted -= HandleThrowStarted;
        PlayerEvents.OnWallJumped -= HandleWallJumped;
        PlayerEvents.OnCeilingStickChanged -= HandleCeilingStickChanged;
        PlayerEvents.OnSoundRequested -= HandleSoundRequested;
        PlayerEvents.OnFootstepRequested -= HandleFootstepRequested;
        PlayerEvents.OnPlayerDied -= HandlePlayerDied;
        PlayerEvents.OnPlayerRevived -= HandlePlayerRevived;
    }

    // === Footstep System ===
    
    private void UpdateFootstepLoop()
    {
        if (!enableFootstepLoop) return;
        if (!playerController.IsSelected) return; // Only play for selected player

        bool shouldPlayFootsteps = ShouldPlayFootsteps();
        string targetLoop = GetFootstepSoundName();

        if (shouldPlayFootsteps)
        {
            // Start or continue footstep loop
            if (currentWalkLoop != targetLoop)
            {
                StopFootstepLoop();
                StartFootstepLoop(targetLoop);
            }
        }
        else
        {
            // Stop footstep loop
            StopFootstepLoop();
        }

        wasRunning = playerState.isRunning;
    }

    private bool ShouldPlayFootsteps()
    {
        // Play footsteps when running on ground
        return playerState.isGrounded && 
               Mathf.Abs(playerState.velocity.x) >= footstepMinSpeed &&
               !playerState.isAttacking &&
               !playerState.isDead &&
               !playerState.isCarried;
    }

    private string GetFootstepSoundName()
    {
        return playerController.PlayerID == SwapController.PlayerChar.P1 
            ? knightWalkSoundName 
            : princessWalkSoundName;
    }

    private void StartFootstepLoop(string soundName)
    {
        if (string.IsNullOrEmpty(soundName)) return;

        currentWalkLoop = soundName;
        // For now, just play the sound without looping
        // In a real implementation, you'd want proper loop management
        PlaySound(soundName, loop: false);
    }

    private void StopFootstepLoop()
    {
        if (!string.IsNullOrEmpty(currentWalkLoop))
        {
            // StopSound(currentWalkLoop); // Commented out since Stop method may not exist
            currentWalkLoop = null;
        }
    }

    // === Event Handlers ===
    
    private void HandleGroundedChanged(bool grounded)
    {
        if (!grounded)
        {
            // Left ground
            leftGroundSinceLast = true;
            leftGroundAt = Time.time;
            StopFootstepLoop(); // Stop footsteps when leaving ground
        }
    }

    private void HandleJumped()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        
        // Play jump sound based on player type
        string jumpSound = playerController.PlayerID == SwapController.PlayerChar.P1 
            ? knightJumpSoundName 
            : princessJumpSoundName;
        
        PlaySound(jumpSound);
    }

    private void HandleLanded()
    {
        // Only play landing sound if we were in air long enough and this is the selected player
        if (!playerController.IsSelected) return;
        if (!leftGroundSinceLast) return;
        if ((Time.time - leftGroundAt) < landSfxMinAirTime) return;

        // Play landing sound based on player type
        string landSound = playerController.PlayerID == SwapController.PlayerChar.P1 
            ? knightJumpSoundName 
            : princessJumpSoundName;
        
        PlaySound(landSound);
        leftGroundSinceLast = false; // Consume the flag
    }

    private void HandleAttackStarted()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        PlaySound(attackSoundName);
    }

    private void HandleCarryStarted()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        PlaySound(carryStartSoundName);
    }

    private void HandleCarryEnded()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        PlaySound(carryEndSoundName);
    }

    private void HandleThrowStarted()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        PlaySound(throwSoundName);
    }

    private void HandleWallJumped()
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        PlaySound(wallJumpSoundName);
    }

    private void HandleCeilingStickChanged(bool sticking)
    {
        // 선택된 플레이어만 사운드 재생
        if (!playerController.IsSelected) return;
        
        if (sticking)
        {
            PlaySound(ceilingStickSoundName);
        }
    }

    private void HandleSoundRequested(string soundName)
    {
        PlaySound(soundName);
    }

    private void HandleFootstepRequested()
    {
        // This can be called from animation events
        string footstepSound = GetFootstepSoundName();
        PlaySound(footstepSound);
    }

    private void HandlePlayerDied()
    {
        StopFootstepLoop();
        PlaySound(deathSoundName);
    }

    private void HandlePlayerRevived()
    {
        PlaySound(reviveSoundName);
    }

    // === Audio Playback Methods ===
    
    private void PlaySound(string soundName, bool loop = false)
    {
        if (string.IsNullOrEmpty(soundName)) return;

        // Check if SoundManager exists before using it
        var soundManagerType = System.Type.GetType("SoundManager");
        if (soundManagerType != null)
        {
            try
            {
                SoundManager.Play(soundName, transform);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerAudioController] Failed to play sound '{soundName}': {e.Message}");
            }
        }
        else
        {
            Debug.Log($"[PlayerAudioController] Would play sound: {soundName}");
        }
    }

    private void StopSound(string soundName)
    {
        if (string.IsNullOrEmpty(soundName)) return;

        try
        {
            // SoundManager.Stop might not exist, so we'll just use Play for now
            // SoundManager.Stop(soundName, transform);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerAudioController] Failed to stop sound '{soundName}': {e.Message}");
        }
    }

    // === Public Interface ===
    
    public void PlayCustomSound(string soundName, bool loop = false)
    {
        PlaySound(soundName, loop);
    }

    public void StopCustomSound(string soundName)
    {
        StopSound(soundName);
    }

    public void SetFootstepEnabled(bool enabled)
    {
        enableFootstepLoop = enabled;
        if (!enabled)
        {
            StopFootstepLoop();
        }
    }

    public bool IsFootstepEnabled()
    {
        return enableFootstepLoop;
    }

    public void SetFootstepMinSpeed(float minSpeed)
    {
        footstepMinSpeed = minSpeed;
    }

    public string GetCurrentFootstepLoop()
    {
        return currentWalkLoop;
    }

    // === Volume Control (if SoundManager supports it) ===
    
    public void SetMasterVolume(float volume)
    {
        try
        {
            // This would depend on your SoundManager implementation
            // SoundManager.SetMasterVolume(volume);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerAudioController] Failed to set master volume: {e.Message}");
        }
    }

    public void SetSFXVolume(float volume)
    {
        try
        {
            // This would depend on your SoundManager implementation
            // SoundManager.SetSFXVolume(volume);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerAudioController] Failed to set SFX volume: {e.Message}");
        }
    }

    // === Audio State Queries ===
    
    public bool IsPlayingFootsteps()
    {
        return !string.IsNullOrEmpty(currentWalkLoop);
    }

    public bool ShouldPlayAudio()
    {
        // Only play audio for selected player or important events
        return playerController.IsSelected || playerState.isDead;
    }

    // === Debug ===
    
    void OnGUI()
    {
        if (!Debug.isDebugBuild) return;
        if (!playerController.IsSelected) return;

        GUILayout.BeginArea(new Rect(1060, 10, 200, 120));
        GUILayout.Label($"Player {playerController.PlayerID} Audio:");
        GUILayout.Label($"Footsteps: {IsPlayingFootsteps()}");
        GUILayout.Label($"Current Loop: {currentWalkLoop ?? "None"}");
        GUILayout.Label($"Should Play: {ShouldPlayFootsteps()}");
        GUILayout.Label($"Speed: {playerState.velocity.x:F2}");
        GUILayout.Label($"Min Speed: {footstepMinSpeed:F2}");
        GUILayout.EndArea();
    }

    // === Cleanup ===
    
    void OnDisable()
    {
        StopFootstepLoop();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopFootstepLoop();
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            StopFootstepLoop();
        }
    }
}
