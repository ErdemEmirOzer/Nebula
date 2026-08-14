using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Nebula.Platform;
using Nebula.UI.Brand;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Binds the delivered app icons and splash plate to PlayerSettings.
    /// <para>
    /// Run once, deliberately, and the result is committed in
    /// <c>ProjectSettings/ProjectSettings.asset</c>. It is <b>not</b> an
    /// <c>InitializeOnLoad</c>: settings that rewrite themselves whenever the editor
    /// opens turn every unrelated branch into a settings diff, and a test that
    /// applied the values before asserting them would prove only that the setter
    /// works. <see cref="NebulaAppIdentityTests"/> reads the committed state instead.
    /// </para>
    /// <para>
    /// Touches icons, the splash logo list and the app's naming — company, product and
    /// the standalone bundle identifier, all read from <see cref="NebulaAppIdentity"/> so
    /// there is one statement of them rather than one per caller. The Unity splash
    /// toggles are left exactly as they are, including <c>m_ShowUnitySplashScreen</c>,
    /// which stays on. The engine splash is a licence term, not a setting to work around;
    /// our plate is appended to that sequence rather than replacing it.
    /// </para>
    /// </summary>
    public static class NebulaAppIdentityInstaller
    {
        /// <summary>How long the Nebula logo holds, in seconds. Unity's floor is 2.</summary>
        public const float SplashLogoSeconds = NebulaBrandAssetCatalog.SplashLogoSeconds;

        [MenuItem("Nebula/Brand/Apply App Identity")]
        public static void Apply()
        {
            if (!ApplyIcons(out string iconReport)) return;
            ApplySplash(out string splashReport);
            ApplyProductIdentity(out string identityReport);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Nebula] App identity applied.\n{iconReport}\n{splashReport}\n{identityReport}");
        }

        /// <summary>
        /// Writes the app's naming into PlayerSettings and then reads it back.
        /// <para>
        /// The values come from <see cref="NebulaAppIdentity"/> and are never spelled
        /// here, because the committed asset and the runtime constants have to be able to
        /// be checked against each other — two independent literals would make that check
        /// meaningless.
        /// </para>
        /// <para>
        /// The read-back is not ceremony. <c>overrideDefaultApplicationIdentifier</c> —
        /// the flag that decides whether Unity uses the identifier below or synthesises
        /// <c>com.&lt;company&gt;.&lt;product&gt;</c> — has no public setter in 2022.3; it
        /// is committed in <c>ProjectSettings.asset</c> and is switched from the
        /// Inspector. So this can write an identifier that does not take effect, and the
        /// only way to know is to ask for it back. If it comes back wrong this says so
        /// loudly rather than reporting a success the build will not honour.
        /// </para>
        /// <para>
        /// Note what this identity is and is not: a private, non-commercial test
        /// distribution's name. It is not a registered company, not a signed or notarised
        /// publisher, and nothing downstream should present it as one.
        /// </para>
        /// </summary>
        public static bool ApplyProductIdentity(out string report)
        {
            PlayerSettings.companyName = NebulaAppIdentity.Company;
            PlayerSettings.productName = NebulaAppIdentity.Product;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, NebulaAppIdentity.BundleIdentifier);

            string applied = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            report = $"identity: {PlayerSettings.companyName} / {PlayerSettings.productName} / {applied}";

            if (applied == NebulaAppIdentity.BundleIdentifier) return true;

            Debug.LogError(
                $"[Nebula] the standalone bundle identifier read back as '{applied}' rather than " +
                $"'{NebulaAppIdentity.BundleIdentifier}'. Tick Player Settings > Other Settings > " +
                "Identification > Override Default Bundle Identifier and run this again; the flag " +
                "behind it has no scripting API in this Unity version.");
            report += " (NOT APPLIED)";
            return false;
        }

        /// <summary>
        /// Fills every standalone icon slot Unity asks for from the delivered set.
        /// <para>
        /// Unity's standalone slots are not the same list the delivery ships — it
        /// wants a 48 and there is no 48 — so each slot takes the smallest delivered
        /// icon at least as large as it, and downscales. Upscaling a smaller one
        /// would put a soft 32 in the 48 slot, which is the one thing an icon set
        /// must not do.
        /// </para>
        /// </summary>
        public static bool ApplyIcons(out string report)
        {
            var target = NamedBuildTarget.Standalone;
            int[] slots = PlayerSettings.GetIconSizes(target, IconKind.Any);
            report = string.Empty;

            if (slots == null || slots.Length == 0)
            {
                Debug.LogError("[Nebula] Standalone reports no icon slots; nothing bound.");
                return false;
            }

            var icons = new Texture2D[slots.Length];
            var lines = new List<string>(slots.Length);

            for (int i = 0; i < slots.Length; i++)
            {
                int chosen = NearestDeliveredIcon(slots[i]);
                Texture2D texture = LoadIcon(chosen);
                if (texture == null)
                {
                    Debug.LogError($"[Nebula] icon {chosen} is missing; app identity not bound.");
                    return false;
                }

                icons[i] = texture;
                lines.Add($"  slot {slots[i]} <- brand.icon.{chosen}");
            }

            PlayerSettings.SetIcons(target, icons, IconKind.Any);
            report = "icons:\n" + string.Join("\n", lines);
            return true;
        }

        /// <summary>
        /// Appends the Nebula plate to the splash sequence.
        /// <para>
        /// Replaces the list rather than adding to it so running this twice does not
        /// produce two Nebula logos — but only our own entry is ever in that list.
        /// The engine's own logo is not one of these; it is controlled by
        /// <c>m_ShowUnitySplashLogo</c>, which this does not touch.
        /// </para>
        /// </summary>
        public static bool ApplySplash(out string report)
        {
            Sprite splash = AssetDatabase.LoadAssetAtPath<Sprite>(
                NebulaBrandAssetCatalog.SplashAssetPath(
                    NebulaBrandAssetCatalog.SplashAspect.SixteenByNine));
            if (splash == null)
            {
                report = "splash: MISSING";
                Debug.LogError("[Nebula] the 16:9 splash plate is missing; splash not bound.");
                return false;
            }

            PlayerSettings.SplashScreen.logos = new[]
            {
                PlayerSettings.SplashScreenLogo.Create(SplashLogoSeconds, splash),
            };

            report = $"splash: {splash.name} ({splash.rect.width}x{splash.rect.height}) for {SplashLogoSeconds}s";
            return true;
        }

        /// <summary>The smallest delivered size that covers <paramref name="slot"/>.</summary>
        public static int NearestDeliveredIcon(int slot) => NebulaBrandAssetCatalog.IconSizeFor(slot);

        /// <summary>
        /// Loaded through <c>AssetDatabase</c> rather than the runtime catalog: this
        /// runs in the editor, where the asset path is the authority, and the catalog
        /// would otherwise cache editor-only loads into a runtime cache.
        /// </summary>
        private static Texture2D LoadIcon(int size) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(NebulaBrandAssetCatalog.IconAssetPath(size));

        // ---- native icon seam ------------------------------------------------
        //
        // Unity builds the Windows .ico and the macOS .icns itself from the PNG set
        // bound above, so the build has a correct identity today with no native file
        // in the repository. If hand-authored native icons are delivered later --
        // typically for a Windows installer or a macOS document type, which the
        // player build does not cover -- they belong in a build post-process beside
        // NebulaBuildAutomation rather than here, and this binding stays as the
        // source for the player itself.
        //
        // Deliberately not stubbed out as an empty method: a hook that does nothing
        // reads as a feature that is broken rather than one that is not needed yet.
    }
}
