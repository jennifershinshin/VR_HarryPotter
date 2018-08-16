using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stupefy : MonoBehaviour {
    public AudioClip spellSound;
    private AudioSource spellSoundSource;

    public Transform firePoint;
    Vector3 startPos = Vector3.zero;
    bool firstPressed = true;
    bool calcDistance = false;
    float totalDistance = 0f;
    float time = 0f;
    float time2 = 0f;

	// Use this for initialization
	void Start () {
		firePoint = transform.Find("firePoint");
        spellSoundSource = GetComponent<AudioSource>();
    }
	
	// Update is called once per frame
	void Update () {
        //Debug.Log("RIGHT " + OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger));
        //Debug.Log("LEFT" + OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger));
        if (OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.3 && firstPressed)
        {
           // Debug.Log("pressed");
            startPos = transform.position;
            firstPressed = false;
            calcDistance = true;
            time = Time.time;
        }

        if(calcDistance == true && OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) == 0)
        {
           // Debug.Log("possible spell");
            float distance = Vector3.Distance(transform.position, startPos);
            time2 = Time.time;
            Debug.Log(distance / (time2 - time));

           

            if (distance / (time2 - time) > 0.2)
            {
                spellSoundSource.PlayOneShot(spellSound, 1);
                Debug.Log("shoot");
                Debug.DrawRay(firePoint.position, firePoint.forward * 100, Color.cyan);
                StartCoroutine(spell());
            }
            time = 0f;
            time2 = 0f;
            calcDistance = false;
            firstPressed = true;
        }
	}

    IEnumerator spell()
    {
        GameObject shootObj = Instantiate(Resources.Load("Sphere"), firePoint.position, firePoint.rotation) as GameObject;
        shootObj.GetComponent<Rigidbody>().velocity = firePoint.forward * 15;
        yield return new WaitForSeconds(5);
        Destroy(shootObj);
    }
}
