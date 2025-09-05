using UnityEngine;

public class OnlyCollideWithGround : MonoBehaviour
{
    [SerializeField] private string groundLayerName = "Ground";

    void OnEnable()
    {
        int myLayer = gameObject.layer;
        int groundLayer = LayerMask.NameToLayer(groundLayerName);

        // 0~31 모든 레이어에 대해, Ground만 충돌 허용, 나머지는 전부 무시
        for (int i = 0; i < 32; i++)
        {
            bool allow = (i == groundLayer);
            Physics2D.IgnoreLayerCollision(myLayer, i, !allow);
        }
    }
}
