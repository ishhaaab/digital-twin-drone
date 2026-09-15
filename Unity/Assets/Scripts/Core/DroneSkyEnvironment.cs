using UnityEngine;
using UnityEngine.Rendering;

/// Configures the mountain-dawn environment for the digital-twin viewport.
public static class DroneSkyEnvironment
{
    const string EnvironmentResourcePath = "Environment/kiara_1_dawn_2k";
    const string GroundName = "Drone Ground Surface";
    const float LitSunIntensity = 0.9f;
    const float LitAmbientIntensity = 0.78f;

    static Material runtimeSky;
    static Material runtimeGround;
    static Light runtimeSun;
    static bool lightingEnabled = true;

    public static void EnsureSceneSky()
    {
        DroneController controller = Object.FindFirstObjectByType<DroneController>();
        float unitsPerMeter = controller != null ? controller.metersToUnity : 1f;
        DroneWorldGrid.EnsureGrid(unitsPerMeter: unitsPerMeter);
        EnsureGround(unitsPerMeter);

        Texture environmentTexture = Resources.Load<Texture>(EnvironmentResourcePath);
        if (environmentTexture == null)
        {
            Debug.LogError("[Sky] Mountain panorama was not found at Resources/" + EnvironmentResourcePath + ".");
            return;
        }

        // Loading the shader itself from Resources prevents standalone shader
        // stripping. Shader.Find("Skybox/Panoramic") was editor-only in practice.
        Shader skyShader = Resources.Load<Shader>("Environment/DronePanoramicSky");
        if (skyShader == null) skyShader = Shader.Find("Skybox/Panoramic");
        if (skyShader == null)
        {
            Debug.LogWarning("[Sky] Panoramic sky shader was not found.");
            return;
        }

        if (runtimeSky == null)
        {
            runtimeSky = new Material(skyShader)
            {
                name = "Kiara Mountain Dawn Sky (Runtime)",
                hideFlags = HideFlags.DontSave
            };
        }
        runtimeSky.SetTexture("_MainTex", environmentTexture);
        runtimeSky.SetColor("_Tint", new Color(0.48f, 0.50f, 0.53f, 1f));
        runtimeSky.SetFloat("_Exposure", 0.84f);
        runtimeSky.SetFloat("_Rotation", 0f);
        runtimeSky.SetFloat("_Mapping", 1f);
        runtimeSky.SetFloat("_ImageType", 0f);
        runtimeSky.SetFloat("_MirrorOnBack", 0f);
        runtimeSky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");

        RenderSettings.skybox = runtimeSky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = LitAmbientIntensity;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.8f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.39f, 0.47f, 0.55f, 1f);
        RenderSettings.fogStartDistance = 10f * unitsPerMeter;
        RenderSettings.fogEndDistance = 85f * unitsPerMeter;

        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    sun = lights[i];
                    break;
                }
            }
        }
        if (sun == null)
        {
            var sunObject = new GameObject("Dawn Sun");
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        sun.name = "Dawn Sun";
        sun.color = new Color(1f, 0.84f, 0.72f, 1f);
        sun.intensity = LitSunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.62f;
        sun.transform.rotation = Quaternion.Euler(18f, 205f, 0f);
        RenderSettings.sun = sun;
        runtimeSun = sun;
        lightingEnabled = true;

        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].clearFlags = CameraClearFlags.Skybox;

        DynamicGI.UpdateEnvironment();
    }

    static void EnsureGround(float unitsPerMeter)
    {
        GameObject ground = GameObject.Find(GroundName);
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundName;
            ground.hideFlags = HideFlags.DontSave;
            Collider collider = ground.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
        }

        // Unity's built-in plane is 10 units wide; this matches the grid's 2 km span.
        float scale = 200f * Mathf.Max(0.0001f, unitsPerMeter);
        ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        ground.transform.localScale = new Vector3(scale, 1f, scale);

        MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        if (runtimeGround == null)
        {
            Shader groundShader = Shader.Find("Standard");
            if (groundShader == null)
            {
                Debug.LogWarning("[Sky] Built-in Standard shader was not found for the ground surface.");
                return;
            }

            runtimeGround = new Material(groundShader)
            {
                name = "Blue Green Ground (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            runtimeGround.SetColor("_Color", new Color(0.19f, 0.34f, 0.33f, 1f));
            runtimeGround.SetFloat("_Metallic", 0f);
            runtimeGround.SetFloat("_Glossiness", 0.12f);
        }

        renderer.sharedMaterial = runtimeGround;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    public static bool ToggleLighting()
    {
        if (runtimeSun == null) EnsureSceneSky();
        lightingEnabled = !lightingEnabled;
        if (runtimeSun != null) runtimeSun.intensity = lightingEnabled ? LitSunIntensity : 0.22f;
        RenderSettings.ambientIntensity = lightingEnabled ? LitAmbientIntensity : 0.36f;
        RenderSettings.fog = lightingEnabled;
        DynamicGI.UpdateEnvironment();
        return lightingEnabled;
    }
}
