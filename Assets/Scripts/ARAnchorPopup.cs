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

    public TextMeshProUGUI LocalizeStatusText;
    public TextMeshProUGUI VpsStatusText;
    public TextMeshProUGUI AccuracyStatusText;
    public TextMeshProUGUI PositionStatusText;
    // public RectTransform GuidePanel;
    public GameObject AnchorPrefab;

    private const string localizationInstructionMessage =
        "Point your camera at buildings, stores, and signs near you.";
    private const string localizationFailureMessage =
        "Localization not possible.\n" +
        "Close and open the app to restart the session.";
    private const string localizationSuccessMessage = "Localization completed.";
    private const float timeoutSeconds = 180;
    private bool isLocalizing = false;
    private float localizationPassedTime = 0f;
    private float configurePrepareTime = 3f;
    private bool isReturning = false;
    private bool enablingGeospatial = false;
    private bool isARReady = false;
    private List<GameObject> anchorList = new List<GameObject>();

    private VpsAvailability vpsAvailability;

    private const double orientationYawAccuracyThreshold = 33;
    private const double horizontalAccuracyThreshold = 33;
    private const double verticalAccuracyThreshold = 33;

    private IEnumerator asyncCheck = null;
    public void OnEnable()
    {
        isLocalizing = true;
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
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            Debug.Log("ARSession is not tracking, skipping anchor updates.");
            return;
        }


        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began
               && !EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
        {
            PlaceAnchorByScreenTap(Input.GetTouch(0).position);
        }
    }

    private void PlaceAnchorByScreenTap(Vector2 position)
    {
        List<ARRaycastHit> planeHitResults = new List<ARRaycastHit>();
        RaycastManager.Raycast(position, planeHitResults, TrackableType.Planes | TrackableType.FeaturePoint);

        if (planeHitResults.Count > 0)
        {
            GameObject anchorObject = Instantiate(AnchorPrefab, planeHitResults[0].pose.position, planeHitResults[0].pose.rotation);

            ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();

            anchorList.Add(anchorObject);

            Debug.Log($"Anchor placed at: {anchor.transform.position}");
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
        LocalizeStatusText.text = reason;
        isReturning = true;
    }

    #endregion

}
