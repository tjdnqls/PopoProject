using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player2HP : MonoBehaviour
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
        SpiralBoxWipe.Run(sceneName);
    }
}
