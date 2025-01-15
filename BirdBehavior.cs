// Copyright (c) 2025 JakobStruye
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.





// This script is provided as-is to be incorporated into a Unity project with MRTK (tested with MRTK2, should also work with 3)
// Chevron is taken from MRTK
// Used with Hummingbird (paid asset) from Unity Asset Store (https://assetstore.unity.com/packages/3d/characters/animals/birds/hummingbird-49235)
// but should work with any appropriate asset
// NO support will be offered for this code

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using System;
using Microsoft.MixedReality.Toolkit;
using TMPro;


#if WINDOWS_UWP
using Windows.Storage;
using Windows.System;
using System.Threading.Tasks;
using Windows.Storage.Streams;
#endif

enum Phase
{
    CLOSEEDGE, // User near the start of the walkway
    GOINGFAR,  // User going from the start to the end of the walkway
    FAREDGE,   // User near the end of the walkway
    GOINGCLOSE // User going from the end to the start of the walkway
}


public class BirdBehavior : MonoBehaviour
{
    private float closeLimit = 1; // Max distance from start to be considered near start
    private float farLimit = 6;  // Min distance from start to be considered near end
    private float threshold = 0; // z-position of bird trigger (placeholder initial value)
    // Distance-related params
    private static float dist = 2.0f; // Distance along walkway from user to bird, in m
    private static Vector3 spawnOffset = new Vector3(0.0f, 0.0f, dist);
    // Bird positions ensuring certain head movement angles 
    private static Vector3 target45left = new Vector3((float)(-2*Math.Tan(Math.PI/180.0*45)), 0, 0);
    private static Vector3 target45right = new Vector3((float)(2 * Math.Tan(Math.PI / 180.0 * 45)), 0, 0);
    private static Vector3 target70left = new Vector3((float)(-2 * Math.Tan(Math.PI / 180.0 * 70)), 0, 0);
    private static Vector3 target70right = new Vector3((float)(2 * Math.Tan(Math.PI / 180.0 * 70)), 0, 0);
    private static Vector3 target45up = new Vector3(0, (float)(-2 * Math.Tan(Math.PI / 180.0 * 45)), 0);
    private static Vector3 target45down = new Vector3(0, (float)(2 * Math.Tan(Math.PI / 180.0 * 45)), 0);   
    
    // Game objects
    [SerializeField] private GameObject bird;
    [SerializeField] private GameObject chevron; // arrow
    [SerializeField] private TextMeshPro text; // only used for logging errors
    
    // RNG
    private System.Random autoRand = new System.Random();

    // Logging
    private Logging.BasicInputLogger logger;
    private int saveCounter = 0;
    private bool running = false;
    private bool forward = true;

    // Bird status
    private bool shouldAppear = false;  // True if the coroutine controlling the bird should be running for this trial
    private bool isAppearing = false; // True if the above coroutine is running

    // User status
    private Phase phase = Phase.CLOSEEDGE;


    // Start is called before the first frame update
    void Start()
    {
        // Disable unneeded things
        Microsoft.MixedReality.Toolkit.Input.PointerUtils.SetGazePointerBehavior(Microsoft.MixedReality.Toolkit.Input.PointerBehavior.AlwaysOff);
        Microsoft.MixedReality.Toolkit.Input.PointerUtils.SetHandGrabPointerBehavior(Microsoft.MixedReality.Toolkit.Input.PointerBehavior.AlwaysOff);
        Microsoft.MixedReality.Toolkit.Input.PointerUtils.SetHandPokePointerBehavior(Microsoft.MixedReality.Toolkit.Input.PointerBehavior.AlwaysOff);
        Microsoft.MixedReality.Toolkit.Input.PointerUtils.SetHandRayPointerBehavior(Microsoft.MixedReality.Toolkit.Input.PointerBehavior.AlwaysOff);
        var observer = CoreServices.GetSpatialAwarenessSystemDataProvider<Microsoft.MixedReality.Toolkit.SpatialAwareness.IMixedRealitySpatialAwarenessMeshObserver>();
        observer.DisplayOption = Microsoft.MixedReality.Toolkit.SpatialAwareness.SpatialAwarenessMeshDisplayOptions.None;
        CoreServices.SpatialAwarenessSystem.SuspendObservers();

        bird.SetActive(!bird.activeSelf);
        chevron.SetActive(bird.activeSelf);

        Debug.Log("STARTING");

        logger = new Logging.BasicInputLogger(text);
        logger.CheckIfInitialized();
    }

