using UnityEngine;

//-- component must be attached to camera (due to OnRenderImage)
[ExecuteAlways, RequireComponent(typeof(Camera))]
public class RayTracerComponent : MonoBehaviour
{
    public ComputeShader RayTracingCS;
    public Texture       SkyboxTexture;
    public Color         AmbientColor = Color.white;

    public Light DirectionalLight;

    private Camera        _camera;
    private RenderTexture _target;

    private Material _addMaterial;
    private uint     _currentSample = 0;

    private static class ShaderProperties
    {
        public static int TexResult = Shader.PropertyToID("Result");
        public static int TexSkybox = Shader.PropertyToID("_SkyboxTexture");

        public static int MatCameraToWorld           = Shader.PropertyToID("_CameraToWorld");
        public static int MatCameraInverseProjection = Shader.PropertyToID("_CameraInverseProjection");

        public static int VecPixelOffsetAA     = Shader.PropertyToID("_PixelOffsetAA");
        public static int VecAmbientColor      = Shader.PropertyToID("_AmbientColor");
        public static int VecDirectionalLight  = Shader.PropertyToID("_DirectionalLight");
    };

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (transform.hasChanged)
        {
            ResetRendering();
            transform.hasChanged = false;
        }
    }

    private void OnValidate()
    {
        ResetRendering();
    }

    private void ResetRendering()
    {
        _currentSample = 0;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!ValidateResources())
            return;

        if (Application.isPlaying)
        {
            RenderToTexture(dest);

            if (_addMaterial == null)
                _addMaterial = new Material(Shader.Find("Hidden/AddBlendingShader"));

            _addMaterial.SetFloat("_Sample", _currentSample++);
            Graphics.Blit(_target, dest, _addMaterial);
        }
        else
        {
            RenderToTexture(dest);

            Graphics.Blit(_target, dest);
        }
    }

    private bool ValidateResources()
    {
        return RayTracingCS     != null &&
               SkyboxTexture    != null &&
               DirectionalLight != null;
    }

    private void RenderToTexture(RenderTexture dest)
    {
        InitRenderTexture();

        int kernelIndex = RayTracingCS.FindKernel("CSMain");

        RayTracingCS.SetTexture(kernelIndex, ShaderProperties.TexResult, _target);
        RayTracingCS.SetTexture(kernelIndex, ShaderProperties.TexSkybox, SkyboxTexture);

        RayTracingCS.SetMatrix(ShaderProperties.MatCameraToWorld,           _camera.cameraToWorldMatrix);
        RayTracingCS.SetMatrix(ShaderProperties.MatCameraInverseProjection, _camera.projectionMatrix.inverse);

        RayTracingCS.SetVector(ShaderProperties.VecPixelOffsetAA, new Vector4(Random.value, Random.value, 0.0f, 0.0f));
        RayTracingCS.SetVector(ShaderProperties.VecAmbientColor, AmbientColor.linear);

        var dirLight = -DirectionalLight.transform.forward;
        RayTracingCS.SetVector(ShaderProperties.VecDirectionalLight, new Vector4(dirLight.x, dirLight.y, dirLight.z, DirectionalLight.intensity));

        int threadsGroupX = Mathf.CeilToInt(Screen.width  / 8.0f);
        int threadsGroupY = Mathf.CeilToInt(Screen.height / 8.0f);

        RayTracingCS.Dispatch(kernelIndex, threadsGroupX, threadsGroupY, 1);
    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            if (_target != null)
                _target.Release();

            _target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
        }
    }
}
