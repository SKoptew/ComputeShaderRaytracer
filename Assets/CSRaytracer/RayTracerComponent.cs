using System.Collections.Generic;
using System.Numerics;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

//-- component must be attached to camera (due to OnRenderImage)
[ExecuteAlways, RequireComponent(typeof(Camera))]
public class RayTracerComponent : MonoBehaviour
{
    [Range(0,16)]
    public int BouncesCount = 6;
    public int Seed = 0;

    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;

    public ComputeShader RayTracingCS;
    public Texture       SkyboxTexture;
    public Color         AmbientColor = Color.white;

    public Light DirectionalLight;

    private ComputeBuffer _bufferSpheres;

    private Camera        _camera;
    private RenderTexture _target;
    private RenderTexture _converged; // high-precision float texture

    private Material _addMaterial;
    private uint     _currentSample = 0;

    private static class ShaderProperties
    {
        internal static readonly int TexResult                  = Shader.PropertyToID("Result");
        internal static readonly int TexSkybox                  = Shader.PropertyToID("_SkyboxTexture");

        internal static readonly int CompBuffSpheres            = Shader.PropertyToID("_Spheres");
        internal static readonly int IntBouncesCount            = Shader.PropertyToID("_BouncesCount");

        internal static readonly int MatCameraToWorld           = Shader.PropertyToID("_CameraToWorld");
        internal static readonly int MatCameraInverseProjection = Shader.PropertyToID("_CameraInverseProjection");

        internal static readonly int VecPixelOffsetAA           = Shader.PropertyToID("_PixelOffsetAA");
        internal static readonly int VecAmbientColor            = Shader.PropertyToID("_AmbientColor");
        internal static readonly int VecDirectionalLight        = Shader.PropertyToID("_DirectionalLight");
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

    private void OnGUI()
    {
        if (GUILayout.Button(($"Samples: {_currentSample}")))
        {
            ResetRendering();
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

        RenderToTexture();

        if (Application.isPlaying && _converged != null)
        {
            if (_addMaterial == null)
                _addMaterial = new Material(Shader.Find("Hidden/AddBlendingShader"));

            _addMaterial.SetFloat("_Sample", _currentSample++);
            Graphics.Blit(_target, _converged, _addMaterial); // accumulate samles in high-precision _converged texture

            Graphics.Blit(_converged, dest);
        }
        else
        {
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

    private void RenderToTexture()
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

        RayTracingCS.SetFloat("_Seed", Random.value);

        var dirLight = -DirectionalLight.transform.forward;
        RayTracingCS.SetVector(ShaderProperties.VecDirectionalLight, new Vector4(dirLight.x, dirLight.y, dirLight.z, DirectionalLight.intensity));

        int threadsGroupX = Mathf.CeilToInt(Screen.width  / 8.0f);
        int threadsGroupY = Mathf.CeilToInt(Screen.height / 8.0f);

        RayTracingCS.Dispatch(kernelIndex, threadsGroupX, threadsGroupY, 1);
    }

    private void SetupScene()
    {
        Random.InitState(Seed);

        List<Sphere> spheres = new List<Sphere>();

        for (int i = 0; i < SpheresMax; i++)
        {
            Sphere sphere = new Sphere();

            bool intersectionWithOthers = false;

            do
            {
                sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
                Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
                sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

                // check intersection
                intersectionWithOthers = false;
                foreach (var other in spheres)
                {
                    float minDist = sphere.radius + other.radius;

                    if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    {
                        intersectionWithOthers = true;
                        break;
                    }
                }
            } while (intersectionWithOthers);

            //-- material parameters
            Color color = Random.ColorHSV();
            bool metal = Random.value < 0.5f;

            sphere.albedo   = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
            sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;

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
            {
                _target.Release();
                _converged.Release();
            }

            _target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();

            _converged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _converged.enableRandomWrite = true;
            _converged.Create();

            ResetRendering();
        }
    }
}
