using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 1 — davranış yerine tip kontrolü yapan
    /// testlerin gerçek karşılıkları.
    /// <para>
    /// Part 1'in bulduğu iki yanlış-yeşil aile burada gerçek davranışla
    /// kapatılıyor. <b>P1-TEST-031</b>: <c>Enum.IsDefined</c> derleyicinin zaten
    /// garanti ettiği şeyi doğrular — sabitin var olduğunu — ve reddin <i>üretildiğine</i>
    /// dair hiçbir şey söylemez; adı reddi ölçtüğünü iddia eden bir test bunun
    /// üzerine kurulmuştu. <b>P1-TEST-033</b>: bir int'in 0→1→0 gittiğini
    /// doğrulamak, seçimin gerçekten <i>başka bir yuvayı</i> hedeflediğini
    /// doğrulamaz.
    /// </para>
    /// <para>
    /// Orijinal iddialar <c>Vs0151PlayabilityPersistenceTests</c> içinde durur ve o
    /// dosya başka bir workstream'in elinde açıktır; bu fixture aynı iki kuralı
    /// gerçek davranışla ölçer, böylece kapsam orada bırakılan boşluğa bağlı
    /// kalmaz.
    /// </para>
    /// </summary>
    public class AuditPart4BehaviourAssertionTests
    {
        // =====================================================================
        // P1-TEST-031 — ret gerçekten üretiliyor mu
        // =====================================================================

        /// <summary>
        /// Collector Mk I ile başlatılan bir bakım ikmali <b>gerçekten</b>
        /// <see cref="ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible"/>
        /// döndürür — ve Mk II için aynı çağrı bu sebeple reddedilmez.
        /// <para>
        /// İkinci yarısı testin körlüğüne karşı: her çağrıyı reddeden bir üretim
        /// yolu ilk iddiayı da geçirirdi. Mk II'nin reddi başka bir sebeple
        /// (hedef/kargo) gelir, ki bu aracın uygunluk kapısının gerçekten araca
        /// baktığını gösterir.
        /// </para>
        /// </summary>
        [Test]
        public void AMkOneCollector_IsRefusedAsVehicleNotEligible_AndAMkTwoIsNot()
        {
            var shipObject = new GameObject("part4-resupply-ship", typeof(ShipController));
            var hangar = shipObject.AddComponent<ShipHangar>();
            try
            {
                var ship = shipObject.GetComponent<ShipController>();

                VehicleAgent mk1 = VehicleAgent.Create(ship, "part4-mk1", VehicleCatalog.Collector);
                VehicleAgent mk2 = VehicleAgent.Create(ship, "part4-mk2", VehicleCatalog.CollectorMk2);
                Assert.IsNotNull(mk1, "baseline: a Mk I collector could be created");
                Assert.IsNotNull(mk2, "baseline: a Mk II collector could be created");

                ShipHangar.MaintenanceResupplyRejectReason refusedMk1 =
                    hangar.TryStartMaintenanceResupply(mk1, VehicleCatalog.MetalAlloyId, 1f, "any-command");
                Assert.AreEqual(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible, refusedMk1,
                    "a Mk I collector was not refused on vehicle eligibility — the Mk>=2 guard "
                    + "either moved or stopped being reached");

                ShipHangar.MaintenanceResupplyRejectReason refusedMk2 =
                    hangar.TryStartMaintenanceResupply(mk2, VehicleCatalog.MetalAlloyId, 1f, "any-command");
                Assert.AreNotEqual(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible, refusedMk2,
                    "a Mk II collector was refused as an ineligible vehicle, so the first assertion "
                    + "above proves nothing: this path refuses everything");

                // And a missing vehicle is the same refusal, not a crash.
                Assert.AreEqual(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible,
                    hangar.TryStartMaintenanceResupply(null, VehicleCatalog.MetalAlloyId, 1f, "any-command"),
                    "a null vehicle did not come back as VehicleNotEligible");

                if (mk1 != null) Object.DestroyImmediate(mk1.gameObject);
                if (mk2 != null) Object.DestroyImmediate(mk2.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(shipObject);
            }
        }

        // =====================================================================
        // P1-TEST-033 — seçim gerçekten başka bir yuvayı hedefliyor mu
        // =====================================================================

        /// <summary>
        /// Destek yuvası seçimi iki <b>farklı</b> yüzey düğümü arasında geçiş yapar,
        /// ve iki düğüm gerçekten ayrı kimliklerdir.
        /// <para>
        /// Eski iddia yalnızca <c>SelectedSupportNodeIndex</c>'in 0→1→0 gittiğini
        /// ölçüyordu. Bir int'in değişmesi, oyuncunun ikinci bir yapıyı gerçekten
        /// kurabildiğini göstermez: iki indeks aynı düğüme çözülseydi ya da yüzeyde
        /// tek bir destek yuvası olsaydı, o test yine yeşil kalır ve "İnşa" ikinci
        /// yapıyı ilkinin üstüne kurmaya çalışırdı.
        /// </para>
        /// </summary>
        [Test]
        public void TogglingTheSupportSlot_MovesTheBuildTargetToADifferentSurfaceNode()
        {
            Assert.GreaterOrEqual(SurfaceSlot.SupportCount, 2,
                "the surface has fewer than two support slots, so the toggle has nothing to toggle "
                + "between and the second support structure can never be built");

            var go = new GameObject("part4-support-selection");
            try
            {
                var extraction = go.AddComponent<ShipExtraction>();

                Assert.AreEqual(0, extraction.SelectedSupportNodeIndex,
                    "the selection does not start on the first support slot");

                extraction.ToggleSupportNodeSelection();
                int second = extraction.SelectedSupportNodeIndex;
                Assert.AreNotEqual(0, second, "the toggle left the selection where it was");

                extraction.ToggleSupportNodeSelection();
                Assert.AreEqual(0, extraction.SelectedSupportNodeIndex,
                    "the toggle does not come back to the first slot");

                // The real consequence: the two indices address two different nodes on
                // a real surface, so the second structure has somewhere of its own to
                // stand. PlanetProfile.SupportNode is the same lookup
                // ShipExtraction.BuildOrUpgradeSupport uses.
                Assert.AreNotEqual(SurfaceSlot.Support(0), SurfaceSlot.Support(second),
                    "both support-slot indices resolve to the same surface slot, so the toggle "
                    + "moves an integer and not the build target");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Ve iki destek yapısı gerçekten farklı şeylerdir: farklı maliyet ve farklı
        /// ön koşul. Aynı olsalardı seçim oyuncu için anlamsız olurdu.
        /// </summary>
        [Test]
        public void TheTwoSupportStructures_CostDifferentThings()
        {
            SurfaceStructureDef sensor = SurfaceStructureCatalog.SensorArray;
            SurfaceStructureDef platform = SurfaceStructureCatalog.MaintenancePlatform;

            Assert.AreNotEqual(sensor.Type, platform.Type,
                "both support slots build the same structure type");
            Assert.AreNotEqual(sensor.PartsCost, platform.PartsCost,
                "the two support structures cost the same parts, so the choice costs the player nothing "
                + "and carries no decision");
        }
    }
}
