// IgnoreDefaultLayer2D.cs
using UnityEngine;

[DisallowMultipleComponent]
public class IgnoreDefaultLayer2D : MonoBehaviour
{
    [SerializeField] private bool revertOnDisable = false;

    private int myLayer;
    private int defaultLayer;
    private bool prevState;

    private void Awake()
    {
        myLayer = gameObject.layer;
        defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0)
        {
            Debug.LogWarning("[IgnoreDefaultLayer2D] 'Default' 레이어를 찾지 못했습니다.");
            enabled = false;
            return;
        }

        // 기존 상태 저장 후, Default와의 충돌 무시
        prevState = Physics2D.GetIgnoreLayerCollision(myLayer, defaultLayer);
        Physics2D.IgnoreLayerCollision(myLayer, defaultLayer, true);
    }

    private void OnDisable()
    {
        if (revertOnDisable && defaultLayer >= 0)
        {
            // 원상복구 (필요할 때만)
            Physics2D.IgnoreLayerCollision(myLayer, defaultLayer, prevState);
        }
    }
}
