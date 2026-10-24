using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Ship outfitting at yards (U.SPC-T65): a hull's hardpoints come from its design (weapon mounts, shield and
    /// engine mounts) and its facilities fill its internal space (U.SPC-T470); a yard fits guns, hardened shields and
    /// torch drives onto the hardpoints and armour plate, cargo racks, scanners and brigs into the free space, buying
    /// the materials from its own market at its prices; and the loadout
    /// rebuilds the hull's CombatSpec, carrying its damage and people over. Navies and raiders upgrade their own
    /// warships while they refit, and prisoners taken into a brig are handed over in port for bounty.
    /// </summary>
    [TestFixture]
    public class OutfittingTests
    {
        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Raiders = new Faction("Black Banner", "", new Rgb(1, 0, 0)) { IsPirate = true };

        static (Economy economy, Settlement yard) Yard(Faction owner, double stock = 1e6)
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, owner, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions }) yard.Stock[(int)g] = stock;
            yard.RefreshPrices();
            return (economy, yard);
        }

        static Ship Hull(ShipClass c, Faction f, double treasury = 1e12)
        {
            f.Treasury = treasury;
            var ship = new Ship { Id = 1, Name = c.ToString(), Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
            if (!ship.Spec.IsMilitary) ship.CapacityTonnes = Economy.CapacityOf(c);
            return ship;
        }

        static int SlotOf(Ship ship, MountType type, Func<OutfitSlot, bool> also = null)
        {
            var l = Outfitting.LoadoutOf(ship);
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Type == type && l.Fitted[i] != ModuleKind.Railgun && (also == null || also(l.Slots[i]))) return i;
            return -1;
        }

        static OutfitOutcome Fit(Economy e, Ship ship, Settlement yard, int slot, ModuleKind? m) => Outfitting.Fit(e, ship, yard, slot, m, out _);

        /// <summary>Fits a facility module (plate, racks, a scanner, a brig) into the hull's free space.</summary>
        static OutfitOutcome FitIn(Economy e, Ship ship, Settlement yard, ModuleKind m) => Outfitting.FitFacility(e, ship, yard, FacilityKinds.ForModule(m), out _);

        /// <summary>Takes the first fitted facility that is <paramref name="m"/> out again.</summary>
        static OutfitOutcome StripOut(Economy e, Ship ship, Settlement yard, ModuleKind m) =>
            Outfitting.StripFacility(e, ship, yard, Outfitting.LoadoutOf(ship).Facilities.FindIndex(f => f.Def.Module == m), out _);

        // ---------------------------------------------------------------- slots

        [Test]
        public void AHullsSlotsAreItsRefittableHardpointsAndItsFacilitiesFillItsInternalSpace()
        {
            var ship = Hull(ShipClass.Cruiser, Navy);
            var l = Outfitting.LoadoutOf(ship);
            var spec = CombatSpec.Of(ShipClass.Cruiser);
            Assert.That(l.Slots.Count(s => s.Type == MountType.Shield), Is.EqualTo(spec.Layout.Count(d => d.Kind == SubsystemKind.ShieldGenerator)));
            Assert.That(l.Slots.Count(s => s.Type == MountType.Engine), Is.EqualTo(spec.Layout.Count(d => d.Kind == SubsystemKind.Engine)));
            Assert.That(l.Slots.Count(s => s.Type == MountType.Energy && s.Size == HardpointSize.Small), Is.EqualTo(spec.Layout.Count(d => d.Kind == SubsystemKind.PointDefense)));
            Assert.That(l.Facilities, Is.EqualTo(FacilityLayouts.For(ShipClass.Cruiser)), "the design's own facilities");
            Assert.That(l.FreeSpace, Is.EqualTo(FacilityLayouts.SpareSpace(ShipClass.Cruiser)), "and the free space beyond them, as many sections as it had bays");
            Assert.That(l.Slots.Any(s => s.LayoutIndex >= 0 && (spec.Layout[s.LayoutIndex].Kind == SubsystemKind.Bridge || spec.Layout[s.LayoutIndex].Kind == SubsystemKind.Reactor
                || spec.Layout[s.LayoutIndex].Kind == SubsystemKind.BoardingPods)), Is.False, "bridge, reactor and pod launcher are part of the hull");
            Assert.That(l.IsStock, Is.True);
            Assert.That(l.Build(), Is.SameAs(spec), "a stock loadout is the design as built");
        }

        [Test]
        public void ModulesGoOnlyWhereTheyFit()
        {
            var ship = Hull(ShipClass.Battleship, Navy);
            var l = Outfitting.LoadoutOf(ship);
            int light = SlotOf(ship, MountType.Energy, s => s.Size == HardpointSize.Small);
            int broadside = SlotOf(ship, MountType.Energy, s => !s.BearsOnBow && s.Size == HardpointSize.Large);
            int bow = SlotOf(ship, MountType.Energy, s => s.BearsOnBow && s.Size == HardpointSize.Large);
            int launcher = SlotOf(ship, MountType.Missile, s => !s.Spinal);
            int tube = SlotOf(ship, MountType.Missile, s => s.Stock == ModuleKind.TorpedoTube);
            Assert.That(Outfitting.CanFit(l, light, ModuleKind.LaserTurret), Is.True);
            Assert.That(Outfitting.CanFit(l, light, ModuleKind.HeavyLaser), Is.False, "a point-defence mount is too small for a heavy laser");
            Assert.That(Outfitting.CanFit(l, light, ModuleKind.Autocannon), Is.False, "a point-defence mount is an energy mount: it takes no gun");
            Assert.That(Outfitting.CanFit(l, broadside, ModuleKind.PlasmaCannon), Is.True, "a large energy mount takes any energy weapon its hull's role allows");
            Assert.That(Outfitting.CanFit(l, broadside, ModuleKind.TachyonLance), Is.False, "the lance is spinal: only a mount bearing on the bow takes one");
            Assert.That(Outfitting.CanFit(l, bow, ModuleKind.TachyonLance), Is.True);
            Assert.That(Outfitting.CanFit(l, bow, ModuleKind.Railgun), Is.False, "a railgun is ballistic: an energy mount takes none, however big");
            Assert.That(Outfitting.CanFit(l, bow, ModuleKind.MissileLauncher), Is.False, "nor a launcher");
            Assert.That(Outfitting.CanFit(l, launcher, ModuleKind.InterceptorMissile), Is.True, "a missile mount takes any launcher no bigger than itself");
            Assert.That(Outfitting.CanFit(l, launcher, ModuleKind.TorpedoTube), Is.False, "a tube is fixed: a broadside launcher mount takes none");
            Assert.That(Outfitting.CanFit(l, tube, ModuleKind.AntimatterTorpedo), Is.True);
            Assert.That(Outfitting.CanFit(l, tube, ModuleKind.HeavyLaser), Is.False);
            Assert.That(Outfitting.CanFit(l, SlotOf(ship, MountType.Shield), ModuleKind.ArmourPlating), Is.False);
            Assert.That(Outfitting.CanFit(l, light, ModuleKind.Scanner), Is.False, "a scanner suite goes inside the hull, not on a mount");
            Assert.That(Outfitting.CanFit(l, light, null), Is.False, "a hardpoint is never left empty");
            Assert.That(l.CanAdd(FacilityKinds.Of(FacilityKinds.Scanner)), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(l.CanAdd(FacilityKinds.Of(FacilityKinds.Bridge)), Is.EqualTo(OutfitOutcome.WontFit), "the hull's structure is not fitted");
            Assert.That(l.CanRemove(0), Is.False, "nor stripped");
        }

        // ---------------------------------------------------------------- buying from the yard's market

        [Test]
        public void FittingAGunBuysItsMaterialsFromTheYardAndChangesTheHardpoint()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Cruiser, Navy, 1e9);
            int slot = SlotOf(ship, MountType.Energy, s => s.Stock == ModuleKind.LaserTurret);
            double strength = ship.Combat.Strength;
            double alloys = yard.Stock[(int)Good.Alloys], electronics = yard.Stock[(int)Good.Electronics];
            var quote = Outfitting.Quote(ship, yard, slot, ModuleKind.HeavyLaser);
            Assert.That(quote.InStock, Is.True);
            Assert.That(quote.Materials, Is.GreaterThan(0));
            Assert.That(quote.TradeIn, Is.GreaterThan(0), "the laser taken out is sold back");

            Assert.That(Outfitting.Fit(economy, ship, yard, slot, ModuleKind.HeavyLaser, out var paid), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(paid.Net, Is.EqualTo(quote.Net).Within(1e-6));
            Assert.That(Navy.Treasury, Is.EqualTo(1e9 - paid.Net + paid.Labour + paid.Materials * Economy.PortTax / (1 + Economy.PortTax)).Within(paid.Net * 0.05),
                "the owner pays the yard (it owns the yard too, so labour and tax come back)");
            var heavy = ModuleSpec.Of(ModuleKind.HeavyLaser);
            var laser = ModuleSpec.Of(ModuleKind.LaserTurret);
            Assert.That(yard.Stock[(int)Good.Alloys], Is.EqualTo(alloys - heavy.Bill[0].tonnes + laser.Bill[0].tonnes * Outfitting.TradeInShare).Within(1e-6));
            Assert.That(yard.Stock[(int)Good.Electronics], Is.EqualTo(electronics - heavy.Bill[1].tonnes + laser.Bill[1].tonnes * Outfitting.TradeInShare).Within(1e-6));
            int index = Outfitting.LoadoutOf(ship).Slots[slot].LayoutIndex;
            Assert.That(ship.Combat.Spec.Layout[index].Kind, Is.EqualTo(SubsystemKind.HeavyLaser));
            Assert.That(ship.Combat.Subsystems[index].Def.Weapon, Is.SameAs(WeaponSpec.Heavy));
            Assert.That(ship.Combat.Strength, Is.GreaterThan(strength));
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.HeavyLaser), Is.EqualTo(OutfitOutcome.AlreadyFitted));
        }

        [Test]
        public void AYardShortOfAMaterialCannotFitWhatNeedsIt()
        {
            var (economy, yard) = Yard(Navy);
            yard.Stock[(int)Good.Electronics] = 5;
            var ship = Hull(ShipClass.Destroyer, Navy);
            int slot = SlotOf(ship, MountType.Shield);
            var before = ship.Combat;
            var quote = Outfitting.Quote(ship, yard, slot, ModuleKind.HardenedShield);
            Assert.That(quote.InStock, Is.False);
            Assert.That(quote.Short, Is.EqualTo(Good.Electronics));
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.HardenedShield), Is.EqualTo(OutfitOutcome.NoStock));
            Assert.That(ship.Combat, Is.SameAs(before));
            Assert.That(yard.Stock[(int)Good.Electronics], Is.EqualTo(5));
            Assert.That(FitIn(economy, ship, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.Ok), "plate needs no electronics");
        }

        [Test]
        public void PricesFollowTheYardsOwnMarket()
        {
            var (_, plenty) = Yard(Navy, 1e6);
            var (_, scarce) = Yard(Navy, 1e6);
            scarce.Demand[(int)Good.Electronics] = 50;
            plenty.Demand[(int)Good.Electronics] = 50;
            scarce.Stock[(int)Good.Electronics] = 400;
            scarce.RefreshPrices();
            plenty.RefreshPrices();
            var ship = Hull(ShipClass.Destroyer, Navy);
            var scanner = FacilityKinds.Of(FacilityKinds.Scanner);
            Assert.That(Outfitting.QuoteFacility(ship, scarce, scanner).Materials, Is.GreaterThan(Outfitting.QuoteFacility(ship, plenty, scanner).Materials * 1.5),
                "electronics are dear where they are short");
        }

        [Test]
        public void AnOwnerWhoCannotPayGetsNothing()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Destroyer, Navy, treasury: 1000);
            Assert.That(FitIn(economy, ship, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.CantAfford));
            Assert.That(Outfitting.LoadoutOf(ship).IsStock, Is.True);
        }

        [Test]
        public void OnlyAYardOutOfBattleAndOpenToTheShipRefitsIt()
        {
            var (economy, yard) = Yard(Navy);
            var colony = economy.Add("No yard", 3, SettlementKind.Colony, Navy, 0);
            var ship = Hull(ShipClass.Destroyer, Navy);
            Assert.That(FitIn(economy, ship, colony, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.NotAYard));
            var raider = Hull(ShipClass.Destroyer, Raiders);
            Assert.That(FitIn(economy, raider, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.CannotDock), "a navy yard does not refit raiders");
            ship.Battle = new Battle(1, 3, "test", DVec3.Zero, 0, 1);
            Assert.That(FitIn(economy, ship, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.Busy));
        }

        // ---------------------------------------------------------------- the loadout changes the hull

        [Test]
        public void HardenedShieldsRaiseTheShieldAndThatGeneratorsShare()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Destroyer, Navy);
            var stock = CombatSpec.Of(ShipClass.Destroyer);
            int slot = SlotOf(ship, MountType.Shield);
            int index = Outfitting.LoadoutOf(ship).Slots[slot].LayoutIndex;
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.HardenedShield), Is.EqualTo(OutfitOutcome.Ok));
            int gens = stock.Layout.Count(d => d.Kind == SubsystemKind.ShieldGenerator);
            Assert.That(ship.Combat.Spec.Shield, Is.EqualTo(stock.Shield / gens * (gens - 1 + Loadout.HardenedRating)).Within(1e-6));
            Assert.That(ship.Combat.Spec.ShieldRegen, Is.GreaterThan(stock.ShieldRegen));
            var hardened = ship.Combat.Subsystems[index];
            var other = ship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator && s != hardened);
            Assert.That(hardened.MaxShield, Is.EqualTo(other.MaxShield * Loadout.HardenedRating).Within(1e-6));
            Assert.That(ship.Combat.Shield, Is.EqualTo(ship.Combat.Spec.Shield).Within(1e-6), "a new generator comes up charged");
        }

        [Test]
        public void ArmourPlateAddsHullAndArmourAndMassThatCostsDeltaV()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Destroyer, Navy);
            var stock = CombatSpec.Of(ShipClass.Destroyer);
            double dv = ship.DeltaVBudgetKmS;
            Assert.That(FitIn(economy, ship, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(FitIn(economy, ship, yard, ModuleKind.ArmourPlating), Is.EqualTo(OutfitOutcome.Ok));
            var spec = ship.Combat.Spec;
            Assert.That(spec.Armor, Is.EqualTo(stock.Armor + 2 * Loadout.ArmourPerPlate));
            Assert.That(spec.Hull, Is.EqualTo(stock.Hull * (1 + 2 * Loadout.HullPerPlate)).Within(1e-6));
            Assert.That(spec.DryMassTonnes, Is.EqualTo(stock.DryMassTonnes + 2 * ModuleSpec.Of(ModuleKind.ArmourPlating).Mass(1)).Within(1e-6));
            Assert.That(spec.Accel, Is.LessThan(stock.Accel), "more mass, same thrust");
            Assert.That(spec.MassKt, Is.GreaterThan(stock.MassKt), "the piloted hull is heavier too");
            Assert.That(ship.DeltaVBudgetKmS, Is.LessThan(dv), "the same tanks push more dry mass");
            Assert.That(ship.Combat.HullFraction, Is.EqualTo(1).Within(1e-9), "plate goes on whole");
        }

        [Test]
        public void TorchDrivesPushHarder()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Destroyer, Navy);
            var stock = CombatSpec.Of(ShipClass.Destroyer);
            var l = Outfitting.LoadoutOf(ship);
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Type == MountType.Engine) Assert.That(Fit(economy, ship, yard, i, ModuleKind.TorchEngine), Is.EqualTo(OutfitOutcome.Ok));
            var spec = ship.Combat.Spec;
            Assert.That(spec.MainThrustMN, Is.EqualTo(stock.MainThrustMN * Loadout.TorchRating).Within(1e-6));
            Assert.That(spec.Accel, Is.GreaterThan(stock.Accel * 1.2));
            Assert.That(spec.MaxSpeed, Is.GreaterThan(stock.MaxSpeed));
            Assert.That(HullDynamics.Of(spec).MainAccel, Is.GreaterThan(HullDynamics.Of(stock).MainAccel));
        }

        [Test]
        public void ARefitKeepsTheHullsDamageAndPeople()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Cruiser, Navy);
            ship.Combat.Hull *= 0.6;
            var lost = ship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.MissileLauncher);
            lost.Hp = 0;
            ship.Combat.Interior.TakeCrew(100);
            double crew = ship.Combat.Interior.Crew;
            int slot = SlotOf(ship, MountType.Shield);
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.HardenedShield), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.Combat.HullFraction, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(ship.Combat.Subsystems[lost.Index].Destroyed, Is.True, "the shot-away launcher is still shot away");
            Assert.That(ship.Combat.Interior.Crew, Is.EqualTo(crew).Within(1e-9));
        }

        [Test]
        public void SwappingAShotAwayGunReplacesItWholeButTheWreckFetchesNothing()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Cruiser, Navy);
            int slot = SlotOf(ship, MountType.Missile, s => s.Stock == ModuleKind.MissileLauncher);
            int index = Outfitting.LoadoutOf(ship).Slots[slot].LayoutIndex;
            ship.Combat.Subsystems[index].Hp = 0;
            Assert.That(Outfitting.Quote(ship, yard, slot, ModuleKind.InterceptorMissile).TradeIn, Is.EqualTo(0));
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.InterceptorMissile), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.Combat.Subsystems[index].Health, Is.EqualTo(1));
            // And back to a launcher: it comes loaded.
            Assert.That(Fit(economy, ship, yard, slot, ModuleKind.MissileLauncher), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.Combat.Subsystems[index].Ammo, Is.EqualTo(WeaponSpec.Missiles.Ammo));
        }

        [Test]
        public void CargoRacksEnlargeBothHoldsAndCannotComeOutFull()
        {
            // An ore hauler's holds: a truss freighter takes pods, not racks (U.SPC-T482).
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Miner, Navy);
            double capacity = ship.CapacityTonnes;
            int rows = ship.PersonalHold.Height;
            Assert.That(FitIn(economy, ship, yard, ModuleKind.CargoRack), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.CapacityTonnes, Is.EqualTo(capacity * (1 + Loadout.RackCapacityShare)).Within(1e-6));
            Assert.That(ship.PersonalHold.Height, Is.EqualTo(rows + Loadout.RackRows));
            while (ship.PersonalHold.AddFirstFit(Good.Electronics) != null) { }
            Assert.That(StripOut(economy, ship, yard, ModuleKind.CargoRack), Is.EqualTo(OutfitOutcome.HoldTooFull));
            foreach (var item in ship.PersonalHold.Items.ToList()) ship.PersonalHold.Remove(item);
            ship.HasCargo = true;
            ship.CargoTonnes = capacity * 1.1;
            Assert.That(StripOut(economy, ship, yard, ModuleKind.CargoRack), Is.EqualTo(OutfitOutcome.HoldTooFull));
            ship.CargoTonnes = capacity * 0.5;
            Assert.That(StripOut(economy, ship, yard, ModuleKind.CargoRack), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.CapacityTonnes, Is.EqualTo(capacity).Within(1e-6));
            Assert.That(ship.PersonalHold.Height, Is.EqualTo(rows));
        }

        [Test]
        public void ScannerSuitesReachFurtherForHavens()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Destroyer, Navy);
            Assert.That(Outfitting.ScanRangeKm(ship), Is.EqualTo(War.ScanRangeKm));
            Assert.That(FitIn(economy, ship, yard, ModuleKind.Scanner), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(Outfitting.ScanRangeKm(ship), Is.EqualTo(War.ScanRangeKm + Loadout.ScannerRangeKm));
        }

        // ---------------------------------------------------------------- the brig

        [Test]
        public void ABrigTakesPrisonersOffAPrizeWhoFetchABountyInPort()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Cruiser, Navy, 1e9);
            Assert.That(FitIn(economy, ship, yard, ModuleKind.Brig), Is.EqualTo(OutfitOutcome.Ok));
            double berths = Outfitting.BrigCapacity(ship);
            Assert.That(berths, Is.GreaterThan(0));

            var prize = Hull(ShipClass.Destroyer, Raiders);
            double crew = prize.Combat.Interior.Crew;
            var nobrig = Hull(ShipClass.Destroyer, Navy, 1e9);
            double taken = Outfitting.TakePrisoners(prize, new[] { nobrig, ship });
            Assert.That(taken, Is.EqualTo(Math.Min(berths, Math.Floor(crew * Outfitting.PrisonerShare))));
            Assert.That(nobrig.Prisoners, Is.EqualTo(0), "no brig, no prisoners");
            Assert.That(ship.Prisoners, Is.EqualTo(taken));
            Assert.That(prize.Combat.Interior.Crew, Is.EqualTo(crew - taken).Within(1e-6));
            Assert.That(ship.PrisonerBounty, Is.EqualTo(taken * Outfitting.PirateBounty));

            Assert.That(StripOut(economy, ship, yard, ModuleKind.Brig), Is.EqualTo(OutfitOutcome.BrigOccupied));
            double before = Navy.Treasury;
            Assert.That(Outfitting.HandOverPrisoners(economy, ship, yard), Is.EqualTo(taken * Outfitting.PirateBounty));
            Assert.That(Navy.Treasury, Is.EqualTo(before + taken * Outfitting.PirateBounty).Within(1e-6));
            Assert.That(ship.Prisoners, Is.EqualTo(0));
            Assert.That(StripOut(economy, ship, yard, ModuleKind.Brig), Is.EqualTo(OutfitOutcome.Ok));
        }

        // ---------------------------------------------------------------- the AI

        [Test]
        public void AFlushNavyUpgradesItsWarshipPlateFirstThenScannersShieldsGunsAndDrives()
        {
            var (economy, yard) = Yard(Navy);
            var ship = Hull(ShipClass.Cruiser, Navy, 1e12);
            Assert.That(Outfitting.AiRefit(economy, ship, yard), Is.SameAs(FacilityKinds.ForModule(ModuleKind.ArmourPlating)));
            Assert.That(Outfitting.AiRefit(economy, ship, yard), Is.SameAs(FacilityKinds.ForModule(ModuleKind.Scanner)));
            var fitted = Enumerable.Range(0, 40).Select(_ => Outfitting.AiRefit(economy, ship, yard)).Where(m => m != null).ToList();
            var l = Outfitting.LoadoutOf(ship);
            Assert.That(l.Count(ModuleKind.Brig), Is.EqualTo(1));
            Assert.That(l.Count(ModuleKind.HardenedShield), Is.EqualTo(l.Slots.Count(s => s.Type == MountType.Shield)));
            Assert.That(l.Count(ModuleKind.HeavyLaser), Is.GreaterThan(0), "a big hull's lasers become heavy lasers");
            Assert.That(l.Count(ModuleKind.TorchEngine), Is.EqualTo(l.Slots.Count(s => s.Type == MountType.Engine)));
            Assert.That(Outfitting.AiRefit(economy, ship, yard), Is.Null, "nothing left to want");
            for (int i = 0; i < l.Slots.Count; i++)
                Assert.That(Outfitting.CanFit(l, i, l.Fitted[i]), Is.True, "the AI's refits obey each mount's type and size (U.SPC-T508)");
        }

        [Test]
        public void EveryHardpointIsTypedAndSizedAndEveryStockDesignKeepsItsOwnParts()
        {
            // U.SPC-T508: a hardpoint is Ballistic, Missile or Energy (or a shield or engine mount) with a size, and each
            // module that goes on one has a type and size of its own; the facility modules go inside the hull instead.
            foreach (var m in ModuleSpec.Catalogue)
                Assert.That(m.Mount == null, Is.EqualTo(m.Facility != null), m.Name);
            foreach (var f in HullFrames.Catalogue)
            {
                var l = f.NewLoadout();
                for (int i = 0; i < l.Slots.Count; i++)
                {
                    var s = l.Slots[i];
                    Assert.That(s.Stock, Is.Not.Null, f.Id);
                    // A spinal mount (U.SPC-T490) is a type of its own that takes any gun built to the hull's length.
                    if (s.Type == MountType.Spinal) Assert.That(SpinalMounts.Takes(s.Stock.Value), Is.True, f.Id);
                    else Assert.That(s.Type, Is.EqualTo(ModuleSpec.Of(s.Stock.Value).Mount), f.Id + " " + s.Name + ": a mount is of its stock part's type");
                    Assert.That(s.Size, Is.GreaterThanOrEqualTo(ModuleSpec.Of(s.Stock.Value).Size), f.Id + " " + s.Name);
                    Assert.That(Outfitting.CanFit(l, i, s.Stock), Is.True, f.Id + ": " + s.Name + " refuses its own " + s.Stock);
                    Assert.That(f.Hardpoints[i].Type, Is.EqualTo(s.Type));
                    Assert.That(f.Hardpoints[i].Size, Is.EqualTo(s.Size));
                }
                Assert.That(l.Build(), Is.SameAs(f.Design), f.Id + ": the stock design is unchanged");
            }
            var battleship = HullFrames.ForClass(ShipClass.Battleship).NewLoadout();
            var bow = battleship.Slots.First(s => s.Stock == ModuleKind.HeavyLaser && s.BearsOnBow);
            Assert.That(bow.Name, Is.EqualTo("Large energy mount"), "the outfitting screen names a mount by its size and type");
            Assert.That(battleship.Slots.First(s => s.Type == MountType.Shield).Name, Is.EqualTo("Shield mount"));
            Assert.That(battleship.Slots.Count(s => s.Type == MountType.Spinal), Is.EqualTo(2), "and its two spinal mounts are their own type");
            Assert.That(battleship.Slots.First(s => s.Type == MountType.Spinal).Name, Is.EqualTo("Spinal mount"));
        }

        [Test]
        public void ANavyKeepsItsMoneyForHullsAndRaidersWantBrigsAndRacks()
        {
            var (economy, yard) = Yard(Navy);
            var poor = Hull(ShipClass.Destroyer, Navy, Economy.HullPrice(ShipClass.Destroyer));
            Assert.That(Outfitting.AiRefit(economy, poor, yard), Is.Null, "a navy that can just afford a hull does not spend it on plate");

            var (havenEconomy, haven) = Yard(Raiders);
            haven.Kind = SettlementKind.Haven;
            var raider = Hull(ShipClass.Destroyer, Raiders, 1e12);
            Assert.That(Outfitting.IsYard(haven), Is.True, "a haven refits its raiders");
            Assert.That(Outfitting.AiRefit(havenEconomy, raider, haven), Is.SameAs(FacilityKinds.ForModule(ModuleKind.ArmourPlating)));
            Assert.That(Outfitting.AiRefit(havenEconomy, raider, haven), Is.SameAs(FacilityKinds.ForModule(ModuleKind.Brig)));
            Assert.That(Outfitting.AiRefit(havenEconomy, raider, haven), Is.SameAs(FacilityKinds.ForModule(ModuleKind.CargoRack)));
        }

        [Test]
        public void NaviesRefitTheirWarshipsAtTheirOwnYardsAsTheWarGoesOn()
        {
            const double start = 2461324.0;
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(start);
            var w = World.CreateDefault(eph, start);
            foreach (var f in w.Fleet.Factions) if (f.IsMilitary) f.Treasury = Math.Max(f.Treasury, 1e12);
            for (double jd = start; jd <= start + 60; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
            }
            var refitted = w.Fleet.Ships.Where(s => !s.Destroyed && s.Loadout != null && !s.Loadout.IsStock).ToList();
            Assert.That(refitted, Is.Not.Empty, "nobody refitted in two months at a yard");
            Expect.All(refitted, s => Assert.That(s.Combat.Spec, Is.Not.SameAs(CombatSpec.Of(s.Spec.Class)), s.Name));
        }
    }
}
