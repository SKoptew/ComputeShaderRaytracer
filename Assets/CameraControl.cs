using UnityEngine;

public class CameraControl : MonoBehaviour
{
    class CameraState
    {
        public float yaw;
        public float pitch;
        public float roll;
        public float x, y, z;

        public void SetFromTransform(Transform t)
        {
            pitch = t.eulerAngles.x;
            yaw   = t.eulerAngles.y;
            roll  = t.eulerAngles.z;

            x = t.position.x;
            y = t.position.y;
            z = t.position.z;
        }

        public void Translate(Vector3 translation)
        {
            var rotatedTranslation = Quaternion.Euler(pitch, yaw, roll) * translation;

            x += rotatedTranslation.x;
            y += rotatedTranslation.y;
            z += rotatedTranslation.z;
        }

        public void LerpTowards(CameraState target, float positionLerpPct, float rotationLerpPct)
        {
            yaw   = Mathf.Lerp(yaw,   target.yaw,   rotationLerpPct);
            pitch = Mathf.Lerp(pitch, target.pitch, rotationLerpPct);
            roll  = Mathf.Lerp(roll,  target.roll,  rotationLerpPct);

            x = Mathf.Lerp(x, target.x, positionLerpPct);
            y = Mathf.Lerp(y, target.y, positionLerpPct);
            z = Mathf.Lerp(z, target.z, positionLerpPct);
        }

        public void UpdateTransform(Transform t)
        {
            t.eulerAngles = new Vector3(pitch, yaw, roll);
            t.position    = new Vector3(x, y, z);
        }
    }

    private CameraState _targetCameraState = new CameraState();
    private CameraState _interpolatingCameraState = new CameraState();

    [Header("Movement settings")]
    [Tooltip("Exponential boost factor on translation, controllable by mouse wheel")]
    public float Boost = 3.5f;

    [Range(0.0f, 0.5f)]
    public float PositionLerpTime = 0.2f;

    [Range(0.0f, 0.1f)]
    public float RotationLerpTime = 0.01f;

    public AnimationCurve MouseSensitivityCurve = new AnimationCurve(new Keyframe(0f, 0.5f, 0f, 5f), new Keyframe(1f, 2.5f, 0f, 0f));

    void OnEnable()
    {
        _targetCameraState.SetFromTransform(transform);
        _interpolatingCameraState.SetFromTransform(transform);
    }

    void Update()
    {
        //-- exit play mode
        if (Input.GetKey(KeyCode.Escape))
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        //-- Hide+lock cursor while RMB pressed
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked; // locked to center of view, invisible
        }
        if (Input.GetMouseButtonUp(1))
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        //-- Camera rotation
        if (Input.GetMouseButton(1))
        {
            var mouseMovement = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            var mouseSensitivityFactor = MouseSensitivityCurve.Evaluate(mouseMovement.magnitude);

            _targetCameraState.yaw   += mouseMovement.x * mouseSensitivityFactor;
            _targetCameraState.pitch -= mouseMovement.y * mouseSensitivityFactor;
        }

        //-- Camera translation (Shift - increase speed)
        var shift = GetInputTranslationDirection() * Time.deltaTime;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            shift *= 10.0f;
        }

        //-- Movement boost factor (controlled by mouse wheel)
        Boost += Input.mouseScrollDelta.y * 0.2f;
        shift *= Mathf.Pow(2.0f, Boost);

        _targetCameraState.Translate(shift);


        //-- interpolation
        var positionLerpFactor = 1f - Mathf.Exp((Mathf.Log(1f - 0.99f) / PositionLerpTime) * Time.deltaTime);
        var rotationLerpFactor = 1f - Mathf.Exp((Mathf.Log(1f - 0.99f) / PositionLerpTime) * Time.deltaTime);

        _interpolatingCameraState.LerpTowards(_targetCameraState, positionLerpFactor, rotationLerpFactor);
        _interpolatingCameraState.UpdateTransform(transform);
    }

    private Vector3 GetInputTranslationDirection()
    {
        var dir = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) dir += Vector3.back;
        if (Input.GetKey(KeyCode.A)) dir += Vector3.left;
        if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
        if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;
        if (Input.GetKey(KeyCode.E)) dir += Vector3.up;

        return dir;
    }
}
