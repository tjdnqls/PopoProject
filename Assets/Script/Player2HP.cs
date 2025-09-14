// ===================== Player2HP.cs (Safe Animator) =====================
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player2HP : MonoBehaviour, global::IDamageable
{
    [Header("HP")]
    [SerializeField] private int maxHP = 1;
    public int CurrentHP { get; private set; }
    public bool IsDead { get; private set; }

    [Header("Anim (optional)")]
    [SerializeField] private Animator rb2;                 // Inspector로 할당 가능
    [SerializeField] private string deathBoolName = "death";

    private bool _sceneReloading = false;

    void OnValidate()
    {
        if (maxHP < 1) maxHP = 1;
        AutoWireAnimator();
    }

    void Reset()
    {
        AutoWireAnimator();
    }

    void Awake()
    {
        CurrentHP = maxHP;
        AutoWireAnimator();
    }

    private void AutoWireAnimator()
    {
        if (rb2 == null)
        {
            rb2 = GetComponent<Animator>();
            if (rb2 == null)
                rb2 = GetComponentInChildren<Animator>(true); // 루트에 없고 자식에 있을 때 커버
        }
    }

    // 표준 인터페이스(3파라미터)
    void global::IDamageable.TakeDamage(int dmg, Vector2 hitPoint, Vector2 hitNormal)
    {
        TakeDamage(dmg);
    }

    // 단순 버전
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return;
        int amount = Mathf.Max(1, dmg);
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        if (CurrentHP == 0) Die();
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Animator가 있으면 파라미터 세팅, 없어도 그냥 넘어가서 리로드 연출 진행
        if (rb2 != null)
        {
            try { rb2.SetBool(deathBoolName, true); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Player2HP] Animator param set skipped: {e.Message}");
            }
        }

        if (_sceneReloading) return;
        _sceneReloading = true;

        string sceneName = SceneManager.GetActiveScene().name;
        // 검은 상자 시계방향 → 씬 리로드 → 반시계 해제 (연출은 SpiralBoxWipe가 담당)
        SpiralBoxWipe.Run(sceneName);
    }
}
