using UnityEngine;
using System.Collections;

public class SensorTrigger : MonoBehaviour
{
    public string trapTag = "SensorTrap"; // 감지할 트랩 태그
    public float moveDistance = 2f;     // 이동 거리
    public float moveSpeed = 2f;        // 이동 속도
    public bool moveUp = true;          // 위로 이동할지 아래로 이동할지
    public bool loop = false;           // 반복 이동 여부
    public bool autoLoop = false;       // 게임 시작 시 자동 반복 여부 추가!

    private bool activated = false;

    void Start()
    {
        if (autoLoop && loop)
        {
            // 자신의 자식 중 trapTag를 가진 것만 작동
            foreach (Transform child in transform)
            {
                if (child.CompareTag(trapTag))
                {
                    Vector3 direction = Vector3.up * (moveUp ? moveDistance : -moveDistance);
                    StartCoroutine(LoopMoveTrap(child, direction));
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && !activated)
        {
            activated = true;

            // 자신의 자식 중 trapTag를 가진 것만 작동
            foreach (Transform child in transform)
            {
                if (child.CompareTag(trapTag))
                {
                    Vector3 direction = Vector3.up * (moveUp ? moveDistance : -moveDistance);
                    Vector3 targetPos = child.position + direction;

                    if (loop)
                    {
                        StartCoroutine(LoopMoveTrap(child, direction));
                    }
                    else
                    {
                        StartCoroutine(MoveTrap(child, targetPos));
                    }
                }
            }
        }
    }

    private IEnumerator MoveTrap(Transform obj, Vector3 target)
    {
        while (Vector3.Distance(obj.position, target) > 0.01f)
        {
            obj.position = Vector3.MoveTowards(obj.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
    }

    private IEnumerator LoopMoveTrap(Transform obj, Vector3 direction)
    {
        Vector3 start = obj.position;
        Vector3 end = start + direction;

        while (true)
        {
            // 이동
            while (Vector3.Distance(obj.position, end) > 0.01f)
            {
                obj.position = Vector3.MoveTowards(obj.position, end, moveSpeed * Time.deltaTime);
                yield return null;
            }

            // 방향 반전
            Vector3 temp = start;
            start = end;
            end = temp;
        }
    }
}