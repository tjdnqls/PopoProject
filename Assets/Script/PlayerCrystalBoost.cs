using UnityEngine;
using System.Collections;

public class PlayerCrystalBoost : MonoBehaviour
{
    public Rigidbody2D rb;
    public float dashPower = 10f;
    public float dashDuration = 0.2f;
    public float jumpPower = 15f;
    public float jumpDuration = 0.3f;
    private float boostTimer = 0f;

    private enum BoostType { None, Dash, Jump }
    private BoostType currentBoost = BoostType.None;

    private bool isBoosting = false;
    private Vector2 dashDirection;

    private float lastClickTime = -1f;
    private float doubleClickThreshold = 0.25f;

    void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (!isBoosting && currentBoost != BoostType.None)
        {
            bool leftClick = Input.GetMouseButtonDown(0);
            bool rightClick = Input.GetMouseButtonDown(1);

            if (leftClick || rightClick)
            {
                if (Time.time - lastClickTime <= doubleClickThreshold)
                {
                    if (currentBoost == BoostType.Dash)
                    {
                        dashDirection = leftClick ? Vector2.left : Vector2.right;
                        StartDash();
                    }
                    else if (currentBoost == BoostType.Jump)
                    {
                        StartJump();
                    }
                }
                lastClickTime = Time.time;
            }
        }
        else if (isBoosting && currentBoost == BoostType.Dash)
        {
            if ((dashDirection == Vector2.left && Input.GetMouseButtonDown(1)) ||
                (dashDirection == Vector2.right && Input.GetMouseButtonDown(0)))
            {
                CancelCurrentBoost();
            }
        }
    }

    void FixedUpdate()
    {
        if (isBoosting)
        {
            boostTimer += Time.fixedDeltaTime;

            if (currentBoost == BoostType.Dash)
            {
                rb.linearVelocity = new Vector2(dashDirection.x * dashPower, 0f);
                if (boostTimer >= dashDuration)
                {
                    CancelCurrentBoost();
                }
            }
            else if (currentBoost == BoostType.Jump)
            {
                rb.linearVelocity = new Vector2(0f, jumpPower);
                if (boostTimer >= jumpDuration)
                {
                    CancelCurrentBoost();
                }
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("CrystalDash"))
        {
            StartCoroutine(ApplyBoostDelayed(BoostType.Dash));
            collision.gameObject.SetActive(false);
        }
        else if (collision.CompareTag("CrystalJump"))
        {
            StartCoroutine(ApplyBoostDelayed(BoostType.Jump));
            collision.gameObject.SetActive(false);
        }
    }

    IEnumerator ApplyBoostDelayed(BoostType type)
    {
        yield return new WaitForFixedUpdate(); // ¶Ç´Â WaitForEndOfFrame
        SetBoost(type);
    }


    void SetBoost(BoostType newBoost)
    {
        CancelCurrentBoost();
        currentBoost = newBoost;
    }

    void StartDash()
    {
        isBoosting = true;
        boostTimer = 0f;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    void StartJump()
    {
        isBoosting = true;
        boostTimer = 0f;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    void CancelCurrentBoost()
    {
        isBoosting = false;
        currentBoost = BoostType.None;
        rb.gravityScale = 2f;
        rb.linearVelocity = Vector2.zero;
    }
}
