using UnityEngine;
using UnityEngine.Rendering;

/// Configures a lightweight dusk sky for the digital-twin viewport. The
/// procedural skybox keeps the scene asset-free while still rendering a real
/// sun disk from the scene's directional light.
public static class DroneSkyEnvironment
{
    static Material runtimeSky;

    public static void EnsureSceneSky()
    {
        Shader skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader == null)
        {
            Debug.LogWarning("[Sky] Built-in procedural sky shader was not found.");
            return;
        }

        if (runtimeSky == null)
        {
            runtimeSky = new Material(skyShader)
            {
                name = "Drone Dusk Sky (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            runtimeSky.SetFloat("_SunDisk", 2f);
            runtimeSky.SetFloat("_SunSize", 0.045f);
            runtimeSky.SetFloat("_SunSizeConvergence", 5f);
            runtimeSky.SetFloat("_AtmosphereThickness", 1.25f);
            runtimeSky.SetColor("_SkyTint", new Color(0.38f, 0.55f, 0.72f, 1f));
            runtimeSky.SetColor("_GroundColor", new Color(0.38f, 0.31f, 0.27f, 1f));
            runtimeSky.SetFloat("_Exposure", 1.05f);
        }

        RenderSettings.skybox = runtimeSky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.45f, 0.58f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.27f, 0.25f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.10f, 1f);
        RenderSettings.ambientIntensity = 0.85f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.30f, 0.36f, 0.42f, 1f);
        RenderSettings.fogStartDistance = 50f;
        RenderSettings.fogEndDistance = 260f;

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
            var sunObject = new GameObject("Dusk Sun");
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        sun.name = "Dusk Sun";
        sun.color = new Color(1f, 0.78f, 0.56f, 1f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.72f;
        // The sun disk sits low and to the right of the default drone camera.
        sun.transform.rotation = Quaternion.Euler(10f, 205f, 0f);
        RenderSettings.sun = sun;

        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].clearFlags = CameraClearFlags.Skybox;

        DynamicGI.UpdateEnvironment();
    }
}
