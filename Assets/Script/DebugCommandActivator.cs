using UnityEngine;
using UnityEngine.UI;

public class DebugCommandActivator : MonoBehaviour
{
    public GameObject debugPanel;

    // ↑↑↓↓←→←→AB
    private KeyCode[] konamiCode = new KeyCode[]
    {
        KeyCode.UpArrow, KeyCode.UpArrow,
        KeyCode.DownArrow, KeyCode.DownArrow,
        KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.A, KeyCode.B
    };

    private int currentIndex = 0;

    void Update()
    {
        if (Input.anyKeyDown)
        {
            if (Input.GetKeyDown(konamiCode[currentIndex]))
            {
                currentIndex++;
                if (currentIndex >= konamiCode.Length)
                {
                    ToggleDebugPanel();
                    currentIndex = 0; // 성공 후 리셋
                }
            }
            else if (Input.anyKeyDown)
            {
                // 잘못된 입력이면 리셋
                currentIndex = 0;
            }
        }
    }

    void ToggleDebugPanel()
    {
        if (debugPanel != null)
        {
            debugPanel.SetActive(!debugPanel.activeSelf);
            Debug.Log(" 디버그 패널 토글 성공! 피티쨩 커맨드 발동~! ");
        }
    }
}
