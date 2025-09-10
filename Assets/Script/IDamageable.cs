// IDamageable.cs
using UnityEngine;

namespace Game.Combat
{
    public interface IDamageable
    {
        void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal);
    }
}
