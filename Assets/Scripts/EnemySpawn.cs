using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawn : MonoBehaviour {

    public float spawnTime = 10.0f;
    //public float lastSpawnTime = 10.0f;
    public float difficulty = 1.0f;

    public GameObject[] enemyList = new GameObject[4]; //drag prefabs to Enemy Spawn (Script) area

    public GameObject Enemyprefab;

    public Vector3 center;
    public Vector3 size;

	// Use this for initialization
	void Start () {
        SpawnEnemy();
	}
	
	// Update is called once per frame
	void Update () {

        spawnTime -= Time.deltaTime * difficulty;

        if (spawnTime <= 0.0f)
        {
            SpawnEnemy();
            difficulty += 0.2f;
            spawnTime = 10.0f;
            //spawnTime = lastSpawnTime - 0.05f;
            //lastSpawnTime = spawnTime;
        }
	}

    public void SpawnEnemy()
    {
        System.Random rnd = new System.Random();
        int chanceNum = rnd.Next(0, 100);
        if(chanceNum < 50)
        {
            Enemyprefab = enemyList[0];
        } else if(chanceNum < 80) {
            Enemyprefab = enemyList[1];
        } else if(chanceNum < 95)
        {
            Enemyprefab = enemyList[2];
        } else
        {
            Enemyprefab = enemyList[3];
        }

        Vector3 pos = transform.position + new Vector3(Random.Range(-size.x / 2, size.x / 2), Random.Range(-size.y / 2, size.y / 2), Random.Range(-size.z / 2, size.z / 2));

        Instantiate(Enemyprefab, pos, Quaternion.identity);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.5f);
        Gizmos.DrawCube(transform.localPosition + center, size);
    }
}
