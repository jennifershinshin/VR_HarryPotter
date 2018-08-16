using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AirSig {
    public class AirSigManager : MonoBehaviour {

        /// Enable debug logging
        public static bool DEBUG_LOG_ENABLED = true;

        // Default interval for sensor sampling rate.
        // Increasing this makes less sample for a fixed period of time
        private const float MIN_INTERVAL = 16f;

        /// Threshold score for a common gesture to be considered pass
        public const int COMMON_MISTOUCH_THRESHOLD = 30; //0.5sec (1 sec is about 60)
        public const float COMMON_PASS_THRESHOLD = 0.9f;
        public const float SMART_TRAIN_PASS_THRESHOLD = 0.8f;

        /// Threshold for engine
        public const float THRESHOLD_TRAINING_MATCH_THRESHOLD = 0.98f;
        public const float THRESHOLD_VERIFY_MATCH_THRESHOLD = 0.98f;
        public const float THRESHOLD_IS_TWO_GESTURE_SIMILAR = 1.0f;

        /// Identification/Training mode
        public enum Mode : int {
            None = 0x00, // will not perform any identification`
            IdentifyPlayerSignature = 0x02, // will perform user defined gesture identification
            DeveloperDefined = 0x08, // will perform predefined common identification
            TrainPlayerSignature = 0x10, // will perform training of a specific target
            AddPlayerGesture = 0x40,
            IdentifyPlayerGesture = 0x80,
            SmartTrainDeveloperDefined = 0x100,
            SmartIdentifyDeveloperDefined = 0x200
        };

        /// Errors used in OnPlayerSignatureTrained callback
        public class Error {

            public static readonly int SIGN_TOO_FEW_WORD = -204;
            public static readonly int SIGN_WITH_MISTOUCH = -200;

            public int code;
            public String message;

            public Error(int errorCode, String message) {
                this.code = errorCode;
                this.message = message;
            }
        }

        /// Strength used in OnPlayerSignatureTrained callback
        public enum SecurityLevel : int {
            None = 0,
            Very_Poor = 1,
            Poor = 2,
            Normal = 3,
            High = 4,
            Very_High = 5
        }

        // Mode of operation
        private Mode mCurrentMode = Mode.None;

        // Current target for 
        private List<int> mCurrentTarget = new List<int>();
        private List<string> mCurrentPredefined = new List<string>();
        private string mClassifier;
        private string mSubClassifier;
        private string FullClassifierPath {
            get { return mClassifier + "_" + mSubClassifier; }
        }
        private bool IsValidClassifier {
            get { return mClassifier != null && mClassifier.Length > 0 && mSubClassifier != null; }
        }

        // Keep the current instance
        private static AirSigManager sInstance;

        /// Event handler for receiving common gesture matching result
        public delegate void OnGestureDrawStart(bool start);
        public event OnGestureDrawStart onGestureDrawStart;

        /// Event handler for receiving custom predefined gesture matching result
        public delegate void OnDeveloperDefinedMatch(long gestureId, string gesture, float score);
        public event OnDeveloperDefinedMatch onDeveloperDefinedMatch;

        /// Event handler for receiving user gesture matching result
        public delegate void OnPlayerSignatureMatch(long gestureId, bool match, int targetIndex);
        public event OnPlayerSignatureMatch onPlayerSignatureMatch;

        /// Event handler for receiving smart identify of predefined gesture matching result
        public delegate void OnSmartIdentifyDeveloperDefinedMatch(long gestureId, string gesture);
        public event OnSmartIdentifyDeveloperDefinedMatch onSmartIdentifyDeveloperDefinedMatch;

        /// Event handler for receiving triggering of a gesture
        public class GestureTriggerEventArgs : EventArgs {
            public bool Continue { get; set; }
            public Mode Mode { get; set; }
            public List<int> Targets { get; set; }
        }
        public delegate void OnGestureTriggered(long gestureId, GestureTriggerEventArgs eventArgs);
        public event OnGestureTriggered onGestureTriggered;

        /// Event handler for receiving training result
        public delegate void OnPlayerSignatureTrained(long gestureId, Error error, float progress, SecurityLevel securityLevel);
        public event OnPlayerSignatureTrained onPlayerSignatureTrained;

        /// Event handler for receiving custom gesture result
        public delegate void OnPlayerGestureAdd(long gestureId, Dictionary<int, int> count);
        public event OnPlayerGestureAdd onPlayerGestureAdd;

        /// Event handler for receiving custom gesture result
        public delegate void OnPlayerGestureMatch(long gestureId, int match);
        public event OnPlayerGestureMatch onPlayerGestureMatch;

        //static Thread mainThread = Thread.CurrentThread;

#if UNITY_ANDROID
        // ========================================================================
        // Android implementation
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows implementation

        // Callback definition for bridging library
        private delegate void DataCallback(IntPtr buffer, int length, int entryLength);
        private DataCallback _DataCallbackHolder;

        private delegate void MovementCallback(int controller, int type);
        private MovementCallback _MovementCallbackHolder;

        private delegate void IdentifySigResult(IntPtr match, IntPtr error, int numberOfTimesCanTry, int secondsToReset);

        private delegate void VerifyPredGesResult(IntPtr gestureObj, float score, float conf);

        private delegate void AddSigResult(IntPtr action, IntPtr error, float progress, IntPtr securityLevel);

        private delegate void VerifyGesResult(IntPtr gesture, float score, float conf);

        // Load in exported functions
        [DllImport("AirsigBridgeDll")]
        private static extern IntPtr GetControllerHelperObject(byte[] buf, int length);

        [DllImport("AirsigBridgeDll")]
        private static extern IntPtr GetControllerHelperObjectWithConfig(byte[] buf, int length, float f1, float f2);

        [DllImport("AirsigBridgeDll")]
        private static extern void Shutdown(IntPtr obj);

        [DllImport("AirsigBridgeDll")]
        private static extern void SetSensorDataCallback(IntPtr obj, DataCallback callback);

        [DllImport("AirsigBridgeDll")]
        private static extern void SetMovementCallback(IntPtr obj, MovementCallback callback);

        [DllImport("AirsigBridgeDll")]
        private static extern void TestIn(IntPtr obj, float[] data, int length, int entryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern int GetActionIndex(IntPtr action);

        [DllImport("AirsigBridgeDll")]
        private static extern int GetErrorType(IntPtr error);

        [DllImport("AirsigBridgeDll")]
        private static extern void IdentifySignature(IntPtr obj, float[] data, int length, int entryLength, int[] targetIndex, int indexLength, IdentifySigResult callback);

        [DllImport("AirsigBridgeDll")]
        private static extern void AddSignature(IntPtr obj, int index, float[] data, int length, int entryLength, AddSigResult callback);

        [DllImport("AirsigBridgeDll")]
        private static extern bool DeleteAction(IntPtr obj, int index);

        [DllImport("AirsigBridgeDll")]
        private static extern void VerifyGesture(IntPtr obj, int[] indexes, int indexesLength, float[] data, int numDataEntry, int entryLength, VerifyGesResult callback);

        [DllImport("AirsigBridgeDll")]
        private static extern void VerifyPredefinedGesture(IntPtr obj,
            byte[] classifier, int classifierLength,
            byte[] subClassifier, int subClassifierLength,
            byte[] targetGestureNames, int[] eachTargetNameLength, int numTargetGesture,
            float[] data, int dataLength, int dataEntryLength,
            VerifyPredGesResult callback);

        [DllImport("AirsigBridgeDll")]
        private static extern void GetResultGesture(IntPtr gestureObj, byte[] buf, int length);

        [DllImport("AirsigBridgeDll")]
        private static extern int GetASGesture(IntPtr gesture);

        [DllImport("AirsigBridgeDll")]
        private static extern bool IsTwoGestureSimilar(IntPtr obj, float[] data1, int numData1Entry, int data1EntryLength, float[] data2, int numData2Entry, int data2EntryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern void SetCustomGesture(IntPtr obj, int gestureIndex, float[] dataArray, int arraySize, int[] numDataEntry, int[] dataEntryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern IntPtr IdentifyCustomGesture(IntPtr obj, int[] indexes, int indexesLength, float[] data, int numDataEntry, int entryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern void SetCustomGestureStr(IntPtr obj, byte[] gestureId, int gestureIdLength, float[] dataArray, int arraySize, int[] numDataEntry, int[] dataEntryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern IntPtr IdentifyCustomGestureStr(IntPtr obj, byte[] gestureIds, int numGesture, int[] gestureLength, float[] data, int numDataEntry, int entryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern bool IsCustomGestureExisted(IntPtr obj, float[] data, int numDataEntry, int entryLength);

        [DllImport("AirsigBridgeDll")]
        private static extern int GetASCustomGestureRecognizeGestureInt(IntPtr gesture);

        [DllImport("AirsigBridgeDll")]
        private static extern float GetASCustomGestureRecognizeGestureConfidence(IntPtr gesture);

        [DllImport("AirsigBridgeDll")]
        private static extern void GetASCustomGestureRecognizeGestureStr(IntPtr gesture, byte[] buf, int bufLength);

        [DllImport("AirsigBridgeDll")]
        private static extern void DeleteASCustomGestureRecognizeGesture(IntPtr gesture);

        IntPtr vrControllerHelper = IntPtr.Zero;
        // ========================================================================
#endif

        // Use to get ID of a gesture
        private static readonly DateTime InitTime = DateTime.UtcNow;
        private static long GetCurrentGestureID() {
            return (long)(DateTime.UtcNow - InitTime).TotalMilliseconds;
        }

        // Train fail accumlative Count
        private int mTrainFailCount = 0;

        // security level too low count
        private static int mSecurityTooLowCount = 0;

        // New training API
        private const int TRAINING_STEP = 5;
        //private float[] mTrainingProgress = new float[TRAINING_STEP] {
        //    0.2f, 0.4f, 0.6f, 0.8f, 1.0f
        //};

#if UNITY_ANDROID
        // ========================================================================
        // Android
        //
    	private List<AndroidJavaObject> mTrainingProgressGestures = new List<AndroidJavaObject>();
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows
        //
        private List<float[]> mTrainingProgressGestures = new List<float[]>();
        // ========================================================================
#endif

        // Cache for recent used sensor data
        private const int CACHE_SIZE = 10;
#if UNITY_ANDROID
        // ========================================================================
        // Android
        //
    	private SortedDictionary<long, AndroidJavaObject> mCache = new SortedDictionary<long, AndroidJavaObject>();
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows
        //
        private SortedDictionary<long, float[]> mCache = new SortedDictionary<long, float[]>();
        // ========================================================================
#endif

        // Data structure for saving current training status
        private TrainData mTrainData = new TrainData();
        //private bool mHasTrainDataChanged = false;

        // To handle short signature when setup 
        private static int mFirstTrainDataSize = 0;
        private const float TRAIN_DATA_THRESHOLD_RATIO = 0.65f;

        // Custom gesture result
        public class CustomGestureResult<T> {
            public T bestMatch;
            public float confidence;
        }


        // Cache for AddPlayerGesture, where int is actionIndex and List<float[]> is sensorData
#if UNITY_ANDROID
		private Dictionary<int, List<AndroidJavaObject>> mCustomGestureCache = new Dictionary<int, List<AndroidJavaObject>>();
#elif UNITY_STANDALONE_WIN
        private Dictionary<int, List<float[]>> mCustomGestureCache = new Dictionary<int, List<float[]>>();
#endif

        // Smart training sensor data of a same target
#if UNITY_ANDROID
        // ========================================================================
        // Android implementation
        //
		private SortedList<float, AndroidJavaObject> mSmartTrainCache = new SortedList<float, AndroidJavaObject> ();
		private class SmartTrainActionBundle {
			public List<AndroidJavaObject> cache;
			public int targetIndex;
			public int nextIndex;
			public float progress;
			public SmartTrainActionBundle (int targetIndex, List<AndroidJavaObject> cache) {
				this.targetIndex = targetIndex;
				this.cache = cache;
				this.nextIndex = cache.Count - 1; // starting from the last element
				this.progress = 0f;
			}
		}
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows implementation
        //
        private SortedList<float, float[]> mSmartTrainCache = new SortedList<float, float[]>();
        private class SmartTrainActionBundle {
            public List<float[]> cache;
            public int targetIndex;
            public int nextIndex;
            public float progress;
            public SmartTrainActionBundle(int targetIndex, List<float[]> cache) {
                this.targetIndex = targetIndex;
                this.cache = cache;
                this.nextIndex = cache.Count - 1; // starting from the last element
                this.progress = 0f;
            }
        }
        private class SmartTrainPredefinedActionBundle {
            public List<float[]> cache;
            public string targetGesture;
            public int nextIndex;
            public float progress;
            public SmartTrainPredefinedActionBundle(string targetPredefined, List<float[]> cache) {
                this.targetGesture = targetPredefined;
                this.cache = cache;
                this.nextIndex = cache.Count - 1; // starting from the last element
                this.progress = 0f;
            }
        }
        // ========================================================================
#endif

        // For storing smart identify result
#if UNITY_ANDROID
        // ========================================================================
        // Android implementation
        //
        private class IdentifyActionBundle {
        	public long id;
        	public int basedIndex;
        	public int matchIndex;
        	public string type;
        	public float score;
        	public AndroidJavaObject sensorData;
        	public IdentifyActionBundle(long gestureId, int basedIndex, AndroidJavaObject sensorData) {
        		this.id = gestureId;
        		this.basedIndex = basedIndex;
        		this.sensorData = sensorData;
        		this.score = 0f;
        	}
        }
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows implementation
        //
        private class IdentifyActionBundle {
            public long id;
            public int basedIndex;
            public int matchIndex;
            public string type;
            public float score;
            public float conf;
            public float[] sensorData;
            public IdentifyActionBundle(long gestureId, int basedIndex, float[] sensorData) {
                this.id = gestureId;
                this.basedIndex = basedIndex;
                this.sensorData = sensorData;
                this.score = 0f;
            }
        }

        private class IdentifyPredefinedActionBundle {
            public long id;
            public string basedGesture;
            public string matchGesture;
            public string type;
            public float score;
            public float conf;
            public float[] sensorData;
            public IdentifyPredefinedActionBundle(long gestureId, string basedGesture, float[] sensorData) {
                this.id = gestureId;
                this.basedGesture = basedGesture;
                this.sensorData = sensorData;
                this.score = 0f;
            }
        }
        // ========================================================================
#endif
        // For internal algorithm picking proirity
        private class ErrorCount {
            public int commonErrCount = 0;
            public int userErrCount= 0;
        }

        private class Confidence {
            public float commonConfidence = 0;
            public float userConfidence = 0;
        }

        // Store all Smart Gesture's error count stat
        private class CommonSmartGestureStat {
            public bool isStatExist = false;
            public Dictionary<int, ErrorCount> gestureStat = new Dictionary<int, ErrorCount>();
            public Dictionary<int, Confidence> gestureConf = new Dictionary<int, Confidence>();

            public void checkThenAdd(int index) {
                if( ! gestureStat.ContainsKey(index)) {
                    gestureStat[index] = new ErrorCount();
                }
                if( ! gestureConf.ContainsKey(index)) {
                    gestureConf[index] = new Confidence();
                }
            }
        }
        private CommonSmartGestureStat mCommonGestureStat = new CommonSmartGestureStat();
        //private bool mIsGestureStatExist = false;
        //private Dictionary<int, ErrorCount> mGestureStat = new Dictionary<int, ErrorCount>();

        private class PredefinedSmartGestureStat {
            public bool isStatExist = false;
            public Dictionary<string, ErrorCount> gestureStat = new Dictionary<string, ErrorCount>();
            public Dictionary<string, Confidence> gestureConf = new Dictionary<string, Confidence>();

            public void checkThenAdd(string index) {
                if (!gestureStat.ContainsKey(index)) {
                    gestureStat[index] = new ErrorCount();
                }
                if (!gestureConf.ContainsKey(index)) {
                    gestureConf[index] = new Confidence();
                }
            }
        }
        private Dictionary<string, PredefinedSmartGestureStat> mPredGestureStatDict = new Dictionary<string, PredefinedSmartGestureStat>();
        //private PredefinedSmartGestureStat mPredGestureStat = new PredefinedSmartGestureStat();
        //private bool mIsPredefinedGestureStatExist = false;
        //private Dictionary<string, ErrorCount> mPredefinedGestureStat = new Dictionary<string, ErrorCount>();

        // Store all cache of smart training
#if UNITY_ANDROID
        // ========================================================================
        // Android
        //
        private Dictionary<int, List<AndroidJavaObject>> mSmartTrainCacheCollection = new Dictionary<int, List<AndroidJavaObject>>();
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows
        //
        private Dictionary<string, List<float[]>> mSmartTrainPredefinedCacheCollection = new Dictionary<string, List<float[]>>();
        // ========================================================================
#endif

        

#if UNITY_ANDROID
		// ====================================================================
        // Android
        //
        // AirSig Engine
    	private const string licenseKey = "2dgc5evux5kp48lb6pjr8vy5dk4rxh9nyiz1ja6cymezv8054squq6sduz7miut187cplpamgb1ezdtih5v51wfq13nqaqhezag3qtr16jrac6jxrbizskdrjvaq91pcr5vua1jndwvspqb66jt0mk9c2xfl82svkgn6mlwhcrncztzev0prs90hwzcj7cvc0wchylui516b7av0f5t1veizg0mo82zzxqkwjfiy5vqwictrotsargs9tr0fifnjd9izjpu5unmd1uypisvvmvpt08meuo3wraxsuik63mf7r5g9gae9pkcgkemydn3q9aua3dz99gdk99o64pk1opxxuk6xg9qg5kp14hxb4nd889y7ksdwj9f7859y7ivxoligdq2qd7dro";

    	private static AndroidJavaObject sASEngineInstance;
    	private bool mIsASEngineValidLicense = false;

    	// Google VR
    	public GvrController mController;

    	/// ddcontroller
    	public GameObject ddcontroller;
    	public GameObject fromController;

		private bool appButtonState;
    	private bool touchButtonDown;
    	public static bool AppButton {
     		get {
     			return sInstance != null ? sInstance.appButtonState : false;
     		}
    	}
    	public static bool TouchButtonDown {
     		get {
     			return sInstance != null ? sInstance.touchButtonDown : false;
     		}
    	}

        // Daydream remote control manager for receving sensor data
    	private static AndroidJavaObject sControlManagerInstance;
		// ====================================================================
#elif UNITY_STANDALONE_WIN
        // ====================================================================
        // Windows
        //
        // Vive
        /*
        public ControllerInput leftHand;
        public ControllerInput rightHand;
        public static bool IsTriggering {
            get {
                return sInstance != null ?
                    (sInstance.leftHand != null && sInstance.leftHand.IsTriggering ()) ||
                    (sInstance.rightHand != null && sInstance.rightHand.IsTriggering ()) : false;
            }
        }
        public static bool IsTriggerUp {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsTriggerUp () : false;
            }
        }
        public static bool IsTriggerDown {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsTriggerDown () : false;
            }
        }
        public static bool IsTouching {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsTouching () : false;
            }
        }
        public static bool IsTouchUp {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsTouchUp () : false;
            }
        }
        public static bool IsTouchDown {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsTouchDown () : false;
            }
        }
        public static bool IsPressUp {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsPressUp () : false;
            }
        }
        public static bool IsPressDown {
            get {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsPressDown () : false;
            }
        }
        public static bool IsPressing
        {
            get
            {
                return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsPressing() : false;
            }
        }
        public static bool IsAppkeyPressing {
            get { return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsAppkeyPressing : false; }
        }
        public static bool IsAppkeyPressDown {
            get { return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsAppkeyDown : false; }
        }
        public static bool IsAppkeyPressUp {
            get { return sInstance != null ? sInstance.rightHand != null && sInstance.rightHand.IsAppkeyUp : false; }
        }
        */
        // ====================================================================
#endif

        private float mNotifyScoreThreshold = -0.5f;
        /// The minimum score value for common gesture to notify the receiver
        public float NotifyScoreThreshold {
            get {
                return mNotifyScoreThreshold;
            }
            set {
                mNotifyScoreThreshold = value;
            }
        }

        /// The minimum touch interval to trigger a gesture recognization
#if UNITY_ANDROID
		public long MinimumTouchInterval {
			get {
				return getControlManagerInstance().Call<long>("getMinimumTouchInterval");
			}
			set {
				object[] arglist = new object[1];
				arglist[0] = (long)value;
				getControlManagerInstance().Call("setMinimumTouchInterval", arglist);
			}
		}
#elif UNITY_STANDALONE_WIN
        // public long MinimumTouchInterval {
        // 	get {
        // 		return getControlManagerInstance().Call<long>("getMinimumTouchInterval");
        // 	}
        // 	set {
        // 		object[] arglist = new object[1];
        // 		arglist[0] = (long)value;
        // 		getControlManagerInstance().Call("setMinimumTouchInterval", arglist);
        // 	}
        // }

        public bool IsDbExist {
            get {
                string dbPath = Application.streamingAssetsPath + "/";
                if (System.IO.Directory.Exists(dbPath)) {
                    string[] files = System.IO.Directory.GetFiles(dbPath, "airsig_*.db", System.IO.SearchOption.TopDirectoryOnly);
                    if (files.Length <= 0) {
                        return false;
                    } else {
                        return true;
                    }
                }
                return false;
            }
           
        }
        
#endif

        /// Set and get for continuous recognization using pause interval for break
#if UNITY_ANDROID
		public bool ContinuousRecognizeEnabled {
			get {
				return getControlManagerInstance().Call<bool>("isContinuousRecognizeEnabled"); 
			}
			set {
				object[] arglist = new object[1];
				arglist[0] = value;
				getControlManagerInstance().Call("setContinuousRecognizeEnabled", arglist);
			}
		}
#elif UNITY_STANDALONE_WIN
        // public bool ContinuousRecognizeEnabled {
        // 	get {
        // 		return getControlManagerInstance().Call<bool>("isContinuousRecognizeEnabled"); 
        // 	}
        // 	set {
        // 		object[] arglist = new object[1];
        // 		arglist[0] = value;
        // 		getControlManagerInstance().Call("setContinuousRecognizeEnabled", arglist);
        // 	}
        // }
#endif

        /// Set and get for allowing click while collecting sensor samples
        public bool AllowTouchPadClick {
            get {
#if UNITY_ANDROID
				return getControlManagerInstance().Call<bool>("isAllowTouchpadClick"); 
#elif UNITY_STANDALONE_WIN
                return true;
#endif
            }
            set {
#if UNITY_ANDROID
				object[] arglist = new object[1];
				arglist[0] = value;
				getControlManagerInstance().Call("setAllowTouchpadClick", arglist);
#elif UNITY_STANDALONE_WIN
                // object[] arglist = new object[1];
                // arglist[0] = value;
                // getControlManagerInstance().Call("setAllowTouchpadClick", arglist);
#endif
            }
        }

        /// Set and get for thumb off touch pad touch pad tolerance
#if UNITY_ANDROID
		public long TouchPadTolerance {
			get {
				return getControlManagerInstance().Call<long>("getTouchPadTolerance"); 
			}
			set {
				object[] arglist = new object[1];
				arglist[0] = value;
				getControlManagerInstance().Call("setTouchPadTolerance", arglist);
			}
		}

		public long PauseInterval {
			get {
				return getControlManagerInstance().Call<long>("getPauseInterval"); 
			}
			set {
				object[] arglist = new object[1];
				arglist[0] = value;
				getControlManagerInstance().Call("setPauseInterval", arglist);
			}
		}
#elif UNITY_STANDALONE_WIN
        // public long TouchPadTolerance {
        // 	get {
        // 		return getControlManagerInstance().Call<long>("getTouchPadTolerance"); 
        // 	}
        // 	set {
        // 		object[] arglist = new object[1];
        // 		arglist[0] = value;
        // 		getControlManagerInstance().Call("setTouchPadTolerance", arglist);
        // 	}
        // }

        // public long PauseInterval {
        // 	get {
        // 		return getControlManagerInstance().Call<long>("getPauseInterval"); 
        // 	}
        // 	set {
        // 		object[] arglist = new object[1];
        // 		arglist[0] = value;
        // 		getControlManagerInstance().Call("setPauseInterval", arglist);
        // 	}
        // }
#endif

        public bool IsLowSecureSignature {
            get {
                return mSecurityTooLowCount > 2;
            }
        }

#if UNITY_ANDROID
		private Quaternion mLastTouchRotation;
		private Dictionary<int, Quaternion> mRecordTouchRotation = new Dictionary<int, Quaternion>();
	
		public void saveLastTouch(int id) {
			if(null != mLastTouchRotation) {
				mRecordTouchRotation.Add(id, mLastTouchRotation);
			}
		}

		public void showControllerForId(int id) {
			if(null != mRecordTouchRotation[id] && null != ddcontroller) {
				 ddcontroller.transform.rotation = mRecordTouchRotation[id];
			}
		}
	
        void OnSensorStartDraw(bool enable) {
            if (null != onGestureDrawStart) {
                sInstance.onGestureDrawStart(enable);
            }
		}
#elif UNITY_STANDALONE_WIN
        void OnSensorStartDraw(bool enable) {
            if (null != onGestureDrawStart) {
                sInstance.onGestureDrawStart(enable);
            }
        }
#endif

        // Receiver for Daydream controller
#if UNITY_ANDROID
        // ========================================================================
        // Android Implementation
        //
		private void OnControllerUpdate() {
			appButtonState = GvrController.AppButton;
			touchButtonDown = GvrController.ClickButtonDown;
			if(GvrController.TouchDown && null != fromController) {
				// store rotation of controller
				mLastTouchRotation = fromController.transform.rotation;
				//ddcontroller.transform.rotation = fromController.transform.rotation;
			}
		}

		private static AndroidJavaObject getControlManagerInstance() {
			if(null == sControlManagerInstance) {
				AndroidJavaClass unityPlayer = new AndroidJavaClass ("com.unity3d.player.UnityPlayer"); 
				AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject> ("currentActivity");

				AndroidJavaClass controlManagerC = new AndroidJavaClass ("com.airsig.dd_control_manager.ControlManager");

				object[] arglist = new object[1];
				arglist [0] = (object)activity;
				sControlManagerInstance = controlManagerC.CallStatic<AndroidJavaObject> ("getInstance", arglist);

				arglist [0] = (object)new ControlListener (sInstance);
				sControlManagerInstance.Call ("setUpdateListener", arglist);

				//arglist [0] = (object)new CallbackListener (sInstance);
				//sControlManagerInstance.Call ("setUpdateCallbackListener", arglist);
			}
			return sControlManagerInstance;
		}

		private static AndroidJavaObject getEngineInstance() {
			if (null == sASEngineInstance) {
				AndroidJavaClass unityPlayer = new AndroidJavaClass ("com.unity3d.player.UnityPlayer"); 
				AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject> ("currentActivity");

				AndroidJavaClass asEngineC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine");

				AndroidJavaClass asEngineParametersC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine$ASEngineParameters");

				AndroidJavaObject engineParam = asEngineParametersC.GetStatic<AndroidJavaObject> ("Default");
				engineParam.Set<int>("maxFailedTrialsInARow", Int32.MaxValue);
				engineParam.Set<int>("secondsToResetFailedTrialRecord", 0);
				engineParam.Set<float>("trainingMatchThreshold", THRESHOLD_TRAINING_MATCH_THRESHOLD);
				engineParam.Set<float>("verifyMatchThreshold", THRESHOLD_VERIFY_MATCH_THRESHOLD);

				object[] arglist = new object[4];  
				arglist [0] = (object)activity;  
				arglist [1] = (object)licenseKey;
				arglist [2] = null;
				arglist [3] = (object)engineParam;
				sASEngineInstance = asEngineC.CallStatic<AndroidJavaObject> ("initSharedInstance", arglist);
			}
			return sASEngineInstance;
		}
        // ==================================================================================
#endif

        /// Delete a trained player's gesture or signature
        public void DeletePlayerRecord(int targetIndex) {
#if UNITY_ANDROID
            // ====================================================================
            // Android implementation
            //
	        object[] arglist = new object[1];
	        arglist[0] = targetIndex;
	        getEngineInstance().Call<bool>("deleteAction", arglist);
            // ====================================================================
#elif UNITY_STANDALONE_WIN
            // ====================================================================
            // Window implementation
            //
            DeleteAction(vrControllerHelper, targetIndex);
            // ====================================================================
#endif
        }

#if UNITY_ANDROID
        // ========================================================================
        // Android implementation
        //
		private void TrainUserGesture(long id, int targetIndex, AndroidJavaObject sensorData, Action<SmartTrainActionBundle> furtherAction, SmartTrainActionBundle bundle) {
			if(mCurrentTarget.Count <= 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][TrainUserGesture] No Target to train");
				return;
			}
			object[] arglist = new object[2];
			arglist[0] = sensorData;
			arglist[1] = (object)true;
			AndroidJavaObject wrapper = new AndroidJavaObject("com.airsig.airsigengmulti.ASEngine$SensorData", arglist);

			AndroidJavaObject arrayList = new AndroidJavaObject("java.util.ArrayList");
			arglist = new object[1];
			arglist[0] = wrapper;
			arrayList.Call<bool>("add", arglist);

			arglist = new object[2];
			arglist [0] = arrayList;
			//arglist [1] = targetIndex;
			arglist [1] = new OnAddUserGestureListener (id, furtherAction, bundle);
			getEngineInstance ().Call ("addSignatures", arglist);
		}

		class OnAddUserGestureListener : AndroidJavaProxy {

			private long mId;
			private Action<SmartTrainActionBundle> mFurtherAction;
			private SmartTrainActionBundle mBundle;

			public OnAddUserGestureListener(long id, Action<SmartTrainActionBundle> furtherAction, SmartTrainActionBundle bundle)
				: base("com.airsig.airsigengmulti.ASEngine$OnAddSignaturesResultListener") {
				mId = id;
				mFurtherAction = furtherAction;
				mBundle = bundle;
			}

			bool onSecurityLevelTooWeak(AndroidJavaObject paramASSignatureSecurityLevel) {
				//Debug.LogWarning("onSecurityLevelTooWeak");
				mSecurityTooLowCount ++;
				if(mSecurityTooLowCount > 2) {
					return true;
				}
				return false;
			}

			// ASEngine.ASAction var1, ASEngine.ASError var2, float var3, ASEngine.ASSignatureSecurityLevel var4
			void onResult(AndroidJavaObject asAction, AndroidJavaObject asError, float progress, AndroidJavaObject securityLevel) {
				if(progress == 0.2f) {
					if(mBundle.cache.Count() > 0 && mFirstTrainDataSize == 0) {
						mFirstTrainDataSize = mBundle.cache[mBundle.cache.Count()-1].Call<int>("size");
						if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][OnAddUserGestureListener] First train data size: " + mFirstTrainDataSize);
					}
				}
				else if(progress >= 1.0f) {
					mFirstTrainDataSize = 0;
					mSecurityTooLowCount = 0;
				}
				else {
					if(mBundle.cache.Count() > 0) {
						int size = mBundle.cache[mBundle.cache.Count()-1].Call<int>("size");
						if(DEBUG_LOG_ENABLED) Debug.Log("2nd+ train size: " + size);
					}
				}

				if(null == sInstance.onUserSignatureTrained) {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][TrainUserGesture] Listener for onUserSignatureTrained does not exist");
					return;
				}
				if(null != asError) {
					if (DEBUG_LOG_ENABLED) Debug.Log (string.Format("[AirSigManager][TrainUserGesture] Add Signature({0}) Fail Due to - {1}: {2}",
						mBundle.targetIndex,
						asError.Get<int>("code"),
						asError.Get<string>("message")));
					if (DEBUG_LOG_ENABLED) Debug.Log (string.Format("[AirSigManager][TrainUserGesture] Progress:{0}  FailCount:{1}",
						progress,
						sInstance.mTrainFailCount));
				
					int size = mBundle.cache[mBundle.cache.Count()-1].Call<int>("size");
					if (DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager][TrainUserGesture] ***<< size=" + size + " >>***");

					if (size > COMMON_MISTOUCH_THRESHOLD) {
						if (mBundle.cache.Count () > 0 && mFirstTrainDataSize > 0 && progress >= 0.2f) {
							// it's not first train data
							if (size >= mFirstTrainDataSize * TRAIN_DATA_THRESHOLD_RATIO) {
								// size is less than threshold of the first data
								if (progress >= 0.5f) {
									// If user has progressed more than 3 times, add cumulative count
									if (DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager][TrainUserGesture] << Don't write consitant signature on purpose >>");
									sInstance.mTrainFailCount++;
									sInstance.onUserSignatureTrained (
										mId,
										new Error (asError.Get<int> ("code"), asError.Get<string> ("message")),
										progress,
										securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
								}
							} else {
								// mistouch for 2nd+ touch
								if (DEBUG_LOG_ENABLED)
									Debug.Log ("[AirSigManager][TrainUserGesture] << Use Sign too few word for mistouch >>");
								sInstance.mTrainFailCount++;
								sInstance.onUserSignatureTrained (
									mId,
									new Error (Error.SIGN_TOO_FEW_WORD, "Sign too few words"),
									progress, // progress stays same
									securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
							}

							if (sInstance.mTrainFailCount >= 3 || progress < 0.5f) {
								// Reset if any error
								sInstance.DeleteUserGesture (mBundle.targetIndex);
								// Report error
								sInstance.onUserSignatureTrained (
									mId,
									new Error (asError.Get<int> ("code"), asError.Get<string> ("message")),
									0f,
									securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));

								// Reset will also reset cumulative count
								sInstance.mTrainFailCount = 0;
							} else if (progress >= 0.5f) {
								sInstance.onUserSignatureTrained (
									mId,
									new Error (asError.Get<int> ("code"), asError.Get<string> ("message")),
									progress,
									securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
							}
						} else {
							sInstance.onUserSignatureTrained (
								mId,
								new Error (asError.Get<int> ("code"), asError.Get<string> ("message")),
								0f,
								securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
						}
					} else {
						// Less sample than the threshold, consider a mistouch
						sInstance.onUserSignatureTrained (
							mId,
							new Error (Error.SIGN_WITH_MISTOUCH, "Sign with mistouch"),
							progress,
							securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
					}
				}
				else if(null != asAction) {
					if (DEBUG_LOG_ENABLED) {
						Debug.Log ("[AirSigManager][TrainUserGesture] Add Signature status:" + asAction.Get<int>("actionIndex")
							+ ", progress:" + progress
							+ ", securityLevel:" + (securityLevel == null ? "N/A" : securityLevel.Get<int>("level").ToString()));
					}

					sInstance.onUserSignatureTrained(
						mId,
						asError == null ? null : new Error(asError.Get<int>("code"), asError.Get<string>("message")),
						progress,
						securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int>("level"));

					// A pass should reset the cumulative 
					//sInstance.mTrainFailCount = 0;
					mSecurityTooLowCount = 0;
				}

				if(null != mFurtherAction && null != mBundle) {
					mBundle.progress = progress;
					mFurtherAction(mBundle);
	        	}
	        }
	    }
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Window implementation
        //
        private void TrainUserGesture(long id, int targetIndex, float[] sensorData, Action<SmartTrainActionBundle> furtherAction, SmartTrainActionBundle bundle) {
            AddSignature(vrControllerHelper, targetIndex, sensorData, sensorData.Length / 10, 10,
                (IntPtr action, IntPtr error, float progress, IntPtr securityLevel) => {
                    if (progress == 0.2f) {
                        if (bundle.cache.Count() > 0 && mFirstTrainDataSize == 0) {
                            mFirstTrainDataSize = bundle.cache[bundle.cache.Count() - 1].Length / 10;
                            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][OnAddUserGestureListener] First train data size: " + mFirstTrainDataSize);
                        }
                    } else if (progress >= 1.0f) {
                        mFirstTrainDataSize = 0;
                        mSecurityTooLowCount = 0;
                    } else {
                        if (bundle.cache.Count() > 0) {
                            int size = bundle.cache[bundle.cache.Count() - 1].Length / 10;
                            if (DEBUG_LOG_ENABLED) Debug.Log("2nd+ train size: " + size);
                        }
                    }

                    if (null == sInstance.onPlayerSignatureTrained) {
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][TrainUserGesture] Listener for onPlayerSignatureTrained does not exist");
                        return;
                    }
                    int errorCode = 0;
                    if (IntPtr.Zero != error) {
                        errorCode = GetErrorType(error);
                    }
                    if (0 != errorCode) {
                        if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][TrainUserGesture] Add Signature({0}) Fail Due to - {1}",
                            bundle.targetIndex,
                            errorCode));
                        if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][TrainUserGesture] Progress:{0}  FailCount:{1}",
                            progress,
                            sInstance.mTrainFailCount));

                        int size = bundle.cache[bundle.cache.Count() - 1].Length / 10;
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][TrainUserGesture] ***<< size=" + size + " >>***");

                        if (size > COMMON_MISTOUCH_THRESHOLD) {
                            if (bundle.cache.Count() > 0 && mFirstTrainDataSize > 0 && progress >= 0.2f) {
                                // it's not first train data
                                if (size >= mFirstTrainDataSize * TRAIN_DATA_THRESHOLD_RATIO) {
                                    // size is less than threshold of the first data
                                    if (progress >= 0.5f) {
                                        // If user has progressed more than 3 times, add cumulative count
                                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][TrainUserGesture] << Don't write consitant signature on purpose >>");
                                        sInstance.mTrainFailCount++;
                                        sInstance.onPlayerSignatureTrained(
                                            id,
                                            new Error(errorCode, ""),
                                            progress,
                                            0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
                                    }
                                } else {
                                    // mistouch for 2nd+ touch
                                    if (DEBUG_LOG_ENABLED)
                                        Debug.Log("[AirSigManager][TrainUserGesture] << Use Sign too few word for mistouch >>");
                                    sInstance.mTrainFailCount++;
                                    sInstance.onPlayerSignatureTrained(
                                        id,
                                        new Error(Error.SIGN_TOO_FEW_WORD, "Sign too few words"),
                                        progress, // progress stays same
                                        0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
                                }

                                if (sInstance.mTrainFailCount >= 3 || progress < 0.5f) {
                                    // Reset if any error
                                    sInstance.DeletePlayerRecord(bundle.targetIndex);
                                    // Report error
                                    sInstance.onPlayerSignatureTrained(
                                        id,
                                        new Error(errorCode, ""),
                                        0f,
                                        0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));

                                    // Reset will also reset cumulative count
                                    sInstance.mTrainFailCount = 0;
                                } else if (progress >= 0.5f) {
                                    sInstance.onPlayerSignatureTrained(
                                        id,
                                        new Error(errorCode, ""),
                                        progress,
                                        0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
                                }
                            } else {
                                sInstance.onPlayerSignatureTrained(
                                    id,
                                    new Error(errorCode, ""),
                                    0f,
                                    0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int> ("level"));
                            }
                        } else {
                            // Less sample than the threshold, consider a mistouch
                            //sInstance.onPlayerSignatureTrained(
                            //    id,
                            //    new Error(Error.SIGN_WITH_MISTOUCH, "Sign with mistouch"),
                            //    progress,
                            //    0);
                        }
                    } else if (IntPtr.Zero != action) {
                        if (DEBUG_LOG_ENABLED) {
                            Debug.Log("[AirSigManager][TrainUserGesture] Add Signature status:" + GetActionIndex(action) +
                                ", progress:" + progress +
                                ", securityLevel: NOT IMPLEMENTED");
                        }

                        sInstance.onPlayerSignatureTrained(
                            id,
                            errorCode == 0 ? null : new Error(errorCode, ""),
                            progress,
                            0); //securityLevel == null ? (SecurityLevel)0 : (SecurityLevel)securityLevel.Get<int>("level"));

                        // A pass should reset the cumulative 
                        sInstance.mTrainFailCount = 0;
                        mSecurityTooLowCount = 0;
                    }

                    if (null != furtherAction && null != bundle) {
                        bundle.progress = progress;
                        furtherAction(bundle);
                    }
                });

        }
        // ========================================================================
#endif

#if UNITY_ANDROID
        // ========================================================================
        // Android implementation
        //
		private void SmartTrainUserGesture2(int target) {
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] index:"+target+" mSmartTrainCache.Count:" + mSmartTrainCache.Count);
			if(mSmartTrainCache.Count >= 3) { // we need minimum of 3 gesture to complete the training
				List<AndroidJavaObject> cache = new List<AndroidJavaObject>(mSmartTrainCache.Values);
				List<float> keys = new List<float>(mSmartTrainCache.Keys);
				keys.Reverse();
				cache.Reverse();
				if(target > (int)CommonGesture._Start && target < (int)CommonGesture._End) {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] targetIndex:" + target + " cacheOrder:" + string.Join(", ", keys.Select(x => x.ToString()).ToArray()));
					SmartTrainGestures(new SmartTrainActionBundle(target, cache));
				}
				else {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] first gestureIndex is not one of defined common gesture!! SmartTrainUserGesture2 quited!!");
				}
				// Store current smart train cache for later process
				mSmartTrainCacheCollection.Add(target, cache);
			}
			mSmartTrainCache.Clear();
		}

		private void SmartTrainGestures(SmartTrainActionBundle bundle) {
			if(bundle.nextIndex >= 0) {
				AndroidJavaObject sensorData = bundle.cache[bundle.nextIndex];
				bundle.nextIndex --;
				AddCustomGesture(sensorData);
				SmartTrainGestures(bundle);
			}
			else {
				mTrainData.useUserGesture.Add(bundle.targetIndex, true);

				AndroidJavaObject arrayList = new AndroidJavaObject("java.util.ArrayList");
				for(int i = 0; i < mTrainingProgressGestures.Count(); i ++) {
					object[] arglist = new object[1];
					arglist[0] = mTrainingProgressGestures[i];
					arrayList.Call<bool>("add", arglist);
				}
				// add them to the engine
				object[] engineArgs = new object[2];  
				engineArgs [0] = arrayList;  
				engineArgs [1] = bundle.targetIndex;
				if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("Add gestures ({0} records) to setCustomGesture for index:{1}", mTrainingProgressGestures.Count(), bundle.targetIndex));
				getEngineInstance().Call ("setCustomGesture", engineArgs);
				mTrainingProgressGestures.Clear();
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrainGestures] Smart Train Completed!!");
			}
		}

		private void SmartIdentifyGesture(long id, AndroidJavaObject sensorData) {
			IEnumerable<int> validTargets  = mCurrentTarget.Where(target => target > (int)CommonGesture._Start && target < (int)CommonGesture._End);
			if(validTargets.Count() <= 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] using SmartIdentify mode but target index doesn't contain valid targets: " + string.Join(", ", mCurrentTarget.Select(x => x.ToString()).ToArray()));
				return;
			}
			List<int> commonGestureTarget = validTargets.ToList();
			// Compare common first
			IdentifyCommonGesture(id, COMMON_PASS_THRESHOLD, sensorData, commonGestureTarget, SmartCommonIdentifyResult, new IdentifyActionBundle(id, 0, sensorData), false);
		}

		private void SmartCommonIdentifyResult(IdentifyActionBundle bundle) {
			if(null != bundle) {
				if(bundle.matchIndex > (int)CommonGesture._Start) {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] common identify ID:" + bundle.id +", matchIndex:" + bundle.matchIndex);
					// match found, but need to check up the gesture stat for error rate
					if(mGestureStat.ContainsKey(bundle.matchIndex) && mAlgorithmVer >= 1) {
						if(mGestureStat[bundle.matchIndex].commonErrCount > mGestureStat[bundle.matchIndex].userErrCount) {
							// common gesture worse than user define, switch to user define
							if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] common identify error count too high, try using 'user gesture'");
							Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
							if(keyColl.Count > 0) {
								if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] try lookup for these keys: " + string.Join(",", keyColl.Select(x => x.ToString()).ToArray()));
								// Verify first before we set to use "user gesture"
								int[] againstIndex = new int[keyColl.Count];
								keyColl.CopyTo(againstIndex, 0);
								againstIndex = againstIndex.Where(val => val != bundle.basedIndex).ToArray();
								Nullable<int> result = IdentifyCustomGesture(bundle.id, bundle.sensorData, againstIndex, false);
								if(null != result && result.HasValue) {
									if(result.Value == bundle.matchIndex) {
										// found user and common match the same target
										sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)result.Value);
										// mTrainData.IncUserGestureCount(bundle.basedIndex);
									}
									else {
										// found not equal
										if(mGestureStat.ContainsKey(result.Value)) {
											if(mGestureStat[result.Value].commonErrCount < mGestureStat[result.Value].userErrCount) {
												// delimma, use common to ensure not worse than before
												sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)bundle.matchIndex);
												return;
											}
										}
										// fallback to to user define if no error count found or user error count is less than common error count
										sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)result.Value);
									}
									return;
								}
							}
						}
					}
					sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)bundle.matchIndex);
					// mTrainData.IncCommonGestureCount(bundle.basedIndex);
					// if(mTrainData.Total() % 20 == 0) {
					// 	mHasTrainDataChanged = true;
					// }
				}
				else {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] common identify failed, try lookup 'user gesture'");
					Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
					if(keyColl.Count > 0) {
						if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] try lookup for these keys: " + string.Join(",", keyColl.Select(x => x.ToString()).ToArray()));
						// Verify first before we set to use "user gesture"
						int[] againstIndex = new int[keyColl.Count];
						keyColl.CopyTo(againstIndex, 0);
						againstIndex = againstIndex.Where(val => val != bundle.basedIndex).ToArray();
						Nullable<int> result = IdentifyCustomGesture(bundle.id, bundle.sensorData, againstIndex, false);
						if(null != result && result.HasValue) {
							sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)result.Value);
							mTrainData.IncUserGestureCount(bundle.basedIndex);
						}
						else {
							sInstance.onSmartIdentifyMatch(bundle.id, CommonGesture.None);
							mTrainData.IncFailedGestureCount(bundle.basedIndex);
						}
					}
					else {
						if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] no user gesture to lookup!");
						sInstance.onSmartIdentifyMatch(bundle.id, CommonGesture.None);
						mTrainData.IncFailedGestureCount(bundle.basedIndex);
					}
				}
			}
		}
        // ========================================================================
