using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

using AirSig;

public class BasedGestureHandle : MonoBehaviour {

    // Reference to AirSigManager for setting operation mode and registering listener
    public AirSigManager airsigManager;

    // Reference to the vive right hand controller for handing key pressing
    //public SteamVR_TrackedObject rightHandControl;

    public ParticleSystem track;

    // UI for displaying current status and operation results 
    public Text textMode;
    public Text textResult;
    public GameObject instruction;
    public GameObject cHeartDown;

    protected string textToUpdate;

    protected readonly string DEFAULT_INSTRUCTION_TEXT = "Pressing trigger and write in the air\nReleasing trigger when finish";
    protected string defaultResultText;

    // Set by the callback function to run this action in the next UI call
    protected Action nextUiAction;
    protected IEnumerator uiFeedback;

    protected string GetDefaultIntructionText() {
        return DEFAULT_INSTRUCTION_TEXT;
    }

    protected void ToggleGestureImage(string target) {
        if ("All".Equals(target)) {
            cHeartDown.SetActive(true);
            foreach (Transform child in cHeartDown.transform) {
                child.gameObject.SetActive(true);
            }
        } else if ("Heart".Equals(target)) {
            cHeartDown.SetActive(true);
            foreach (Transform child in cHeartDown.transform) {
                if (child.name == "Heart") {
                    child.gameObject.SetActive(true);
                } else {
                    child.gameObject.SetActive(false);
                }
            }
        } else if ("C".Equals(target)) {
            cHeartDown.SetActive(true);
            foreach (Transform child in cHeartDown.transform) {
                if (child.name == "C") {
                    child.gameObject.SetActive(true);
                } else {
                    child.gameObject.SetActive(false);
                }
            }
        } else if ("Down".Equals(target)) {
            cHeartDown.SetActive(true);
            foreach (Transform child in cHeartDown.transform) {
                if (child.name == "Down") {
                    child.gameObject.SetActive(true);
                } else {
                    child.gameObject.SetActive(false);
                }
            }
        } else {
            cHeartDown.SetActive(false);
        }
    }

    protected IEnumerator setResultTextForSeconds(string text, float seconds, string defaultText = "") {
        string temp = textResult.text;
        textResult.text = text;
        yield return new WaitForSeconds(seconds);
        textResult.text = defaultText;
    }

    protected void checkDbExist() {
        bool isDbExist = airsigManager.IsDbExist;
        if (!isDbExist) {
            textResult.text = "<color=red>Cannot find DB files!\nMake sure\n'Assets/AirSig/StreamingAssets'\nis copied to\n'Assets/StreamingAssets'</color>";
            textMode.text = "";
            instruction.SetActive(false);
            cHeartDown.SetActive(false);
        }
    }

    protected void UpdateUIandHandleControl() {
        if (Input.GetKeyUp(KeyCode.Escape)) {
            Application.Quit();
        }
        if (null != textToUpdate) {
            if(uiFeedback != null) StopCoroutine(uiFeedback);
            uiFeedback = setResultTextForSeconds(textToUpdate, 5.0f, defaultResultText);
            StartCoroutine(uiFeedback);
            textToUpdate = null;
        }

        float triggerKeyValue = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
        if (triggerKeyValue > 0.8f) {
            track.Play();
        } else if (triggerKeyValue < 0.1f) {
            track.Stop();
        }

        if (nextUiAction != null) {
            nextUiAction();
            nextUiAction = null;
        }
    }

}
