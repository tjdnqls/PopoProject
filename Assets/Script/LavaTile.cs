using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LavaTileh : MonoBehaviour
{
    [Header("Detection")]
    public string lavaTag = "Lava";
    public bool reactToTrigger = true;     // Trigger 충돌도 처리할지
    public bool reactToCollision = true;   // 물리 충돌도 처리할지

    [Header("On Kill Action")]
    public KillAction killAction = KillAction.ReloadScene;
    public string sendMessageName = "OnKilled"; // SendMessage 모드에서 호출될 메서드명

    [Tooltip("RespawnAtPoint 모드에서 사용할 리스폰 지점")]
    public Transform respawnPoint;
    [Tooltip("리스폰 후 무적 시간(초). 0이면 비활성")]
    public float respawnInvincibleTime = 0.5f;

    private bool _isProcessing;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!reactToTrigger || _isProcessing) return;
        if (other.CompareTag(lavaTag)) HandleKill();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!reactToCollision || _isProcessing) return;
        if (collision.collider.CompareTag(lavaTag)) HandleKill();
    }

    void HandleKill()
    {
        if (_isProcessing) return;
        _isProcessing = true;

        switch (killAction)
        {
            case KillAction.ReloadScene:
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                break;

            case KillAction.DestroyPlayer:
                Destroy(gameObject);
                break;

            case KillAction.SendMessage:
                SendMessage(sendMessageName, SendMessageOptions.DontRequireReceiver);
                // SendMessage는 보통 외부에서 씬 리셋/애니/사운드 처리
                // 중복 발생 방지용 플래그만 유지
                break;

            case KillAction.RespawnAtPoint:
                if (respawnPoint != null)
                {
                    var rb = GetComponent<Rigidbody2D>();
                    if (rb) rb.linearVelocity = Vector2.zero;
                    transform.position = respawnPoint.position;

                    if (respawnInvincibleTime > 0f)
                        StartCoroutine(InvincibleWindow(respawnInvincibleTime));
                    else
                        _isProcessing = false; // 무적 없다면 즉시 입력 재개
                }
                else
                {
                    // 리스폰 지점이 없으면 안전하게 씬 리로드
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
                break;
        }
    }

    IEnumerator InvincibleWindow(float seconds)
    {
        bool prevTrigger = reactToTrigger;
        bool prevCollision = reactToCollision;

        reactToTrigger = false;
        reactToCollision = false;

        yield return new WaitForSeconds(seconds);

        reactToTrigger = prevTrigger;
        reactToCollision = prevCollision;
        _isProcessing = false;
    }
}

public enum KillAction
{
    ReloadScene,
    DestroyPlayer,
    SendMessage,
    RespawnAtPoint
}