#elif UNITY_STANDALONE_WIN
        // ========================================================================
        // Windows implementation
        //
        private void SmartTrainPredefinedGesture(string target) {
            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrainDeveloperDefined] target:" + target + " mSmartTrainCache.Count:" + mSmartTrainCache.Count);
            if (mSmartTrainCache.Count >= 3) { // we need minimum of 3 gesture to complete the training
                List<float[]> cache = new List<float[]>(mSmartTrainCache.Values);
                List<float> keys = new List<float>(mSmartTrainCache.Keys);
                keys.Reverse();
                cache.Reverse();
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrainDeveloperDefined] target: " + target + ", cacheOrder:" + string.Join(", ", keys.Select(x => x.ToString()).ToArray()));
                SmartTrainPredefinedGesture2(new SmartTrainPredefinedActionBundle(target, cache));
                // Store current smart train cache for later process
                mSmartTrainPredefinedCacheCollection.Add(target, cache);
            }
            mSmartTrainCache.Clear();
        }

        private void SmartTrainGestures(SmartTrainActionBundle bundle) {
            if (bundle.nextIndex >= 0) {
                float[] sensorData = bundle.cache[bundle.nextIndex];
                bundle.nextIndex--;
                if (DEBUG_LOG_ENABLED) Debug.Log("[SmartTrainGestures] bundle.nextIndex: " + bundle.nextIndex);
                AddCustomGesture(sensorData);
                SmartTrainGestures(bundle);
            } else {
                try {
                    mTrainData.useUserGesture.Add(bundle.targetIndex, true);
                } catch (ArgumentException) {
                    mTrainData.useUserGesture[bundle.targetIndex] = true;
                }

                int totalLength = 0;
                int[] numDataEntryList = new int[mTrainingProgressGestures.Count()];
                int[] dataEntryLengthList = new int[mTrainingProgressGestures.Count()];
                for (int i = 0; i < mTrainingProgressGestures.Count(); i++) {
                    totalLength += mTrainingProgressGestures[i].Length;
                    numDataEntryList[i] = mTrainingProgressGestures[i].Length / 10;
                    dataEntryLengthList[i] = 10;
                }
                float[] dataList = new float[totalLength];
                for (int i = 0, k = 0; i < mTrainingProgressGestures.Count(); i++) {
                    float[] entry = mTrainingProgressGestures[i];
                    for (int j = 0; j < entry.Length; j++) {
                        dataList[k] = entry[j];
                        k++;
                    }
                }
                // float[][] dataList = new float[mTrainingProgressGestures.Count()][];
                // int[] numDataEntryList = new int[mTrainingProgressGestures.Count()];
                // int[] dataEntryLengthList = new int[mTrainingProgressGestures.Count()];
                // for(int i = 0; i < mTrainingProgressGestures.Count(); i ++) {
                // 	dataList[i] = mTrainingProgressGestures[i];
                // 	numDataEntryList[i] = mTrainingProgressGestures[i].Length / 10;
                // 	dataEntryLengthList[i] = 10;
                // }
                // add them to the engine
                if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("Add gestures ({0} records) to setCustomGesture for index:{1}", mTrainingProgressGestures.Count(), bundle.targetIndex));
                SetCustomGesture(vrControllerHelper, bundle.targetIndex, dataList, mTrainingProgressGestures.Count(), numDataEntryList, dataEntryLengthList);
                mTrainingProgressGestures.Clear();
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrainGestures] Smart Train Completed!!");
            }
        }

        private void SmartTrainPredefinedGesture2(SmartTrainPredefinedActionBundle bundle) {
            if (bundle.nextIndex >= 0) {
                float[] sensorData = bundle.cache[bundle.nextIndex];
                bundle.nextIndex--;
                if (DEBUG_LOG_ENABLED) Debug.Log("[SmartTrainPredefinedGestures] bundle.nextIndex: " + bundle.nextIndex);
                AddCustomGesture(sensorData);
                SmartTrainPredefinedGesture2(bundle);
            } else {
                try {
                    mTrainData.usePredefinedUserGesture.Add(bundle.targetGesture, true);
                } catch (ArgumentException) {
                    mTrainData.usePredefinedUserGesture[bundle.targetGesture] = true;
                }

                int totalLength = 0;
                int[] numDataEntryList = new int[mTrainingProgressGestures.Count()];
                int[] dataEntryLengthList = new int[mTrainingProgressGestures.Count()];
                for (int i = 0; i < mTrainingProgressGestures.Count(); i++) {
                    totalLength += mTrainingProgressGestures[i].Length;
                    numDataEntryList[i] = mTrainingProgressGestures[i].Length / 10;
                    dataEntryLengthList[i] = 10;
                }
                float[] dataList = new float[totalLength];
                for (int i = 0, k = 0; i < mTrainingProgressGestures.Count(); i++) {
                    float[] entry = mTrainingProgressGestures[i];
                    for (int j = 0; j < entry.Length; j++) {
                        dataList[k] = entry[j];
                        k++;
                    }
                }
                // add them to the engine
                if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("Add gestures ({0} records) to setCustomGesture for index:{1}", mTrainingProgressGestures.Count(), bundle.targetGesture));
                byte[] targetGestureInByte = Encoding.ASCII.GetBytes(bundle.targetGesture);
                SetCustomGestureStr(vrControllerHelper, targetGestureInByte, bundle.targetGesture.Length, dataList, mTrainingProgressGestures.Count(), numDataEntryList, dataEntryLengthList);
                mTrainingProgressGestures.Clear();
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrainPredefinedGestures] Smart Train Completed!!");
            }
        }

        private void SmartIdentifyPredefinedGesture(long id, float[] sensorData) {
            if(null == mCurrentPredefined || mCurrentPredefined.Count == 0) {
                if (DEBUG_LOG_ENABLED) Debug.LogWarning("[AirSigManager][SmartIdentifyDeveloperDefined] Identify without target!");
                return;
            }
            if (null == mClassifier || mClassifier.Length == 0) {
                if (DEBUG_LOG_ENABLED) Debug.LogWarning("[AirSigManager][SmartIdentifyDeveloperDefined] Identify without classifier!");
                return;
            }
            IdentifyPredefined(id, sensorData, mCurrentPredefined, mClassifier, mSubClassifier, SmartPredefinedIdentifyResult, new IdentifyPredefinedActionBundle(0, "", sensorData), false);
        }

        private void SmartPredefinedIdentifyResult(IdentifyPredefinedActionBundle bundle) {
            if(null != bundle) {
                if(bundle.matchGesture != null && bundle.matchGesture.Length > 0 && bundle.score > 1.0f) {
                    if(IsValidClassifier && mPredGestureStatDict.ContainsKey(FullClassifierPath)) {

                        if (mPredGestureStatDict[FullClassifierPath].gestureStat.ContainsKey(bundle.matchGesture)) {

                            bool isConfidenceFavorCommon = true;
                            if (mPredGestureStatDict[FullClassifierPath].gestureConf.ContainsKey(bundle.matchGesture)) {
                                if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("Confidence Level id {0} - common: {1}  user: {2}",
                                     bundle.id,
                                     mPredGestureStatDict[FullClassifierPath].gestureConf[bundle.matchGesture].commonConfidence,
                                     mPredGestureStatDict[FullClassifierPath].gestureConf[bundle.matchGesture].userConfidence));
                                isConfidenceFavorCommon = mPredGestureStatDict[FullClassifierPath].gestureConf[bundle.matchGesture].commonConfidence >=
                                    mPredGestureStatDict[FullClassifierPath].gestureConf[bundle.matchGesture].userConfidence;
                            }
                            bool toCompareCustom = false;
                            if (mPredGestureStatDict[FullClassifierPath].gestureStat[bundle.matchGesture].commonErrCount ==
                                mPredGestureStatDict[FullClassifierPath].gestureStat[bundle.matchGesture].userErrCount && !isConfidenceFavorCommon) {
                                toCompareCustom = true;
                            }

                            Dictionary<string, ErrorCount> gestureStat = mPredGestureStatDict[FullClassifierPath].gestureStat;
                            if (gestureStat[bundle.matchGesture].commonErrCount > gestureStat[bundle.matchGesture].userErrCount ||
                                toCompareCustom) {
                                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentifyDeveloperDefined] try lookup for these keys: " + string.Join(",", mCurrentPredefined.Select(x => x.ToString()).ToArray()));
                                // Verify first before we set to use "user gesture"
                                CustomGestureResult<string> result = IdentifyCustomGesture(bundle.id, bundle.sensorData, mCurrentPredefined);
                                if (result != null && mCurrentPredefined.Contains(result.bestMatch)) {
                                    if (result.bestMatch == bundle.matchGesture) {
                                        // found user and common match the same target
                                        sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, result.bestMatch);
                                    } else {
                                        // found not equal
                                        if (gestureStat.ContainsKey(result.bestMatch)) {
                                            if (gestureStat[result.bestMatch].commonErrCount < gestureStat[result.bestMatch].userErrCount) {
                                                // delimma, use common to ensure not worse than before
                                                sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, bundle.matchGesture);
                                                return;
                                            }
                                        }
                                        // fallback to to user define if no error count found or user error count is less than common error count
                                        sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, result.bestMatch);
                                    }
                                    return;
                                }
                                // custom gesture result invalid, use predefined result
                                sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, bundle.matchGesture);
                                return;
                            }
                        }
                    }
                    // no comparing stat or comparing stat cannot tell which one is worse,
                    // just report back the builtin predefined result
                    sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, bundle.matchGesture);
                } else {
                    if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentifyDeveloperDefined] common identify failed, try lookup 'user gesture'");
                    //Dictionary<string, bool>.KeyCollection keyColl = mTrainData.usePredefinedUserGesture.Keys;
                    if (mCurrentPredefined.Count > 0) {
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentifyDeveloperDefined] try lookup for these keys: " + string.Join(",", mCurrentPredefined.Select(x => x.ToString()).ToArray()));
                        // Verify first before we set to use "user gesture"
                        //List<string> keys = keyColl.ToList();
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentifyDeveloperDefined] against index: " + string.Join(",", mCurrentPredefined.Select(x => x.ToString()).ToArray()));
                        //string result = IdentifyPlayerGesture(bundle.id, bundle.sensorData, mCurrentPredefined);
                        CustomGestureResult<string> result = IdentifyCustomGesture(bundle.id, bundle.sensorData, mCurrentPredefined);
                        if (result != null) {
                            sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, result.bestMatch);
                        } else {
                            sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, "");
                        }
                    } else {
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentifyDeveloperDefined] no user gesture to lookup!");
                        sInstance.onSmartIdentifyDeveloperDefinedMatch(bundle.id, "");
                    }
                }
            }
        }
        // ========================================================================
