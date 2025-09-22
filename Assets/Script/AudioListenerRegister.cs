using UnityEngine;

[RequireComponent(typeof(AudioListener))]
public class AudioListenerRegister : MonoBehaviour
{
    private void OnEnable() => AudioRuntime.RegisterListener(transform);
    private void OnDisable() => AudioRuntime.UnregisterListener(transform);
}

public static class AudioRuntime
{
    public static Transform Listener { get; private set; }

    public static void RegisterListener(Transform t) => Listener = t;
    public static void UnregisterListener(Transform t)
    {
        if (Listener == t) Listener = null;
    }
}
