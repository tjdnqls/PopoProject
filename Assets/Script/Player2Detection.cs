
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

    [Header("Position by Facing (Player2)")]
    [SerializeField] private Vector2 offsetRight = new Vector2(0.6f, 1.6f); // 바라보는 방향이 오른쪽일 때 오프셋
    [SerializeField] private Vector2 offsetLeft = new Vector2(-0.6f, 1.6f); // 바라보는 방향이 왼쪽일 때 오프셋
    [SerializeField] private bool mirrorScaleXByFacing = true;  // 왼/오 전환 시 프리팹 X 스케일 반전

    [Header("Facing source (optional)")]
    [SerializeField] private SpriteRenderer facingSprite; // flipX 기준; 비우면 자동 탐색
    [SerializeField] private Rigidbody2D facingRb;        // 없을 때 속도로 보조 판단

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
    private Vector3 _popupOriginalScale = Vector3.one;

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

        if (!facingSprite) facingSprite = player2.GetComponentInChildren<SpriteRenderer>();
        if (!facingRb) facingRb = player2.GetComponent<Rigidbody2D>();

        if (player1) _p1hp = player1.GetComponent<Player1HP>();

        if (popupPrefab)
        {
            _popup = Instantiate(popupPrefab, transform); // player2의 자식으로 생성(월드 공간 프리팹이어도 괜찮음)
            _popupOriginalScale = _popup.transform.localScale;
            _popupCG = _popup.GetComponentInChildren<CanvasGroup>();

            // 스프라이트/문자 렌더러 수집(알파 페이드용)
            _popupSprites.AddRange(_popup.GetComponentsInChildren<SpriteRenderer>(true));
            _popupTexts.AddRange(_popup.GetComponentsInChildren<TMP_Text>(true));

            SetPopupAlpha(0f);
            _popup.SetActive(false);
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
            // 표시 중이라면 바로 페이드아웃(강제 끝)
            if (_isShowing) StopAllCoroutines();
            if (_isShowing) StartCoroutine(FadeOutNow());
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

        // 표시 중/아니더라도 항상 위치/거울 스케일 갱신 (자연스럽게 따라다니게)
        UpdatePopupTransformByFacing();
    }

    // ===== Popup =====
    private IEnumerator ShowPopupRoutine()
    {
        _isShowing = true;

        _popup.SetActive(true);
        UpdatePopupTransformByFacing();

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
                // 유지 중에도 방향 변하면 계속 업데이트
                UpdatePopupTransformByFacing();
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

    // ===== Facing / Transform =====
    private int FacingDir()
    {
        // 우: +1, 좌: -1
        if (facingSprite) return facingSprite.flipX ? -1 : +1;

        // 스프라이트 없으면 속도로 추정
        if (facingRb && Mathf.Abs(facingRb.linearVelocity.x) > 0.01f)
            return facingRb.linearVelocity.x > 0 ? +1 : -1;

        // 결정 불가 시 기본 우측
        return +1;
    }

    private void UpdatePopupTransformByFacing()
    {
        if (!_popup || !player2) return;

        int f = FacingDir();
        Vector2 off = (f >= 0) ? offsetRight : offsetLeft;

        // 월드 포지션 갱신 (player2 기준)
        _popup.transform.position = (Vector2)player2.position + off;

        // 좌/우 반전
        if (mirrorScaleXByFacing)
        {
            var sc = _popupOriginalScale;
            sc.x = Mathf.Abs(sc.x) * (f >= 0 ? 1f : -1f);
            _popup.transform.localScale = sc;
        }
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

        // 대략적인 좌/우 오프셋 미리보기
        Gizmos.color = Color.green;
        Gizmos.DrawSphere((Vector2)p2.position + offsetRight, 0.06f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere((Vector2)p2.position + offsetLeft, 0.06f);
    }
}
