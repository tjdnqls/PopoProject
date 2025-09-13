// ===================== Player2HP.cs =====================
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player2HP : MonoBehaviour, global::IDamageable
{
    [Header("HP")]
    [SerializeField] private int maxHP = 1;
    public int CurrentHP { get; private set; }
    public bool IsDead { get; private set; }
    private bool _sceneReloading = false;
    public SwapController playerID;

    private bool _reloading;

    void Awake()
    {
        CurrentHP = Mathf.Max(1, maxHP);
    }

    // 표준 인터페이스(3파라미터) — 내부 단순 위임
    void global::IDamageable.TakeDamage(int dmg, Vector2 hitPoint, Vector2 hitNormal)
    {
        TakeDamage(dmg);
    }

    // 기존 단순 버전(외부 메시지/폴백 용)
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return;
        int amount = Mathf.Max(1, dmg);
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        if (CurrentHP == 0) Die();
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        if (amount <= 0) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (_sceneReloading) return;
        _sceneReloading = true;

        string sceneName = SceneManager.GetActiveScene().name;
        // 화면 외곽→시계 방향 상자 와이프 후 리로드, 이후 반시계로 해제 (연출은 SpiralBoxWipe에서 담당)
        SpiralBoxWipe.Run(sceneName);
    }
}
