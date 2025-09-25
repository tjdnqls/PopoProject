using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class MasterVolumeSliderBinder : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private bool applyOnEnable = true;

    void Reset()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    void Awake()
    {
        if (!slider) slider = GetComponent<Slider>();
        slider.onValueChanged.AddListener(OnChanged);
    }

    void OnEnable()
    {
        float v = SoundManager.GetMasterVolumeLinear();
        slider.SetValueWithoutNotify(v);
        if (applyOnEnable) OnChanged(v);
    }

    void OnDestroy()
    {
        if (slider) slider.onValueChanged.RemoveListener(OnChanged);
    }

    private void OnChanged(float v)
    {
        SoundManager.SetMasterVolumeLinear(v);
    }
}
