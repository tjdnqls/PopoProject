using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

[DisallowMultipleComponent]
public class Player1HP : MonoBehaviour, global::IDamageable
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
    public Animator rb2;
    // Player1HP 필드 구역 어딘가
    [SerializeField] private float hurtDuration = 0.2f;
    private Coroutine _hurtCo;

    [Header("Layers (사망 시 Ground로 변경)")]
    [SerializeField] private string groundLayerName = "Ground";

    [Header("Optional")]
    [SerializeField] private string deadBoolName = "dead"; // Animator bool 파라미터명(있으면 세팅)

    [Header("Timing")]
    [SerializeField] private float swapDisableDelay = 1.5f;
    // ---- Player1HP.cs 상단 필드 추가 ----
    [Header("Revive")]
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private float reviveIFrame = 1.2f;    // 부활 후 무적 시간
    private int _originalLayer;
    private float _invincibleUntil = -1f;
    public bool IsInvincible => Time.time < _invincibleUntil;

    // === 사망 낙하 옵션 ===
    [Header("Death Fall")]
    [SerializeField] private bool keepFallingOnDeath = true;  // 죽어도 낙하 유지
    [SerializeField] private float deadHorizontalDamp = 6f;   // 사망 후 수평 감쇠
    [SerializeField] private bool makeStaticOnLand = true;    // 착지 후 시체를 Static으로 고정
    [SerializeField] private float landStaticDelay = 0.10f;   // 착지 감지 후 약간의 지연
    [SerializeField] private float maxDeathFallSeconds = 6f;  // 안전 타임아웃

    private PlayerMouseMovement move;
    private Rigidbody2D rb;
    private Animator anim;

    void Awake()
    {
        move = GetComponent<PlayerMouseMovement>();
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        CurrentHP = Mathf.Max(1, maxHP);
        _originalLayer = gameObject.layer;
        // 시작 상태 브로드캐스트(초기 UI 동기화)
        HpChanged?.Invoke(CurrentHP, maxHP);
    }

    /// <summary>외부에서 데미지 줄 때 사용</summary>
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead || IsInvincible) return;

        int amount = Mathf.Max(1, dmg);
        int prev = CurrentHP;
        CurrentHP = Mathf.Max(0, CurrentHP - amount);

        if (CurrentHP <= 0)
        {
            CameraShaker.Shake(0.5f, 0.2f);
            rb2.SetBool("hurt", true);
            rb2.SetBool("death", true);
            Die(); // Die() 내부에서 HpChanged(0, max)와 Died 호출
        }
        else
        {
            if (rb2)
            {
                rb2.SetBool("hurt", true);
                SoundManager.Play("KnightHit", transform);
                // 이전 타이머 있으면 리셋
                if (_hurtCo != null) StopCoroutine(_hurtCo);
                _hurtCo = StartCoroutine(HurtOffAfter(hurtDuration));
            }

            CameraShaker.Shake(0.5f, 0.2f);
            HpChanged?.Invoke(CurrentHP, maxHP);
        }
    }

    // ChargerSentinelAI, Monster 등에서 이 시그니처로 호출합니다.
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        TakeDamage(amount); // 기존 단일 인자 버전 재사용
    }

    // ================== 추가: SendMessage 폴백 대응 ==================
    public void OnHit(int damage)
    {
        TakeDamage(damage);
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

    /// <summary>P1 사망 처리: 조작불가 + (낙하 유지) + 착지 후 고정</summary>
    public void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // 캐리 중이면 안전하게 내려놓기(던지지 않음)
        if (move != null && move.isCarrying && move.otherPlayer != null)
        {
            var op = move.otherPlayer;
            op.transform.SetParent(null, true);
            move.SetOtherPlayerVisible(true);
            if (op.rb) op.rb.simulated = true;
            op.isCarried = false;
            move.isCarrying = false;
            move.extraAirJumps = 1;
        }

        // 이동/입력 차단
        if (move) move.enabled = false;

        // 애니메이터 플래그
        if (rb2) rb2.SetBool("death", true);
        if (anim && !string.IsNullOrEmpty(deadBoolName)) anim.SetBool(deadBoolName, true);

        // ▼▼ 기존: 여기서 Static/중력0/레이어 Ground → 공중정지 원인
        //          이제는 낙하를 유지하고, 착지 후에만 Static/레이어 Ground로 전환
        if (keepFallingOnDeath && rb != null)
        {
            // 낙하 보장 세팅
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            // PlayerMouseMovement 세팅을 최대한 재활용 (없으면 적당한 값)
            float fallGravity = (move != null) ? move.gravityScaleFall : 5f;
            rb.gravityScale = fallGravity;

            // 낙하 상태 유지 코루틴 시작
            StartCoroutine(DeathFallRoutine());
        }
        else
        {
            // 원래 동작(즉시 고정)이 필요할 때를 위해 남겨둠
            MakeStaticAndGroundNow();
        }

        if (swap != null)
        {
            if (swapDisableDelay <= 0f) swap.swapsup = false;
            else StartCoroutine(DisableSwapAfterDelay());
        }

        HpChanged?.Invoke(0, maxHP);
        Died?.Invoke();

        Debug.Log("[Player1HP] 사망 처리: 낙하 유지 → 착지 후 고정(옵션)");
    }

    // ---- 강제 부활 본체 ----
    public void ForceReviveAt(Vector3 pos)
    {
        // --- 기본 복원 ---
        IsDead = false;
        Dead = false;                                // ✅ P1 외부 참조용 데드 플래그도 해제
        CurrentHP = Mathf.Max(1, maxHP);

        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!anim) anim = GetComponent<Animator>();
        if (!move) move = GetComponent<PlayerMouseMovement>();

        // 레이어/물리/위치
        gameObject.layer = LayerMask.NameToLayer("Player");
        if (rb)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = move ? move.gravityScaleFall : 3f;
        }
        transform.position = pos;

        // 조작/애니 재활성
        if (move)
        {
            move.enabled = true;
            move.ResetJumpStateOnRevive(assumeGrounded: true, restoreExtraJumpsTo: 1);
        }
        if (rb2) rb2.SetBool("death", false);
        if (anim && !string.IsNullOrEmpty(deadBoolName)) anim.SetBool(deadBoolName, false);

        // HP UI 갱신 이벤트
        HpChanged?.Invoke(CurrentHP, maxHP);

        if (move) move.OnRevivedSafe();

    }

    private IEnumerator DeathFallRoutine()
    {
        float t0 = Time.time;
        int groundedFrames = 0;

        while (Time.time - t0 < maxDeathFallSeconds)
        {
            // 동적/중력 상태 유지(혹시 다른 스크립트가 바꿔도 복구)
            if (rb == null) yield break;
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 하강 중 중력
            float fallGravity = (move != null) ? move.gravityScaleFall : rb.gravityScale;
            rb.gravityScale = fallGravity;

            // 수평 감쇠(자연스러운 정지 느낌)
            Vector2 v = rb.linearVelocity;
            if (deadHorizontalDamp > 0f)
                v.x = Mathf.MoveTowards(v.x, 0f, deadHorizontalDamp * Time.fixedDeltaTime);
            rb.linearVelocity = v;

            // 착지 판정(가능하면 PlayerMouseMovement의 접지 체크 사용)
            bool grounded = (move != null) ? move.IsGroundedStrictSmall_Public() : false;
            if (grounded) groundedFrames++; else groundedFrames = 0;

            if (groundedFrames >= 2) break; // 2프레임 연속 접지 시 착지로 간주

            yield return new WaitForFixedUpdate();
        }

        if (makeStaticOnLand)
        {
            if (landStaticDelay > 0f) yield return new WaitForSeconds(landStaticDelay);
            MakeStaticAndGroundNow();
        }
    }
    private System.Collections.IEnumerator HurtOffAfter(float sec)
    {
        yield return new WaitForSeconds(sec);
        if (!IsDead && rb2) rb2.SetBool("hurt", false);
        _hurtCo = null;
    }

    private void MakeStaticAndGroundNow()
    {
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        int groundIdx = LayerMask.NameToLayer(groundLayerName);
        if (groundIdx >= 0) gameObject.layer = groundIdx;
        else Debug.LogWarning($"[Player1HP] Ground 레이어 '{groundLayerName}'를 찾을 수 없습니다.");
    }

    private IEnumerator DisableSwapAfterDelay()
    {
        yield return new WaitForSecondsRealtime(swapDisableDelay);
        if (swap != null) swap.swapsup = false;
        Dead = true;
    }
}
