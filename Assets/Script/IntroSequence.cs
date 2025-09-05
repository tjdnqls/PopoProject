using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public class IntroSequence : MonoBehaviour
{
    [Header("UI Targets")]
    public CanvasGroup textGroup;     // 처음 텍스트
    public CanvasGroup image1Group;   // 이미지 1
    public CanvasGroup image2Group;   // 이미지 2

    [Header("Video")]
    public VideoPlayer videoPlayer;   // 동영상
    public CanvasGroup videoCanvas;   // 비디오 출력용 캔버스
    public string nextSceneName = "SampleScene 1";

    [Header("Durations")]
    public float fadeTime = 1.5f;
    public float stayTime = 1.5f;

    private bool skipRequested = false;

    void Start()
    {
        StartCoroutine(RunSequence());
        
    }

    IEnumerator RunSequence()
    {
        // Step 1: 텍스트
        yield return StartCoroutine(FadeInOut(textGroup));

        // Step 2: 이미지1
        yield return StartCoroutine(FadeInOut(image1Group));

        // Step 3: 이미지2
        yield return StartCoroutine(FadeInOut(image2Group));

        // Step 5: 다음 씬
        SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator FadeInOut(CanvasGroup group)
    {
        // 초기 알파 0
        group.alpha = 0f;
        group.gameObject.SetActive(true);

        // Fade In
        yield return StartCoroutine(Fade(group, 0f, 1f, fadeTime));
        yield return new WaitForSeconds(stayTime);

        // Fade Out
        yield return StartCoroutine(Fade(group, 1f, 0f, fadeTime));
        group.gameObject.SetActive(false);
    }

    IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }

    IEnumerator PlayVideo()
    {
        skipRequested = false;
        videoCanvas.alpha = 1f;
        videoCanvas.gameObject.SetActive(true);
        videoPlayer.time = 9f;
        videoPlayer.Play();

        // 이벤트 등록
        videoPlayer.loopPointReached += OnVideoEnd;

        // 유저 입력 체크
        while (!skipRequested)
        {
            if (Input.GetMouseButtonDown(0)) // 마우스 클릭 시 스킵
            {
                videoPlayer.Stop();
                break;
            }
            yield return null;
        }

        // 정리
        videoCanvas.alpha = 0f;
        videoCanvas.gameObject.SetActive(false);
    }

    void OnVideoEnd(VideoPlayer vp)
    {
        skipRequested = true;
    }
}
