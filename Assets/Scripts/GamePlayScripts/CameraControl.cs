using UnityEngine;

// 3D-fighter style camera: frames both fighters and swings around so the line
// between them always runs across the screen. Fighters face each other on their
// own (CharacterControl); the camera never rotates them.
public class CameraControl : MonoBehaviour
{
    public static CameraControl Instance { get; private set; }

    public Camera Camera1;
    public GameObject Player1;
    public GameObject Player2;

    [SerializeField] float minCameraDistance = 7.5f;
    [SerializeField] float maxCameraDistance = 22f;
    [SerializeField] float distanceZoomFactor = 1.5f;
    [SerializeField] float followSharpness = 6f;
    [SerializeField] float orbitSharpness = 3f;
    [SerializeField] float pitchAngle = 8f;                 // Degrees of downward tilt.
    [SerializeField] float minSeparationToOrbit = 0.75f;    // Below this the fighter axis is too noisy to follow.
    [Tooltip("How far (degrees) the camera may swing away from the level's original viewing direction. Enclosed stages like the fridge can only be seen from the open side.")]
    [SerializeField] float maxOrbitAngle = 35f;

    public bool isCameraFollowing = true;
    public Vector3 midpoint;
    public Vector3 camoffset = new Vector3(0f, 1.5f, 0f);

    Vector3 viewForward = Vector3.forward;  // Flat (yaw-only) viewing direction.
    Vector3 homeForward = Vector3.forward;  // The level's authored viewing direction.

    // Flat camera axes used for camera-relative movement input (where the camera really looks).
    public Vector3 FlatForward
    {
        get
        {
            if (Camera1 == null)
            {
                return viewForward;
            }

            Vector3 flat = Camera1.transform.forward;
            flat.y = 0f;
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : viewForward;
        }
    }

    public Vector3 FlatRight => Vector3.Cross(Vector3.up, FlatForward);

    private void Awake()
    {
        // Each level prefab brings its own CameraManager, which is destroyed with the level.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (Camera1 != null)
        {
            Vector3 flat = Camera1.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
            {
                viewForward = flat.normalized;
                homeForward = viewForward;
            }
        }
    }

    private void LateUpdate()
    {
        if (!isCameraFollowing || Camera1 == null || Player1 == null || Player2 == null)
        {
            return;
        }

        FixedCameraFollowSmooth(Camera1, Player1.transform, Player2.transform, Player1, Player2);
    }

    public void FixedCameraFollowSmooth(Camera cam, Transform t1, Transform t2, GameObject p1, GameObject p2)
    {
        midpoint = (t1.position + t2.position) * 0.5f;

        Vector3 axis = t2.position - t1.position;
        axis.y = 0f;
        float separation = axis.magnitude;

        if (separation > minSeparationToOrbit)
        {
            // Look perpendicular to the fighter axis, picking whichever side is
            // closer to where we already are so the camera never flips 180 degrees.
            Vector3 perpendicular = Vector3.Cross(axis / separation, Vector3.up);
            if (Vector3.Dot(perpendicular, viewForward) < 0f)
            {
                perpendicular = -perpendicular;
            }

            perpendicular = Vector3.RotateTowards(homeForward, perpendicular, maxOrbitAngle * Mathf.Deg2Rad, 0f);

            float orbitStep = 1f - Mathf.Exp(-orbitSharpness * Time.deltaTime);
            viewForward = Vector3.Slerp(viewForward, perpendicular, orbitStep);
            viewForward.y = 0f;
            viewForward.Normalize();
        }

        Quaternion viewRotation = Quaternion.LookRotation(viewForward, Vector3.up) * Quaternion.Euler(pitchAngle, 0f, 0f);

        float distance = Vector3.Distance(t1.position, t2.position);
        float cameraDistance = Mathf.Clamp(distance * distanceZoomFactor, minCameraDistance, maxCameraDistance);
        Vector3 cameraDestination = (midpoint + camoffset) - (viewRotation * Vector3.forward) * cameraDistance;

        if (cam.orthographic)
        {
            cam.orthographicSize = Mathf.Max(minCameraDistance, distance);
        }

        // Enclosed stages (the fridge) keep the camera on their open side so walls never
        // block the view; it then aims at the fighters from wherever it's allowed to be.
        StageBounds stage = Stage;
        if (stage != null)
        {
            cameraDestination = stage.ClampCamera(cameraDestination);
        }

        float followStep = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, cameraDestination, followStep);

        Vector3 toTarget = (midpoint + camoffset) - cam.transform.position;
        Quaternion lookRotation = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget, Vector3.up) : viewRotation;
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, lookRotation, followStep);
    }

    StageBounds stage;
    bool stageSearched;

    StageBounds Stage
    {
        get
        {
            // Looked up once: levels without a StageBounds just never limit the camera.
            if (!stageSearched)
            {
                stageSearched = true;
                stage = GetComponentInParent<StageBounds>();
                if (stage == null)
                {
                    stage = FindAnyObjectByType<StageBounds>();
                }
            }

            return stage;
        }
    }
}
