using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Random = UnityEngine.Random;

//-- component must be attached to camera (due to OnRenderImage)
[ExecuteAlways, RequireComponent(typeof(Camera))]
public class RayTracerComponent : MonoBehaviour
{
    [Range(0,16)]
    public int BouncesCount = 6;

    public ComputeShader RayTracingCS;
    public Texture       SkyboxTexture;
    public Color         AmbientColor = Color.white;

    public Light DirectionalLight;

    private ComputeBuffer _bufferSpheres;

    private Camera        _camera;
    private RenderTexture _target;

    private Material _addMaterial;
    private uint     _currentSample = 0;

    private static class ShaderProperties
    {
        public static int TexResult = Shader.PropertyToID("Result");
        public static int TexSkybox = Shader.PropertyToID("_SkyboxTexture");

        public static int CompBuffSpheres = Shader.PropertyToID("_Spheres");

        public static int IntBouncesCount = Shader.PropertyToID("_BouncesCount");

        public static int MatCameraToWorld           = Shader.PropertyToID("_CameraToWorld");
        public static int MatCameraInverseProjection = Shader.PropertyToID("_CameraInverseProjection");

        public static int VecPixelOffsetAA     = Shader.PropertyToID("_PixelOffsetAA");
        public static int VecAmbientColor      = Shader.PropertyToID("_AmbientColor");
        public static int VecDirectionalLight  = Shader.PropertyToID("_DirectionalLight");
    };

    private struct Sphere
    {
        public Vector3 position;
        public float   radius;
        public Vector3 albedo;
        public Vector3 specular;
    };

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        SetupScene();
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

    private void OnDestroy()
    {
        if (_bufferSpheres != null)
        {
            _bufferSpheres.Release();
            _bufferSpheres.Dispose();
            _bufferSpheres = null;
        }
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
               DirectionalLight != null &&
               _bufferSpheres   != null;
    }

    private void RenderToTexture(RenderTexture dest)
    {
        InitRenderTexture();

        int kernelIndex = RayTracingCS.FindKernel("CSMain");

        RayTracingCS.SetTexture(kernelIndex, ShaderProperties.TexResult, _target);
        RayTracingCS.SetTexture(kernelIndex, ShaderProperties.TexSkybox, SkyboxTexture);

        RayTracingCS.SetBuffer(kernelIndex, ShaderProperties.CompBuffSpheres, _bufferSpheres);

        RayTracingCS.SetInt(ShaderProperties.IntBouncesCount, BouncesCount);

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

    private void SetupScene()
    {
        List<Sphere> spheres = new List<Sphere>();

        int cnt = 4;
        float step = 2.0f;

        for (int x = 0; x < cnt; x++)
        for (int y = 0; y < cnt; y++)
        {
            var sphere = new Sphere();

            sphere.position = new Vector3(x * step, 1.0f, y * step);
            sphere.radius = Mathf.Lerp(1.0f, 1.5f, Random.value);
            sphere.albedo = new Vector3(0.4f + Mathf.Abs(x) * 0.3f, 0.2f + Mathf.Abs(y) * 0.4f, 1.0f - Mathf.Abs(x * y) * 0.15f);
            sphere.specular = new Vector3(1.0f - Mathf.Clamp01(y), 1.0f - Mathf.Clamp01(y), 1.0f - Mathf.Clamp01(y));

            spheres.Add(sphere);
        }

        //-- fill computeBuffer
        if (_bufferSpheres == null)
        {
            int stride = UnsafeUtility.SizeOf<Sphere>();
            _bufferSpheres = new ComputeBuffer(spheres.Count, stride);
        }

        _bufferSpheres.SetData(spheres);
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
