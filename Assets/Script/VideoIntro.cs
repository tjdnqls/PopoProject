using UnityEngine;
using UnityEngine.Video;
using System.IO;

public class VideoIntro : MonoBehaviour
{
    public VideoPlayer videoPlayer;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Dr.mp4");
        videoPlayer.url = path;
        videoPlayer.Play();
    }
}
