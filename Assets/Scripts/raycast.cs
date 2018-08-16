using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class raycast : MonoBehaviour {
    public Transform firePoint;

	// Use this for initialization
	void Start () {
        firePoint = transform.Find("firePoint");
        if (firePoint == null)
        {
            Debug.LogError("No firepoint");
        }
    }
	
	// Update is called once per frame
	void Update () {
        RaycastHit hit;
        Debug.DrawRay(firePoint.position, firePoint.forward * 100, Color.cyan);
        if(Physics.Raycast(firePoint.position, firePoint.forward, out hit, 100f) && hit.transform.tag == "rock")
        {
            Debug.Log("hit rock");
            StartCoroutine(moveObject(hit));
        }
    }

    IEnumerator moveObject(RaycastHit hit)
    {
        Vector3 direction = hit.rigidbody.velocity;
        direction.z = 5.0f;
        hit.rigidbody.velocity = direction;

        yield return new WaitForSeconds(3);
        hit.rigidbody.velocity = Vector3.zero;
    }
}