#endif
        // private void SmartTrainUserGesture2(SmartTrainActionBundle bundle) {
        // 	if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][SmartTrain] targetIndex:{0} nextIndex:{1} progress:{2}",
        // 		bundle.targetIndex, bundle.nextIndex, bundle.progress));
        // 	if(bundle.nextIndex >= 0 && bundle.progress < 1f) {
        // 		// only we have more and progress is not completed so should we continue
        // 		AndroidJavaObject sensorData = bundle.cache[bundle.nextIndex];
        // 		bundle.nextIndex --;
        // 		TrainUserGesture(0, bundle.targetIndex, sensorData, SmartTrainUserGesture2, bundle);
        // 	}
        // 	else {
        // 		if(bundle.progress >= 1f) {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] we've done training~ grat!");
        // 			// Trained fine but we need verify against other trained data in order to let use of 'user gesture'
        // 			AndroidJavaObject sensorData = bundle.cache[bundle.cache.Count - 1]; // use last piece of data
        // 			if(null != sensorData) {
        // 				Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
        // 				if(keyColl.Count > 0) {
        // 					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] check with other user gesture - keyColl: " + string.Join(",", keyColl.Select(x => x.ToString()).ToArray()));
        // 					// Verify first before we set to use "user gesture"
        // 					int[] againstIndex = new int[keyColl.Count];
        // 					keyColl.CopyTo(againstIndex, 0);
        // 					againstIndex = againstIndex.Where(val => val != bundle.targetIndex).ToArray();
        // 					IdentifyUserGesture(0, sensorData, againstIndex, SmartTrainUserGestureAndVerify, new IdentifyActionBundle(0, bundle.targetIndex, null), true);
        // 				}
        // 				else {
        // 					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] no other user gesture to compare, just add 'user gesture' of index:" + bundle.targetIndex);
        // 					// No Key exist, just make this gesture to use "user gesture"
        // 					mTrainData.useUserGesture.Add(bundle.targetIndex, true);
        // 				}
        // 			}
        // 		}
        // 		else if(bundle.nextIndex < 0) {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] run out of sensor data! quit SmartTrain process!");
        // 			// Failed to train so let's remove 'use user gesture' flag for this index
        // 			mTrainData.useUserGesture.Remove(bundle.targetIndex);
        // 			mHasTrainDataChanged = true;
        // 		}
        // 		else {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] unexpected situation here!");
        // 		}
        // 	}
        // }

        // private void SmartTrainUserGestureAndVerify(IdentifyActionBundle bundle) {
        // 	if(bundle.matchIndex >= 0) {
        // 		if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] confliction of 'user gesture' and found - matchIndex:" + bundle.matchIndex + " AND basedIndex:" + bundle.basedIndex);
        // 		if(bundle.matchIndex != bundle.basedIndex) {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] This index will NOT be added due to confliction");
        // 			// We have conflict, not to add this index
        // 			// mTrainData.useUserGesture.Remove(bundle.matchIndex);
        // 			mTrainData.useUserGesture.Remove(bundle.basedIndex);
        // 			mHasTrainDataChanged = true;
        // 		}
        // 		else {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] Same index... keep it");
        // 		}
        // 	}
        // 	else {
        // 		if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartTrain] verify and no confliction found, add basedIndex:" + bundle.basedIndex);
        // 		mTrainData.useUserGesture[bundle.basedIndex] = true;
        // 		mHasTrainDataChanged = true;
        // 	}
        // }

        // private void SmartUserIdentifyResult(IdentifyActionBundle bundle) {
        // 	if(null != bundle) {
        // 		if(bundle.matchIndex > (int)CommonGesture._Start) {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] user identify ID:" + bundle.id +", matchIndex:" + bundle.matchIndex);
        // 			sInstance.onSmartIdentifyMatch(bundle.id, (CommonGesture)bundle.matchIndex);
        // 			mTrainData.IncUserGestureCount(bundle.basedIndex);
        // 		}
        // 		else {
        // 			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SmartIdentify] no user gesture match!");
        // 			sInstance.onSmartIdentifyMatch(bundle.id, CommonGesture.None);
        // 			mTrainData.IncFailedGestureCount(bundle.basedIndex);
        // 		}
        // 	}
        // }

