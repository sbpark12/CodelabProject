using Google.XR.ARCoreExtensions;
using Google.XR.ARCoreExtensions.Samples.Geospatial;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARAnchorPopup : MonoBehaviour
{
    public XROrigin SessionOrigin;
    public ARSession Session;
    public AREarthManager EarthManager;
    public ARCoreExtensions ARCoreExtensions;
    public ARAnchorManager AnchorManager;
    public ARRaycastManager RaycastManager;

    public GameObject AnchorPrefab;

    private float configurePrepareTime = 3f;
    private bool isReturning = false;
    private bool enablingGeospatial = false;
    private bool isARReady = false;
    private List<GameObject> anchorList = new List<GameObject>();

    private VpsAvailability vpsAvailability;

    private const double orientationYawAccuracyThreshold = 10;
    private const double horizontalAccuracyThreshold = 10;
    private const double verticalAccuracyThreshold = 10;
    private IEnumerator startLocationService = null;
    private IEnumerator asyncCheck = null;
    private bool waitingForLocationService = false;


    public void OnEnable()
    {
        StartCoroutine(AvailabilityCheck());
    }

    void Start()
    {
        if (ARCoreExtensions != null && ARCoreExtensions.ARCoreExtensionsConfig != null)
        {
            Debug.Log("ARCoreExtensions != null && ARCoreExtensions.ARCoreExtensionsConfig != null");

            ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode = GeospatialMode.Enabled;
        }
    }
    public void OnDisable()
    {
        Debug.Log("ar tour popup ondisable start . . .");
        StopCoroutine(asyncCheck);
        asyncCheck = null;

        Debug.Log("ar tour popup ondisable finish . . .");
    }

    void Update()
    {

        checkAR();

        if (!isARReady) return;

        if (ARSession.state != ARSessionState.SessionTracking)
        {
            Debug.Log("ARSession is not tracking, skipping anchor updates.");
            return;
        }

        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began
               && !EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
        {



            createArAnchorByTap(Input.GetTouch(0).position);
        }
    }

    private void createArAnchorByTap(Vector2 position)
    {
        List<ARRaycastHit> planeHitResults = new List<ARRaycastHit>();
        RaycastManager.Raycast(position, planeHitResults, TrackableType.Planes);

        if (planeHitResults.Count > 0)
        {
            GameObject anchorObject = Instantiate(AnchorPrefab, planeHitResults[0].pose.position, planeHitResults[0].pose.rotation);

            ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();

            anchorList.Add(anchorObject);

            Debug.Log($"AR Anchor placed at: {anchor.transform.position}");
        }
    }


    public void ClearAllAnchor()
    {
        foreach (var anchor in anchorList)
        {
            Destroy(anchor.gameObject);
        }
        anchorList.Clear();
    }

    #region ARCheck
    private void checkAR()
    {
        isARReady = false;
        LifecycleUpdate();
        if (ARCoreExtensions == null)
            return;

        if (isReturning)
        {
            return;
        }

        if (ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            return;
        }

        // Check feature support and enable Geospatial API when it's supported.
        var featureSupport = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
        switch (featureSupport)
        {
            case FeatureSupported.Unknown:
                return;
            case FeatureSupported.Unsupported:
                ReturnWithReason("The Geospatial API is not supported by this device.");
                return;
            case FeatureSupported.Supported:
                if (ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode ==
                    GeospatialMode.Disabled)
                {
                    Debug.Log("Geospatial sample switched to GeospatialMode.Enabled.");
                    ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode =
                        GeospatialMode.Enabled;
                    ARCoreExtensions.ARCoreExtensionsConfig.StreetscapeGeometryMode =
                        StreetscapeGeometryMode.Enabled;
                    configurePrepareTime = 3.0f;
                    enablingGeospatial = true;
                    return;
                }

                break;
        }

        // Waiting for new configuration to take effect.
        if (enablingGeospatial)
        {
            configurePrepareTime -= Time.deltaTime;
            if (configurePrepareTime < 0)
            {
                enablingGeospatial = false;
            }
            else
            {
                return;
            }
        }

        // Check earth state.
        var earthState = EarthManager.EarthState;
        if (earthState == EarthState.ErrorEarthNotReady)
        {
            Debug.LogError("earthState: " + earthState);
            return;
        }
        else if (earthState != EarthState.Enabled)
        {
            Debug.LogError("earthState: " + earthState);
            string errorMessage =
                "Geospatial sample encountered an EarthState error: " + earthState;
            return;
        }

        //Get tracking results
        var earthTrackingState = EarthManager.EarthTrackingState;
        if (earthTrackingState != TrackingState.Tracking)
        {
            Debug.LogError("earthTrackingState: " + earthTrackingState);
            return;
        }
        isARReady = true;
    }


    private void LifecycleUpdate()
    {
        // Pressing 'back' button quits the app.
        if (Input.GetKeyUp(KeyCode.Escape))
        {
            Application.Quit();
        }

        if (isReturning)
        {
            return;
        }

        // Only allow the screen to sleep when not tracking.
        var sleepTimeout = SleepTimeout.NeverSleep;
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            sleepTimeout = SleepTimeout.SystemSetting;
        }

        Screen.sleepTimeout = sleepTimeout;

        // Quit the app if ARSession is in an error status.
        string returningReason = string.Empty;
        if (ARSession.state != ARSessionState.CheckingAvailability &&
            ARSession.state != ARSessionState.Ready &&
            ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            returningReason = string.Format(
                "Geospatial sample encountered an ARSession error state {0}.\n" +
                "Please restart the app.",
                ARSession.state);
        }
        else if (Input.location.status == LocationServiceStatus.Failed)
        {
            returningReason =
                "Geospatial sample failed to start location service.\n" +
                "Please restart the app and grant the fine location permission.";
        }
        else if (SessionOrigin == null || Session == null || ARCoreExtensions == null)
        {
            returningReason = string.Format(
                "Geospatial sample failed due to missing AR Components.");
        }

        ReturnWithReason(returningReason);
    }

    private void ReturnWithReason(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return;
        }

        Debug.LogError(reason);
        isReturning = true;
    }

    private IEnumerator StartLocationService()
    {
        waitingForLocationService = true;
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Debug.Log("Requesting the fine location permission.");
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(3.0f);
        }