    // Update is called once per frame
    void Update()
    {
        if (!logger.isInit) { return; }
        if (!running)
        {
            // Initial log
            running = true;
            logger.Append("Timestamp;type;posX;posY;posZ;dirX;dirY;dirZ;gPosX;gPosY;gPosZ;gDirX;gDirY;gDirZ;GazeEnabled;Calibrated;DataValid;dPosX;dPosY;dPosZ;dDirx;dDirY;dDirZ;cos", false);
            logger.SaveLogs();
        }
        
        // Calculate all positions and orientations in the correct format and log
        Vector3 rot = Camera.main.transform.rotation * Vector3.forward;
        Vector3 tobird = (bird.transform.position - CoreServices.InputSystem.EyeGazeProvider.GazeOrigin).normalized;
        logger.Append("data;" + Camera.main.transform.position.x + ";" + Camera.main.transform.position.y + ";" + Camera.main.transform.position.z + ";"
            + rot.x + ";" + rot.y + ";" + rot.z + ";"
            + CoreServices.InputSystem.EyeGazeProvider.GazeOrigin.x + ";" + CoreServices.InputSystem.EyeGazeProvider.GazeOrigin.y + ";" + CoreServices.InputSystem.EyeGazeProvider.GazeOrigin.z + ";"
            + CoreServices.InputSystem.EyeGazeProvider.GazeDirection.x + ";" + CoreServices.InputSystem.EyeGazeProvider.GazeDirection.y + ";" + CoreServices.InputSystem.EyeGazeProvider.GazeDirection.z + ";"
            + CoreServices.InputSystem.EyeGazeProvider.IsEyeTrackingEnabled + ";" + CoreServices.InputSystem.EyeGazeProvider.IsEyeCalibrationValid + ";" + CoreServices.InputSystem.EyeGazeProvider.IsEyeTrackingDataValid + ";"
            + (bird.activeSelf ?
                bird.transform.position.x + ";" + bird.transform.position.y + ";" + bird.transform.position.z + ";"
                + tobird.x + ";" + tobird.y + ";" + tobird.z + ";" + ((tobird.x * CoreServices.InputSystem.EyeGazeProvider.GazeDirection.x) + (tobird.y * CoreServices.InputSystem.EyeGazeProvider.GazeDirection.y) + (tobird.z * CoreServices.InputSystem.EyeGazeProvider.GazeDirection.z))
                : "")
            ) ;

        // Make sure we know which direction user is going (can't change while bird is visible) 
        if (!bird.activeSelf)
        {
            forward = rot.z > 0;
        }

        // Flush frequently to catch write errors early
        saveCounter++;
        if (saveCounter >= 200) {
            saveCounter = 0;
            logger.SaveLogs();
        }

        if (!logger.hasSaved) { return; }

        float zPos = Camera.main.transform.position.z;

        // Check for transitions in user state and alter variables as needed
        // Thresholds are set for spawning 2-4m from edge of walkway assuming a 7m walkway, alter as needed
        if (zPos > closeLimit && phase == Phase.CLOSEEDGE)
        {
            phase = Phase.GOINGFAR;
            threshold = 2 + (float) autoRand.NextDouble() * 2;
            shouldAppear = true;
        } else if (zPos > farLimit && phase == Phase.GOINGFAR)
        {
            phase = Phase.FAREDGE;
        } else if (zPos < farLimit && phase == Phase.FAREDGE)
        {
            phase = Phase.GOINGCLOSE;
            threshold = 3 + (float) autoRand.NextDouble() * 2;
            shouldAppear = true;
        } else if (zPos < closeLimit && phase == Phase.GOINGCLOSE)
        {
            phase = Phase.CLOSEEDGE;
        }

        // Trigger coroutine in separate thread when needed
        if (!isAppearing && shouldAppear)
        {
            isAppearing = true;
            StartCoroutine(ControlBird());
            shouldAppear = false;
        }


    }

