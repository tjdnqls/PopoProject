using UnityEngine;
using UnityEngine.UI;

public class MouseClickUI : MonoBehaviour
{
    public Image leftClickUI;   // 왼쪽 클릭 UI 이미지
    public Image rightClickUI;  // 오른쪽 클릭 UI 이미지

    public Color normalColor = new Color(1, 1, 1, 0.3f);             // 기본 투명색
    public Color leftPressedColor = new Color(1, 0.3f, 0.3f, 1f);    // 빨간색
    public Color rightPressedColor = new Color(0.3f, 0.3f, 1f, 1f);  // 파란색
    public Color bothPressedColor = new Color(0.6f, 0.3f, 1f, 1f);   // 보라색

    void Update()
    {
        // 입력 상태 체크
        bool leftPressed = Input.GetMouseButton(0) || Input.GetKey(KeyCode.A);
        bool rightPressed = Input.GetMouseButton(1) || Input.GetKey(KeyCode.S);

        // 양쪽 동시 입력이면 보라색으로 설정
        if (leftPressed && rightPressed)
        {
            leftClickUI.color = bothPressedColor;
            rightClickUI.color = bothPressedColor;
        }
        else
        {
            // 각각 독립적으로 처리
            leftClickUI.color = leftPressed ? leftPressedColor : normalColor;
            rightClickUI.color = rightPressed ? rightPressedColor : normalColor;
        }
    }
}
