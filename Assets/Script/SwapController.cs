using System;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SwapController : MonoBehaviour
{
    public enum PlayerChar { P1, P2 }
    public PlayerChar charSelect = PlayerChar.P1; // 기본은 P1 선택
    public PlayerMouseMovement carry;
    public Player1HP dead;
    public bool coubt;
    public PlayerChar Current; // 실제 프로젝트의 소스 오브 트루스

    void Update()
    {
        if (SpiralBoxWipe.IsBusy)
        {
            coubt = false;
            return;
        }

        if (dead.Dead == true)
        {
            coubt = false;
            charSelect = PlayerChar.P2;
            Current = PlayerChar.P2;
        }
        else
        {
            coubt = true;
        }

        if (Input.GetKeyDown(KeyCode.Tab) && coubt == true)
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
