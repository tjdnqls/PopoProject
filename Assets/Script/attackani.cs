using UnityEngine;

public class attackani : MonoBehaviour
{
    float attime = 0f;
    bool attack = false;
    Animator ani;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ani = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        attime -= Time.deltaTime;
        if(Input.GetKeyDown(KeyCode.F) && attime <= 0 && attack == false)
        {
            ani.SetBool("attack", true);
            attack = true;
            attime = 0.5f;
        }

        if(attime <= 0 && attack == true)
        {
            ani.SetBool("attack", false);
            attack = false;
        }


    }
}
