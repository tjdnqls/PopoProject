using UnityEngine;

namespace Game.AI
{
    public interface IAggroPingOwner
    {
        // 핑이 플레이어에 닿았을 때 주인에게 알려줌
        void OnAggroPingHit(Transform hitPlayer);
    }
}

