using UnityEngine;

/// <summary>
/// 다른 스크립트에서 BossRunBreakController의 달리기를 트리거하는 예제 스크립트
/// </summary>
public class BossRunTrigger : MonoBehaviour
{
    [Header("Boss Reference")]
    [SerializeField] private BossRunBreakController bossController;
    
    [Header("Trigger Settings")]
    [SerializeField] private KeyCode triggerKey = KeyCode.T;
    [SerializeField] private KeyCode stopKey = KeyCode.Y;
    
    [Header("Auto Trigger")]
    [SerializeField] private bool autoTriggerOnStart = false;
    [SerializeField] private float autoTriggerDelay = 2f;
    
    void Start()
    {
        // BossRunBreakController 자동 찾기
        if (bossController == null)
        {
            bossController = FindObjectOfType<BossRunBreakController>();
            if (bossController == null)
            {
                Debug.LogWarning("[BossRunTrigger] BossRunBreakController not found!");
                return;
            }
        }
        
        // 자동 트리거 설정
        if (autoTriggerOnStart)
        {
            Invoke(nameof(TriggerBossRun), autoTriggerDelay);
        }
    }
    
    void Update()
    {
        if (bossController == null) return;
        
        // 수동 트리거 키
        if (Input.GetKeyDown(triggerKey))
        {
            TriggerBossRun();
        }
        
        // 수동 정지 키
        if (Input.GetKeyDown(stopKey))
        {
            StopBossRun();
        }
    }
    
    /// <summary>
    /// 보스 달리기 시작 (외부에서 호출 가능)
    /// </summary>
    public void TriggerBossRun()
    {
        if (bossController == null) return;
        
        Debug.Log("[BossRunTrigger] Triggering boss run!");
        bossController.TriggerRun();
    }
    
    /// <summary>
    /// 보스 달리기 중단 (외부에서 호출 가능)
    /// </summary>
    public void StopBossRun()
    {
        if (bossController == null) return;
        
        Debug.Log("[BossRunTrigger] Stopping boss run!");
        bossController.StopRun();
    }
    
    /// <summary>
    /// 보스 상태 확인
    /// </summary>
    public bool IsBossRunning()
    {
        return bossController != null && bossController.IsRunning;
    }
    
    /// <summary>
    /// 보스 현재 속도 확인
    /// </summary>
    public float GetBossSpeed()
    {
        return bossController != null ? bossController.CurrentSpeed : 0f;
    }
    
    // Unity Events나 다른 시스템에서 호출할 수 있는 래퍼 함수들
    public void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어가 트리거에 들어오면 보스 달리기 시작
        if (other.CompareTag("Player"))
        {
            TriggerBossRun();
        }
    }
    
    public void OnTriggerExit2D(Collider2D other)
    {
        // 플레이어가 트리거에서 나가면 보스 달리기 중단
        if (other.CompareTag("Player"))
        {
            StopBossRun();
        }
    }
}
