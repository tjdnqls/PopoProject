using UnityEngine;

public class DebugPanelToggle : MonoBehaviour
{
    public GameObject debugPanel;  // 디버그 UI 패널

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            debugPanel.SetActive(!debugPanel.activeSelf);
        }
    }
}
