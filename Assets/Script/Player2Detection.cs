using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class P2RangeDetectPopupFacing : MonoBehaviour
{
    [Header("Who is who")]
    [SerializeField] private Transform player2;      // 이 스크립트를 단 캐릭터(폰) — 비우면 자체 transform
    [SerializeField] private Transform player1;      // 감지 대상

    [Header("Detection")]
    [SerializeField] private float detectRadius = 4f;      // 원형 감지 반경
    [SerializeField] private float requiredStaySec = 10f;  // 이 시간 연속 유지되면 발동
    [SerializeField] private bool ignoreIfPlayer1Dead = true; // Player1HP.IsDead이면 감지 무시

    [Header("Popup Prefab")]
    [SerializeField] private GameObject popupPrefab; // 띄울 프리팹(한 번만 생성해 재사용)
    [SerializeField] private float showSeconds = 2f; // 총 표시 시간(페이드 구간 포함)
    [SerializeField] private float fadeInSec = 0.25f;
    [SerializeField] private float fadeOutSec = 0.25f;

    [Header("Popup Position")]
    [SerializeField] private Vector2 offsetRight = new Vector2(0.6f, 1.6f); // 항상 이 오프셋만 사용

    [Header("Facing Freeze")]
    [SerializeField] private bool freezeFacingToPrefab = true;   // 부모 좌우 반전 영향 금지(월드 X스케일 고정)
    [SerializeField] private bool detachPopupFromParent = false; // 팝업을 부모에서 완전히 분리(원하면 사용)
    private Vector3 _popupBaseWorldScale;                        // 팝업 생성 시 월드 스케일(기준)

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // --- internals ---
    private GameObject _popup;        // 인스턴스
    private CanvasGroup _popupCG;     // UI일 경우
    private List<SpriteRenderer> _popupSprites = new(); // 스프라이트 기반일 경우
    private List<TMP_Text> _popupTexts = new();         // 텍스트 알파도 조정

    private float _stayTimer = 0f;
    private bool _wasInside = false;
    private bool _lockedUntilExit = false; // 한 번 발동 후, 범위에서 벗어날 때까지 재발동 잠금
    private bool _isShowing = false;

    private Player1HP _p1hp; // 죽음 무시 옵션을 위한 캐시

    // ===== Unity =====
    private void Reset()
    {
        player2 = transform;
        if (!player1)
        {
            var p1 = FindAnyObjectByType<Player1HP>();
            if (p1) player1 = p1.transform;
        }
    }

    private void Awake()
    {
        if (!player2) player2 = transform;

        if (player1) _p1hp = player1.GetComponent<Player1HP>();

        if (popupPrefab)
        {
            _popup = Instantiate(popupPrefab, transform); // 기본은 자식으로
            _popupCG = _popup.GetComponentInChildren<CanvasGroup>();

            // 스프라이트/문자 렌더러 수집(알파 페이드용)
            _popupSprites.AddRange(_popup.GetComponentsInChildren<SpriteRenderer>(true));
            _popupTexts.AddRange(_popup.GetComponentsInChildren<TMP_Text>(true));

            // 기준 월드 스케일 저장(이 값을 유지하도록 보정)
            _popupBaseWorldScale = _popup.transform.lossyScale;

            SetPopupAlpha(0f);
            _popup.SetActive(false);

            // 옵션: 완전 분리(부모 스케일/반전 상속 자체를 차단)
            if (detachPopupFromParent)
            {
                _popup.transform.SetParent(null, true);
            }
        }
        else
        {
            Debug.LogWarning("[P2RangeDetectPopupFacing] popupPrefab이 비어있습니다.");
        }
    }

    private void Update()
    {
        if (!player1 || !_popup) return;

        // 죽음 무시 옵션
        if (ignoreIfPlayer1Dead && _p1hp && _p1hp.IsDead)
        {
            _stayTimer = 0f;
            _wasInside = false;
            _lockedUntilExit = false;

            if (_isShowing)
            {
                StopAllCoroutines();
                StartCoroutine(FadeOutNow());
            }
            return;
        }

        // 감지
        float dist = Vector2.Distance(player2.position, player1.position);
        bool inside = dist <= detectRadius;

        if (inside)
        {
            if (!_wasInside) _stayTimer = 0f;    // 막 들어왔으면 타이머 리셋
            _stayTimer += Time.deltaTime;

            if (_stayTimer >= requiredStaySec && !_isShowing && !_lockedUntilExit)
            {
                StartCoroutine(ShowPopupRoutine());
                _lockedUntilExit = true; // 나갈 때까지 재발동 X
            }
        }
        else
        {
            _stayTimer = 0f;
            if (_wasInside) _lockedUntilExit = false; // 범위에서 완전히 나가면 잠금 해제
        }
        _wasInside = inside;

        // 표시 중/아니더라도 항상 위치 갱신 (항상 오른쪽 고정)
        UpdatePopupTransform();
    }

    // ===== Popup =====
    private IEnumerator ShowPopupRoutine()
    {
        _isShowing = true;

        _popup.SetActive(true);
        UpdatePopupTransform();

        // 페이드 인
        float t = 0f;
        if (fadeInSec > 0f)
        {
            while (t < fadeInSec)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / fadeInSec);
                SetPopupAlpha(a);
                yield return null;
            }
        }
        else SetPopupAlpha(1f);

        // 유지 시간 (페이드 시간 빼고 남은 만큼)
        float hold = Mathf.Max(0f, showSeconds - fadeInSec - fadeOutSec);
        if (hold > 0f)
        {
            float h = 0f;
            while (h < hold)
            {
                h += Time.deltaTime;
                UpdatePopupTransform();
                yield return null;
            }
        }

        // 페이드 아웃
        t = 0f;
        if (fadeOutSec > 0f)
        {
            while (t < fadeOutSec)
            {
                t += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(t / fadeOutSec);
                SetPopupAlpha(a);
                yield return null;
            }
        }
        else SetPopupAlpha(0f);

        _popup.SetActive(false);
        _isShowing = false;
    }

    // 죽음 등으로 강제 종료 시 바로 페이드아웃
    private IEnumerator FadeOutNow()
    {
        _isShowing = true;
        float t = 0f;
        float dur = Mathf.Max(0.05f, fadeOutSec * 0.5f);
        while (t < dur)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / dur);
            SetPopupAlpha(a);
            yield return null;
        }
        _popup.SetActive(false);
        _isShowing = false;
    }

    // ===== Transform (오른쪽 고정) =====
    private void UpdatePopupTransform()
    {
        if (!_popup || !player2) return;
        _popup.transform.position = (Vector2)player2.position + offsetRight;

        // 부모 좌우 반전에 영향받지 않게 월드 X스케일을 초기값으로 유지
        if (freezeFacingToPrefab && !detachPopupFromParent)
        {
            MaintainWorldFacing();
        }
    }

    // 부모 좌우 반전(scale.x = ±1)에 영향받지 않도록,
    // 팝업의 월드 X스케일을 생성 시점의 값(_popupBaseWorldScale.x)으로 유지
    private void MaintainWorldFacing()
    {
        if (_popup == null) return;

        // 부모의 현재 월드 스케일
        var parent = player2 ? player2 : transform;
        var pLossy = parent.lossyScale;

        // 0 나눗셈 방지
        float px = Mathf.Abs(pLossy.x) < 1e-6f ? 1f : pLossy.x;

        // 현재 로컬 스케일에서 X만 보정 (Y/Z는 프리팹값 유지)
        var ls = _popup.transform.localScale;

        // 부모 * 자식 = 월드 -> 월드 X를 _popupBaseWorldScale.x가 되도록 역산
        ls.x = _popupBaseWorldScale.x / px;

        _popup.transform.localScale = ls;

        // 필요 시 회전까지 고정하고 싶다면:
        // _popup.transform.rotation = Quaternion.identity;
    }

    // ===== Fade helpers =====
    private void SetPopupAlpha(float a)
    {
        if (_popupCG)
        {
            _popupCG.alpha = a;
        }
        else
        {
            for (int i = 0; i < _popupSprites.Count; i++)
            {
                if (!_popupSprites[i]) continue;
                var c = _popupSprites[i].color;
                c.a = a;
                _popupSprites[i].color = c;
            }
            for (int i = 0; i < _popupTexts.Count; i++)
            {
                if (!_popupTexts[i]) continue;
                var c = _popupTexts[i].color;
                c.a = a;
                _popupTexts[i].color = c;
            }
        }
    }

    // ===== Gizmos =====
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Transform p2 = player2 ? player2 : transform;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawWireSphere(p2.position, detectRadius);

        // 고정될 오프셋 미리보기
        Gizmos.color = Color.green;
        Gizmos.DrawSphere((Vector2)p2.position + offsetRight, 0.06f);
    }

    // 부모에서 완전 분리 모드일 때, 원 오브젝트 파괴 시 팝업도 정리
    private void OnDestroy()
    {
        if (detachPopupFromParent && _popup)
        {
            Destroy(_popup);
        }
    }
}
