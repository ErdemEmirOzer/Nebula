namespace Nebula.Platform
{
    /// <summary>
    /// The app's identity, stated once.
    /// <para>
    /// <b>What a build actually reads is <c>ProjectSettings/ProjectSettings.asset</c></b>
    /// — Unity's PlayerSettings are the only thing the player, the Info.plist and
    /// <c>Tools/Distribution/build_manifest.py</c> consult. This class does not
    /// configure anything; it is the one place in C# that states what those committed
    /// values are supposed to be, so a test can bind the asset and the code together
    /// instead of letting them drift. Adding a second copy of these strings anywhere
    /// else re-creates exactly the drift this exists to prevent.
    /// </para>
    /// <para>
    /// This is a <b>private, non-commercial test-distribution identity</b>. "Erdem" is
    /// the author's own name used as a folder and bundle namespace, not a registered
    /// company; <c>com.erdem.nebula</c> is not a signed or notarised publisher claim
    /// and nothing here should be read as one. It exists so two installs of this build
    /// land in two different folders and so the OS has a stable key for the app.
    /// </para>
    /// <para>
    /// The legacy pair is kept because it is load-bearing, not for nostalgia:
    /// <c>Application.persistentDataPath</c> is derived from company and product,
    /// so renaming them moves every save, rebind, log and telemetry file out from under
    /// the running game. <see cref="NebulaLegacyDataMigrator"/> needs the old pair to
    /// find that folder, and it is the only reason these two constants are public.
    /// </para>
    /// </summary>
    public static class NebulaAppIdentity
    {
        /// <summary>Must equal <c>companyName</c> in ProjectSettings.asset.</summary>
        public const string Company = "Erdem";

        /// <summary>Must equal <c>productName</c> in ProjectSettings.asset. Also the app's display name.</summary>
        public const string Product = "NEBULA";

        /// <summary>
        /// Must equal the <c>Standalone</c> entry of <c>applicationIdentifier</c> in
        /// ProjectSettings.asset, with <c>overrideDefaultApplicationIdentifier: 1</c>.
        /// <para>
        /// Spelled lower-case rather than following <see cref="Company"/> and
        /// <see cref="Product"/>: bundle identifiers are compared case-insensitively by
        /// some tooling and case-sensitively by others, and the only way that ambiguity
        /// never costs anything is to pick one case and never vary it. Without the
        /// override flag Unity would synthesise <c>com.Erdem.NEBULA</c> from the two
        /// names above, which is a different string under a case-sensitive comparison.
        /// </para>
        /// </summary>
        public const string BundleIdentifier = "com.erdem.nebula";

        /// <summary>The company half of the identity every build before this one shipped with.</summary>
        public const string LegacyCompany = "DefaultCompany";

        /// <summary>The product half of the identity every build before this one shipped with.</summary>
        public const string LegacyProduct = "Nebula";

        /// <summary>
        /// The folder every persistent-data consumer nests under, inside the
        /// per-app root. Owned here because the migrator has to walk the same
        /// subtree that <c>SaveService</c>, <c>NebulaInputBindingStore</c>,
        /// <c>SessionLog</c> and <c>TelemetryService</c> each build for themselves.
        /// </summary>
        public const string PayloadFolder = "nebula";
    }
}
