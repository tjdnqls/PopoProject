using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)] // 플레이어보다 먼저 실행되게
public class MovingPlatformMotion2D : MonoBehaviour
{
    public Vector2 Delta { get; private set; }        // 이번 FixedStep에서의 세계 좌표 이동량
    public Vector2 WorldVelocity { get; private set; } // 참조용(필수 아님)

    Rigidbody2D rb;
    Vector2 lastPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        lastPos = rb ? rb.position : (Vector2)transform.position;
    }

    void FixedUpdate()
    {
        Vector2 curr = rb ? rb.position : (Vector2)transform.position;
        Delta = curr - lastPos;
        WorldVelocity = Delta / Time.fixedDeltaTime;
        lastPos = curr;
    }
}