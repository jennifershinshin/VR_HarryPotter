using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMove : MonoBehaviour {
    public AudioClip hitSound;
    private AudioSource hitSoundSource;
    public AudioClip goblinSound;
    private AudioSource goblinSoundSource;

    public string PlayerName = "PlayerCylinder"; //Set String to name of player object
    public GameObject Playerprefab;

    public GameObject gm;

    public float idleTime = 4.0f;
    float deathTime = 2.0f;
    float attackTime = 0.0f;
    bool dead = false;

    // Use this for initialization
    void Start () {
        Playerprefab = GameObject.Find(PlayerName);
        gm = GameObject.Find("GameManager");
        Idle();

        hitSoundSource = GetComponent<AudioSource>();
        goblinSoundSource = GetComponent<AudioSource>();
    }
	
	// Update is called once per frame
	void Update () {

        if (!dead)
        {
            idleTime -= Time.deltaTime;
            if (idleTime >= 2.0f)
            {
                Idle();
            }
            else if (idleTime <= 2.0f && idleTime >= 0.0f)
            {
                WaitingForBattle();
            }
            else if (idleTime <= 0.0f)
            {
                float distanceToPlayer = Vector3.Distance(transform.position, Playerprefab.transform.position);
                if (distanceToPlayer >= 1f)
                {
                    MoveTowardPlayer();
                }
                else
                {
                    AttackPlayer();
                }
            }
        } else
        {
            Die();
        }
	}

    void MoveTowardPlayer()
    {
        GetComponent<Animation>().Play("run");

        float MoveRate = .2f;
        transform.position = Vector3.MoveTowards(transform.position, Playerprefab.transform.position, MoveRate);

        transform.LookAt(Playerprefab.transform);
    }

    void AttackPlayer()
    {
        GetComponent<Animation>().Play("attack");
        attackTime -= Time.deltaTime;
        if(attackTime == 1.0f)
        {
            goblinSoundSource.PlayOneShot(goblinSound, 0.7f);
        }
        if (attackTime <= 0.0f)
        {
            hitSoundSource.PlayOneShot(hitSound, 0.7f);
            gm.GetComponent<gameManager>().decrementHealth();
            attackTime = 2.0f;
        }
    }

    public void Idle()
    {
        GetComponent<Animation>().Play("idle");
    }

    public void WaitingForBattle()
    {
        GetComponent<Animation>().Play("waitingforbattle");
    }

    public void Dance()
    {
        GetComponent<Animation>().Play("dance");
    }

    public void Die()
    {
        dead = true;
        Debug.Log("dying");
        GetComponent<Animation>().Play("die");
        
        deathTime -= Time.deltaTime;
        if(deathTime <= 0.0f)
        {
            Debug.Log("Destroying");
            Destroy(this.gameObject);
            Debug.Log("Destroyed bug");
        }
        Debug.Log("after die if");
    }
}
