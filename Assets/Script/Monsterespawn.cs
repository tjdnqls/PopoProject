using UnityEngine;

public class Monsterespawn : MonoBehaviour
{
    public GameObject genw;
    [SerializeField] GameObject genmon;
    public GameObject monster;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if(monster == null)
        {
            GameObject mon = Instantiate(genmon, new Vector3(genw.transform.position.x, genw.transform.position.y), Quaternion.identity);
            monster = mon;
        }
    }
}
