using System.Collections.Generic;
using UnityEngine;

using AirSig;

public class PlayerSignature : BasedGestureHandle {

    // Gesture index to use for training and verifying player signature. Valid index is 100 only
    readonly int PLAYER_SIGNATURE_INDEX = 100;

    // Callback for receiving signature/gesture progression or identification results
    AirSigManager.OnPlayerSignatureTrained signatureTrained;
    AirSigManager.OnPlayerSignatureMatch signatureMatch;

    int trainFailCount;
    bool hasSignatureSet = false;

    // Handling player signature match callback - This is invoked when the Mode is set to Mode.IdentifyPlayerSignature and a gesture is recorded.
    // gestureId - a serial number
    // match - true/false indicates that whether a gesture recorded match the gesture trained
    // targetIndex - one of the index in the SetTarget range.
    void HandleOnPlayerSignatureMatch(long gestureId, bool match, int targetIndex) {
        string result = "<color=red>Player signature failed to match</color>";
        if (PLAYER_SIGNATURE_INDEX == targetIndex && match) {
            result = string.Format("<color=cyan>Player signature match ^_^</color>");
        }
        textToUpdate = result;
    }

    // Handling player signature training callback - This is invoked when the Mode is set to Mode.TrainPlayerSignature and a gesture is recorded.
    // gestureId - a serial number
    // error - error while training for this signature
    // progress - progress of training. 1 indicates the training is completed
    // securityLevel - the strength of this player sinature
    void HandleOnPlayerSignatureTrained(long gestureId, AirSigManager.Error error, float progress, AirSigManager.SecurityLevel securityLevel) {
        if (null == error) {
            if (progress < 1.0f) {
                textToUpdate = string.Format("Player signature training\nunder progress {0}%", Mathf.RoundToInt(progress * 100));
            } else {
                // The training has completed, switch to the identification mode
                nextUiAction = () => {
                    StopCoroutine(uiFeedback);
                    SwitchToIdentify();
                    hasSignatureSet = true;
                };
            }
        } else {
            textToUpdate = string.Format("<color=red>This attempt of training failed\ndue to {0} (see error code document),\ntry again</color>", error.code);

            trainFailCount++;

            if (trainFailCount >= 3 || progress < 0.5f) {
                // Reset if any error
                airsigManager.DeletePlayerRecord(PLAYER_SIGNATURE_INDEX);
                // Report error
                //textToUpdate = string.Format("<color=red>Inconsistent signature\ndue to {0} (see error code document),\nReset progress to 0%</color>", error.code);

                // Reset will also reset cumulative count
                trainFailCount = 0;
            } else if (progress >= 0.5f) {
                //textToUpdate = string.Format("<color=red>Failed {0} attempt(s)\ndue to {1} (see error code document),\ntry again</color>", trainFailCount, error.code);
            }

            if (error.code == -204) {   //Too few words
                if (progress > 0) {
                    textToUpdate = string.Format("<color=red>Inconsistent signature</color>");
                } else {
                    textToUpdate = string.Format("<color=red>Please write your full name.\nShort signature is less secure</color>");   
                }
            } else if (error.code == -201) {    //The signature is too short that considered as empty.
                if (progress > 0) {
                    textToUpdate = string.Format("<color=red>Inconsistent signature</color>");
                } else {
                    textToUpdate = string.Format("<color=red>Too short. Please write your full name</color>");
                }
            } else if (error.code == -205) {    //Too few wrist
                                                //Use too few wriste
                textToUpdate = string.Format("<color=red>Too slow. Please try to write faster</color>");
            } else if (error.code == -207) {    //Too many kinds of signature (diverse)
                textToUpdate = string.Format("<color=red>Inconsistent signature</color>");
            } else if (error.code == -209) {    //Second Input is too different than first one
                textToUpdate = string.Format("<color=red>Inconsistent signature</color>");
            } else if (error.code == -202) {    //Too long
                textToUpdate = string.Format("<color=red>Signature too long</color>");
            } else if (error.code == -200) {    //Mistouch
                textToUpdate = string.Format("<color=red>Inconsistent signature</color>");
            }
        }
    }

    void SwitchToIdentify() {
        textResult.text = defaultResultText = string.Format("Try write the trained signature to identify\nPress the B key to reset");
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.IdentifyPlayerSignature.ToString());
        airsigManager.SetMode(AirSigManager.Mode.IdentifyPlayerSignature);
        airsigManager.SetTarget(new List<int> { PLAYER_SIGNATURE_INDEX });
    }

    void ResetSignature() {
        if (hasSignatureSet) {
            airsigManager.DeletePlayerRecord(PLAYER_SIGNATURE_INDEX);
        }
        textResult.text = defaultResultText = "Pressing trigger and write a signature in the air\nReleasing trigger when finish\nUse the Application key to reset progress";
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.TrainPlayerSignature.ToString());
        airsigManager.SetMode(AirSigManager.Mode.TrainPlayerSignature);
        airsigManager.SetTarget(new List<int> { PLAYER_SIGNATURE_INDEX });
    }

    // Use this for initialization
    void Awake() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

        // Update the display text
        textResult.alignment = TextAnchor.UpperCenter;
        instruction.SetActive(false);
        ToggleGestureImage("");

        // Configure AirSig by specifying target 
        signatureTrained = new AirSigManager.OnPlayerSignatureTrained(HandleOnPlayerSignatureTrained);
        airsigManager.onPlayerSignatureTrained += signatureTrained;
        signatureMatch = new AirSigManager.OnPlayerSignatureMatch(HandleOnPlayerSignatureMatch);
        airsigManager.onPlayerSignatureMatch += signatureMatch;

        ResetSignature();
    }


    void OnDestroy() {
        // Unregistering callback
        airsigManager.onPlayerSignatureTrained -= signatureTrained;
        airsigManager.onPlayerSignatureMatch -= signatureMatch;
    }

    void Update() {
        UpdateUIandHandleControl();
        
        if (OVRInput.GetDown(OVRInput.RawButton.B)) {
            ResetSignature();
        }
    }
}