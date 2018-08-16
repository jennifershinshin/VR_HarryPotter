using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;

public class gameManager : MonoBehaviour {
    public int score = 0;
    public int health = 10;
    public bool gameOver = false;
    public Text text;
    

	// Use this for initialization
	void Start () {
        text.text = "Score: 0" + "\n" + "Health: 10";
	}
	
	// Update is called once per frame
	void Update () {
		
	}

    public void incrementScore()
    {
        if (!gameOver)
        {
            Debug.Log("increment");
            score++;
            text.text = "Score: " + score + "\n" + "Health: " + health;
        }
    }

    public void decrementHealth()
    {
        health--;
        text.text = "Score: " + score + "\n" + "Health: " + health;
        if(health <= 0)
        {
            gameOver = true;
            text.text = "Score: " + score + "\n" + "GAME OVER";
        }
    }
}
