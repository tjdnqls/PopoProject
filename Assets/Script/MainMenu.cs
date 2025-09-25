using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenu : MonoBehaviour
{
    [Header("Panels")]
    public GameObject optionsPanel;

    [Header("UI (assign GameObject for dropdown, not component)")]
    public GameObject screenModeDropdownGO;

    private Dropdown uiDropdown;
    private TMP_Dropdown tmpDropdown;

    private void Start()
    {
        if (screenModeDropdownGO != null)
        {
            uiDropdown = screenModeDropdownGO.GetComponent<Dropdown>();
            tmpDropdown = screenModeDropdownGO.GetComponent<TMP_Dropdown>();
        }

        if (uiDropdown == null && tmpDropdown == null)
        {
            uiDropdown = FindObjectOfType<Dropdown>();
            if (uiDropdown == null)
                tmpDropdown = FindObjectOfType<TMP_Dropdown>();
        }

        if (uiDropdown != null)
        {
            uiDropdown.value = Screen.fullScreen ? 1 : 0;
            uiDropdown.onValueChanged.AddListener(SetScreenMode);
        }
        else if (tmpDropdown != null)
        {
            tmpDropdown.value = Screen.fullScreen ? 1 : 0;
            tmpDropdown.onValueChanged.AddListener(SetScreenMode);
        }
        else
        {
            Debug.LogWarning("MainMenu: Screen mode dropdown not found. Assign a Dropdown or TMP_Dropdown in the inspector (or set screenModeDropdownGO).");
        }
    }

    private void Update()
    {
        // ESC 키가 눌렸을 때 옵션 패널의 활성 상태를 토글(반전)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (optionsPanel != null)
            {
                optionsPanel.SetActive(!optionsPanel.activeSelf);
            }
        }
    }

    // 아래의 모든 함수는 기존 코드와 동일합니다.
    public void StartGame()
    {
        SceneManager.LoadScene("GameScene");
    }

    public void OpenOptions()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(true);
    }

    public void CloseOptions()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetScreenMode(int index)
    {
        if (index == 0)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
        }
        else if (index == 1)
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = true;
        }
    }
}