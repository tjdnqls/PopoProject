using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SwapPad : MonoBehaviour
{
    [Header("Players (비우면 Tag로 자동 탐색)")]
    public Transform player1;     // Tag: "Player1"
    public Transform player2;     // Tag: "Player2"

    [Header("Swap Settings")]
    public float delaySeconds = 2f;
    public bool keepVelocity = false;   // true면 서로의 속도 교환, false면 0으로 정지
    public bool once = false;           // 한 번만 동작
    public float cooldown = 2.5f;       // 재트리거 쿨타임

    [Header("Cancel Option")]
    public bool cancelIfExit = true;    // ← 패드에서 벗어나면 카운트다운 취소

    [Header("Trigger Filter (선택)")]
    public LayerMask triggerLayers;     // 비워두면 모든 레이어 허용

    private bool busy = false;
    private float lastSwapTime = -999f;
    private int occupants = 0;          // 현재 패드 위에 있는 플레이어 수
    private Coroutine pending;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    void Awake()
    {
        AutoAssignPlayersIfNeeded();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;

        // 레이어 필터
        if (triggerLayers.value != 0 && (triggerLayers.value & (1 << other.gameObject.layer)) == 0)
            return;

        occupants = Mathf.Max(0, occupants + 1);

        if (busy) return;
        if (Time.time - lastSwapTime < cooldown) return;

        if (player1 == null || player2 == null)
        {
            AutoAssignPlayersIfNeeded();
            if (player1 == null || player2 == null) return;
        }

        pending = StartCoroutine(SwapRoutine());
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;

        occupants = Mathf.Max(0, occupants - 1);

        // 떠나면 취소 옵션
        if (cancelIfExit && occupants <= 0 && pending != null)
        {
            StopCoroutine(pending);
            pending = null;
            busy = false;
        }
    }

    IEnumerator SwapRoutine()
    {
        busy = true;

        // delaySeconds 동안 계속 올라와 있어야 진행
        float t = 0f;
        while (t < delaySeconds)
        {
            if (cancelIfExit && occupants <= 0)
            {
                busy = false;
                yield break; // 취소
            }
            t += Time.deltaTime;
            yield return null;
        }

        // 최종 스왑
        if (player1 == null || player2 == null)
        {
            busy = false;
            yield break;
        }

        Vector3 p1 = player1.position;
        Vector3 p2 = player2.position;

        Rigidbody2D rb1 = player1.GetComponent<Rigidbody2D>();
        Rigidbody2D rb2 = player2.GetComponent<Rigidbody2D>();

        Vector2 v1 = Vector2.zero, v2 = Vector2.zero;
        if (keepVelocity)
        {
            if (rb1) v1 = rb1.linearVelocity;
            if (rb2) v2 = rb2.linearVelocity;
        }

        if (rb1) rb1.position = p2; else player1.position = p2;
        if (rb2) rb2.position = p1; else player2.position = p1;

        if (keepVelocity)
        {
            if (rb1) rb1.linearVelocity = v2;
            if (rb2) rb2.linearVelocity = v1;
        }
        else
        {
            if (rb1) rb1.linearVelocity = Vector2.zero;
            if (rb2) rb2.linearVelocity = Vector2.zero;
        }

        lastSwapTime = Time.time;
        busy = false;
        pending = null;

        if (once) gameObject.SetActive(false);
    }

    bool IsPlayer(Collider2D other)
    {
        return other.CompareTag("Player")
               || other.CompareTag("Player1")
               || other.CompareTag("Player2")
               || other.GetComponent<PlayerMouseMovement>() != null;
    }

    void AutoAssignPlayersIfNeeded()
    {
        if (!player1)
        {
            var p1Obj = GameObject.FindGameObjectWithTag("Player1");
            if (p1Obj) player1 = p1Obj.transform;
        }
        if (!player2)
        {
            var p2Obj = GameObject.FindGameObjectWithTag("Player2");
            if (p2Obj) player2 = p2Obj.transform;
        }
    }

#if UNITY_EDITOR
    void OnValidate() { Reset(); }
    void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col is BoxCollider2D b)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(b.offset, b.size);
        }
    }
#endif
}
