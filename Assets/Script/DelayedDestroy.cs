using UnityEngine;

public class DelayedDestroy : MonoBehaviour
{
    private float destroyAt = -1f;

    // 이미 예약이 있다면 더 빠른 시간으로만 갱신
    public void Schedule(float delay)
    {
        float t = Time.time + Mathf.Max(0f, delay);
        if (destroyAt < 0f || t < destroyAt) destroyAt = t;
    }

    void Update()
    {
        if (destroyAt >= 0f && Time.time >= destroyAt)
        {
            Destroy(gameObject);
        }
    }
}