#if UNITY_ANDROID
        // ====================================================================
        // Android implementation
        //
		private void IdentifyUserGesture(long id, AndroidJavaObject sensorData, int[] targetIndex, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool notifyObserver) {
			object[] arglist = new object[2];
			arglist[0] = sensorData;
			arglist[1] = (object)true;
			AndroidJavaObject wrapper = new AndroidJavaObject("com.airsig.airsigengmulti.ASEngine$SensorData", arglist);

			//arglist = new object[3];
			arglist = new object[2];
			//arglist [0] = targetIndex;
			arglist [0] = wrapper;
			arglist [1] = new OnIdentifyUserGestureListener (id, furtherAction, bundle, notifyObserver);
			timeStart2 = DateTime.UtcNow;
			getEngineInstance ().Call ("identifySignature", arglist);
		}

		private void IdentifyUserGesture(long id, AndroidJavaObject sensorData, int[] targetIndex) {
			IdentifyUserGesture(id, sensorData, targetIndex, null, null, true);
		}

		class OnIdentifyUserGestureListener : AndroidJavaProxy {

			private long mId;
			private Action<IdentifyActionBundle> mFurtherAction;
			private IdentifyActionBundle mBundle;
			private bool mNotifyObserver;

			public OnIdentifyUserGestureListener(long id, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool notifyObserver)
				: base("com.airsig.airsigengmulti.ASEngine$OnIdentifySignatureResultListener") {
				mId = id;
				mFurtherAction = furtherAction;
				mBundle = bundle;
				mNotifyObserver = notifyObserver;
			}

			void onResult(AndroidJavaObject asAction, AndroidJavaObject asError) {
				//Debug.Log("%%% Time spent " + ((long) (DateTime.UtcNow - sInstance.timeStart).TotalMilliseconds) + "ms, " + ((long) (DateTime.UtcNow - sInstance.timeStart2).TotalMilliseconds) + "ms");
				if(null == sInstance.onUserSignatureMatch) {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UserGesture] Listener for not onUserSignatureMatch does not exist");
					return;
				}
				if(null != asError) {
					if(DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager][UserGesture] Identify Fail: " + asError.Get<string>("message"));
					// no match
					if(mNotifyObserver) {
						sInstance.onUserSignatureMatch(mId, false, 0);
					}
					if(null != mFurtherAction && null != mBundle) {
						mBundle.matchIndex = -1;
						mBundle.type = "user";
						mFurtherAction(mBundle);
					}
				}
				else if(null != asAction) {
					int actionIndex = asAction.Get<int>("actionIndex");
					if(DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager][UserGesture] Identify Pass: " + asAction.Get<int>("actionIndex"));
					if(mNotifyObserver) {
						sInstance.onUserSignatureMatch(mId, true, actionIndex);
					}
					if(null != mFurtherAction && null != mBundle) {
						mBundle.matchIndex = actionIndex;
						mBundle.type = "user";
						mFurtherAction(mBundle);
					}
				}
			}
		}

		private void IdentifyCommonGesture(long id, float passScore, AndroidJavaObject sensorData, List<int> targetIndex) {
			IdentifyCommonGesture(id, passScore, sensorData, targetIndex, null, null, true);
		}
        // ====================================================================
#elif UNITY_STANDALONE_WIN
        // ====================================================================
        // Windows implementation
        //
        public void IdentifyUserGesture(float[] sensorData) {
            IdentifyUserGesture(0, sensorData, new int[] { 101 }, null, null, true);
        }

        private void IdentifyUserGesture(long id, float[] sensorData, int[] targetIndex, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool notifyObserver) {
            IdentifySignature(vrControllerHelper, sensorData, sensorData.Length / 10, 10, targetIndex, targetIndex.Length,
                (IntPtr match, IntPtr error, int numberOfTimesCanTry, int secondsToReset) => {
                    if (null == sInstance.onPlayerSignatureMatch) {
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UserGesture] Listener for not onPlayerSignatureMatch does not exist");
                        return;
                    }
                    int errorCode = 0;
                    if (IntPtr.Zero != error) {
                        errorCode = GetErrorType(error);
                    }
                    if (0 != errorCode) {
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UserGesture] Identify Fail: code-" + errorCode);
                        // no match
                        if (notifyObserver) {
                            sInstance.onPlayerSignatureMatch(id, false, 0);
                        }
                        if (null != furtherAction && null != bundle) {
                            bundle.matchIndex = -1;
                            bundle.type = "user";
                            furtherAction(bundle);
                        }
                    } else if (IntPtr.Zero != match) {
                        int actionIndex = GetActionIndex(match);
                        if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UserGesture] Identify Pass: " + actionIndex);
                        if (notifyObserver) {
                            sInstance.onPlayerSignatureMatch(id, true, actionIndex);
                        }
                        if (null != furtherAction && null != bundle) {
                            bundle.matchIndex = actionIndex;
                            bundle.type = "user";
                            furtherAction(bundle);
                        }
                    }
                });
        }

        private void IdentifyUserGesture(long id, float[] sensorData, int[] targetIndex) {
            IdentifyUserGesture(id, sensorData, targetIndex, null, null, true);
        }

        // ====================================================================
