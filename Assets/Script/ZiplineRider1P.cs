using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class ZiplineRider1P : MonoBehaviour
{
    [Header("탑승 탐지")]
    [SerializeField] private LayerMask handleLayer;
    [SerializeField, Min(0f)] private float grabRadius = 0.6f;
    [SerializeField] private bool requireAngleGate = true;
    [SerializeField, Range(-1f, 1f)] private float minApproachDot = 0.0f;

    [Header("입력")]
    [SerializeField] private bool requireGrabInput = false;
    [SerializeField] private bool allowMouseGrab = true;

    [Header("탑승 중 물리")]
    [SerializeField] private float rideGravityScale = 0f;
    [SerializeField] private bool freezeRotationOnRide = true;

    [Header("하차/재탑승 제어")]
    [SerializeField, Min(0f)] private float exitSpeedScale = 1.0f;
    [SerializeField, Min(0f)] private float sameHandleCooldown = 0.3f; // 같은 핸들 재탑승 금지

    private Rigidbody2D rb;
    private ZiplineHandle currentHandle;
    private float originalGravity;
    private bool originalFreezeRotation;
    public SmartCameraFollowByWall player;


    private readonly Dictionary<int, float> lastDetachTimeByHandle = new Dictionary<int, float>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        // === 탑승 중: 하차 입력만 처리 ===
        if (currentHandle != null)
        {
            if (IsExitPressedWhileAttached() && player.swapsup == true)
                Detach(applyMomentum: true);
            return;
        }

        // === 비탑승 중: 하차 입력은 무시, 탑승만 처리 ===
        if (!requireGrabInput || IsGrabPressed())
        {
            ZiplineHandle target = FindBestHandleInRadius();
            if (target != null && PassAngleGate(target) && PassSameHandleCooldown(target))
                Attach(target);
        }
    }

    private void FixedUpdate()
    {
        if (currentHandle == null) return;

        Vector3 hp = currentHandle.transform.position;
        rb.position = hp;
        rb.linearVelocity = currentHandle.CurrentLinearVelocity;
        rb.gravityScale = rideGravityScale;
        if (freezeRotationOnRide) rb.freezeRotation = true;
    }

    // ---------- 입력 ----------
    private bool IsGrabPressed()
    {
        bool key = Input.GetKeyDown(KeyCode.E) ||
                   Input.GetKeyDown(KeyCode.W) ||
                   Input.GetKeyDown(KeyCode.UpArrow);

        bool mouse = allowMouseGrab &&
                     (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1));

        return key || mouse;
    }

    // 탑승 중일 때만 true를 반환하는 하차 입력
    private bool IsExitPressedWhileAttached()
    {
        if (currentHandle == null) return false; // 비탑승 시 무조건 차단

        return Input.GetKeyDown(KeyCode.LeftArrow) ||
               Input.GetKeyDown(KeyCode.RightArrow) ||
               Input.GetKeyDown(KeyCode.UpArrow) ||
               Input.GetKeyDown(KeyCode.DownArrow) ||
               Input.GetKeyDown(KeyCode.A) ||
               Input.GetKeyDown(KeyCode.D) ||
               Input.GetKeyDown(KeyCode.W) ||
               Input.GetKeyDown(KeyCode.S) ||
               Input.GetKeyDown(KeyCode.Space) ||
               Input.GetMouseButtonDown(0) ||
               Input.GetMouseButtonDown(1);                                                                                                
    }

    // ---------- 탐지/게이트 ----------
    private ZiplineHandle FindBestHandleInRadius()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, grabRadius, handleLayer);
        float bestSq = float.MaxValue;
        ZiplineHandle best = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var handle = hits[i].GetComponent<ZiplineHandle>();
            if (handle == null) continue;

            float sq = (handle.transform.position - transform.position).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = handle;
            }
        }
        return best;
    }

    private bool PassAngleGate(ZiplineHandle handle)
    {
        if (!requireAngleGate) return true;

        Vector2 toHandle = (handle.transform.position - transform.position).normalized;
        Vector2 tangent = handle.CurrentTangent().normalized;
        return Vector2.Dot(tangent, toHandle) >= minApproachDot;
    }

    private bool PassSameHandleCooldown(ZiplineHandle handle)
    {
        int id = handle.GetInstanceID();
        if (lastDetachTimeByHandle.TryGetValue(id, out float lastTime))
            return Time.time - lastTime >= sameHandleCooldown;
        return true;
    }

    // ---------- 상태 전이 ----------
    private void Attach(ZiplineHandle handle)
    {
        if (currentHandle == handle) return;

        currentHandle = handle;

        originalGravity = rb.gravityScale;
        originalFreezeRotation = rb.freezeRotation;

        rb.gravityScale = rideGravityScale;
        if (freezeRotationOnRide) rb.freezeRotation = true;

        rb.position = currentHandle.transform.position;
        rb.linearVelocity = currentHandle.CurrentLinearVelocity;
    }

    public void Detach(bool applyMomentum)
    {
        if (currentHandle == null) return; // 비탑승 시 하차 호출 무시(안전장치)

        if (applyMomentum)
            rb.linearVelocity = currentHandle.CurrentLinearVelocity * exitSpeedScale;

        int id = currentHandle.GetInstanceID();
        lastDetachTimeByHandle[id] = Time.time;

        rb.gravityScale = originalGravity;
        rb.freezeRotation = originalFreezeRotation;
        currentHandle = null;
    }

    private void OnDisable()
    {
        // 비활성화 시 강제 해제(선택 사항). 비활성화 중에도 붙어있길 원하면 이 블록 제거.
        if (currentHandle != null)
            Detach(applyMomentum: false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }
}