    IEnumerator LerpPosition(Vector3 targetPosition, float duration)
    {
        // Simple linear interpolation of bird location. Flies at fixed speed for simplicity.
        float time = 0;
        Vector3 startPosition = bird.transform.position;
        float thisDist = forward ? dist : -dist;
        while (time < duration)
        {
            bird.transform.position = Vector3.Lerp(new Vector3(startPosition.x, startPosition.y, Camera.main.transform.position.z + thisDist), new Vector3(targetPosition.x, targetPosition.y, Camera.main.transform.position.z + thisDist), time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        bird.transform.position = new Vector3(targetPosition.x, targetPosition.y, Camera.main.transform.position.z + thisDist);
    }

    IEnumerator ControlBird()
    {
        // Check if it's time to spawn once every 100ms
        while (phase == Phase.CLOSEEDGE || phase == Phase.FAREDGE || (phase == Phase.GOINGFAR && Camera.main.transform.position.z < threshold) || (phase == Phase.GOINGCLOSE && Camera.main.transform.position.z > threshold))
        {
            yield return new WaitForSeconds(0.1f);
        }
        bird.SetActive(!bird.activeSelf); // Assumes bird starts as invisible
        chevron.SetActive(bird.activeSelf);

        if (bird.activeSelf)
        {
            bird.transform.position = Camera.main.transform.position + spawnOffset;

            // Pick one of six trajectories at random
            int option = autoRand.Next(0, 6);
            logger.Append("motion;" + option + ";" + (forward ? "forward" : "backward"));
            Vector3 targetA = new Vector3(0,0,0);
            Vector3 targetB = new Vector3(0, 0, 0);
            float timeBefore = 0.75f;
            float timeA = 0;
            float timeB = 0;
            float timeBetween = 0;
            bool hasB = false;
            if (option == 0)
            {
                targetA = bird.transform.position + target70left;
                bird.transform.rotation = Quaternion.LookRotation(targetA - bird.transform.position, Vector3.up);
                timeA = 2.0f;
            } else if (option == 1)
            {
                targetA = bird.transform.position + target70right;
                bird.transform.rotation = Quaternion.LookRotation(targetA - bird.transform.position, Vector3.up);
                timeA = 2.0f;
            } else if (option == 2)
            {
                targetA = bird.transform.position + target45left;
                targetB = bird.transform.position + target70left;
                bird.transform.rotation = Quaternion.LookRotation(targetA - bird.transform.position, Vector3.up);
                timeA = 2.0f;
                timeB = 2.0f;
                timeBetween = 1.0f;
                hasB = true;
            }
            else if (option == 3)
            {
                targetA = bird.transform.position + target45right;
                targetB = bird.transform.position + target70right;
                bird.transform.rotation = Quaternion.LookRotation(targetA - bird.transform.position, Vector3.up);
                timeA = 2.0f;
                timeB = 2.0f;
                timeBetween = 1.0f;
                hasB = true;

            } else if (option == 4)
            {
                targetA = bird.transform.position + target45down;
                bird.transform.rotation = Quaternion.LookRotation(targetA + new Vector3(0,0,3.2f)- bird.transform.position, Vector3.up);

                timeA = 2.0f;
            }
            else if (option == 5)
            {
                targetA = bird.transform.position + target45up;
                bird.transform.rotation = Quaternion.LookRotation(targetA + new Vector3(0, 0, 3.2f) - bird.transform.position, Vector3.up);
                timeA = 2.0f;
            }
            yield return LerpPosition(bird.transform.position, timeBefore);
            yield return LerpPosition(targetA, timeA);
            if (hasB)
            {
                yield return LerpPosition(targetA, timeBetween);
                yield return LerpPosition(targetB, timeB);
            }
            yield return new WaitForSeconds(1.0f);

            // Hide bird when done
            bird.SetActive(!bird.activeSelf);
            chevron.SetActive(bird.activeSelf);
        }
        isAppearing = false;
    }
}


namespace Logging
{
    public class BasicInputLogger : MonoBehaviour
    {
        public bool addTimestampToLogfileName = true;

#if WINDOWS_UWP
        private StorageFile logFile = null;
        private StorageFolder logRootFolder;
#endif

        internal bool isLogging = true;
        private StringBuilder buffer = null;
        public bool isInit = false;
        public bool hasSaved = false;
        private TextMeshPro text;

        public BasicInputLogger(TextMeshPro text)
        {
            this.text = text;
        }



#if WINDOWS_UWP
        protected virtual async void CreateNewLogFile()
        {
            try
            {
                // Logs to Music folder because accessing other folders is hard
                //logRootFolder = KnownFolders.MusicLibrary;
                StorageLibrary logRootLib = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Music);
                logRootFolder = logRootLib.SaveFolder;
                Debug.Log(">> BasicInputLogger.CreateNewLogFile:  " + logRootFolder.ToString());
                if (logRootFolder != null)
                {
                    string fullPath =logRootFolder.Path;
                    Debug.LogFormat("Does directory already exist {0} --\nLogRootFolder: {2} \n {1}", Directory.Exists(fullPath), fullPath, logRootFolder.Path);

                    try
                    {
                        //if (!Directory.Exists(fullPath))
                        //{
                        //    Debug.LogFormat("Trying to create new directory..");
                        //    Debug.LogFormat("Full path: " + fullPath);
                        //    sessionFolder = await logRootFolder.CreateFolderAsync(LogDirectory, CreationCollisionOption.GenerateUniqueName);
                        //}

                        logFile = await logRootFolder.CreateFileAsync(Filename, CreationCollisionOption.FailIfExists);

                        Debug.Log(string.Format("*** Create log file to: {0} -- \n -- {1}", logRootFolder.Name, logRootFolder.Path));
                        Debug.Log(string.Format("*** The log file path is: {0} -- \n -- {1}", logFile.Name, logFile.Path));

                        isInit = logFile != null;
                    }
                    catch (FileNotFoundException)
                    {
                        Debug.Log("file not found");
                    }
                    catch (DirectoryNotFoundException) { }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Debug.Log(string.Format("Exception in BasicLogger: {0}", e.Message));
            }
        }
#endif

        public void CheckIfInitialized()
        {
            if (buffer == null)
                ResetLog();
        }

        public void ResetLog()
        {
            buffer = new StringBuilder();
            buffer.Length = 0;
            Debug.Log(string.Format("Resetting log"));
#if WINDOWS_UWP
            Debug.Log(string.Format("Creating file"));
            CreateNewLogFile();
#endif
        }

        private string FormattedTimeStamp
        {
            get
            {
                string year = (DateTime.Now.Year - 2000).ToString();
                string month = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Month);
                string day = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Day);
                string hour = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Hour);
                string minute = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Minute);
                string sec = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Second);