#endif

#if UNITY_ANDROID
        // ====================================================================
        // Android implementation
        //
		
		DateTime timeStart2;
		private void IdentifyCommonGesture(long id, float passScore, AndroidJavaObject sensorData, List<int> targetIndex, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool toInvokeCommonObserver) {
			if(targetIndex.Count <= 0) {
				return;
			}
			AndroidJavaClass asGestureC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine$ASGesture");
			AndroidJavaObject heart = asGestureC.GetStatic<AndroidJavaObject> ("HEART");
			AndroidJavaObject s = asGestureC.GetStatic<AndroidJavaObject> ("s");
			AndroidJavaObject c = asGestureC.GetStatic<AndroidJavaObject> ("c");
			AndroidJavaObject up = asGestureC.GetStatic<AndroidJavaObject> ("UP");
			AndroidJavaObject right = asGestureC.GetStatic<AndroidJavaObject> ("RIGHT");
			AndroidJavaObject down = asGestureC.GetStatic<AndroidJavaObject> ("DOWN");
			AndroidJavaObject left = asGestureC.GetStatic<AndroidJavaObject> ("LEFT");

			IEnumerable<int> query = targetIndex.Where(target => target > (int)CommonGesture._Start && target < (int)CommonGesture._End);
			List<AndroidJavaObject> targetList = new List<AndroidJavaObject>();
			IntPtr jniArray = AndroidJNI.NewObjectArray(query.Count(), asGestureC.GetRawClass(), heart.GetRawObject());

			for(int i = 0; i < query.Count(); i ++) {
				switch(query.ElementAt(i)) {
				case (int)CommonGesture.Heart:
					targetList.Add(heart);
					AndroidJNI.SetObjectArrayElement(jniArray, i, heart.GetRawObject());
					break;
				case (int)CommonGesture.S:
					targetList.Add(s);
					AndroidJNI.SetObjectArrayElement(jniArray, i, s.GetRawObject());
					break;
				case (int)CommonGesture.C:
					targetList.Add(c);
					AndroidJNI.SetObjectArrayElement(jniArray, i, c.GetRawObject());
					break;
				case (int)CommonGesture.Up:
					targetList.Add(up);
					AndroidJNI.SetObjectArrayElement(jniArray, i, up.GetRawObject());
					break;
				case (int)CommonGesture.Right:
					targetList.Add(right);
					AndroidJNI.SetObjectArrayElement(jniArray, i, right.GetRawObject());
					break;
				case (int)CommonGesture.Down:
					targetList.Add(down);
					AndroidJNI.SetObjectArrayElement(jniArray, i, down.GetRawObject());
					break;
				case (int)CommonGesture.Left:
					targetList.Add(left);
					AndroidJNI.SetObjectArrayElement(jniArray, i, left.GetRawObject());
					break;
				}
			}
			if(targetList.Count <= 0) {
				return;
			}
			IntPtr methodId = AndroidJNIHelper.GetMethodID(
				getEngineInstance().GetRawClass(),
				"multipleRecognizeGesture",
				"([Lcom/airsig/airsigengmulti/ASEngine$ASGesture;Ljava/util/ArrayList;Lcom/airsig/airsigengmulti/ASEngine$OnMultipleGestureRecognizingResultListener;)V");

			object[] argsToConv = new object[3];
			argsToConv[0] = jniArray;
			argsToConv[1] = sensorData.GetRawObject();
			argsToConv[2] = new OnMultipleGestureRecognizingResultListener(id, passScore, furtherAction, bundle, toInvokeCommonObserver);
			jvalue[] methodArgs = AndroidJNIHelper.CreateJNIArgArray(argsToConv);
			methodArgs[0].l = jniArray;
	 		methodArgs[1].l = sensorData.GetRawObject();
 
	 		timeStart2 = DateTime.UtcNow;
			AndroidJNI.CallVoidMethod(getEngineInstance().GetRawObject(), methodId, methodArgs);

		}

		class OnMultipleGestureRecognizingResultListener : AndroidJavaProxy {

			private long mId;
			private Action<IdentifyActionBundle> mFurtherAction;
			private IdentifyActionBundle mBundle;
			private float mPassScore;
			private bool mToInvokeCommonObserver;

			public OnMultipleGestureRecognizingResultListener(long id, float passScore, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool toInvokeCommonObserver)
				: base("com.airsig.airsigengmulti.ASEngine$OnMultipleGestureRecognizingResultListener") {
				mId = id;
				mFurtherAction = furtherAction;
				mBundle = bundle;
				mPassScore = passScore;
				mToInvokeCommonObserver = toInvokeCommonObserver;
			}

			void onResult(AndroidJavaObject asGesture, float v, AndroidJavaObject asError) {
				if(DEBUG_LOG_ENABLED) {
					Debug.Log(
						string.Format("[AirSigManager][IdentifyCommon] id:{0}, gesture:{1}, score:{2}, error:{3}",
							mId,
							asGesture == null ? "null" : asGesture.Call<string>("name"),
							v,
							asError == null ? "null" : asError.Get<string>("message"))
					);

				}

				String gestureName = asGesture.Call<string>("name");

				// TODO: refactor this matching
				CommonGesture gesture = 0;
				if(String.Compare(gestureName, "HEART", true) == 0) {
					gesture = CommonGesture.Heart;
				}
				else if(String.Compare(gestureName, "s", true) == 0) {
					gesture = CommonGesture.S;
				}
				else if(String.Compare(gestureName, "c", true) == 0) {
					gesture = CommonGesture.C;
				}
				else if(String.Compare(gestureName, "UP", true) == 0) {
					gesture = CommonGesture.Up;
				}
				else if(String.Compare(gestureName, "RIGHT", true) == 0) {
					gesture = CommonGesture.Right;
				}
				else if(String.Compare(gestureName, "DOWN", true) == 0) {
					gesture = CommonGesture.Down;
				}
				else if(String.Compare(gestureName, "LEFT", true) == 0) {
					gesture = CommonGesture.Left;
				}

				if(null != mFurtherAction && null != mBundle) {
					if(v >= mPassScore) {
						mBundle.matchIndex = (int)gesture;
					}
					else {
						mBundle.matchIndex = (int)CommonGesture.None;
					}
					mBundle.score = v;
					mBundle.type = "common";
					mFurtherAction(mBundle);
				}

				if(mToInvokeCommonObserver) {
					if(null != sInstance.onPredefinedGestureMatch) {
						if(v >= mPassScore) {
							sInstance.onPredefinedGestureMatch(mId, gesture, v);
						}
						else {
							sInstance.onPredefinedGestureMatch(mId, CommonGesture.None, v);
						}
					}
					else {
						if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][IdentifyCommon] Listener for onPredefinedGestureMatch does not exist!");
					}
				}
			}
		}
        // ====================================================================
#elif UNITY_STANDALONE_WIN
        // ====================================================================
        // Windows implementation
        //
        static char[] TAIL_TO_REMOVE = Encoding.ASCII.GetString(new byte[] { (byte)254 }).ToCharArray();
        static char[] NULL_TO_REMOVE = Encoding.ASCII.GetString(new byte[] { (byte)0 }).ToCharArray();
        private void IdentifyPredefined(long id, float[] sensorData, List<string> targetGesture, string classifier, string subClassifier) {
            IdentifyPredefined(id, sensorData, targetGesture, classifier, subClassifier, null, null, true);
        }

        private void IdentifyPredefined(long id, float[] sensorData, List<string> targetGesture, string classifier, string subClassifier, Action<IdentifyPredefinedActionBundle> furtherAction, IdentifyPredefinedActionBundle bundle, bool toInvokeCommonObserver) {
            byte[] tmpClassifierInByte = Encoding.ASCII.GetBytes(classifier);
            byte[] classifierInByte = new byte[tmpClassifierInByte.Length];
            Array.Copy(tmpClassifierInByte, classifierInByte, tmpClassifierInByte.Length);
            byte[] subClassifierInByte = Encoding.ASCII.GetBytes(subClassifier);

            int totalLength = 0;
            int[] targetGestureLength = new int[targetGesture.Count];
            for (int i = 0; i < targetGesture.Count; i++) {
                totalLength += targetGesture[i].Length;
                targetGestureLength[i] = targetGesture[i].Length;
            }
            byte[] targetGestureInByte = new byte[totalLength];

            int offset = 0;
            for(int i = 0; i < targetGesture.Count; i ++) {
                byte[] tmp = Encoding.ASCII.GetBytes(targetGesture[i]);
                Array.Copy(tmp, 0, targetGestureInByte, offset, targetGesture[i].Length);
                offset += targetGesture[i].Length;
            }
            VerifyPredefinedGesture(vrControllerHelper,
                classifierInByte, classifierInByte.Length,
                subClassifierInByte, subClassifierInByte.Length,
                targetGestureInByte, targetGestureLength, targetGesture.Count,
                sensorData, sensorData.Length / 10, 10,
                (IntPtr gestureObj, float score, float conf) => {
                    byte[] buf = Enumerable.Repeat<byte>(0, 1024).ToArray();
                    GetResultGesture(gestureObj, buf, 1024);
                    string gesture = Encoding.ASCII.GetString(buf)
                        .TrimEnd(TAIL_TO_REMOVE)
                        .TrimEnd(NULL_TO_REMOVE);
                    if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][DeveloperDefined] match: " + gesture + ", score: " + score + ", conf: " + conf);
                    if (null != furtherAction && null != bundle) {
                        bundle.matchGesture = gesture;
                        bundle.score = score;
                        bundle.type = "common";
                        bundle.conf = conf;
                        furtherAction(bundle);
                    }
                    if (toInvokeCommonObserver) {
                        if (null != sInstance.onDeveloperDefinedMatch) {
                            sInstance.onDeveloperDefinedMatch(id, gesture, score);
                        } else {
                            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][DeveloperDefined] Listener for onDeveloperDefinedMatch does not exist!");
                        }
                    }
                });
        }
        
        // ====================================================================
#endif

#if UNITY_ANDROID 
        // ====================================================================
        // Android implementation (via JNI)
        //
		class CallbackListener : AndroidJavaProxy {
			private AirSigManager manager;
			public CallbackListener(AirSigManager manager) : base("com.airsig.dd_control_manager.CallbackListener") {
				this.manager = manager;
			}

			void OnSensorStartDraw (bool enable) {
				//if(DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager] OnSensorStartDraw Event!!");
				sInstance.onGestureDrawStart(enable);
			}
		}

		class ControlListener : AndroidJavaProxy {

			private AirSigManager manager;

			public ControlListener(AirSigManager manager) : base("com.airsig.dd_control_manager.ControlListener") {
				this.manager = manager;
			}

			void OnSensorDataRecorded(AndroidJavaObject data, float length) {
				long id = GetCurrentGestureID();
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] OnSensorDataRecorded - id:" + id + ", mode: " + manager.mCurrentMode + ", length: " + length);
				manager.AddToCache(id, data);
				manager.PerformActionWithGesture(manager.mCurrentMode, id, data);
				if(null != sInstance.onGestureTriggered) {
					sInstance.onGestureTriggered(id, null);
				}
			}
		}

		void AddToCache(long id, AndroidJavaObject sensorData) {
			while(mCache.Count >= CACHE_SIZE) {
				KeyValuePair<long, AndroidJavaObject> instance = mCache.First();
				mCache.Remove(instance.Key);
			}
			mCache.Add(id, sensorData);
		}

		AndroidJavaObject GetFromCache(long id) {
			if(mCache.ContainsKey(id)) {
				return mCache[id];
			}
			return null;
		}

		KeyValuePair<long, AndroidJavaObject> GetLastFromCache() {
			if(mCache.Count > 0) {
				return mCache.Last();
			}
			return default(KeyValuePair<long, AndroidJavaObject>);
		}

		DateTime timeStart = DateTime.UtcNow;

		void PerformActionWithGesture(Mode action, long gestureId, AndroidJavaObject sensorData) {
			if(null == sensorData) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Sensor data is not available!");
				return;
			}
			if((action & AirSigManager.Mode.IdentifyUser) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyUserGesture for " + gestureId + "...");

				timeStart = DateTime.UtcNow;
				IdentifyUserGesture (gestureId, sensorData, mCurrentTarget.ToArray());
			}
			if((action & AirSigManager.Mode.IdentifyCommon) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyCommonGesture for " + gestureId + "...");

				timeStart = DateTime.UtcNow;
				IdentifyCommonGesture (gestureId, COMMON_PASS_THRESHOLD, sensorData, mCurrentTarget);
			}
			if((action & AirSigManager.Mode.TrainUser) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Train for " + gestureId + "...");
				TrainUserGesture(gestureId, mCurrentTarget.First(), sensorData, null, new SmartTrainActionBundle(mCurrentTarget.First(), new List<AndroidJavaObject>() {sensorData}));
			}
			if((action & AirSigManager.Mode.SmartTrain) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Add SmartTrain cache for " + gestureId + "...");
				//mSmartTrainCache.Add(sensorData);
				IdentifyCommonGesture (gestureId, COMMON_PASS_THRESHOLD, sensorData, new List<int>(){mCurrentTarget.First()}, SmartTrainFilterData, new IdentifyActionBundle(gestureId, 0, sensorData), true);
			}
			if((action & AirSigManager.Mode.SmartIdentify) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] SmartIdentify for " + gestureId + "...");

				timeStart = DateTime.UtcNow;
				SmartIdentifyGesture (gestureId, sensorData);
			}
			if((action & AirSigManager.Mode.AddCustomGesture) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] AddCustomGesture for " + gestureId + "...");

				//AddCustomGesture(sensorData);
				AddCustomGesture(gestureId, sensorData, mCurrentTarget);
			}
			if((action & AirSigManager.Mode.IdentifyCustomGesture) > 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyCustomGesture for " + gestureId + "...");

				IdentifyCustomGesture(gestureId, sensorData, mCurrentTarget.ToArray(), true);
			}
		}
    
		// Callback for filtering bad gesture before adding to smart train database
		private void SmartTrainFilterData(IdentifyActionBundle bundle) {
			if(mSmartTrainCache.Count > 0) {
				if(bundle.score > SMART_TRAIN_PASS_THRESHOLD) {
					if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][SmartTrain] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
					float key = bundle.score;
					while(mSmartTrainCache.ContainsKey(key)) {
						key += 0.0001f;
					}
					mSmartTrainCache.Add(key, bundle.sensorData);
				}
			}
			else {
				// The 1st data must be greater than COMMON_PASS_THRESHOLD
				if(bundle.score > COMMON_PASS_THRESHOLD) {
					if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][SmartTrain] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
					float key = bundle.score;
					mSmartTrainCache.Add(key, bundle.sensorData);
				}
			}
		}

		public void PerformActionWithGesture(Mode action, long gestureId) {
			PerformActionWithGesture(action, gestureId, GetFromCache(gestureId));
		}
        // ====================================================================
