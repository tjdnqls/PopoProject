using UnityEngine;

public class PlayerHP : MonoBehaviour
{
    public int maxHP = 5;
    public int currentHP;

    public bool IsDead { get; private set; } = false; // 외부에서 읽기 가능, 내부에서만 세팅

    void Start()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return; // 이미 죽었으면 무시

        currentHP -= dmg;
        Debug.Log("플레이어 HP: " + currentHP);

        if (currentHP <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        IsDead = true;
        Debug.Log("플레이어 사망!");

        // TODO: 사망 모션, 조작 불가 처리, Rigidbody2D 멈춤 등
        // (Player1처럼 특수 모션 필요하면 따로 상속/확장 가능)
    }
}
