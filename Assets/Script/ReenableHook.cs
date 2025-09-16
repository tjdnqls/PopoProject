using UnityEngine;
public class ReenableHook : MonoBehaviour
{

    public SpriteAnimationManager anim;
    void OnEnable()
    {
        PlayAnim("Wind", true);
    }

    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (anim != null) { anim.Play(key, forceRestart); return; }
    }
}

