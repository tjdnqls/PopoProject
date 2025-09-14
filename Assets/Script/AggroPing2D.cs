//using System;
//using UnityEngine;

//[RequireComponent(typeof(Rigidbody2D))]
//[RequireComponent(typeof(Collider2D))]
//[DisallowMultipleComponent]
//public class SentinelAggroPing : MonoBehaviour
//{
//    private ChargerSentinelAI _owner;
//    private LayerMask _hitMask;
//    private float _speed;
//    private float _dieAt;
//    private Vector2 _dir;
//    private Rigidbody2D _rb;

//    public void Init(MonsterABPatrolFSM monsterABPatrolFSM, ChargerSentinelAI owner, LayerMask hitMask, float speed, float life, Vector2 dir)
//    {
//        _owner = owner;
//        _hitMask = hitMask;
//        _speed = speed;
//        _dieAt = Time.time + life;
//        _dir = dir.normalized;
//        _rb = GetComponent<Rigidbody2D>();
//        if (_rb)
//        {
//            _rb.gravityScale = 0f;
//            _rb.linearVelocity = _dir * _speed;
//            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
//            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
//        }
//    }

//    private void Update()
//    {
//        if (Time.time >= _dieAt) Destroy(gameObject);
//        if (_rb) _rb.linearVelocity = _dir * _speed;
//        else transform.position += (Vector3)(_dir * _speed * Time.deltaTime);
//    }

//    private void OnTriggerEnter2D(Collider2D other)
//    {
//        if (((1 << other.gameObject.layer) & _hitMask) == 0) return;

//        var root = other.attachedRigidbody ? other.attachedRigidbody.transform.root : other.transform.root;
//        _owner?.OnAggroPingHit(root);
//        Destroy(gameObject);
//    }

//    internal void Init(MonsterABPatrolFSM monsterABPatrolFSM, Transform target, float pingSpeed, float pingLifetime, LayerMask groundMask, LayerMask playerMask, LayerMask obstacleMask, Action value)
//    {
//        throw new NotImplementedException();
//    }
//}