#elif UNITY_STANDALONE_WIN
        // ====================================================================
        // Windows implementation (via DLL)
        void onSensorDataRecorded(IntPtr buffer, int length, int entryLength) {
            long id = GetCurrentGestureID();
            if (DEBUG_LOG_ENABLED) Debug.Log("gesture - id: " + id + ", length: " + length + ", entryLength: " + entryLength + " received");
            int totalLength = length * entryLength;
            float[] data = new float[totalLength];
            Marshal.Copy(buffer, data, 0, totalLength);
            AddToCache(id, data);
            if (null != sInstance.onGestureTriggered) {
                GestureTriggerEventArgs eventArgs = new GestureTriggerEventArgs();
                eventArgs.Continue = true;
                eventArgs.Mode = mCurrentMode;
                eventArgs.Targets = mCurrentTarget.Select(item => item).ToList<int>();
                sInstance.onGestureTriggered(id, eventArgs);
                if (eventArgs.Continue) {
                    PerformActionWithGesture(eventArgs.Mode, eventArgs.Targets, id, data);
                }
            } else {
                PerformActionWithGesture(mCurrentMode, mCurrentTarget, id, data);
            }

        }

        void onMovementDetected(int controller, int type) {
            OnSensorStartDraw(type == 0);
        }

        void AddToCache(long id, float[] sensorData) {
            while (mCache.Count >= CACHE_SIZE) {
                KeyValuePair<long, float[]> instance = mCache.First();
                mCache.Remove(instance.Key);
            }
            mCache.Add(id, sensorData);
        }

        public float[] GetFromCache(long id) {
            if (mCache.ContainsKey(id)) {
                return mCache[id];
            }
            return null;
        }

        KeyValuePair<long, float[]> GetLastFromCache() {
            if (mCache.Count > 0) {
                return mCache.Last();
            }
            return default(KeyValuePair<long, float[]>);
        }

        public void PerformActionWithGesture(Mode action, List<int> targets, long gestureId, float[] sensorData) {
            if (null == sensorData || 0 == sensorData.Length) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Sensor data is not available!");
                return;
            }
            if ((action & AirSigManager.Mode.IdentifyPlayerSignature) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyUserGesture for " + gestureId + "...");

                // timeStart = DateTime.UtcNow;
                IdentifyUserGesture(gestureId, sensorData, targets.ToArray());
            }
            /*
            // Oculus Rift TODO
            if ((action & AirSigManager.Mode.IdentifyCommon) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyCommonGesture for " + gestureId + "...");

                IdentifyCommonGesture(gestureId, COMMON_PASS_THRESHOLD, sensorData, targets);
            }
            */
            if ((action & AirSigManager.Mode.TrainPlayerSignature) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Train for " + gestureId + "...");
                TrainUserGesture(gestureId, targets.First(), sensorData, null, new SmartTrainActionBundle(targets.First(), new List<float[]>() { sensorData }));
            }
            /*
            // Oculus Rift TODO
            if ((action & AirSigManager.Mode.SmartTrain) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Add SmartTrain cache for " + gestureId + "...");
                IdentifyCommonGesture(gestureId, COMMON_PASS_THRESHOLD, sensorData, new List<int>() { targets.First() }, SmartTrainFilterData, new IdentifyActionBundle(gestureId, 0, sensorData), true);
            }
            if ((action & AirSigManager.Mode.SmartIdentify) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] SmartIdentify for " + gestureId + "...");
                SmartIdentifyGesture(gestureId, sensorData);
            }
            */
            if ((action & AirSigManager.Mode.AddPlayerGesture) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] CustomGesture for " + gestureId + "...");
                //AddPlayerGesture (sensorData);
                AddCustomGesture(gestureId, sensorData, targets);
            }
            if ((action & AirSigManager.Mode.IdentifyPlayerGesture) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyPlayerGesture for " + gestureId + "...");
                IdentifyCustomGesture(gestureId, sensorData, targets.ToArray(), true);
            }
            
            // Oculus Rift TODO
            if ((action & AirSigManager.Mode.DeveloperDefined) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] DeveloperDefined for " + gestureId + "...");
                if (mClassifier == null || mClassifier.Length == 0) {
                    Debug.LogWarning("Empty classifiers are provided for DeveloperDefined! No identification will be performed!");
                    return;
                }
                IdentifyPredefined(gestureId, sensorData, mCurrentPredefined, mClassifier, mSubClassifier);
            }
            if((action & AirSigManager.Mode.SmartIdentifyDeveloperDefined) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] SmartIdentifyDeveloperDefined for " + gestureId + "...");
                SmartIdentifyPredefinedGesture(gestureId, sensorData);
            }
            if((action & AirSigManager.Mode.SmartTrainDeveloperDefined) > 0) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Add SmartTrain cache for " + gestureId + "...");
                IdentifyPredefined(gestureId, sensorData, new List<string>() { mCurrentPredefined.First() }, mClassifier, mSubClassifier, SmartTrainPredefinedFilterData, new IdentifyPredefinedActionBundle(gestureId, mCurrentPredefined.First(), sensorData), true);
            }
        }

        private void SmartTrainPredefinedFilterData(IdentifyPredefinedActionBundle bundle) {
            if (mSmartTrainCache.Count > 0) {
                if (bundle.score > SMART_TRAIN_PASS_THRESHOLD) {
                    if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][SmartTrainDeveloperDefined] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
                    float key = bundle.score;
                    while (mSmartTrainCache.ContainsKey(key)) {
                        key += 0.0001f;
                    }
                    mSmartTrainCache.Add(key, bundle.sensorData);
                }
            } else {
                // The 1st data must be greater than COMMON_PASS_THRESHOLD
                if (bundle.score > SMART_TRAIN_PASS_THRESHOLD) {
                    if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][SmartTrainDeveloperDefined] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
                    float key = bundle.score;
                    mSmartTrainCache.Add(key, bundle.sensorData);
                }
            }
        }

        private void SmartTrainFilterData (IdentifyActionBundle bundle) {
            if (mSmartTrainCache.Count > 0) {
                if (bundle.score > SMART_TRAIN_PASS_THRESHOLD) {
                    if (DEBUG_LOG_ENABLED) Debug.Log (string.Format ("[AirSigManager][SmartTrain] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
                    float key = bundle.score;
                    while (mSmartTrainCache.ContainsKey (key)) {
                        key += 0.0001f;
                    }
                    mSmartTrainCache.Add (key, bundle.sensorData);
                }
            } else {
                // The 1st data must be greater than COMMON_PASS_THRESHOLD
                if (bundle.score > SMART_TRAIN_PASS_THRESHOLD) {
                    if (DEBUG_LOG_ENABLED) Debug.Log (string.Format ("[AirSigManager][SmartTrain] Add gesture for smart train ID:{0} Score:{1}", bundle.id, bundle.score));
                    float key = bundle.score;
                    mSmartTrainCache.Add (key, bundle.sensorData);
                }
            }
        }

        public void PerformActionWithGesture (Mode action, List<int> targets, long gestureId) {
            PerformActionWithGesture (action, targets, gestureId, GetFromCache (gestureId));
        }
        // ====================================================================
#endif

#if UNITY_ANDROID
        // ====================================================================
        // Android
        //
		private Nullable<int> IdentifyCustomGesture(long id, AndroidJavaObject floatArray, int[] targets, bool notify) {
			object[] arglist = new object[2];
			arglist[0] = floatArray;
			arglist[1] = (object)true;
			AndroidJavaObject wrapper = new AndroidJavaObject("com.airsig.airsigengmulti.ASEngine$SensorData", arglist);

			arglist = new object[2];
			arglist [0] = targets;
			arglist [1] = wrapper;
			try {
				AndroidJavaObject intObj = getEngineInstance ().Call<AndroidJavaObject> ("identifyCustomGesture", arglist);
				if(intObj == null) {
					if(DEBUG_LOG_ENABLED) Debug.Log("IdentifyCustomGesture and '(404) NOT FOUND'");	
				}
				else {
					if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("IdentifyCustomGesture and found match index '{0}'", intObj.Call<string>("toString")));
					if(notify) {
	            		if(null != sInstance.onCustomGestureMatch) {
	                		sInstance.onCustomGestureMatch(id, intObj.Call<int>("intValue"));
	            		}
	        		}
					return new Nullable<int>(intObj.Call<int>("intValue"));
				}
			}
			catch(Exception e) {
				if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("IdentifyCustomGesture and found exception:\n{0}", e.ToString()));
			}
			if(notify) {
	            if(null != sInstance.onCustomGestureMatch) {
	           		sInstance.onCustomGestureMatch(id, -1);
	           	}
	       	}
			return null;
		}

		private bool AddCustomGesture (long gestureId, AndroidJavaObject floatArray, List<int> targets) {
			object[] tmp = new object[2];
			tmp[0] = floatArray;
			tmp[1] = (object)true;
			AndroidJavaObject sensorData = new AndroidJavaObject("com.airsig.airsigengmulti.ASEngine$SensorData", tmp);

			Dictionary<int, int> result = new Dictionary<int, int>();
			foreach(int index in targets) {
				if(mCustomGestureCache.ContainsKey(index)) {
					mCustomGestureCache[index].Add(sensorData);
				}
				else {
					mCustomGestureCache.Add(index, new List<AndroidJavaObject> { sensorData });
				}
				result.Add(index, mCustomGestureCache[index].Count);
			}
			if (null != sInstance.onCustomGestureAdd) {
				sInstance.onCustomGestureAdd(gestureId, result);
			}
			return true;
		}

		private bool AddCustomGesture(AndroidJavaObject floatArray) {
			bool isThisGestureAccepted = false;
			object[] tmp = new object[2];
			tmp[0] = floatArray;
			tmp[1] = (object)true;
			AndroidJavaObject sensorData = new AndroidJavaObject("com.airsig.airsigengmulti.ASEngine$SensorData", tmp);
			// check with previous data
			if(DEBUG_LOG_ENABLED) Debug.Log("=== AddCustomGesture ===");
			if(mTrainingProgressGestures.Count() == 0) {
				if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("Adding first data..."));
				mTrainingProgressGestures.Add(sensorData);
			
			}
			else {
				if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("Data {0}...", mTrainingProgressGestures.Count()));
				bool hasSimilar = false;
				for(int i = 0; i < mTrainingProgressGestures.Count(); i ++) {
					AndroidJavaObject previous = mTrainingProgressGestures[i];
					object[] arglist = new object[3];  
					arglist [0] = previous;  
					arglist [1] = sensorData;
					arglist [2] = THRESHOLD_IS_TWO_GESTURE_SIMILAR;
					bool isSimilar = getEngineInstance().Call<bool> ("isTwoGestureSimilar", arglist);
					if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("[{0}]  {1}", i, isSimilar));
					if(isSimilar) {
						hasSimilar = true;
					}
				}
				if(hasSimilar) {
					mTrainingProgressGestures.Add(sensorData);
					isThisGestureAccepted = true;
				}
			}
			return isThisGestureAccepted;
		}
        // ====================================================================
#elif UNITY_STANDALONE_WIN
        // ====================================================================
        // Windows
        //
        public bool IsPlayerGestureExisted(float[] floatArray) {
            return IsCustomGestureExisted(vrControllerHelper, floatArray, floatArray.Length / 10, 10);
        }

        public CustomGestureResult<int> IdentifyCustomGesture (long id, float[] floatArray, int[] targets, bool notify) {
            //int match = IdentifyCustomGesture (vrControllerHelper, targets, targets.Length, floatArray, floatArray.Length / 10, 10);
            IntPtr result = IdentifyCustomGesture(vrControllerHelper, targets, targets.Length, floatArray, floatArray.Length / 10, 10);
            int match = GetASCustomGestureRecognizeGestureInt(result);
            float conf = GetASCustomGestureRecognizeGestureConfidence(result);
            DeleteASCustomGestureRecognizeGesture(result);
            if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][IdentifyPlayerGesture] IdentifyPlayerGesture match: {0}, conf: {1}", match, conf));
            if (notify) {
                if(null != sInstance.onPlayerGestureMatch) {
                    sInstance.onPlayerGestureMatch(id, match);
                }
            }
            CustomGestureResult<int> resultMatch = new CustomGestureResult<int>();
            resultMatch.bestMatch = match;
            resultMatch.confidence = conf;
            return resultMatch;
        }

        public CustomGestureResult<string> IdentifyCustomGesture(long id, float[] floatArray, List<string> targetGesture) {
            int totalLength = 0;
            int[] targetGestureLength = new int[targetGesture.Count];
            for (int i = 0; i < targetGesture.Count; i++) {
                totalLength += targetGesture[i].Length;
                targetGestureLength[i] = targetGesture[i].Length;
            }
            byte[] targetGestureInByte = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < targetGesture.Count; i++) {
                byte[] tmp = Encoding.ASCII.GetBytes(targetGesture[i]);
                Array.Copy(tmp, 0, targetGestureInByte, offset, targetGesture[i].Length);
                offset += targetGesture[i].Length;
            }
            byte[] buf = Enumerable.Repeat<byte>(0, 1024).ToArray();
            IntPtr result = IdentifyCustomGestureStr(vrControllerHelper, targetGestureInByte, targetGesture.Count, targetGestureLength, floatArray, floatArray.Length / 10, 10);
            GetASCustomGestureRecognizeGestureStr(result, buf, 1024);
            float conf = GetASCustomGestureRecognizeGestureConfidence(result);
            DeleteASCustomGestureRecognizeGesture(result);
            string matchedGesture = Encoding.ASCII.GetString(buf)
                .TrimEnd(TAIL_TO_REMOVE)
                .TrimEnd(NULL_TO_REMOVE);
            if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("[AirSigManager][IdentifyPlayerGesture] IdentifyCustomGestureStr match: {0}, conf: {1}", matchedGesture, conf));
            CustomGestureResult<string> resultMatch = new CustomGestureResult<string>();
            resultMatch.bestMatch = matchedGesture;
            resultMatch.confidence = conf;
            return resultMatch;
        }

        // Add to cache only
        private bool AddCustomGesture (long gestureId, float[] data, List<int> targets) {
            Dictionary<int, int> result = new Dictionary<int, int>();
            foreach(int index in targets) {
                if(mCustomGestureCache.ContainsKey(index)) {
                    mCustomGestureCache[index].Add(data);
                }
                else {
                    mCustomGestureCache.Add(index, new List<float[]> { data });
                }
                result.Add(index, mCustomGestureCache[index].Count);
            }
            if (null != sInstance.onPlayerGestureAdd) {
                sInstance.onPlayerGestureAdd(gestureId, result);
            }
            return true;
        }

        private bool AddCustomGesture (float[] floatArray) {
            bool isThisGestureAccepted = false;
            // check with previous data
			if(DEBUG_LOG_ENABLED) Debug.Log("===== AddPlayerGesture =====");
            if (mTrainingProgressGestures.Count () == 0) {
                if(DEBUG_LOG_ENABLED) Debug.Log (string.Format ("Adding first data..."));
                mTrainingProgressGestures.Add (floatArray);

                isThisGestureAccepted = true;
            } else {
                if(DEBUG_LOG_ENABLED) Debug.Log (string.Format ("Data {0}...", mTrainingProgressGestures.Count ()));
                bool hasSimilar = false;
                for (int i = 0; i < mTrainingProgressGestures.Count (); i++) {
                    float[] previous = mTrainingProgressGestures[i];
                    bool isSimilar = IsTwoGestureSimilar (vrControllerHelper, previous, previous.Length / 10, 10, floatArray, floatArray.Length / 10, 10);
                    if(DEBUG_LOG_ENABLED) Debug.Log (string.Format ("[{0}]  {1}", i, isSimilar));
                    if (isSimilar) {
                        hasSimilar = true;
                    }
                }
                if (hasSimilar) {
                    mTrainingProgressGestures.Add (floatArray);
                    isThisGestureAccepted = true;
                }
            }
            return isThisGestureAccepted;
        }

        public bool IsTwoGestureSimilar(float[] gesture1, float[] gesture2) {
            return IsTwoGestureSimilar(vrControllerHelper, gesture1, gesture1.Length / 10, 10, gesture2, gesture2.Length / 10, 10);
        }
        // ====================================================================
