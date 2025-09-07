using System; // ★ 추가
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player1HP : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private int maxHP = 2;
    public int CurrentHP { get; private set; }
    public int MaxHP => maxHP;                // ★ UI에서 읽을 수 있게 공개 Getter
    public bool IsDead { get; private set; }

    // ★ HP 변경/사망 이벤트
    public event Action<int, int> HpChanged;   // (current, max)
    public event Action Died;
    public bool Dead = false;
    public SmartCameraFollowByWall swap;


    [Header("Layers (사망 시 Ground로 변경)")]
    [SerializeField] private string groundLayerName = "Ground";

    [Header("Optional")]
    [SerializeField] private string deadBoolName = "dead"; // Animator bool 파라미터명(있으면 세팅)

    private PlayerMouseMovement move;
    private Rigidbody2D rb;
    private Animator anim;

    void Awake()
    {
        move = GetComponent<PlayerMouseMovement>();
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        CurrentHP = Mathf.Max(1, maxHP);

        // ★ 시작 상태 브로드캐스트(초기 UI 동기화)
        HpChanged?.Invoke(CurrentHP, maxHP);
    }

    /// <summary>외부에서 데미지 줄 때 사용</summary>
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return;

        int amount = Mathf.Max(1, dmg);
        int prev = CurrentHP;
        CurrentHP = Mathf.Max(0, CurrentHP - amount);

        if (CurrentHP <= 0)
        {
            CameraShaker.Shake(0.5f, 0.2f);
            Die(); // Die() 내부에서 HpChanged(0, max)와 Died 호출
        }
        else
        {
            CameraShaker.Shake(0.5f, 0.2f);
            HpChanged?.Invoke(CurrentHP, maxHP); // ★ 감소 알림
        }
    }

    /// <summary>회복이 필요하면 사용(최대치 초과 방지)</summary>
    public void Heal(int amount)
    {
        if (IsDead) return;
        if (amount <= 0) return;

        int prev = CurrentHP;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);

        if (CurrentHP != prev)
            HpChanged?.Invoke(CurrentHP, maxHP); // ★ 회복 알림
    }

    /// <summary>P1 사망 처리: 조작불가 + Ground 레이어 + 정적화</summary>
    public void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // 캐리 중이면 안전하게 놓기(던지지 않음)
        if (move != null && move.isCarrying && move.otherPlayer != null)
        {
            var op = move.otherPlayer;
            op.transform.SetParent(null, true);
            if (op.rb) op.rb.simulated = true;
            op.isCarried = false;
            move.isCarrying = false;
            move.carryset = false;
        }

        // 이동/입력 차단
        if (move) move.enabled = false;

        // 물리 정지 및 정적 발판화
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        // 레이어 변경
        int groundIdx = LayerMask.NameToLayer(groundLayerName);
        if (groundIdx >= 0) gameObject.layer = groundIdx;
        else Debug.LogWarning($"[Player1HP] Ground 레이어 '{groundLayerName}'를 찾을 수 없습니다.");

        swap.swapsup = false;
        // 애니메이터 dead 플래그
        if (anim && !string.IsNullOrEmpty(deadBoolName))
            anim.SetBool(deadBoolName, true);
        Dead = true;
        // ★ 마지막으로 UI에 0 동기화 & 사망 알림
        HpChanged?.Invoke(0, maxHP);
        Died?.Invoke();

        Debug.Log("[Player1HP] 사망 처리 완료: 조작불가, Ground 레이어, Static 고정(씬 리로드 없음)");
    }
}
