using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
[RequireComponent(typeof(AudioSource))]
public class TooltipVideoPlayer : MonoBehaviour
{
    [Header("UI Target")]
    [SerializeField] private RawImage videoRawImage;     // 영상 표시할 RawImage
    [SerializeField] private string videoFileName;       // StreamingAssets 안 파일명 (예: sword.mp4)

    [Header("Options")]
    [SerializeField] private bool loop = false;          // Inspector에서 체크로 반복 재생 여부 설정

    private VideoPlayer videoPlayer;
    private AudioSource audioSource;

    private void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        audioSource = GetComponent<AudioSource>();

        // 경로 설정 (StreamingAssets 폴더 안)
        string videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, videoFileName);
        videoPlayer.url = videoPath;

        // VideoPlayer 기본 설정
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = loop;  // Loop 옵션 반영
        videoPlayer.renderMode = VideoRenderMode.APIOnly;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.SetTargetAudioSource(0, audioSource);

        // 준비 완료 이벤트 연결
        videoPlayer.prepareCompleted += OnVideoPrepared;
    }

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(videoFileName))
        {
            videoPlayer.Prepare();
        }
    }

    private void OnDisable()
    {
        StopVideo();
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        videoRawImage.texture = videoPlayer.texture;
        videoPlayer.Play();
        audioSource.Play();
    }

    private void StopVideo()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
            audioSource.Stop();
        }

        if (videoRawImage != null)
            videoRawImage.texture = null;
    }
}
