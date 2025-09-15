using UnityEngine;

public class UIManager : MonoBehaviour
{
    [SerializeField] private bool IsPause = true;

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (IsPause)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
            IsPause = !IsPause;
        }
    }

    void PauseGame()
    {
        Time.timeScale = 0; // 게임 일시정지
    }
    void ResumeGame()
    {
        Time.timeScale = 1; // 게임 재개
    }
}