#endif
        public enum Controller {
            RIGHT_HAND = 1,
            LEFT_HAND = 2
        }

        public enum TriggerButton {
            NONE,
            TOP_THUMB_BUTTON,
            BOTTOM_THUMB_BUTTON,
            INDEX_FINGER_BUTTON,
            HAND_BUTTON
        }

        public TriggerButton rightHandStartTrigger = TriggerButton.HAND_BUTTON;
        private TriggerButton rightHandEndTrigger = TriggerButton.NONE;
        public TriggerButton leftHandStartTrigger = TriggerButton.NONE;
        private TriggerButton leftHandEndTrigger = TriggerButton.NONE;

        bool isOculusRIndexPressed = false, isOculusRIndexPressedPrev = false;
        bool isOculusRIndexTriggeredUp = false, isOculusRIndexTriggeredDown = false;
        bool isOculusLIndexPressed = false, isOculusLIndexPressedPrev = false;
        bool isOculusLIndexTriggeredUp = false, isOculusLIndexTriggeredDown = false;

        //OVRInput.RawButton mRHandStartTrigger, mRHandEndTrigger;
        //OVRInput.RawButton mLHandStartTrigger, mLHandEndTrigger;

        /*
        private void UpdateKeys() {
            switch(rightHandStartTrigger) {
                case TriggerButton.TOP_THUMB_BUTTON:
                    mRHandStartTrigger = OVRInput.RawButton.B;
                    break;
                case TriggerButton.BOTTOM_THUMB_BUTTON:
                    mRHandStartTrigger = OVRInput.RawButton.A;
                    break;
                case TriggerButton.INDEX_FINGER_BUTTON:
                    mRHandStartTrigger = OVRInput.RawButton.RIndexTrigger;
                    break;
                case TriggerButton.HAND_BUTTON:
                    mRHandStartTrigger = OVRInput.RawButton.RHandTrigger;
                    break;
                default:
                    mRHandStartTrigger = OVRInput.RawButton.None;
                    break;
            }

            switch (rightHandEndTrigger) {
                case TriggerButton.TOP_THUMB_BUTTON:
                    mRHandEndTrigger = OVRInput.RawButton.B;
                    break;
                case TriggerButton.BOTTOM_THUMB_BUTTON:
                    mRHandEndTrigger = OVRInput.RawButton.A;
                    break;
                case TriggerButton.INDEX_FINGER_BUTTON:
                    mRHandEndTrigger = OVRInput.RawButton.RIndexTrigger;
                    break;
                case TriggerButton.HAND_BUTTON:
                    mRHandEndTrigger = OVRInput.RawButton.RHandTrigger;
                    break;
                default:
                    mRHandEndTrigger = OVRInput.RawButton.None;
                    break;
            }

            switch (leftHandStartTrigger) {
                case TriggerButton.TOP_THUMB_BUTTON:
                    mLHandStartTrigger = OVRInput.RawButton.Y;
                    break;
                case TriggerButton.BOTTOM_THUMB_BUTTON:
                    mLHandStartTrigger = OVRInput.RawButton.X;
                    break;
                case TriggerButton.INDEX_FINGER_BUTTON:
                    mLHandStartTrigger = OVRInput.RawButton.LIndexTrigger;
                    break;
                case TriggerButton.HAND_BUTTON:
                    mLHandStartTrigger = OVRInput.RawButton.LHandTrigger;
                    break;
                default:
                    mLHandStartTrigger = OVRInput.RawButton.None;
                    break;
            }

            switch (leftHandEndTrigger) {
                case TriggerButton.TOP_THUMB_BUTTON:
                    mLHandEndTrigger = OVRInput.RawButton.Y;
                    break;
                case TriggerButton.BOTTOM_THUMB_BUTTON:
                    mLHandEndTrigger = OVRInput.RawButton.X;
                    break;
                case TriggerButton.INDEX_FINGER_BUTTON:
                    mLHandEndTrigger = OVRInput.RawButton.LIndexTrigger;
                    break;
                case TriggerButton.HAND_BUTTON:
                    mLHandEndTrigger = OVRInput.RawButton.LHandTrigger;
                    break;
                default:
                    mLHandEndTrigger = OVRInput.RawButton.None;
                    break;
            }
        }

        
        private void SetTriggerStartKeys(OVRInput.RawButton btn) {
            if (btn == OVRInput.RawButton.B ||
                btn == OVRInput.RawButton.A ||
                btn == OVRInput.RawButton.RIndexTrigger ||
                btn == OVRInput.RawButton.RHandTrigger) {
                mRHandStartTrigger = btn;
            }
            else if(btn == OVRInput.RawButton.X ||
                btn == OVRInput.RawButton.Y ||
                btn == OVRInput.RawButton.LIndexTrigger ||
                btn == OVRInput.RawButton.LHandTrigger) {
                mLHandStartTrigger = btn;
            }
            else {
                Debug.LogWarning("Unsupported RawButton");
            }
        }

        private void ClearTriggerStartKeys(bool isRightHand) {
            if (isRightHand) mRHandStartTrigger = 0;
            else mLHandStartTrigger = 0;
        }

        private void SetTriggerEndKeys(OVRInput.RawButton btn) {
            if (btn == OVRInput.RawButton.B ||
                btn == OVRInput.RawButton.A ||
                btn == OVRInput.RawButton.RIndexTrigger ||
                btn == OVRInput.RawButton.RHandTrigger) {
                mRHandEndTrigger = btn;
            } else if (btn == OVRInput.RawButton.X ||
                  btn == OVRInput.RawButton.Y ||
                  btn == OVRInput.RawButton.LIndexTrigger ||
                  btn == OVRInput.RawButton.LHandTrigger) {
                mLHandEndTrigger = btn;
            } else {
                Debug.LogWarning("Unsupported RawButton");
            }
        }

        private void ClearTriggerEndKeys(bool isRightHand) {
            if (isRightHand) mRHandEndTrigger = 0;
            else mLHandEndTrigger = 0;
        }
        */

        long c = 0;

        void Update () {
            OVRInput.Update();

            c++;

            isOculusRIndexTriggeredDown = isOculusRIndexTriggeredUp = false;
            isOculusLIndexTriggeredDown = isOculusLIndexTriggeredUp = false;

            float rTriggerKeyValue = 0f, lTriggerKeyValue = 0f;
            if(rightHandStartTrigger == TriggerButton.TOP_THUMB_BUTTON) {
                if(OVRInput.Get(OVRInput.RawButton.B)) {
                    rTriggerKeyValue = 1f;
                }
                else {
                    rTriggerKeyValue = 0f;
                }              
            }
            else if(rightHandStartTrigger == TriggerButton.BOTTOM_THUMB_BUTTON) {
                if (OVRInput.Get(OVRInput.RawButton.A)) {
                    rTriggerKeyValue = 1f;
                } else {
                    rTriggerKeyValue = 0f;
                }
            }
            else if (rightHandStartTrigger == TriggerButton.INDEX_FINGER_BUTTON) {
                rTriggerKeyValue = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
            } 
            else if (rightHandStartTrigger == TriggerButton.HAND_BUTTON) {
                rTriggerKeyValue = OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger);
            }
            else {
                rTriggerKeyValue = 0f;
            }

            if (leftHandStartTrigger == TriggerButton.TOP_THUMB_BUTTON) {
                if (OVRInput.Get(OVRInput.RawButton.Y)) {
                    lTriggerKeyValue = 1f;
                } else  {
                    lTriggerKeyValue = 0f;
                }
                //Debug.Log("left trigger down: " + OVRInput.GetDown(OVRInput.RawButton.Y) + ", up: " + OVRInput.GetUp(OVRInput.RawButton.Y));
            } else if (leftHandStartTrigger == TriggerButton.BOTTOM_THUMB_BUTTON) {
                
                if (OVRInput.Get(OVRInput.RawButton.X)) {
                    lTriggerKeyValue = 1f;
                } else {
                    lTriggerKeyValue = 0f;
                }
                //Debug.Log("left trigger down: " + OVRInput.GetDown(OVRInput.RawButton.X) + ", up: " + OVRInput.GetUp(OVRInput.RawButton.X));
            } else if (leftHandStartTrigger == TriggerButton.INDEX_FINGER_BUTTON) {
                lTriggerKeyValue = OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger);
            } else if (leftHandStartTrigger == TriggerButton.HAND_BUTTON) {
                lTriggerKeyValue = OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger);
            } else {
                lTriggerKeyValue = 0f;
            }

            //Debug.Log(string.Format("{0}: RightHand {1}, LeftHand {2}", c, rTriggerKeyValue, lTriggerKeyValue));

            if (rTriggerKeyValue > 0.8f) {
                isOculusRIndexPressed = true;
            }
            else if(rTriggerKeyValue < 0.1f) {
                isOculusRIndexPressed = false;
            }

            if (lTriggerKeyValue > 0.8f) {
                isOculusLIndexPressed = true;
            } else if (lTriggerKeyValue < 0.1f) {
                isOculusLIndexPressed = false;
            }

            if ( ! isOculusRIndexPressedPrev && isOculusRIndexPressed ) {
                isOculusRIndexTriggeredDown = true;
            } else if( isOculusRIndexPressedPrev && ! isOculusRIndexPressed) {
                isOculusRIndexTriggeredUp = true;
            }

            if (!isOculusLIndexPressedPrev && isOculusLIndexPressed) {
                isOculusLIndexTriggeredDown = true;
            } else if (isOculusLIndexPressedPrev && !isOculusLIndexPressed) {
                isOculusLIndexTriggeredUp = true;
            }

            if (isOculusRIndexTriggeredDown)
            {
                Debug.Log("R Down");
                isCollectingRControllerData = true;
                rThread = new Thread(CollectRControllerData);
                rThread.IsBackground = true;
                rThread.Start();
            } else if (isOculusRIndexTriggeredUp) {
                Debug.Log("R UP");
                isCollectingRControllerData = false;
                rThread.Abort();

                Thread t = new Thread(() => processCollectData(rCollectSamples));
                t.IsBackground = true;
                t.Start();
            }

            if (isOculusLIndexTriggeredDown) {
                Debug.Log("L Down");
                isCollectingLControllerData = true;
                lThread = new Thread(CollectLControllerData);
                lThread.IsBackground = true;
                lThread.Start();
            } else if (isOculusLIndexTriggeredUp) {
                Debug.Log("L UP");
                isCollectingLControllerData = false;
                lThread.Abort();

                Thread t = new Thread(() => processCollectData(lCollectSamples));
                t.IsBackground = true;
                t.Start();
            }

            isOculusRIndexPressedPrev = isOculusRIndexPressed;
            isOculusLIndexPressedPrev = isOculusLIndexPressed;
        }

        bool mIsInitReady = false;
        void Awake() {
            // Application.targetFrameRate = 45;
            // print("[AirSigManager] after changing framerate target: " + Application.targetFrameRate);

            if (sInstance != null) {
                Debug.LogError("More than one AirSigManager instance was found in your scene. " +
                    "Ensure that there is only one AirSigManager GameObject.");
                this.enabled = false;
                return;
            }
            sInstance = this;

            // Shorten the debug message
            //Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            // Init AirSig engine
#if UNITY_ANDROID
            // ====================================================================
            // Android implementation
            //
			AirSigManager.getEngineInstance ();

			// Register Gvr update
			mController.OnControllerUpdate += new GvrController.OnControllerUpdateEvent (OnControllerUpdate);

			// Init Daydream control
			AirSigManager.getControlManagerInstance ();
			PauseInterval = 185;
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Continuous recognize pause interval: " + PauseInterval);
            // ====================================================================
#elif UNITY_STANDALONE_WIN
            // ====================================================================
            // Windows implementation
            string dbPath = Application.streamingAssetsPath + "/";
            
            if (System.IO.Directory.Exists(dbPath)) { 
                string[] files = System.IO.Directory.GetFiles(dbPath, "airsig_*.db", System.IO.SearchOption.TopDirectoryOnly);
                if (files.Length <= 0) {
                    Debug.LogError("Db files do not exist!");
                    return;
                } else {
                    foreach (string file in files) {
                        Debug.Log("DB file found: " + file);
                    }
                }
            }
            else {
                Debug.LogError("Db folder does not exist!");
                return;
            }
            
            

            byte[] path = Encoding.ASCII.GetBytes(dbPath);
            vrControllerHelper = GetControllerHelperObjectWithConfig(path, path.Length, 0.98f, 0.98f);
            if (DEBUG_LOG_ENABLED) Debug.Log ("IntPtr: " + vrControllerHelper);

            _DataCallbackHolder = new DataCallback (onSensorDataRecorded);
            SetSensorDataCallback (vrControllerHelper, _DataCallbackHolder);

            _MovementCallbackHolder = new MovementCallback(onMovementDetected);
            SetMovementCallback(vrControllerHelper, _MovementCallbackHolder);
            // ====================================================================
#endif

            mCache.Clear ();

            // if(File.Exists(Application.persistentDataPath + "/trainData.dat")) {
            // 	BinaryFormatter bf = new BinaryFormatter();
            // 	FileStream file = File.Open(Application.persistentDataPath + "/trainData.dat", FileMode.Open);
            // 	mTrainData = (TrainData)bf.Deserialize(file);
            // 	file.Close();
            // }
			Load();

            mIsInitReady = true;
        }

        void OnDestroy () {
            isCollectingRControllerData = false;
            isCollectingLControllerData = false;
            if (rThread != null) {
                rThread.Abort();
            }
            if (lThread != null) {
                lThread.Abort();
            }

            sInstance = null;

            if (!mIsInitReady)
                return;

#if UNITY_ANDROID
            // ====================================================================
            // Android implementation
            // [v]
            // mController.OnControllerUpdate -= new GvrController.OnControllerUpdateEvent (OnControllerUpdate);
            // ====================================================================
#elif UNITY_STANDALONE_WIN
            // ====================================================================
            // Windows implementation
            SetSensorDataCallback (vrControllerHelper, null);
            _DataCallbackHolder = null;

            Shutdown (vrControllerHelper);

            vrControllerHelper = IntPtr.Zero;
            // ====================================================================
#endif
        }

        void Load () {
            if (File.Exists (Application.persistentDataPath + "/trainData.dat")) {
                BinaryFormatter bf = new BinaryFormatter ();
                FileStream file = File.Open (Application.persistentDataPath + "/trainData.dat", FileMode.Open);
                mTrainData = (TrainData) bf.Deserialize (file);
                file.Close ();

                Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
                if (DEBUG_LOG_ENABLED) Debug.Log ("[AirSigManager] Trained Data Loaded - " + string.Join (",", keyColl.Select (x => x.ToString ()).ToArray ()));

                long user = mTrainData.userGestureCount.Sum (x => x.Value);
                long common = mTrainData.commonGestureCount.Sum (x => x.Value);
                long fail = mTrainData.failedGestureCount.Sum (x => x.Value);
                long total = mTrainData.Total ();
                if (DEBUG_LOG_ENABLED) Debug.Log (string.Format ("[AirSigManager] History stats:\n === User:{0}/{1}\n === Common:{2}/{3}\n === Fail:{4}/{5}",
                    user, total,
                    common, total,
                    fail, total));

            }
        }
        
        void Save () {
            BinaryFormatter bf = new BinaryFormatter ();
            FileStream file;
            if (File.Exists (Application.persistentDataPath + "/trainData.dat")) {
                file = File.Open (Application.persistentDataPath + "/trainData.dat", FileMode.Open);
            } else {
                file = File.Create (Application.persistentDataPath + "/trainData.dat");
            }

            bf.Serialize (file, mTrainData);
            file.Close ();
        }

        private void printGestureStat() {
            foreach(KeyValuePair<int, ErrorCount> item in mCommonGestureStat.gestureStat) {
	            if(DEBUG_LOG_ENABLED) Debug.Log("----------------------------");
	            if(DEBUG_LOG_ENABLED) Debug.Log(String.Format("key:{0}  userErr:{1}  commErr:{2}", item.Key, item.Value.userErrCount, item.Value.commonErrCount));
            }
        }

        /// Ask to calculate a new set of error count stat for collected gesture
#if UNITY_ANDROID
		// ======================
		// Android
		public IEnumerator UpdateGestureStat() {
			if(mIsGestureStatExist) {
				yield return null;
			}
			else {
				foreach(KeyValuePair<int, List<AndroidJavaObject>> entry in mSmartTrainCacheCollection) {
					foreach(AndroidJavaObject sensorData in entry.Value) {
						IdentifyActionBundle bundleForCommon = new IdentifyActionBundle(0, entry.Key, sensorData);
						IdentifyCommonGesture (0, COMMON_PASS_THRESHOLD, sensorData,
							new List<int>(){(int)CommonGesture.Heart, (int)CommonGesture.Down, (int)CommonGesture.C},
							(bundleArgu) => {
								if(bundleArgu.matchIndex != bundleArgu.basedIndex) {
									if( ! mGestureStat.ContainsKey(bundleArgu.matchIndex)) {
										mGestureStat[bundleArgu.matchIndex] = new ErrorCount();
									}
									mGestureStat[bundleArgu.matchIndex].commonErrCount ++;

									if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("Try {0} but match {1} >>> commonErr + 1", bundleArgu.basedIndex, bundleArgu.matchIndex));
								}
							}, bundleForCommon,
							false );

						// IdentifyActionBundle bundleForUser = new IdentifyActionBundle(0, entry.Key, sensorData);
						// IdentifyUserGesture(0, sensorData, 
						// 	new int[]{(int)CommonGesture.Heart, (int)CommonGesture.Down, (int)CommonGesture.C},
						// 	(bundleArgu) => {
						// 		if(bundleArgu.matchIndex != bundleArgu.basedIndex) {
						// 			if( ! mGestureStat.ContainsKey(bundleArgu.matchIndex)) {
						// 				mGestureStat[bundleArgu.matchIndex] = new ErrorCount();
						// 			}
						// 			mGestureStat[bundleArgu.matchIndex].userErrCount ++;
								
						// 			Debug.Log(string.Format("Try {0} but match {1} >>> userErr + 1", bundleArgu.basedIndex, bundleArgu.matchIndex));
						// 		}
						// 	}, bundleForUser,
						// 	false);
						Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
						if(keyColl.Count > 0) {
							//if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UpdateGestureStat] try lookup for these keys: " + string.Join(",", keyColl.Select(x => x.ToString()).ToArray()));
							// Verify first before we set to use "user gesture"
							int[] againstIndex = new int[keyColl.Count];
							keyColl.CopyTo(againstIndex, 0);
							Nullable<int> result = IdentifyCustomGesture(0, sensorData, againstIndex, false);
							if(null != result && result.HasValue) {
								if(result.Value != entry.Key) {
									if( ! mGestureStat.ContainsKey(result.Value)) {
										mGestureStat[result.Value] = new ErrorCount();
									}
									mGestureStat[result.Value].userErrCount ++;
								
									if(DEBUG_LOG_ENABLED) Debug.Log(string.Format("Try {0} but match {1} >>> userErr + 1", entry.Key, result.Value));
								}
							}
						}
					}
				}
				mIsGestureStatExist = true;
				yield return null;
			}
		}
	// ======================
