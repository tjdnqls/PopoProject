using UnityEngine;

/// <summary>
/// 플레이어 입력을 처리하고 다른 모듈에 전달하는 클래스
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode carryKey = KeyCode.Mouse1;
    [SerializeField] private KeyCode diveKey = KeyCode.S;

    // === Component References ===
    private PlayerController playerController;
    private PlayerState playerState;

    // === Input State ===
    private Vector2 movementInput;
    private bool jumpPressed;
    private bool jumpHeld;
    private bool attackPressed;
    private bool carryPressed;
    private bool divePressed;
    private bool aimUpHeld;

    // === Input Timing ===
    private float lastJumpPressTime = -999f;
    private float jumpBufferTime = 0.08f;

    // === Unity Lifecycle ===
    
    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = playerController.State;
    }

    void Start()
    {
        // Register this module with the controller
        playerController.RegisterModule(this);
    }

    void Update()
    {
        // 선택된 플레이어만 입력 처리
        if (!playerController.IsSelected)
        {
            // Clear all inputs for non-selected player
            ClearAllInputs();
            return;
        }

        ProcessInputs();
        BroadcastInputs();
    }

    // === Input Processing ===
    
    private void ProcessInputs()
    {
        ProcessMovementInput();
        ProcessJumpInput();
        ProcessActionInputs();
    }

    private void ProcessMovementInput()
    {
        float horizontal = 0f;
        
        // 선택된 플레이어만 입력 처리
        if (playerController.IsSelected && playerState.IsInputAllowed())
        {
            // Horizontal movement
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                horizontal -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                horizontal += 1f;
        }
        
        // 디버깅 로그 (입력 테스트용)
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))
        {
            Debug.Log($"[{playerController.PlayerID}] Input: IsSelected={playerController.IsSelected}, CanInput={playerState.IsInputAllowed()}, Horizontal={horizontal}");
        }

        movementInput = new Vector2(horizontal, 0f);
        
        // Aim up input (for throwing)
        aimUpHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        
        // Dive input (선택된 플레이어만)
        divePressed = playerController.IsSelected && Input.GetKeyDown(diveKey);
    }

    private void ProcessJumpInput()
    {
        jumpPressed = false;
        jumpHeld = false;

        // 선택된 플레이어만 점프 입력 처리
        if (playerController.IsSelected && playerState.IsInputAllowed())
        {
            if (Input.GetKeyDown(jumpKey))
            {
                jumpPressed = true;
                lastJumpPressTime = Time.time;
                Debug.Log($"[{playerController.PlayerID}] Jump pressed!");
            }

            jumpHeld = Input.GetKey(jumpKey);
        }
    }

    private void ProcessActionInputs()
    {
        attackPressed = false;
        carryPressed = false;

        // 선택된 플레이어만 액션 입력 처리
        if (playerController.IsSelected && playerState.IsInputAllowed())
        {
            if (Input.GetKeyDown(attackKey))
            {
                attackPressed = true;
                Debug.Log($"[{playerController.PlayerID}] Attack pressed!");
            }
            
            if (Input.GetKeyDown(carryKey))
            {
                carryPressed = true;
                Debug.Log($"[{playerController.PlayerID}] Carry pressed!");
            }
        }
    }

    // === Input Broadcasting ===
    
    private void BroadcastInputs()
    {
        // Update player state with horizontal input
        playerState.SetHorizontalInput(movementInput.x);

        // Broadcast movement input
        if (movementInput.magnitude > 0.01f)
        {
            PlayerEvents.TriggerMovementChanged(movementInput);
        }
    }

    // === Input Management ===
    
    private void ClearAllInputs()
    {
        movementInput = Vector2.zero;
        jumpPressed = false;
        jumpHeld = false;
        attackPressed = false;
        carryPressed = false;
        divePressed = false;
        aimUpHeld = false;
    }
    
    // === Input Queries ===
    
    public Vector2 GetMovementInput()
    {
        return movementInput;
    }

    public bool GetJumpPressed()
    {
        return jumpPressed;
    }

    public bool GetJumpHeld()
    {
        return jumpHeld;
    }

    public bool GetAttackPressed()
    {
        return attackPressed;
    }

    public bool GetCarryPressed()
    {
        return carryPressed;
    }

    public bool GetDivePressed()
    {
        return divePressed;
    }

    public bool GetAimUpHeld()
    {
        return aimUpHeld;
    }

    public bool IsJumpBuffered()
    {
        return (Time.time - lastJumpPressTime) <= jumpBufferTime;
    }

    // === Input Control ===
    
    public void ClearInputs()
    {
        movementInput = Vector2.zero;
        jumpPressed = false;
        jumpHeld = false;
        attackPressed = false;
        carryPressed = false;
        divePressed = false;
        aimUpHeld = false;
    }

    public void ConsumeJumpInput()
    {
        jumpPressed = false;
        lastJumpPressTime = -999f;
    }

    public void ConsumeAttackInput()
    {
        attackPressed = false;
    }

    public void ConsumeCarryInput()
    {
        carryPressed = false;
    }

    // === Input Blocking ===
    
    public bool IsInputBlocked()
    {
        return !playerState.IsInputAllowed();
    }

    // === Debug ===
    
    void OnGUI()
    {
        if (!Debug.isDebugBuild) return;
        if (!playerController.IsSelected) return;

        GUILayout.BeginArea(new Rect(10, 10, 200, 200));
        GUILayout.Label($"Player {playerController.PlayerID} Input:");
        GUILayout.Label($"Movement: {movementInput}");
        GUILayout.Label($"Jump Pressed: {jumpPressed}");
        GUILayout.Label($"Jump Held: {jumpHeld}");
        GUILayout.Label($"Attack: {attackPressed}");
        GUILayout.Label($"Carry: {carryPressed}");
        GUILayout.Label($"Aim Up: {aimUpHeld}");
        GUILayout.Label($"Input Blocked: {IsInputBlocked()}");
        GUILayout.EndArea();
    }
}
