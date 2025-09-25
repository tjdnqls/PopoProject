using UnityEngine;
using UnityEngine.UI;

public class MainBGMController : MonoBehaviour
{
    public AudioSource MainBGM;
    public Slider volumeSlider;

    void Start()
    {
        if (MainBGM != null)
        {
            MainBGM.Play();
        }

        if (volumeSlider != null)
        {
            // 슬라이더의 최소/최대값을 데시벨 단위로 설정
            volumeSlider.minValue = -80f; // 거의 들리지 않는 볼륨
            volumeSlider.maxValue = 0f;   // 최대 볼륨

            // 초기 볼륨을 슬라이더에 동기화 (선형 볼륨 -> 데시벨 변환)
            volumeSlider.value = Mathf.Log10(Mathf.Max(0.0001f, MainBGM.volume)) * 20f;

            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
    }

    // 슬라이더 값(데시벨)을 받아서 오디오 소스 볼륨으로 변환하여 적용
    public void SetVolume(float v)
    {
        if (MainBGM != null)
        {
            // 데시벨 값 -> 선형 볼륨 값 변환
            MainBGM.volume = Mathf.Pow(10, v / 20f);
        }
    }
}