#elif UNITY_STANDALONE_WIN
	// ======================
	// Windows
        public IEnumerator UpdateDeveloperDefinedGestureStat(bool force) {
            bool isExist = false;
            if (!IsValidClassifier) {
                yield return null;
            }
            if (!mPredGestureStatDict.ContainsKey(FullClassifierPath)) {
                mPredGestureStatDict[FullClassifierPath] = new PredefinedSmartGestureStat();
            }
            isExist = mPredGestureStatDict[FullClassifierPath].isStatExist;

            if (isExist && !force) {
                yield return null;
            } else {
                /*
                if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("mSmartTrainPredefinedCacheCollection.Length: {0}, Keys: {1}",
                   mSmartTrainPredefinedCacheCollection.Count,
                   string.Join(", ", mSmartTrainPredefinedCacheCollection.Keys.ToArray())));
                if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("mTrainData.usePredefinedUserGesture.Length: {0}, Keys: {1}",
                    mTrainData.usePredefinedUserGesture.Count,
                    string.Join(", ", mTrainData.usePredefinedUserGesture.Keys.ToArray())));
                */
                foreach (KeyValuePair<string, List<float[]>> entry in mSmartTrainPredefinedCacheCollection) {
                    foreach (float[] sensorData in entry.Value) {
                        IdentifyPredefinedActionBundle bundleForPredefined = new IdentifyPredefinedActionBundle(0, entry.Key, sensorData);
                        //Dictionary<string, List<float[]>>.KeyCollection keyColl = mSmartTrainPredefinedCacheCollection.Keys;
                        //List<string> keys = keyColl.ToList();
                        Dictionary<string, bool>.KeyCollection keyColl = mTrainData.usePredefinedUserGesture.Keys;
                        List<string> keys = keyColl.ToList();
                        IdentifyPredefined(0, sensorData, keys, mClassifier, mSubClassifier,
                            (bundleArgu) => {
                                mPredGestureStatDict[FullClassifierPath].checkThenAdd(bundleArgu.matchGesture);
                                
                                if (bundleArgu.basedGesture != bundleArgu.matchGesture) {
                                    mPredGestureStatDict[FullClassifierPath].gestureStat[bundleArgu.matchGesture].commonErrCount++;
                                    if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("Try {0} but match {1} >>> commonErr + 1", bundleArgu.basedGesture, bundleArgu.matchGesture));
                                }
                                else {
                                    mPredGestureStatDict[FullClassifierPath].gestureConf[bundleArgu.matchGesture].commonConfidence += bundleArgu.conf;
                                }
                            }, bundleForPredefined,
                            false);

                        //Dictionary<int, bool>.KeyCollection keyColl = mTrainData.useUserGesture.Keys;
                        if (keyColl.Count > 0) {
                            //if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][UpdateGestureStat] try lookup for these keys: " + string.Join(",", keyColl.Select(x => x.ToString()).ToArray()));
                            // Verify first before we set to use "user gesture"
                            string[] againstIndex = new string[keyColl.Count];
                            keyColl.CopyTo(againstIndex, 0);
                            //string result = IdentifyPlayerGesture(0, sensorData, keys);
                            CustomGestureResult<string> result = IdentifyCustomGesture(0, sensorData, keys);
                            
                            if (null != result && result.bestMatch.Length > 0) {
                                mPredGestureStatDict[FullClassifierPath].checkThenAdd(result.bestMatch);
                                if (result.bestMatch != entry.Key) {
                                    mPredGestureStatDict[FullClassifierPath].gestureStat[result.bestMatch].userErrCount++;
                                    if (DEBUG_LOG_ENABLED) Debug.Log(string.Format("Try {0} but match {1} >>> userErr + 1", entry.Key, result));
                                }
                                else {
                                    mPredGestureStatDict[FullClassifierPath].gestureConf[result.bestMatch].userConfidence += result.confidence;
                                }
                            }
                        }
                    }
                }
                if (DEBUG_LOG_ENABLED) {
                    /*
                    string[] keys = mPredefinedGestureStat.Keys.ToArray();
                    Debug.Log(string.Format("key count: {0}", keys.Length));
                    foreach (string key in keys) {
                        Debug.Log(string.Format("key: {0}, userErr: {1}, commErr: {2}",
                            key, mPredefinedGestureStat[key].userErrCount, mPredefinedGestureStat[key].commonErrCount));
                    }
                    */
                }
                mPredGestureStatDict[FullClassifierPath].isStatExist = true;
                yield return null;
            }
        }
        // ======================
#endif

        /// thread for Obtaining Oculus controller data
        private Thread rThread, lThread;
        private bool isCollectingRControllerData, isCollectingLControllerData;
        private struct Sample {
            public long time;
            public Vector3 rotation;
        }
        private List<Sample> rCollectSamples = new List<Sample>();
        private List<Sample> lCollectSamples = new List<Sample>();

        private void CollectRControllerData() {
            System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
            rCollectSamples.Clear();

            while (isCollectingRControllerData) {
                //Debug.Log("Collect Sample");
                Vector3 vector = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
                vector.x *= -1;
                stopWatch.Stop();
                long timeElapsedMilliseconds = stopWatch.ElapsedMilliseconds;
                //Debug.Log(String.Format("{0}  {1}  {2}  {3}", timeElapsedMilliseconds, vector.x, vector.y, vector.z));
                Sample sample = new Sample();
                sample.time = timeElapsedMilliseconds;
                sample.rotation = vector;
                rCollectSamples.Add(sample);
                stopWatch.Start();
                Thread.Sleep(16);
            }
        }

        private void CollectLControllerData() {
            System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
            lCollectSamples.Clear();

            while (isCollectingLControllerData) {
                //Debug.Log("Collect Sample");
                Vector3 vector = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.LTouch);
                vector.y *= -1;
                stopWatch.Stop();
                long timeElapsedMilliseconds = stopWatch.ElapsedMilliseconds;
                //Debug.Log(String.Format("{0}  {1}  {2}  {3}", timeElapsedMilliseconds, vector.x, vector.y, vector.z));
                Sample sample = new Sample();
                sample.time = timeElapsedMilliseconds;
                sample.rotation = vector;
                lCollectSamples.Add(sample);
                stopWatch.Start();
                Thread.Sleep(16);
            }
        }

        private void processCollectData(List<Sample> data) {
            if (data.Count <= 0) return;
            float[] transformData = new float[data.Count * 10];
            for(int i = 0; i < data.Count; i ++) {
                transformData[i * 10] = data[i].time;
                transformData[i * 10 + 1] = data[i].rotation.x;
                transformData[i * 10 + 2] = data[i].rotation.y;
                transformData[i * 10 + 3] = data[i].rotation.z;
            }
            int size = Marshal.SizeOf(transformData[0]) * transformData.Length;
            IntPtr pnt = Marshal.AllocHGlobal(size);
            try {
                // Copy the array to unmanaged memory.
                Marshal.Copy(transformData, 0, pnt, transformData.Length);
                // Pass to the callback
                onSensorDataRecorded(pnt, data.Count, 10);
            } finally {
                // Free the unmanaged memory.
                Marshal.FreeHGlobal(pnt);
            }
        }

        /// Set the identification mode for the next incoming gesture
        public void SetMode(Mode mode) {
            if (mCurrentMode == mode) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetMode] New mode (" + mode + ") equals to the existing mode so nothing will change...");
                return;
            }
            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetMode] New mode (" + mode + ")");
            if ((mCurrentMode & Mode.SmartTrainDeveloperDefined) > 0 && (mode & Mode.SmartTrainDeveloperDefined) == 0 && mCurrentPredefined.Count > 0) {
                // current mode contain smart train predefined and the new mode doesn't, trigger the smart train process
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetMode] Changing mode and trigger SmartTrainDeveloperDefined process ...");
                SmartTrainPredefinedGesture(mCurrentPredefined.First());
            }
            mCurrentMode = mode;

#if UNITY_ANDROID
			// ==================
			// Android
			if((mCurrentMode & Mode.IdentifyUser) > 0 || (mCurrentMode & Mode.TrainUser) > 0) {
					ContinuousRecognizeEnabled = true;
					//ContinuousRecognizeEnabled = false;
			}
			else {
				ContinuousRecognizeEnabled = true;
				//ContinuousRecognizeEnabled = false;
			}
			// ==================
#elif UNITY_STANDALONE_WIN
			// ==================
			// Window
			// ==================
#endif

            mTrainFailCount = 0;

           // clear all training
            mTrainingProgressGestures.Clear();
        }

        /// Set the identification target for the next incoming gesture
        public void SetTarget(List<int> target) {
            if (mCurrentTarget.SequenceEqual(target)) {
                if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetTarget] New targets equal to the existing targets so nothing will change...");
                return;
            }
            
            mCurrentTarget = target;

            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetTarget] Targets: " + string.Join(",", mCurrentTarget.Select(x => x.ToString()).ToArray()));

            mTrainFailCount = 0;

            // clear all incomplete training
            mTrainingProgressGestures.Clear();
        }

        public void SetDeveloperDefinedTarget(List<string> target) {
            string gesture = null;
            if (mCurrentPredefined.Count > 0) {
                gesture = mCurrentPredefined.First(); // This gesture is recorded as before changed (for smart train to exec)
            }
            mCurrentPredefined = target==null?new List<string>():target;
            if (DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][SetDeveloperDefinedTarget] Targets: " + string.Join(",", mCurrentPredefined.Select(x => x.ToString()).ToArray()));

            if ((mCurrentMode & Mode.SmartTrainDeveloperDefined) > 0 && null != gesture) {
                // current mode contain smart train predefined and the new mode doesn't, trigger the smart train process
                SmartTrainPredefinedGesture(gesture);
            }
        }

        public void SetClassifier(string classifier, string subClassifier) {
            mClassifier = classifier;
            mSubClassifier = subClassifier;
        }

        /// Reset smart training data
        public void ResetSmartTrain() {
            mTrainData.useUserGesture.Clear();
            mTrainData.usePredefinedUserGesture.Clear();
        }

        public void GetCustomGestureCache() {

        }

        /// Set custom gesture to engine
#if UNITY_ANDROID
		// =========================
		// Window
		public Dictionary<int, int> SetCustomGesture(List<int> targets, bool clearOnSet) {
			Dictionary<int, int> result = new Dictionary<int, int>();
			foreach (int index in targets) {
				if( ! mCustomGestureCache.ContainsKey(index)) {
					continue;
				}
				List<AndroidJavaObject> cache = mCustomGestureCache[index];
				if(cache.Count() == 0) {
					continue;
				}

				AndroidJavaObject arrayList = new AndroidJavaObject("java.util.ArrayList");
				for(int i = 0; i < cache.Count(); i ++) {
					object[] arglist = new object[1];
					if(DEBUG_LOG_ENABLED) Debug.Log("Adding: " + cache[i].ToString());
					arglist[0] = cache[i];
					arrayList.Call<bool>("add", arglist);
				}
					
				object[] engineArgs = new object[2];  
				engineArgs [0] = arrayList;  
				engineArgs [1] = index;

				getEngineInstance().Call ("setCustomGesture", engineArgs);

				if(clearOnSet) {
					mCustomGestureCache[index].Clear();
				}
			}
			return result;
		}
		// ========================
#elif UNITY_STANDALONE_WIN
        // =========================
        // Window
        public Dictionary<int, int> SetPlayerGesture(List<int> targets) {
            return SetPlayerGesture(targets, true);
        }

        public Dictionary<int, int> SetPlayerGesture(List<int> targets, bool clearOnSet) {
            Dictionary<int, int> result = new Dictionary<int, int>();
            foreach (int index in targets) {
                if ( ! mCustomGestureCache.ContainsKey(index)) {
                    continue;
                }
                List<float[]> cache = mCustomGestureCache[index];
                if(cache.Count() == 0) {
                    continue;
                }
                int totalLength = 0;
                int[] numDataEntryList = new int[cache.Count()];
                int[] dataEntryLengthList = new int[cache.Count()];
                for (int i = 0; i < cache.Count(); i++) {
                    totalLength += cache[i].Length;
                    numDataEntryList[i] = cache[i].Length / 10;
                    dataEntryLengthList[i] = 10;
                }
                float[] dataList = new float[totalLength];
                for (int i = 0, k = 0; i < cache.Count(); i++) {
                    float[] entry = cache[i];
                    for (int j = 0; j < entry.Length; j++) {
                        dataList[k] = entry[j];
                        k++;
                    }
                }
                result.Add(index, cache.Count());
                SetCustomGesture(vrControllerHelper, index, dataList, cache.Count(), numDataEntryList, dataEntryLengthList);
                if(clearOnSet) {
                    mCustomGestureCache[index].Clear();
                }
            }
            return result;
        }

        private void SetCustomGesture(string target, float[] data, int[] numDataEntry, int[] dataEntryLength) {
            byte[] targetInByte = Encoding.ASCII.GetBytes(target);
            SetCustomGestureStr(vrControllerHelper, targetInByte, target.Length, data, data.Length, numDataEntry, dataEntryLength);
        }
        // ========================
#endif
    }

    [Serializable]
    class TrainData {
        public Dictionary<int, float> trainProgress = new Dictionary<int, float> ();
        public Dictionary<int, bool> useUserGesture = new Dictionary<int, bool> (); // builtin common gesture settings for predefined gesture
        public Dictionary<string, bool> usePredefinedUserGesture = new Dictionary<string, bool>(); // smart gesture settings for predefined gesture
        // common gesture and user gesture statistic
        public Dictionary<int, long> userGestureCount = new Dictionary<int, long> ();
        public Dictionary<int, long> commonGestureCount = new Dictionary<int, long> ();
        public Dictionary<int, long> failedGestureCount = new Dictionary<int, long> ();

        public long IncUserGestureCount (int target) {
            long newValue;
            if (userGestureCount.ContainsKey (target)) {
                newValue = userGestureCount[target] + 1;
                userGestureCount[target] = newValue;
            } else {
                newValue = userGestureCount[target] = 1;
            }
            return newValue;
        }

        public long IncCommonGestureCount (int target) {
            long newValue;
            if (commonGestureCount.ContainsKey (target)) {
                newValue = commonGestureCount[target] + 1;
                commonGestureCount[target] = newValue;
            } else {
                newValue = commonGestureCount[target] = 1;
            }
            return newValue;
        }

        public long IncFailedGestureCount (int target) {
            long newValue;
            if (failedGestureCount.ContainsKey (target)) {
                newValue = failedGestureCount[target] + 1;
                failedGestureCount[target] = newValue;
            } else {
                newValue = failedGestureCount[target] = 1;
            }
            return newValue;
        }

        public long Total () {
            long userGestureTotal = userGestureCount.Sum (x => x.Value);
            long commonGestureTotal = commonGestureCount.Sum (x => x.Value);
            long failedGestureTotal = failedGestureCount.Sum (x => x.Value);
            return userGestureTotal + commonGestureTotal + failedGestureTotal;
        }
    }
}