                return string.Format("{0}{1}{2}-{3}{4}{5}",
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    sec);
            }
        }

        private string FormattedTimeStampMillisecond
        {
            get
            {
                string year = (DateTime.Now.Year).ToString();
                string month = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Month);
                string day = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Day);
                string hour = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Hour);
                string minute = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Minute);
                string sec = AddLeadingZeroToSingleDigitIntegers(DateTime.Now.Second);
                string millisec = AddLeadingZeros3(DateTime.Now.Millisecond);

                return string.Format("{0}{1}{2}-{3}{4}{5}.{6}",
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    sec,
                    millisec);
            }
        }

        private string AddLeadingZeroToSingleDigitIntegers(int val)
        {
            return (val < 10) ? ("0" + val) : ("" + val);
        }

        private string AddLeadingZeros3(int val)
        {
            return ((val < 100) ? "0" : "") + ((val < 10) ? "0" : "") + val;
        }


        #region Append log
        public bool Append(string msg, bool timestamped=true)
        {
            CheckIfInitialized();
#if WINDOWS_UWP
            if (this.logFile == null)
            {
                return false;
            }
#endif
            if (isLogging)
            {
                // post IO to a separate thread.
                this.buffer.AppendLine((timestamped ? (this.FormattedTimeStampMillisecond + ";") : "") + msg);
                return true;
            }
            return false;
        }
        #endregion

        //#if WINDOWS_UWP
        //        public async void LoadLogs()
        //        {
        //            try
        //            {
        //                if (logRootFolder != null)
        //                {
        //                    string fullPath = Path.Combine(logRootFolder.Path, LogDirectory);
        //
        //                    try
        //                    {
        //                        if (!Directory.Exists(fullPath))
        //                        {
        //                            return;
        //                        }
        //
        //                        sessionFolder = await logRootFolder.GetFolderAsync(LogDirectory);
        //                        logFile = await sessionFolder.GetFileAsync(Filename);/
        //
        //
        //                    }
        //                    catch (FileNotFoundException)
        //                    {
        //                        sessionFolder = await logRootFolder.CreateFolderAsync(LogDirectory, CreationCollisionOption.GenerateUniqueName);
        //                    }
        //                    catch (DirectoryNotFoundException) { }
        //                    catch (Exception) { }
        //                }
        //            }
        //            catch (Exception e)
        //            {
        //                Debug.Log(string.Format("Exception in BasicLogger to load log file: {0}", e.Message));
        //            }
        //        }
        //#endif

#if WINDOWS_UWP
        public async void SaveLogs()
        {
            if (isLogging)
            {
                try {
                if (buffer.Length > 0 && logFile != null)
                {
                    // Log buffer to the file
                    await FileIO.AppendTextAsync(logFile, buffer.ToString());
                    buffer.Clear();
                    Windows.Storage.FileProperties.BasicProperties basicProperties = await logFile.GetBasicPropertiesAsync();
                    hasSaved = basicProperties.Size > 0 && logRootFolder.Path.Contains("Music");
                }
                
                } catch (Exception ex) {
                    text.text = ex.Message;
                }
            }
        }
#else
        public void SaveLogs()
        {
            if (isLogging)
            {
                // Create new Stream writer
                using (var writer = new StreamWriter(Filename))
                {
                    Debug.Log("SAVE LOGS to " + Filename);
                    writer.Write(this.buffer.ToString());
                    buffer.Clear();
                }
            }
        }
#endif

        private string Filename
        {
            get
            {
                return FilenameWithTimestamp;

            }
        }

        protected string FilenameWithTimestamp
        {
            get { return (FormattedTimeStamp + FilenameNoTimestamp); }
        }

        protected string FilenameNoTimestamp
        {
            get { return ".csv"; }
        }

        public virtual void OnDestroy()
        {
            Debug.Log("DESTROYING THIS");
            if (isLogging)
                SaveLogs();
        }
    }
}
