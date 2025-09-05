using System;
using UnityEngine;

public class SwapController : MonoBehaviour
{
    public enum PlayerChar { P1, P2 }
    public PlayerChar charSelect = PlayerChar.P1; // 기본은 P1 선택
    public PlayerMouseMovement carry;

    public PlayerChar Current; // 실제 프로젝트의 소스 오브 트루스

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (carry.carryset == false)
            {
                // P1 <-> P2 토글
                charSelect = (charSelect == PlayerChar.P1) ? PlayerChar.P2 : PlayerChar.P1;
                Debug.Log($"[SwapController] 현재 선택 = {charSelect}");
            }
        }
    }

}
