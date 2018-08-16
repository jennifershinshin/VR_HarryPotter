using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class collide : MonoBehaviour {
    public GameObject gm;

	// Use this for initialization
	void Start () {
        gm = GameObject.Find("GameManager");
	}
	
	// Update is called once per frame
	void Update () {
		
	}

    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log("triggered");
        if(other.gameObject.tag == "enemy")
        {
            gm.GetComponent<gameManager>().incrementScore();
            other.GetComponent<EnemyMove>().Die();
        }
    }
}
