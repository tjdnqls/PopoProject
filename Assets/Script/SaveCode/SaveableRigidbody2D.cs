using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SaveID))]
[DisallowMultipleComponent]
public class SaveableRigidbody2D : MonoBehaviour, ISaveable
{
    [Serializable]
    private struct State
    {
        public Vector2 pos;
        public float rotZ;
        public Vector2 linVel;
    }

    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public object CaptureState()
    {
        return new State
        {
            pos = transform.position,
            rotZ = transform.eulerAngles.z,
            linVel = rb.linearVelocity
        };
    }

    public void RestoreState(object boxed)
    {
        var s = (State)boxed;
        transform.position = s.pos;
        var e = transform.eulerAngles;
        e.z = s.rotZ;
        transform.eulerAngles = e;

        // 물리 스텝 뒤집힘 방지: 위치/회전 세팅 후 속도
        rb.linearVelocity = s.linVel;
    }
}
