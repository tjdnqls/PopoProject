using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MouseEvent : MonoBehaviour
{
    bool esc = false;

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape) && esc == false)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            esc = true;
        }
        else if(Input.GetKeyDown(KeyCode.Escape) && esc == true)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            esc = false;
        }
    }
}
