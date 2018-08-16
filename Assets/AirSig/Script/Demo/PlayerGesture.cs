using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using AirSig;

public class PlayerGesture : BasedGestureHandle {

    // Gesture index to use for training and verifying custom gesture. Valid range is between 1 and 1000
    // Beware that setting to 100 will overwrite your player signature.
    readonly int PLAYER_GESTURE_ONE = 101;
    readonly int PLAYER_GESTURE_TWO = 102;

    readonly string GESTURE_ONE = "<color=#FF00FF>Gesture #1</color>";
    readonly string GESTURE_TWO = "<color=yellow>Gesture #2</color>";

    // How many gesture we need to collect for each gesture type
    readonly int MAX_TRAIN_COUNT = 5;

    // Use these steps to iterate gesture when train 'Smart Train' and 'Custom Gesture'
    int currentPlayerGestureTarget; // 101 = heart, 102 = down

    bool hasSetupGestureOne = false;
    bool hasSetupGestureTwo = false;

    // Callback for receiving signature/gesture progression or identification results
    AirSigManager.OnPlayerGestureMatch playerGestureMatch;
    AirSigManager.OnPlayerGestureAdd playerGestureAdd;

    // Handling custom gesture match callback - This is inovked when the Mode is set to Mode.IdentifyPlayerGesture and a gesture
    // is recorded.
    // gestureId - a serial number
    // match - the index that match or -1 if no match. The match index must be one in the SetTarget()
    void HandleOnPlayerGestureMatch(long gestureId, int match) {
        if (gestureId == 0) {

        } else {
            string result = "<color=red>Cannot find closest custom gesture</color>";
            if (PLAYER_GESTURE_ONE == match) {
                result = string.Format("<color=#FF00FF>Closest Custom Gesture Gesture #1</color>");
            } else if (PLAYER_GESTURE_TWO == match) {
                result = string.Format("<color=yellow>Closest Custom Gesture Gesture #2</color>");
            }

            // Check whether this gesture match any custom gesture in the database
            float[] data = airsigManager.GetFromCache(gestureId);
            bool isExisted = airsigManager.IsPlayerGestureExisted(data);
            result += isExisted ? string.Format("\n<color=green>There is a similar gesture in DB!</color>") :
                string.Format("\n<color=red>There is no similar gesture in DB!</color>");

            textToUpdate = result;
        }
    }

    // Handling custom gesture adding callback - This is invoked when the Mode is set to Mode.AddPlayerGesture and a gesture is
    // recorded. Gestures are only added to a cache. You should call SetPlayerGesture() to actually set gestures to database.
    // gestureId - a serial number
    // result - return a map of all un-set custom gestures and number of gesture collected.
    void HandleOnPlayerGestureAdd(long gestureId, Dictionary<int, int> result) {
        int count = result[currentPlayerGestureTarget];
        textToUpdate = string.Format("{0}{1}/{2} gesture(s) collected for {3}\nContinue to collect more samples</color>",
            currentPlayerGestureTarget == PLAYER_GESTURE_ONE ? "<color=#FF00FF>" : "<color=yellow>",
            count, MAX_TRAIN_COUNT,
            currentPlayerGestureTarget == PLAYER_GESTURE_ONE ? "Gesture #1" : "Gesture #2");
        if (count >= MAX_TRAIN_COUNT && currentPlayerGestureTarget != PLAYER_GESTURE_TWO) {
            currentPlayerGestureTarget++;
            textToUpdate = null; // UI will be handled by next UI action
            nextUiAction = () => {
                StopCoroutine(uiFeedback);
                EnterGesture(PLAYER_GESTURE_TWO);
                hasSetupGestureOne = true;
            };
        }
        else if (count >= MAX_TRAIN_COUNT && currentPlayerGestureTarget >= PLAYER_GESTURE_TWO) {
            textToUpdate = null; // UI will be handled by next UI action
            nextUiAction = () => {
                SwitchToIdentify();
                hasSetupGestureTwo = true;
            };
        } else {
            airsigManager.SetTarget(new List<int> { currentPlayerGestureTarget });
        }
    }

    void SwitchToIdentify() {
        StopCoroutine(uiFeedback);
        airsigManager.SetPlayerGesture(new List<int> {
                PLAYER_GESTURE_ONE,
                PLAYER_GESTURE_TWO
            }, true);
        textResult.text = defaultResultText = string.Format("Write gestures you just trained\nin AddPlayerGesture.\nPress the B key to reset");
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.IdentifyPlayerGesture.ToString());
        airsigManager.SetMode(AirSigManager.Mode.IdentifyPlayerGesture);
        airsigManager.SetTarget(new List<int> { PLAYER_GESTURE_ONE, PLAYER_GESTURE_TWO });
    }

    void EnterGesture(int target) {
        textResult.text = defaultResultText = string.Format("Think of a gesture\nWrite it 5 times~\n{0}",
            target == PLAYER_GESTURE_ONE ? "<color=#FF00FF>Gesture #1</color>" : "<color=yellow>Gesture #2</color>");
        textMode.text = string.Format("Mode: {0}", AirSigManager.Mode.AddPlayerGesture.ToString());
        airsigManager.SetMode(AirSigManager.Mode.AddPlayerGesture);
        airsigManager.SetTarget(new List<int> { target });
        currentPlayerGestureTarget = target;

        if (hasSetupGestureOne) {
            Debug.Log("Delete Gesture One");
            airsigManager.DeletePlayerRecord(PLAYER_GESTURE_ONE);
            hasSetupGestureOne = false;
        }
        if(hasSetupGestureTwo) {
            Debug.Log("Delete Gesture Two");
            airsigManager.DeletePlayerRecord(PLAYER_GESTURE_TWO);
            hasSetupGestureTwo = false;
        }
    }

    // Use this for initialization
    void Awake() {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

        // Update the display text
        textResult.alignment = TextAnchor.UpperCenter;
        instruction.SetActive(false);
        ToggleGestureImage("");

        // Configure AirSig by specifying target 
        playerGestureAdd = new AirSigManager.OnPlayerGestureAdd(HandleOnPlayerGestureAdd);
        airsigManager.onPlayerGestureAdd += playerGestureAdd;
        playerGestureMatch = new AirSigManager.OnPlayerGestureMatch(HandleOnPlayerGestureMatch);
        airsigManager.onPlayerGestureMatch += playerGestureMatch;

        EnterGesture(PLAYER_GESTURE_ONE);

    }


    void OnDestroy() {
        // Unregistering callback
        airsigManager.onPlayerGestureAdd -= playerGestureAdd;
        airsigManager.onPlayerGestureMatch -= playerGestureMatch;
    }

    void Update() {
        UpdateUIandHandleControl();

        if (OVRInput.GetDown(OVRInput.RawButton.B)) {
            EnterGesture(PLAYER_GESTURE_ONE);
        }
    }
}