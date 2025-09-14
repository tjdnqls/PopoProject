// AggroPingCompat.cs
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Game.AI
{
    /// <summary>
    /// SentinelAggroPing 시그니처가 프로젝트마다 다른 문제를 우회하기 위한 호환 레이어.
    /// - 가능한 여러 Init/Launch 오버로드를 리플렉션으로 시도
    /// - 아무것도 없으면 public 필드/프로퍼티를 세팅하고 Rigidbody2D로 방향/속도를 밀어줌
    /// - 파괴 시점 콜백이 없으면 OnDestroy 훅(자동 콜백)을 붙여 _activePings 감소 보장
    /// </summary>
    public static class AggroPingCompat
    {
        public static void Init(
            Component ping,                // SentinelAggroPing 컴포넌트(또는 그 파생)
            IAggroPingOwner owner,         // 소유자(몬스터)
            Transform target,              // 조준 대상
            Vector2 origin,                // 발사 위치
            Vector2 aimPoint,              // 조준 월드 포인트(타깃 콜라이더 센터 등)
            float speed,
            float lifetime,
            LayerMask groundMask,
            LayerMask playerMask,
            LayerMask obstacleMask,
            Action onDespawn               // 핑 소멸 시 호출할 콜백(활성 카운트 감소 등)
        )
        {
            if (!ping) return;

            Vector2 dir = (aimPoint - origin);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();

            // 1) 널리 쓰이는 형태들부터 시도 (이름/개수만 맞으면 통과)
            if (TryCall(ping, "Init", owner, target, speed, lifetime, groundMask, playerMask, obstacleMask, onDespawn)) return;
            if (TryCall(ping, "Init", owner, origin, dir, speed, lifetime, groundMask, playerMask, obstacleMask, onDespawn)) return;
            if (TryCall(ping, "Init", owner, target, speed, lifetime)) { AttachAutoCallback(ping, lifetime, onDespawn); return; }
            if (TryCall(ping, "Init", owner, target)) { AttachAutoCallback(ping, lifetime, onDespawn); return; }

            // 2) Init이 없다면 Launch/Fire 류도 시도
            if (TryCall(ping, "Launch", owner, target, dir, speed, lifetime)) { AttachAutoCallback(ping, lifetime, onDespawn); return; }
            if (TryCall(ping, "Fire", owner, target, dir, speed, lifetime)) { AttachAutoCallback(ping, lifetime, onDespawn); return; }

            // 3) 마지막 수단: 필드/프로퍼티 세팅 + Rigidbody2D로 밀기
            TrySet(ping, "owner", owner); TrySet(ping, "Owner", owner);
            TrySet(ping, "target", target); TrySet(ping, "Target", target);
            TrySet(ping, "speed", speed); TrySet(ping, "Speed", speed);
            TrySet(ping, "lifetime", lifetime); TrySet(ping, "Lifetime", lifetime);
            TrySet(ping, "groundMask", groundMask);
            TrySet(ping, "playerMask", playerMask);
            TrySet(ping, "obstacleMask", obstacleMask);

            if (ping.TryGetComponent<Rigidbody2D>(out var r2d))
                r2d.linearVelocity = dir * speed;

            AttachAutoCallback(ping, lifetime, onDespawn);
        }

        // ---------- 내부 유틸 ----------
        private static bool TryCall(Component obj, string method, params object[] args)
        {
            var t = obj.GetType();
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name != method) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;

                bool ok = true;
                for (int k = 0; k < ps.Length; k++)
                {
                    if (args[k] == null) continue;
                    var pt = ps[k].ParameterType;
                    if (!pt.IsInstanceOfType(args[k]))
                    {
                        // 값형(Primitive/Struct) 허용(박싱) — float, int, LayerMask 등
                        if (pt.IsValueType == false) { ok = false; break; }
                    }
                }
                if (!ok) continue;

                try { m.Invoke(obj, args); return true; }
                catch { /*다음 후보*/ }
            }
            return false;
        }

        private static void TrySet(Component obj, string name, object value)
        {
            var t = obj.GetType();
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { f.SetValue(obj, value); return; } catch { } }
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) { try { p.SetValue(obj, value, null); } catch { } }
        }

        private static void AttachAutoCallback(Component ping, float lifetime, Action onDespawn)
        {
            var auto = ping.gameObject.AddComponent<_PingAutoCallback>();
            auto.delay = Mathf.Max(0.01f, lifetime + 0.1f); // 핑이 자체 파괴 못하면 안전망으로 제거
            auto.onDestroyed = onDespawn;
        }

        // 파괴 시점 보장용 보조 컴포넌트
        private class _PingAutoCallback : MonoBehaviour
        {
            public float delay = -1f;
            public Action onDestroyed;

            private IEnumerator Start()
            {
                if (delay > 0f) yield return new WaitForSeconds(delay);
                // 핑이 스스로 파괴되지 않으면 안전망 제거
                if (this && gameObject) Destroy(gameObject);
            }
            private void OnDestroy() { try { onDestroyed?.Invoke(); } catch { } }
        }
    }
}
