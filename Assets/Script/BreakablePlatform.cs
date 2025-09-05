using UnityEngine;

public class BreakablePlatform : MonoBehaviour
{
    [Tooltip("플레이어로 인식할 태그(비우면 누구든)")]
    public string playerTag = "Player";

    [Tooltip("이 하강 속도(절댓값) 이상일 때만 파괴. 점프 착지만 노리고 싶으면 2~4 권장")]
    public float minDownSpeed = 3f;

    [Tooltip("접촉 노멀의 y가 이 값 이상이면 '위에서 눌렀다'로 판정")]
    [Range(0f, 1f)] public float normalYThreshold = 0.2f;

    private void OnCollisionEnter2D(Collision2D c)
    {
        var other = c.collider;

        // 플레이어만 (원하면 조건 제거 가능)
        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag)) return;

        // 플레이어 RB 필요
        var prb = c.rigidbody;
        if (!prb) return;

        // 1) 하강 중인지 (점프 착지/낙하)
        float vDown = -prb.linearVelocity.y;   // 하강이면 +값
        if (vDown < minDownSpeed) return;

        // 2) 위에서 눌렀는지(법선 체크) — 옆/아래쪽 접촉 배제
        for (int i = 0; i < c.contactCount; i++)
        {
            if (c.GetContact(i).normal.y >= normalYThreshold)
            {
                Destroy(gameObject);          // 바로 파괴
                return;
            }
        }
    }
}
