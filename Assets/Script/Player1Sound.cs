using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class Player1Sound : MonoBehaviour
{
    public AudioClip walkSound1;
    public AudioClip walkSound2;

    private AudioSource audioSource;
    private bool isGrounded;
    private int currentClipIndex = 0;
    private Coroutine walkCoroutine;

    public float stepDelay = 0.4f; // 발걸음 간의 고정된 딜레이

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    void Update()
    {
        CheckGround();

        bool isMoving = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);

        if (isMoving && isGrounded)
        {
            if (walkCoroutine == null)
            {
                walkCoroutine = StartCoroutine(PlayWalkSounds());
            }
        }
        else
        {
            // 이동 중이 아니거나 조건이 충족되지 않으면 코루틴 중지
            StopWalkingSound();
        }
    }

    private void StopWalkingSound()
    {
        if (walkCoroutine != null)
        {
            StopCoroutine(walkCoroutine);
            walkCoroutine = null;
            audioSource.Stop(); // 혹시 재생 중인 소리가 있다면 정지
            Debug.Log("움소리 멈춤");
        }
    }

    private IEnumerator PlayWalkSounds()
    {
        // 무한 루프로 발소리 재생
        while (true)
        {
            // 클립을 번갈아가며 선택
            audioSource.clip = (currentClipIndex == 0) ? walkSound1 : walkSound2;
            audioSource.Play();
            currentClipIndex = 1 - currentClipIndex;

            // 정해진 딜레이만큼 대기
            yield return new WaitForSeconds(stepDelay);
        }
    }

    private void CheckGround()
    {
        // 캐릭터 피벗에서 아래로 Raycast를 쏘아 지면 감지
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 3f, LayerMask.GetMask("Ground"));
        isGrounded = hit.collider != null;
    }
}