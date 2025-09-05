using UnityEngine;

public class AniManager : MonoBehaviour
{
    public Animator animator;
    public Animator animators;

    // Update is called once per frame


    private void Awake()
    {
        animator = GetComponent<Animator>();    
        animators = GetComponent<Animator>();    
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            animator.SetBool("run", false);
            animator.SetBool("jump", false);
            animators.SetBool("run", false);
            animators.SetBool("jump", false);
        }
    }

    public void Sap()
    {
        
    }
}
