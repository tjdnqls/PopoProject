using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 전환 제어 - PlayerMouseMovement 시스템에 맞게 최적화
/// </summary>
public class SwapController : MonoBehaviour
{
    public enum PlayerChar { P1, P2 }
    
    [Header("Player Selection")]
    public PlayerChar charSelect = PlayerChar.P1; // 기본은 P1 선택
    public PlayerChar Current; // 실제 프로젝트의 소스 오브 트루스
    
    [Header("Player References - PlayerMouseMovement System")]
    public PlayerMouseMovement p1Movement; // P1 PlayerMouseMovement 참조
    public PlayerMouseMovement p2Movement; // P2 PlayerMouseMovement 참조
    
    [Header("Legacy References (for compatibility)")]
    public PlayerController p1Controller; // 호환성을 위한 참조
    public PlayerController p2Controller; // 호환성을 위한 참조
    
    [Header("Health System")]
    public Player1HP dead;
    
    [Header("Swap Control")]
    public bool coubt = true;
    public bool soundcount = true;
    
    [Header("Swap Restrictions")]
    [SerializeField] private bool blockSwapDuringCarry = true; // 캐리 중 전환 차단

    void Start()
    {
        // P1로 시작 확실히 설정
        charSelect = PlayerChar.P1;
        Current = PlayerChar.P1;
        
        // PlayerMouseMovement 참조 자동 찾기
        FindPlayerMovementReferences();
        
        Debug.Log($"[SwapController] Started with P1 selected: {charSelect}");
    }
    
    private void FindPlayerMovementReferences()
    {
        // PlayerMouseMovement 참조 자동 찾기
        if (p1Movement == null || p2Movement == null)
        {
            var allMovements = FindObjectsOfType<PlayerMouseMovement>();
            foreach (var movement in allMovements)
            {
                if (movement.playerID == PlayerChar.P1)
                    p1Movement = movement;
                else if (movement.playerID == PlayerChar.P2)
                    p2Movement = movement;
            }
        }
        
        // 경고 메시지
        if (p1Movement == null) Debug.LogWarning("[SwapController] P1 PlayerMouseMovement not found!");
        if (p2Movement == null) Debug.LogWarning("[SwapController] P2 PlayerMouseMovement not found!");
    }
    
    void Update()
    {
        // 전환 차단 조건 체크
        if (SpiralBoxWipe.IsBusy)
        {
            coubt = false;
            return;
        }

        // 사망 상태 처리
        if (dead != null && dead.Dead == true)
        {
            coubt = false;
            charSelect = PlayerChar.P2;
            Current = PlayerChar.P2;
        }
        else
        {
            coubt = true;
        }

        // Tab 키 입력 처리
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (coubt == true)
            {
                // 전환 가능 상태
                if (CanSwapPlayers())
                {
                    SwapPlayers();
                }
                else
                {
                    // 전환 불가 사운드
                    PlaySwapBlockedSound();
                }
            }
            else
            {
                // 전환 차단 상태 사운드
                PlaySwapBlockedSound();
            }
        }
    }
    
    private bool CanSwapPlayers()
    {
        // 캐리 중인지 확인 (PlayerMouseMovement 시스템 사용)
        if (blockSwapDuringCarry && p1Movement != null && p1Movement.isCarrying)
        {
            Debug.Log("[SwapController] Swap blocked: P1 is carrying P2");
            return false;
        }
        
        return true;
    }
    
    private void SwapPlayers()
    {
        // P1 <-> P2 토글
        charSelect = (charSelect == PlayerChar.P1) ? PlayerChar.P2 : PlayerChar.P1;
        Current = charSelect;
     
        Debug.Log($"[SwapController] 현재 선택 = {charSelect}");
        
        // 사운드 재생
        PlaySwapSound();
    }
    
    private void PlaySwapSound()
    {
        try
        {
            // PlayerEvents를 통한 사운드 요청
            PlayerEvents.TriggerSoundRequested("PlayerSwap");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SwapController] Failed to play swap sound: {e.Message}");
        }
    }
    
    private void PlaySwapBlockedSound()
    {
        try
        {
            SoundManager.Play("SwapBeep", transform);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SwapController] Failed to play SwapBeep: {e.Message}");
        }
    }
    
    // === Public Interface ===
    
    /// <summary>
    /// 현재 선택된 플레이어의 PlayerMouseMovement 반환
    /// </summary>
    public PlayerMouseMovement GetCurrentPlayerMovement()
    {
        return charSelect == PlayerChar.P1 ? p1Movement : p2Movement;
    }
    
    /// <summary>
    /// 비선택 플레이어의 PlayerMouseMovement 반환
    /// </summary>
    public PlayerMouseMovement GetOtherPlayerMovement()
    {
        return charSelect == PlayerChar.P1 ? p2Movement : p1Movement;
    }
    
    /// <summary>
    /// 호환성을 위한 PlayerController 반환
    /// </summary>
    public PlayerController GetCurrentPlayer()
    {
        return charSelect == PlayerChar.P1 ? p1Controller : p2Controller;
    }
    
    /// <summary>
    /// 호환성을 위한 PlayerController 반환
    /// </summary>
    public PlayerController GetOtherPlayer()
    {
        return charSelect == PlayerChar.P1 ? p2Controller : p1Controller;
    }
    
    /// <summary>
    /// P1이 P2를 캐리 중인지 확인
    /// </summary>
    public bool IsCarrying()
    {
        return p1Movement != null && p1Movement.isCarrying;
    }
    
    /// <summary>
    /// 전환 가능 여부 확인
    /// </summary>
    public bool CanSwap()
    {
        return coubt && CanSwapPlayers();
    }
    
    /// <summary>
    /// 강제로 P1로 전환 (캐리 시작 시 사용)
    /// </summary>
    public void ForceSelectP1()
    {
        if (charSelect != PlayerChar.P1)
        {
            charSelect = PlayerChar.P1;
            Current = PlayerChar.P1;
            Debug.Log("[SwapController] Forced switch to P1 (carry started)");
        }
    }
    
    /// <summary>
    /// 선택된 플레이어가 P1인지 확인
    /// </summary>
    public bool IsP1Selected()
    {
        return charSelect == PlayerChar.P1;
    }
    
    /// <summary>
    /// 선택된 플레이어가 P2인지 확인
    /// </summary>
    public bool IsP2Selected()
    {
        return charSelect == PlayerChar.P2;
    }

}
