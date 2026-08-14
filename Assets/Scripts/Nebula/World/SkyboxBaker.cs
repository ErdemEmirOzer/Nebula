using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Bakes the live starfield backdrop (12k star sprites + ~30 huge nebula /
    /// color-zone quads) into a static cubemap skybox once at startup, then
    /// destroys the live objects. The backdrop always moved with the camera
    /// anyway, so a skybox is visually identical — but dozens of near-fullscreen
    /// additive quads per frame become a single skybox pass.
    /// </summary>
    public static class SkyboxBaker
    {
        public static bool TryBake(GameObject backdrop, Color backgroundColor, int resolution = 1024)
        {
            if (backdrop == null) return false;

            // Bake in an isolated spot so no world objects leak into the sky.
            Vector3 originalPosition = backdrop.transform.position;
            Vector3 isolated = new Vector3(0f, 6e7f, 0f);
            backdrop.transform.position = isolated;

            var cubemap = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false
            };

            var cameraGO = new GameObject("Skybox Bake Camera");
            var bakeCamera = cameraGO.AddComponent<Camera>();
            bakeCamera.transform.position = isolated;
            bakeCamera.clearFlags = CameraClearFlags.SolidColor;
            bakeCamera.backgroundColor = backgroundColor;
            bakeCamera.nearClipPlane = 10f;
            bakeCamera.farClipPlane = 2000000f;
            bakeCamera.enabled = false;

            bool rendered = bakeCamera.RenderToCubemap(cubemap);
            Object.Destroy(cameraGO);

            Shader skyboxShader = rendered ? Shader.Find("Skybox/Cubemap") : null;
            if (skyboxShader == null)
            {
                // Graceful fallback: keep the live backdrop.
                //
                // The cube surface is six 1024×1024 ARGB32 faces plus depth — the
                // largest single graphics allocation a session makes — and it is
                // allocated above, before anyone knows whether the bake will work.
                // A bare Destroy leaves that surface for the driver to reclaim
                // whenever it likes; RuntimeResourceScope.Release is the same
                // clear-active/Release/Destroy the scope gives the success path's
                // copy, and this branch is the one a headless run always takes,
                // because a null graphics device cannot RenderToCubemap.
                backdrop.transform.position = originalPosition;
                RuntimeResourceScope.Release(cubemap);
                return false;
            }

            // The cubemap outlives this call inside the skybox material, so it needs
            // the same owner. Without this it is the one runtime texture nothing
            // destroys: RenderSettings.skybox = null at teardown drops the reference
            // but not the RenderTexture, so every session left one 1024³ cube behind.
            RuntimeResourceScope.Own(cubemap);
            var skybox = RuntimeResourceScope.Own(new Material(skyboxShader));
            skybox.SetTexture("_Tex", cubemap);
            RenderSettings.skybox = skybox;
            Object.Destroy(backdrop);
            return true;
        }
    }
}
