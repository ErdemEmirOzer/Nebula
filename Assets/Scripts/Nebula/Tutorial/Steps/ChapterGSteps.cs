using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Sectors;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter G -- Gezegen Yapıları ve Bakım (curriculum steps 94-112).
    /// <para>
    /// Every structure step below is validated on the pure
    /// <see cref="StructureRuntime"/> / <see cref="Durability"/> state the real
    /// build, upgrade, damage and repair paths write -- the tier, the lifecycle
    /// transition, the layer cascade, the reservation -- never on a HUD label and
    /// never on a tutorial timer. The long real durations (Command 60s/120s,
    /// Laser 20s/45s, Plasma 30s/60s, Sensor 60s/120s, Platform 75s/150s) are
    /// advanced through the production tick seams
    /// <see cref="StructureRuntime.Tick"/> and
    /// <see cref="StructureUpgradeJob.Tick"/>; not one constant is touched.
    /// </para>
    /// </summary>
    public static class ChapterGSteps
    {
        private const string WeaponCoordinationId = "defense.weapon_coordination";

        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return Step("G-01", "Yapı yuvasını seç",
                "Destek yuvası seçimini değiştir ve seçilen yuvanın boş olduğunu doğrula.",
                "Komuta, savunma ve destek yuvalarını ayırt et.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Extraction == null || site.Profile == null) return false;
                        if (site.Extraction.SelectedSupportNodeIndex != 1) return false;
                        SurfaceNode node = site.Profile.SupportNode(1);
                        return !ExtractionState.IsNodeOccupied(site.WorldId, node.Id)
                               && node.Id == site.WorldId + "/node/" + SurfaceSlot.Support(1);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile != null) site.Profile.Scanned = true;
                        // Selection starts on node 0; moving it is the step.
                        if (site.Extraction != null && site.Extraction.SelectedSupportNodeIndex != 0)
                            site.Extraction.ToggleSupportNodeSelection();
                    },
                    reject: _ =>
                    {
                        if (site.Profile == null) return null;
                        return ExtractionState.IsNodeOccupied(site.WorldId, site.Profile.CommandNode.Id)
                            ? "KOMUTA YUVASI DOLU" : null;
                    }),
                checklist: new[] { "Komuta, savunma ve destek yuvaları ayırt edilsin", "Seçilen yuvanın boş olduğu görülsün" });

            // The instruction used to name the K key and nothing else, which is
            // exactly the gap this slice closes: the key was the only way in, it was
            // on no screen anywhere, and a menu being open switched it off. The card
            // is taught first and the key is offered as the shortcut it is.
            yield return Step("G-02", "Komuta merkezi kur",
                "Sağdaki YÜZEY İNŞASI kartında istenen gezegen malzemesini oku, sonra KUR · T1 düğmesine bas "
                + "(kısayol: komuta tuşu). Kapsülün yola çıktığını kartta gör.",
                "Yüzey İnşası kartından komuta merkezi kapsülünü yola çıkar.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                        if (command == null || command.Runtime.State != DrillLifecycle.CapsuleInTransit) return false;
                        return Mathf.Approximately(
                            site.Cargo.GetOwnerReservation(command.BuildOwnerId, ProcessingCatalog.BasicPartId),
                            SurfaceStructureCatalog.Command.PartsCost);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.Profile.Scanned = true;
                        site.GrantAll(TutorialSite.BuildCost(SurfaceStructureCatalog.Command, site.Profile.Family));
                        TutorialSite.ForgetAwards(ProgressionEvents.CommandFirst(site.WorldId));
                    },
                    reject: _ =>
                    {
                        if (site.Profile == null) return null;
                        if (ExtractionState.IsNodeOccupied(site.WorldId, site.Profile.CommandNode.Id))
                            return "KOMUTA YUVASI DOLU";
                        return site.Cargo.GetAvailable(ProcessingCatalog.BasicPartId)
                               < SurfaceStructureCatalog.Command.PartsCost
                            ? "30 TEMEL YAPI PARÇASI GEREKLİ" : null;
                    }),
                checklist: new[]
                {
                    "YÜZEY İNŞASI kartını bul",
                    "Kartta bu gezegenin doğru işlenmiş malzemesini oku",
                    "KUR · T1 düğmesini kullan",
                    "30 parça ve 10 gezegen malzemesi ayrılsın",
                    "Kapsül yola çıksın ve kart KAPSÜL YOLDA yazsın"
                },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.command.t1", "the Command Center body")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.CommandKey));

            yield return Step("G-03", "Komuta merkezini T2'ye yükselt",
                "Aynı YÜZEY İNŞASI kartında düğme YÜKSELT · T2 olur. Eksik şart varsa kart bunu yazar; "
                + "şartlar hazırken düğmeye bas.",
                "Komuta T2 yükseltmesini başlat; yapı çalışmayı sürdürsün.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                        StructureUpgradeJob job = command != null ? command.ActiveUpgradeJob : null;
                        return job != null && job.TargetTier == 2
                               && Mathf.Approximately(job.TotalSeconds, PlanetaryDefenseCatalog.CommandT2.BuildSeconds)
                               && command.Runtime.State == DrillLifecycle.Active;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode);
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.CommandT2.RequiredCharacterLevel);
                        ExtractionState.GetDominance(site.WorldId).Restore(DominanceState.Controlled, 0f, 0f);
                        site.GrantAll(PlanetaryDefenseCatalog.CommandT2.FullCost());
                    },
                    reject: _ => RejectFor(site, SurfaceStructureType.Command)),
                checklist: new[]
                {
                    "Kartta düğmenin YÜKSELT · T2 olduğunu gör",
                    "Seviye 21 sağlansın",
                    "En az bir kontrol edilen gezegen olsun",
                    "Yapı çalışmaya devam etsin"
                },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.command.t2", "the upgraded Command body")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.CommandKey));

            yield return Step("G-04", "Lazer savunması kur",
                "Savunma tuşuna basarak ilk savunma yuvasına lazer kur.",
                "İlk savunma yapısını kur.",
                site => TutorialSteps.Poll(
                    complete: _ => ExtractionState.ActiveDefenseCount(site.WorldId) == 1
                                   && SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Laser),
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode);
                        site.GrantAll(TutorialSite.BuildCost(SurfaceStructureCatalog.Laser, site.Profile.Family));
                        TutorialSite.ForgetAwards(ProgressionEvents.DefenseInnerFirst(site.WorldId));
                    },
                    reject: _ =>
                    {
                        if (!ExtractionState.HasActiveCommand(site.WorldId)) return "AKTİF BİR KOMUTA MERKEZİ GEREKLİ";
                        return HasAnyMaterial(site, SurfaceStructureCatalog.Laser.MaterialCost)
                            ? null : "5 BİRİM HERHANGİ BİR MALZEME GEREKLİ";
                    }),
                checklist: new[] { "Aktif komuta merkezi bulunsun", "15 parça ve 5 malzeme ayrılsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.defense.laser.t1", "the laser emplacement")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.LaserKey));

            yield return Step("G-05", "Lazeri T2'ye yükselt",
                "Komuta T2 ve Silah Koordinasyonu T2 hazırken lazeri yükselt.",
                "Lazeri T2'ye çıkar; dayanıklılık oranları korunsun.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure laser = site.FindStructure(SurfaceStructureType.Laser);
                        if (laser == null || laser.Runtime.Tier != 2) return false;
                        Durability durability = laser.Runtime.Durability;
                        return Mathf.Approximately(durability.ShieldMax, PlanetaryDefenseCatalog.LaserT2.ShieldMax)
                               && Mathf.Approximately(durability.ArmorMax, PlanetaryDefenseCatalog.LaserT2.ArmorMax)
                               && Mathf.Approximately(durability.HullMax, PlanetaryDefenseCatalog.LaserT2.HullMax);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        site.InstallStructure(SurfaceStructureCatalog.Laser, site.Profile.DefenseNode(0));
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.LaserT2.RequiredCharacterLevel);
                        site.ModuleTiers.SetTier(WeaponCoordinationId, 2);
                        site.GrantAll(PlanetaryDefenseCatalog.LaserT2.FullCost());
                    },
                    reject: _ => RejectFor(site, SurfaceStructureType.Laser)),
                checklist: new[] { "Komuta T2 şartı sağlansın", "Silah Koordinasyonu T2 şartı sağlansın", "Maliyet ayrılsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.defense.laser.t2", "the T2 laser body")));

            yield return Step("G-06", "Plazma savunması kur",
                "İkinci savunma yuvasını seç ve plazma savunmasını kur.",
                "İkinci savunma yuvasına plazma kur.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (site.Profile == null) return false;
                        SurfaceNode node = site.Profile.DefenseNode(1);
                        SurfaceStructure plasma = null;
                        foreach (SurfaceStructure candidate in ExtractionState.Structures)
                            if (candidate != null && candidate.Type == SurfaceStructureType.Plasma
                                && candidate.NodeId == node.Id) plasma = candidate;
                        if (plasma == null) return false;
                        return PlanetaryDefenseCatalog.PlasmaT1.RequiredCharacterLevel == 30
                               && PlanetaryDefenseCatalog.PlasmaT1.RequiredCommandTier == 2
                               && ExtractionState.ActiveDefenseCount(site.WorldId) == 2;
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        site.InstallStructure(SurfaceStructureCatalog.Laser, site.Profile.DefenseNode(0));
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.PlasmaT1.RequiredCharacterLevel);
                        site.ModuleTiers.SetTier(WeaponCoordinationId, 1);
                        site.GrantAll(PlanetaryDefenseCatalog.PlasmaT1.FullCost());
                    },
                    reject: _ =>
                    {
                        if (ProgressionService.State.Level < PlanetaryDefenseCatalog.PlasmaT1.RequiredCharacterLevel)
                            return "SEVİYE 30 GEREKLİ";
                        // Read from ExtractionState, never ShipExtraction.CommandTierOnTarget():
                        // that one dereferences TargetPlanet with no null guard, and a step
                        // may be evaluated long before proximity has resolved a target.
                        if (CommandTier(site) < 2) return "KOMUTA MERKEZİ T2 GEREKLİ";
                        return site.ModuleTiers.GetTier(WeaponCoordinationId) < 1
                            ? "SİLAH KOORDİNASYONU T1 GEREKLİ" : null;
                    }),
                checklist: new[] { "Yuva 1 seçilsin", "Seviye 30, Komuta T2 ve Silah Koordinasyonu T1 sağlansın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.defense.plasma.t1", "the plasma emplacement")),
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.LaserKey));

            yield return Step("G-07", "Plazmayı T2'ye yükselt",
                "Silah Koordinasyonu T2 hazırken plazma savunmasını yükselt.",
                "Plazmayı T2'ye çıkar; maliyet tam olarak bir kez tüketilsin.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure plasma = site.FindStructure(SurfaceStructureType.Plasma);
                        if (plasma == null || plasma.Runtime.Tier != 2) return false;
                        Durability durability = plasma.Runtime.Durability;
                        return Mathf.Approximately(durability.ArmorMax, PlanetaryDefenseCatalog.PlasmaT2.ArmorMax)
                               && Mathf.Approximately(durability.HullMax, PlanetaryDefenseCatalog.PlasmaT2.HullMax)
                               && Mathf.Approximately(site.Cargo.GetOwnerReservation(
                                   StructureUpgradeService.OwnerId(plasma), ProcessingCatalog.BasicPartId), 0f);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        site.InstallStructure(SurfaceStructureCatalog.Plasma, site.Profile.DefenseNode(1));
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.PlasmaT2.RequiredCharacterLevel);
                        site.ModuleTiers.SetTier(WeaponCoordinationId, 2);
                        site.GrantAll(PlanetaryDefenseCatalog.PlasmaT2.FullCost());
                    },
                    reject: _ => RejectFor(site, SurfaceStructureType.Plasma)),
                checklist: new[] { "Maliyet ayrılsın", "Yapı aktif kalsın", "Kademe 2 olsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.defense.plasma.t2", "the T2 plasma body")));

            yield return Step("G-08", "Sensör dizisi kur",
                "Destek panelinden sensör dizisini seç ve inşa et.",
                "Sensör dizisini kur.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure sensor = site.FindStructure(SurfaceStructureType.SensorArray);
                        return sensor != null
                               && (sensor.Runtime.State == DrillLifecycle.CapsuleInTransit
                                   || sensor.Runtime.State == DrillLifecycle.Constructing
                                   || sensor.Runtime.State == DrillLifecycle.Active);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.Profile.Scanned = true;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.SensorArrayT1.RequiredCharacterLevel);
                        site.GrantAll(PlanetaryDefenseCatalog.SensorArrayT1.FullCost());
                        if (site.Extraction != null && site.Extraction.SelectedSupportNodeIndex != 0)
                            site.Extraction.ToggleSupportNodeSelection();
                    },
                    reject: _ =>
                    {
                        if (CommandTier(site) < PlanetaryDefenseCatalog.SensorArrayT1.RequiredCommandTier)
                            return "KOMUTA T2 GEREKLİ";
                        if (ProgressionService.State.Level < PlanetaryDefenseCatalog.SensorArrayT1.RequiredCharacterLevel)
                            return "SEVİYE 21 GEREKLİ";
                        return site.Extraction != null && !site.Extraction.HasForeignEntityNearby()
                            ? "YABANCI VARLIK GEREKLİ" : null;
                    }),
                checklist: new[] { "Seviye 21 sağlansın", "Komuta T2 sağlansın", "Yakında yabancı varlık bulunsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.support.sensor_array.t1", "the sensor array")));

            yield return Step("G-09", "Sensör dizisini T2'ye yükselt",
                "Sensör dizisini T2'ye yükselt ve uyarı süresi kazancının arttığını gör.",
                "Sensör dizisini T2'ye çıkar; uyarı bonusu artsın.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure sensor = site.FindStructure(SurfaceStructureType.SensorArray);
                        return sensor != null && sensor.Runtime.Tier == 2
                               && Mathf.Approximately(PlanetaryDefenseCatalog.SensorArrayT2.WarningBonusSeconds, 35f)
                               && Mathf.Approximately(PlanetaryDefenseCatalog.SensorArrayT1.WarningBonusSeconds, 20f);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        site.InstallStructure(SurfaceStructureCatalog.SensorArray, site.Profile.SupportNode(0));
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.SensorArrayT2.RequiredCharacterLevel);
                        site.GrantAll(PlanetaryDefenseCatalog.SensorArrayT2.FullCost());
                    },
                    reject: _ => RejectFor(site, SurfaceStructureType.SensorArray)),
                checklist: new[] { "Kademe 2 olsun", "Uyarı bonusu 35 saniyeye çıksın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.support.sensor_array.t2", "the T2 sensor array")));

            yield return Step("G-10", "Bakım platformu kur",
                "Sistemde bir mühendis varken bakım platformunu kur.",
                "Bakım platformunu kur.",
                site => TutorialSteps.Poll(
                    complete: _ => site.FindStructure(SurfaceStructureType.MaintenancePlatform) != null,
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.Profile.Scanned = true;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.MaintenancePlatformT1.RequiredCharacterLevel);
                        site.GrantAll(PlanetaryDefenseCatalog.MaintenancePlatformT1.FullCost());
                        if (site.Extraction != null && site.Extraction.SelectedSupportNodeIndex != 1)
                            site.Extraction.ToggleSupportNodeSelection();
                    },
                    reject: _ =>
                    {
                        if (ProgressionService.State.Level < PlanetaryDefenseCatalog.MaintenancePlatformT1.RequiredCharacterLevel)
                            return "SEVİYE 25 GEREKLİ";
                        if (CommandTier(site) < PlanetaryDefenseCatalog.MaintenancePlatformT1.RequiredCommandTier)
                            return "KOMUTA T2 GEREKLİ";
                        return site.Extraction != null && !site.Extraction.HasEngineerInSystem()
                            ? "MÜHENDİS GEREKLİ" : null;
                    }),
                checklist: new[] { "Seviye 25 sağlansın", "Komuta T2 sağlansın", "Sistemde mühendis bulunsun" },
                assets: TutorialAssets.Of(
                    TutorialAssets.Visual("structure.support.maintenance_platform.t1", "the maintenance platform"),
                    TutorialAssets.Visual("vehicle.engineer.mk1", "the Engineer the gate reads")));

            yield return Step("G-11", "Bakım platformunu T2'ye yükselt",
                "Bakım platformunu T2'ye yükselt ve onarım hızının arttığını gör.",
                "Platformu T2'ye çıkar; onarım çarpanı artsın.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure platform = site.FindStructure(SurfaceStructureType.MaintenancePlatform);
                        return platform != null && platform.Runtime.Tier == 2
                               && Mathf.Approximately(
                                   PlanetaryDefenseCatalog.MaintenancePlatformT2.EngineerRepairMultiplier, 1.50f)
                               && Mathf.Approximately(
                                   PlanetaryDefenseCatalog.MaintenancePlatformT2.RecoveryTimeMultiplier, 0.70f);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        site.InstallStructure(SurfaceStructureCatalog.Command, site.Profile.CommandNode, tier: 2);
                        site.InstallStructure(SurfaceStructureCatalog.MaintenancePlatform, site.Profile.SupportNode(1));
                        TutorialSite.SetLevel(PlanetaryDefenseCatalog.MaintenancePlatformT2.RequiredCharacterLevel);
                        site.GrantAll(PlanetaryDefenseCatalog.MaintenancePlatformT2.FullCost());
                    },
                    reject: _ => RejectFor(site, SurfaceStructureType.MaintenancePlatform)),
                checklist: new[] { "Kademe 2 olsun", "Onarım çarpanı 1.50'ye çıksın", "Kurtarma süresi çarpanı 0.70'e insin" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.support.maintenance_platform.t2",
                    "the T2 maintenance platform")));

            yield return Step("G-12", "Yapı inşasını iptal et",
                "Henüz tamamlanmamış bir yapıyı iptal et ve her kalemden %75 iadeyi gör.",
                "İnşaatı iptal et; her kalem ayrı ayrı iade edilsin.",
                site =>
                {
                    var expected = new Dictionary<string, float>();
                    string ownerId = null;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            if (ownerId == null || expected.Count == 0 || site.Cargo == null) return false;
                            if (site.FindStructure(SurfaceStructureType.Command) != null) return false;
                            foreach (KeyValuePair<string, float> line in expected)
                            {
                                if (!Mathf.Approximately(site.Cargo.Get(line.Key), line.Value)) return false;
                                if (site.Cargo.GetOwnerReservation(ownerId, line.Key) > 0.0001f) return false;
                            }
                            return true;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ClearStructures();
                            if (site.Profile == null) return;
                            site.Profile.Scanned = true;
                            List<(string itemId, float amount)> cost =
                                TutorialSite.BuildCost(SurfaceStructureCatalog.Command, site.Profile.Family);
                            site.GrantAll(cost);
                            SurfaceStructure built = site.BuildStructure(SurfaceStructureCatalog.Command,
                                site.Profile.CommandNode);
                            if (built == null) return;
                            ownerId = built.BuildOwnerId;
                            expected.Clear();
                            for (int i = 0; i < cost.Count; i++)
                            {
                                float held = site.Cargo.Get(cost[i].itemId);
                                expected[cost[i].itemId] =
                                    held - cost[i].amount
                                    + Mathf.Floor(cost[i].amount * SurfaceStructureCatalog.RefundFraction);
                            }
                        },
                        reject: _ => site.FindStructure(SurfaceStructureType.Command) == null
                            ? null : "İNŞA İPTAL — %75 İADE");
                },
                checklist: new[] { "Yapı yok olsun", "Parça iadesi gelsin", "Her malzeme kalemi ayrı ayrı iade edilsin" },
                keyHint: () => UI.Controls.NebulaControlBindings.KeyLabel(ShipExtraction.ExitKey));

            yield return Step("G-13", "Kalkan, zırh ve gövde katmanlarını oku",
                "Bir yapıya kalkanından fazla hasar ver ve hasarın katman katman indiğini gör.",
                "Kalkan, zırh ve gövde sıralamasını gör.",
                site =>
                {
                    float armorBaseline = -1f, hullBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            SurfaceStructure laser = site.FindStructure(SurfaceStructureType.Laser);
                            if (laser == null || armorBaseline < 0f) return false;
                            Durability durability = laser.Runtime.Durability;
                            return Mathf.Approximately(durability.Shield, 0f)
                                   && durability.Armor < armorBaseline - 0.0001f
                                   && durability.Armor > 0.0001f
                                   && Mathf.Approximately(durability.Hull, hullBaseline);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ClearStructures();
                            if (site.Profile == null) return;
                            SurfaceStructure laser = site.InstallStructure(SurfaceStructureCatalog.Laser,
                                site.Profile.DefenseNode(0));
                            if (laser == null) return;
                            // A freshly built structure has no shield and half its plating;
                            // the reading is only meaningful from a full one.
                            Durability durability = laser.Runtime.Durability;
                            durability.RestoreFromSave(durability.ShieldMax, durability.ArmorMax, durability.HullMax, 0f);
                            armorBaseline = durability.Armor;
                            hullBaseline = durability.Hull;
                        });
                },
                checklist: new[] { "Kalkan önce insin", "Taşan hasar zırha geçsin", "Gövde el değmemiş kalsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.state.structure.damage_disabled",
                    "the damaged-structure overlay")));

            yield return Step("G-14", "Hasarlı, devre dışı ve onarım durumlarını oku",
                "Gövdesi biten yapının yok olmadığını, devre dışı kaldığını ve onarılabildiğini gör.",
                "Devre dışı kalma ve onarımla geri dönüşü gör.",
                site =>
                {
                    bool sawDisabled = false;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            SurfaceStructure laser = site.FindStructure(SurfaceStructureType.Laser);
                            if (laser == null) return false;
                            if (laser.Runtime.State == DrillLifecycle.Disabled) sawDisabled = true;
                            return sawDisabled && laser.Runtime.State == DrillLifecycle.Active
                                   && !laser.Runtime.Durability.Disabled;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ClearStructures();
                            sawDisabled = false;
                            if (site.Profile == null) return;
                            SurfaceStructure laser = site.InstallStructure(SurfaceStructureCatalog.Laser,
                                site.Profile.DefenseNode(0));
                            Durability durability = laser != null ? laser.Runtime.Durability : null;
                            durability?.RestoreFromSave(durability.ShieldMax, durability.ArmorMax, durability.HullMax, 0f);
                        },
                        reject: _ =>
                        {
                            SurfaceStructure laser = site.FindStructure(SurfaceStructureType.Laser);
                            return laser != null && laser.Runtime.State == DrillLifecycle.Disabled
                                ? TutorialTr.Engineer(ShipHangar.EngineerSendRejectReason.NothingToRepair) : null;
                        });
                },
                checklist: new[] { "Gövde sıfırlanınca yapı devre dışı olsun", "Yapı yok olmasın", "Onarım sonrası yeniden aktif olsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("visual.state.structure.repair_upgrade",
                    "the repair overlay")));

            yield return Step("G-15", "Bakım rezervini oku",
                "Komuta merkezini T2'ye çıkar ve bakım rezervi kapasitesinin arttığını oku.",
                "Rezerv kapasitesinin komuta kademesine bağlı olduğunu gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                        MaintenanceReserve reserve = command != null ? command.MaintenanceReserve : null;
                        return reserve != null && command.Runtime.Tier >= 2
                               && Mathf.Approximately(reserve.Cargo.Capacity, MaintenanceReserve.Tier2Capacity);
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        SurfaceStructure command = site.InstallStructure(SurfaceStructureCatalog.Command,
                            site.Profile.CommandNode);
                        command?.MaintenanceReserve?.ApplyTier(1);
                    },
                    reject: _ => site.ReserveAccess != null && !site.ReserveAccess.InRange
                        ? "KOMUTA MERKEZİNE YAKLAŞIN" : null),
                checklist: new[] { "Rezerv paneli açılsın", "Doluluk ve boş alan okunsun", "Kapasite komuta kademesine bağlı olsun" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("structure.command.t2", "the T2 Command Center")));

            yield return Step("G-16", "Rezerve fiziksel olarak yükle ve al",
                "Rezerve on birim malzeme yükle, sonra geri al.",
                "Rezerve yükleme ve rezervden almayı yap.",
                site =>
                {
                    string itemId = ProcessingCatalog.MaterialIds[0];
                    float shipBaseline = -1f, shipLowWater = float.MaxValue;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                            MaintenanceReserve reserve = command != null ? command.MaintenanceReserve : null;
                            if (reserve == null || site.Cargo == null || shipBaseline < 0f) return false;
                            float inReserve = reserve.Cargo.GetStored(itemId);
                            float onShip = site.Cargo.Get(itemId);
                            // The outbound leg is the only thing that lowers the ship's own
                            // holding; the return leg is the only thing that raises it again.
                            if (inReserve > 0.0001f && onShip < shipLowWater) shipLowWater = onShip;
                            return shipLowWater < shipBaseline - 0.0001f
                                   && onShip > shipLowWater + 0.0001f
                                   && inReserve > 0.0001f
                                   && Mathf.Approximately(inReserve + onShip, shipBaseline);
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ClearStructures();
                            shipLowWater = float.MaxValue;
                            if (site.Profile == null) return;
                            SurfaceStructure command = site.InstallStructure(SurfaceStructureCatalog.Command,
                                site.Profile.CommandNode);
                            command?.MaintenanceReserve?.Cargo.Clear();
                            site.Strip(itemId);
                            site.Grant(itemId, ShipMaintenanceReserveAccess.DepositWithdrawIncrement * 2f);
                            shipBaseline = site.Cargo.Get(itemId);
                        },
                        reject: _ => site.ReserveAccess != null && !site.ReserveAccess.InRange
                            ? TutorialTr.Reserve(ReserveTransferRejectReason.NotInRange) : null);
                },
                checklist: new[] { "Gemi kargosu azalsın", "Rezerv artsın", "Ters yönde de çalışsın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("material.metal_alloy", "a whitelisted reserve item")));

            yield return Step("G-17", "Bakım politikasını seç",
                "Komuta merkezinin bakım politikasını Acil olarak ayarla.",
                "Bakım politikasını değiştir ve hedef sırasını gör.",
                site => TutorialSteps.Poll(
                    complete: _ =>
                    {
                        SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                        MaintenanceReserve reserve = command != null ? command.MaintenanceReserve : null;
                        if (reserve == null || reserve.Policy != MaintenancePolicy.Emergency) return false;
                        var defenses = new List<string> { site.WorldId + "/defense" };
                        return MaintenancePolicyResolver.ResolveEmergencyTarget(command.TargetId, true, defenses)
                                   == command.TargetId
                               && MaintenancePolicyResolver.ResolveEmergencyTarget(command.TargetId, false, defenses)
                                   == defenses[0];
                    },
                    prepare: ctx =>
                    {
                        site.Open(ctx);
                        site.ClearStructures();
                        if (site.Profile == null) return;
                        SurfaceStructure command = site.InstallStructure(SurfaceStructureCatalog.Command,
                            site.Profile.CommandNode);
                        command?.MaintenanceReserve?.SetPolicy(MaintenancePolicy.Manual);
                    },
                    reject: _ => "BU SİSTEMLE BAĞLANTI YOK — KOMUTA AĞI DIŞINDA"),
                checklist: new[] { "Politika değişsin", "Otomatik hedef sırası buna göre kurulsun" });

            yield return Step("G-18", "Bakım ikmali gönder",
                "Collector Mk II ile komuta merkezinin bakım rezervine malzeme gönder.",
                "İkmal görevini başlat ve tamamla.",
                site =>
                {
                    string itemId = ProcessingCatalog.MaterialIds[0];
                    float reserveBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            MaintenanceResupplyMission mission = site.FindResupply();
                            SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
                            MaintenanceReserve reserve = command != null ? command.MaintenanceReserve : null;
                            if (mission == null || reserve == null || reserveBaseline < 0f) return false;
                            return mission.Phase == MaintenanceResupplyPhase.Completed
                                   && reserve.Cargo.GetStored(itemId) > reserveBaseline + 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            site.ClearStructures();
                            TutorialSite.PendingResupply = null;
                            if (site.Profile == null) return;
                            SurfaceStructure command = site.InstallStructure(SurfaceStructureCatalog.Command,
                                site.Profile.CommandNode);
                            command?.MaintenanceReserve?.Cargo.Clear();
                            site.Strip(itemId);
                            site.Grant(itemId, 20f);
                            reserveBaseline = command != null && command.MaintenanceReserve != null
                                ? command.MaintenanceReserve.Cargo.GetStored(itemId) : 0f;
                        },
                        reject: _ =>
                        {
                            MaintenanceResupplyMission mission = site.FindResupply();
                            return mission != null && mission.Phase == MaintenanceResupplyPhase.Cancelled
                                ? TutorialTr.Resupply(ShipHangar.MaintenanceResupplyRejectReason.VehicleNotEligible)
                                : null;
                        });
                },
                checklist: new[] { "Araç yüklensin", "Rezerve gitsin", "Boşaltsın", "Hangara dönsün" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("vehicle.collector.mk2", "the Mk II collector")));

            yield return Step("G-19", "Devre dışı merkezin rotaları durdurduğunu gör",
                "Lojistik merkezi devre dışı kaldığında rotaların durakladığını ve yükün kaybolmadığını gör.",
                "Merkez devre dışı kalınca rotaların duraklamasını gör.",
                site =>
                {
                    float drillBaseline = -1f;
                    return TutorialSteps.Poll(
                        complete: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            DrillStructure drill = site.FindDrill();
                            if (center == null || drill == null || drillBaseline < 0f) return false;
                            if (center.Runtime.State != LogisticsCenterState.Disabled) return false;
                            bool anyRoute = false;
                            foreach (CollectorRoute route in LogisticsState.Routes)
                            {
                                if (route == null || route.SystemId != site.SystemId) continue;
                                anyRoute = true;
                                if (!route.Paused) return false;
                                if (route.HeldAmount > 0.0001f) return false;
                            }
                            return anyRoute && drill.Runtime.StoredAmount > drillBaseline + 0.0001f;
                        },
                        prepare: ctx =>
                        {
                            site.Open(ctx);
                            if (site.Profile == null) return;
                            DrillStructure drill = site.InstallDrill(DrillLifecycle.Active, stored: 5f);
                            site.InstallCenter(LogisticsCenterState.Active);
                            CollectorRoute existing = LogisticsState.FindRouteByDrill(site.WorldId);
                            if (existing != null) LogisticsState.RemoveRoute(existing);
                            var route = new CollectorRoute(LogisticsState.NextRouteId(), site.SystemId,
                                site.WorldId, "tutorial-collector", RoutePriority.Normal);
                            route.SetPhase(RoutePhase.ToCenter);
                            route.SetHeld(ExtractionCatalog.ResourceIds[(int)site.Profile.Family], 6f);
                            LogisticsState.AddRoute(route);
                            ctx.Scope.OnCleanup(() => LogisticsState.RemoveRoute(route));
                            drillBaseline = drill != null ? drill.Runtime.StoredAmount : 0f;
                        },
                        reject: _ =>
                        {
                            LogisticsCenter center = LogisticsState.FindCenter(site.SystemId);
                            return center != null && center.Runtime.IsDisabled ? "MERKEZ DEVRE DIŞI" : null;
                        });
                },
                checklist: new[] { "Merkez devre dışı olsun", "Rotalar duraklasın", "Yüklü toplayıcı yükünü sondaja geri bıraksın" },
                assets: TutorialAssets.Of(TutorialAssets.Visual("station.system_logistics.t1", "the disabled center")));
        }

        // ---- shared helpers ---------------------------------------------------

        private static int CommandTier(TutorialSite site)
        {
            SurfaceStructure command = site.FindStructure(SurfaceStructureType.Command);
            return command != null && command.Runtime.IsActive ? command.Runtime.Tier : 0;
        }

        private static bool HasAnyMaterial(TutorialSite site, int amount)
        {
            if (site.Cargo == null) return false;
            for (int i = 0; i < ProcessingCatalog.MaterialIds.Length; i++)
                if (site.Cargo.GetAvailable(ProcessingCatalog.MaterialIds[i]) >= amount - 0.0001f) return true;
            return false;
        }

        /// <summary>The real upgrade gate's answer for this structure, in Turkish.</summary>
        private static string RejectFor(TutorialSite site, SurfaceStructureType type)
        {
            SurfaceStructure structure = site.FindStructure(type);
            if (structure == null || structure.ActiveUpgradeJob != null) return null;
            StructureUpgradeRejectReason reason = StructureUpgradeService.CheckStart(structure,
                ProgressionService.State.Level, CommandTier(site),
                site.ModuleTiers.GetTier(WeaponCoordinationId),
                ExtractionState.HasAnyControlledPlanet(), site.Cargo, out _);
            return TutorialTr.Structure(reason);
        }

        private static TutorialStepDefinition Step(string id, string title, string instruction, string summary,
            Func<TutorialSite, ITutorialStepRuntime> build, string[] checklist = null,
            TutorialAssetRequirement[] assets = null, Func<string> keyHint = null) =>
            new TutorialStepDefinition(TutorialChapterId.Structures, id, title, instruction, summary,
                () => build(new TutorialSite()), checklist, assets, keyHint);
    }
}
