using System.Collections.Generic;
using UnityEngine;

using AirSig;

public class SmartGesture : BasedGestureHandle {

    readonly int MAX_TRAIN_COUNT = 5;
    int smartTrainCount = 0;

    // Use these steps to iterate gesture when train 'Smart Train' and 'Custom Gesture'
    int currentPlayerGestureStep; // 101 = heart, 102 = down
    string currentSmartTrainDeveloperDefinedTarget;

    // Callback for receiving signature/gesture progression or identification results
    AirSigManager.OnDeveloperDefinedMatch developerDefined;
    AirSigManager.OnSmartIdentifyDeveloperDefinedMatch smartDeveloperDefined;

    // Handling developer defined gesture match callback - This is invoked when the Mode is set to Mode.DeveloperDefined and a gesture is recorded.
    // gestureId - a serial number
    // gesture - gesture matched or null if no match. Only guesture in SetDeveloperDefinedTarget range will be verified against
    // score - the confidence level of this identification. Above 1 is generally considered a match
    void HandleOnDeveloperDefinedMatch(long gestureId, string gesture, float score) {
        // Handle SmartTrain's progress result here
        if (currentSmartTrainDeveloperDefinedTarget == gesture) {
            smartTrainCount++;
            string extraText = "Continue to add more samples";
            textToUpdate = string.Format(
                    "<color=cyan>Smart Train passed criteria ({0}/{1})\n" +
                    "{2}</color>", smartTrainCount, MAX_TRAIN_COUNT, extraText);
            if (smartTrainCount >= MAX_TRAIN_COUNT) {
                smartTrainCount = 0;
                textToUpdate = null; // let nextUiAction to handle UI update
                if ("HEART" == currentSmartTrainDeveloperDefinedTarget) {
                    currentSmartTrainDeveloperDefinedTarget = "C";
                    
                    airsigManager.SetDeveloperDefinedTarget(new List<string> { "C" });
                    nextUiAction = () => {
                        ToggleGestureImage("C");
                        textResult.text = defaultResultText = "Please write below gesture 5 times\nPress trigger to start\nRelease trigger when finish";
                    };
                } else if ("C" == currentSmartTrainDeveloperDefinedTarget) {
                    currentSmartTrainDeveloperDefinedTarget = "DOWN";
                    
                    airsigManager.SetDeveloperDefinedTarget(new List<string> { "DOWN" });
                    nextUiAction = () => {
                        ToggleGestureImage("Down");
                        textResult.text = defaultResultText = "Please write below gesture 5 times\nPress trigger to start\nRelease trigger when finish";
                    };
                } else {
                    nextUiAction = () => {
                        ToggleGestureImage("All");
                        SwitchToIdentify();
                    };
                }
            }

        } else {
            textToUpdate = string.Format(
                "<color=red>Smart Train failed criteria\n" +
                "Continue to add more samples</color>");
        }
    }

    // Handling smart identify match callback for developer defined gesture profile - This is invoked when the Mode is set to
    // Mode.SmartIdentify and a gesture is recorded.
    // The result is combination of common gesture result and custom gesture result. Either one match will return a positive result.
    // gestureId - a serial number
    // gesture - the gesture that match or None. If match, then must be one of gesture in SetTarget()
    void HandleOnSmartDeveloperDefinedMatch(long gestureId, string gesture) {
        string result = string.Format("<color=cyan>Smart Identify Match '{0}'</color>", gesture);
        textToUpdate = result;
    }

    void SwitchToIdentify() {
        StartCoroutine(airsigManager.UpdateDeveloperDefinedGestureStat(true));
        textResult.text = defaultResultText = string.Format("Try write 'Heart', 'C' and 'Down' gesture");
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.SmartIdentifyDeveloperDefined.ToString());
        airsigManager.SetMode(AirSigManager.Mode.SmartIdentifyDeveloperDefined);
        airsigManager.SetClassifier("SampleGestureProfile", "");
        airsigManager.SetDeveloperDefinedTarget(new List<string> { "HEART", "C", "DOWN" });
    }

    void ResetSmartGesture() {
        textResult.text = defaultResultText = "Please write below gesture 5 times\nPress trigger to start\nRelease trigger when finish";
        ToggleGestureImage("Heart");
        
        smartTrainCount = 0;
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.SmartTrainDeveloperDefined.ToString());
        airsigManager.SetMode(AirSigManager.Mode.SmartTrainDeveloperDefined);
        airsigManager.SetClassifier("SampleGestureProfile", "");
        airsigManager.SetDeveloperDefinedTarget(new List<string> { "HEART" });

        currentSmartTrainDeveloperDefinedTarget = "HEART";
    }

    // Use this for initialization
    void Awake() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

        // Update the display text
        textResult.alignment = TextAnchor.UpperCenter;
        instruction.SetActive(false);
        ToggleGestureImage("");

        // Configure AirSig by specifying target 
        developerDefined = new AirSigManager.OnDeveloperDefinedMatch(HandleOnDeveloperDefinedMatch);
        airsigManager.onDeveloperDefinedMatch += developerDefined;
        smartDeveloperDefined = new AirSigManager.OnSmartIdentifyDeveloperDefinedMatch(HandleOnSmartDeveloperDefinedMatch);
        airsigManager.onSmartIdentifyDeveloperDefinedMatch += smartDeveloperDefined;

        ResetSmartGesture();
    }


    void OnDestroy() {
        // Unregistering callback
        airsigManager.onDeveloperDefinedMatch -= developerDefined;
        airsigManager.onSmartIdentifyDeveloperDefinedMatch -= smartDeveloperDefined;
    }

    void Update() {
        UpdateUIandHandleControl();

        if (OVRInput.GetDown(OVRInput.RawButton.B)) {
            ResetSmartGesture();
        }
    }
}