#endif

        if (!Input.location.isEnabledByUser)
        {
            Debug.Log("Location service is disabled by the user.");
            waitingForLocationService = false;
            yield break;
        }

        Debug.Log("Starting location service.");
        Input.location.Start();

        while (Input.location.status == LocationServiceStatus.Initializing)
        {
            yield return null;
        }

        waitingForLocationService = false;
        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarningFormat(
                "Location service ended with {0} status.", Input.location.status);
            Input.location.Stop();

            yield break;
        }

        yield return new WaitForSeconds(2f);
    }


    private IEnumerator AvailabilityCheck()
    {
        yield return StartCoroutine(StartLocationService());

        Debug.Log("AvailabilityCheck started!"); // 실행 확인용 로그

        if (ARSession.state == ARSessionState.None)
        {
            yield return ARSession.CheckAvailability();
        }

        yield return null; // Waiting for ARSessionState.CheckingAvailability.

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            yield return ARSession.Install();
        }

        yield return null; // Waiting for ARSessionState.Installing.

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Requesting camera permission.");
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(3.0f);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.LogWarning("Failed to get the camera permission. VPS availability check isn't available.");
            yield break;
        }
#endif

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("Location services aren't running. VPS availability check is not available.");
            yield break;
        }

        if (isReturning)
        {
            Debug.Log("isReturning is TRUE, stopping AvailabilityCheck");
            yield break;
        }

        var location = Input.location.lastData;

        // 🚀 추가: 위치 데이터가 유효한지 확인
        if (location.latitude == 0 && location.longitude == 0)
        {
            Debug.LogError("GPS data is invalid! Latitude and Longitude are both zero.");
            yield break;
        }

        Debug.Log($"Latitude: {location.latitude}, Longitude: {location.longitude}");

        var vpsAvailabilityPromise = AREarthManager.CheckVpsAvailabilityAsync(location.latitude, location.longitude);
        yield return vpsAvailabilityPromise;

        if (vpsAvailabilityPromise == null)
        {
            Debug.LogError("VPS Availability check failed: Null response.");
            yield break;
        }

        vpsAvailability = vpsAvailabilityPromise.Result;
        string vpsStatus = $"VPS Availability at ({location.latitude}, {location.longitude}): {vpsAvailability}";

        Debug.Log("vps status: " + vpsStatus);
    }

    #endregion

}
