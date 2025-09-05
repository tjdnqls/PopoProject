using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player1HP : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private int maxHP = 2;
    public int CurrentHP { get; private set; }
    public bool IsDead { get; private set; }

    [Header("Layers (사망 시 Ground로 변경)")]
    [SerializeField] private string groundLayerName = "Ground";

    [Header("Optional")]
    [SerializeField] private string deadBoolName = "dead"; // Animator bool 파라미터명(있으면 세팅)

    private PlayerMouseMovement move;   // 공통/파생 이동 컴포넌트 어느 것이든 OK
    private Rigidbody2D rb;
    private Animator anim;

    void Awake()
    {
        move = GetComponent<PlayerMouseMovement>(); // Player1Movement 또는 기존 공용 스크립트여도 동작
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        CurrentHP = Mathf.Max(1, maxHP);
    }

    /// <summary>외부에서 데미지 줄 때 사용</summary>
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return;
        int amount = Mathf.Max(1, dmg);
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        if (CurrentHP == 0) Die();
    }

    /// <summary>회복이 필요하면 사용(최대치 초과 방지)</summary>
    public void Heal(int amount)
    {
        if (IsDead) return;
        if (amount <= 0) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
    }

    /// <summary>P1 사망 처리: 조작불가 + Ground 레이어 + 정적화</summary>
    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // 1) 캐리 중이면 안전하게 놓기(던지지 않음)
        if (move != null && move.isCarrying && move.otherPlayer != null)
        {
            var op = move.otherPlayer;
            // 원래 부모 복원은 불가(이동 스크립트 내부에 private 보관)하니 월드 최상위로
            op.transform.SetParent(null, true);
            if (op.rb) op.rb.simulated = true;
            op.isCarried = false;
            move.isCarrying = false;
            move.carryset = false;
        }

        // 2) 이동/입력 차단 (컴포넌트 자체 비활성)
        if (move) move.enabled = false;

        // 3) 물리 정지 및 정적 발판화
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        // 4) 레이어 변경: Player -> Ground
        int groundIdx = LayerMask.NameToLayer(groundLayerName);
        if (groundIdx >= 0) gameObject.layer = groundIdx;
        else Debug.LogWarning($"[Player1HP] Ground 레이어 '{groundLayerName}'를 찾을 수 없습니다.");

        // 5) 선택: 애니메이터에 dead 플래그 세팅
        if (anim && !string.IsNullOrEmpty(deadBoolName))
            anim.SetBool(deadBoolName, true);

        Debug.Log("[Player1HP] 사망 처리 완료: 조작불가, Ground 레이어, Static 고정(씬 리로드 없음)");
    }
}
