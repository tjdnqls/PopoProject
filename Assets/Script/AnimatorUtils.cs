using UnityEngine;

public static class AnimatorUtils
{
    /// <summary>
    /// 애니메이터를 컨트롤러의 기본 상태(Entry) 시점으로 되돌리고,
    /// 파라미터를 컨트롤러의 기본값으로 초기화합니다.
    /// </summary>
    public static void ResetToDefaults(Animator a, bool resetParams = true)
    {
        if (!a) return;

        if (resetParams)
        {
            // 트리거/Bool/Int/Float 모두 컨트롤러 기본값으로
            foreach (var p in a.parameters)
            {
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Trigger:
                        a.ResetTrigger(p.name);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        a.SetBool(p.name, p.defaultBool);
                        break;
                    case AnimatorControllerParameterType.Int:
                        a.SetInteger(p.name, p.defaultInt);
                        break;
                    case AnimatorControllerParameterType.Float:
                        a.SetFloat(p.name, p.defaultFloat);
                        break;
                }
            }
        }

        // 런타임 그래프 및 포즈를 초기 상태로 재바인딩
        a.Rebind();     // 내부 상태머신 리셋
        a.Update(0f);   // 프레임 0 샘플(즉시 적용)
        a.speed = 1f;   // 혹시 0으로 멈춰있으면 복구
    }

    /// <summary>현재 재생 중인 상태를 처음(0f)부터 다시 시작.</summary>
    public static void RestartCurrentState(Animator a, int layer = 0)
    {
        if (!a) return;
        var info = a.GetCurrentAnimatorStateInfo(layer);
        a.Play(info.fullPathHash, layer, 0f);
        a.Update(0f);
    }

    /// <summary>지정한 상태명을 해당 레이어에서 처음부터 재생.</summary>
    public static void PlayFromStart(Animator a, string stateName, int layer = 0)
    {
        if (!a || string.IsNullOrEmpty(stateName)) return;
        a.Play(stateName, layer, 0f);
        a.Update(0f);
    }
}
