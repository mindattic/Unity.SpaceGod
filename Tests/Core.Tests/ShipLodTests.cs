using System.Linq;
using NUnit.Framework;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>A far-off ship swaps its model for a named faction icon (U.SPC-T376), and its card lists its weapons.</summary>
    [TestFixture]
    public class ShipLodTests
    {
        [Test]
        public void AShipSwapsToItsIconOnceItIsShorterOnScreenThanTheSwapPoint()
        {
            // 1 unit long, 1000 px per unit at unit distance: 10 px at 100 units, 2.5 px at 400.
            Assert.That(ShipLod.ApparentPx(1, 100, 1000), Is.EqualTo(10).Within(1e-9));
            Assert.That(ShipLod.ApparentPx(1, 400, 1000), Is.EqualTo(2.5).Within(1e-9));
            Assert.That(ShipLod.ApparentPx(1, 0, 1000), Is.GreaterThan(1e9), "a ship at the camera is as big as it gets, not a division by zero");

            Assert.That(ShipLod.DrawsModel(10, 5), Is.True);
            Assert.That(ShipLod.DrawsModel(5, 5), Is.True, "exactly at the swap point it is still a model");
            Assert.That(ShipLod.DrawsModel(4.99, 5), Is.False);
            Assert.That(ShipLod.DrawsModel(10, 12), Is.False, "a higher swap point turns models to icons sooner");
        }

        [Test]
        public void TheSwapPointAndIconNamesAreSettingsInTheRenderBudgetGroup()
        {
            Assert.That(ShipLod.IconBelowPx.Group, Is.EqualTo(SettingGroups.RenderBudget));
            Assert.That(ShipLod.IconBelowPx.Default, Is.EqualTo(5), "the swap point the game has always used");
            Assert.That(ShipLod.IconBelowPx.Min, Is.GreaterThan(0));
            Assert.That(SettingsRegistry.Default.Find("lod.shipIconPx"), Is.SameAs(ShipLod.IconBelowPx));

            Assert.That(ShipLod.IconNames.Group, Is.EqualTo(SettingGroups.RenderBudget));
            Assert.That(ShipLod.IconNames.Options, Is.EqualTo(new[] { "Off", "Warships", "All" }));
            Assert.That(ShipLod.IconNames.Default, Is.EqualTo(ShipLod.NamesAll), "an icon carries its name unless the player turns names down");
            Assert.That(SettingsRegistry.Default.Find("lod.shipIconNames"), Is.SameAs(ShipLod.IconNames));
        }

        [Test]
        public void IconNamesFollowTheSettingAndAnEscortIsFoldedIntoItsLeader()
        {
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesAll, military: false, escort: false), Is.True);
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesAll, military: true, escort: false), Is.True);
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesWarships, military: true, escort: false), Is.True);
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesWarships, military: false, escort: false), Is.False);
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesOff, military: true, escort: false), Is.False);
            Assert.That(ShipLod.NamesIcon(ShipLod.NamesAll, military: true, escort: true), Is.False);
        }

        [Test]
        public void TheCardListsEachWeaponKindWithHowManyAreMountedAndLost()
        {
            var combat = new ShipCombat(CombatSpec.Of(ShipClass.Battleship), ShipClass.Battleship);
            var weapons = combat.Subsystems.Where(s => s.Def.IsWeapon).ToList();
            Assume.That(weapons, Is.Not.Empty);

            string intact = ShipLod.Armament(combat.Subsystems);
            Assert.That(intact, Does.Not.Contain("lost"));
            foreach (var kind in weapons.Select(s => s.Kind).Distinct())
            {
                int n = weapons.Count(s => s.Kind == kind);
                Assert.That(intact, Does.Contain(n + " " + SubsystemDef.Name(kind).ToLowerInvariant()));
            }
            Assert.That(intact, Does.Not.Contain("engine").And.Not.Contain("bridge"), "only weapons are armament");

            weapons[0].Hp = 0;
            Assert.That(ShipLod.Armament(combat.Subsystems), Does.Contain(SubsystemDef.Name(weapons[0].Kind).ToLowerInvariant() + " (1 lost)"));

            Assert.That(ShipLod.Armament(new Subsystem[0]), Is.EqualTo("Unarmed"));
            Assert.That(ShipLod.Armament(null), Is.EqualTo("Unarmed"));
        }
    }
}
