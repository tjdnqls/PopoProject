using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameSceneManager : MonoBehaviour
{
    public static GameSceneManager Instance { get; private set; }

    [Header("로딩씬 및 기본 씬 설정")]
    public string loadingSceneName = "Loading";      // 로딩씬
    public string defaultNextScene = "Stage1";      // 로딩 끝난 후 씬

    [Header("Player2 거리 기반 트리거")]
    public Transform triggerTarget;                 // Player2와 거리 측정할 기준 오브젝트
    public float triggerDistance = 3f;              // Player2가 가까이 오면 씬 전환
    public string triggerNextScene;                 // Player2가 가까워지면 들어갈 씬

    private string nextSceneName;
    private bool triggered = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (!triggered && triggerTarget != null)
        {
            GameObject player2 = GameObject.FindGameObjectWithTag("Player2");
            if (player2 != null)
            {
                float distance = Vector3.Distance(player2.transform.position, triggerTarget.position);
                if (distance <= triggerDistance)
                {
                    // 지정 씬이 있으면 그것, 없으면 기본 씬
                    LoadScene(string.IsNullOrEmpty(triggerNextScene) ? defaultNextScene : triggerNextScene);
                    triggered = true; // 한 번 실행 후 중복 방지
                }
            }
        }
    }

    public void LoadScene(string sceneName = null)
    {
        nextSceneName = string.IsNullOrEmpty(sceneName) ? defaultNextScene : sceneName;
        StartCoroutine(LoadSceneRoutine());
    }

    private IEnumerator LoadSceneRoutine()
    {
        // 1. 로딩씬으로 이동
        if (!string.IsNullOrEmpty(loadingSceneName))
        {
            AsyncOperation loadingOp = SceneManager.LoadSceneAsync(loadingSceneName);
            yield return new WaitUntil(() => loadingOp.isDone);
        }

        // 2. 실제 다음 씬 로드 (비동기)
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        asyncLoad.allowSceneActivation = false;

        float minLoadTime = 2f;
        float timer = 0f;

        while (!asyncLoad.isDone)
        {
            timer += Time.deltaTime;

            if (asyncLoad.progress >= 0.9f && timer >= minLoadTime)
            {
                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
