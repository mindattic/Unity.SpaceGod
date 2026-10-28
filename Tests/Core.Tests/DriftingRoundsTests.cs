using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>A Breacher round that fails is not the instant loss of its team (U.SPC-T519).</summary>
    [TestFixture]
    public class DriftingRoundsTests
    {
        const double Jd = 2461324.0;

        static Faction Navy() => new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };
        static Faction Rival() => new Faction("Rival", "RV", new Rgb(0, 1, 0)) { IsMilitary = true };
        static Faction Raiders() => new Faction("Black Banner", "", new Rgb(1, 0, 0)) { IsPirate = true };

        [Test]
        public void A_round_glancing_off_a_shield_tumbles_back_out_slower()
        {
            var off = DriftingRounds.Glance(new DVec3(-2, 0, 0), DVec3.UnitX);
            Assert.That(off.X, Is.GreaterThan(0), "turned back out of the shield");
            Assert.That(off.Length, Is.EqualTo(2 * DriftingRounds.GlanceKeep).Within(1e-9));
        }

        [Test]
        public void A_drifting_round_keeps_its_team_alive_until_its_air_runs_out()
        {
            var crews = new Crews(1);
            var navy = Navy();
            var pod = crews.Adrift("Breacher round from the NS Test", navy, 70, Rival(), 3, DVec3.Zero, DVec3.Zero, DVec3.UnitX, null, Jd);
            Assert.That(pod.Breacher, Is.True);
            Assert.That(pod.People, Is.EqualTo(70));
            crews.Expire(Jd + DriftingRounds.AirDays - 0.1);
            Assert.That(pod.Drifting, Is.True);
            crews.Expire(Jd + DriftingRounds.AirDays + 0.1);
            Assert.That(pod.State, Is.EqualTo(PodState.Lost));
        }

        [Test]
        public void Its_own_side_brings_the_marines_home()
        {
            var crews = new Crews(1);
            var navy = Navy();
            var pod = crews.Adrift("round", navy, 70, Rival(), 3, DVec3.Zero, DVec3.Zero, DVec3.UnitX, null, Jd);
            Assert.That(crews.Collect(pod, navy, null, null, Jd + 1), Is.EqualTo(PodState.Rescued));
            Assert.That(navy.CrewRescued, Is.EqualTo(70));
        }

        [Test]
        public void Pirates_who_salvage_it_sell_the_marines_to_their_enemy_when_the_owners_cannot_pay()
        {
            var crews = new Crews(1);
            var navy = Navy();
            var rival = Rival();
            var raiders = Raiders();
            navy.Treasury = 0;
            rival.Treasury = 1e9;
            var pod = crews.Adrift("round", navy, 70, rival, 3, DVec3.Zero, DVec3.Zero, DVec3.UnitX, null, Jd);
            Assert.That(crews.Collect(pod, raiders, null, null, Jd + 1), Is.EqualTo(PodState.Hostage));
            Assert.That(crews.Held[0].Marines, Is.True);
            Assert.That(crews.Held[0].Buyer, Is.SameAs(rival));
            crews.Daily(null, Jd + 30);
            Assert.That(crews.Held, Is.Empty);
            Assert.That(rival.Treasury, Is.EqualTo(1e9 - 70 * DriftingRounds.SlavePricePerHead).Within(1e-3));
            Assert.That(raiders.Treasury, Is.GreaterThanOrEqualTo(70 * DriftingRounds.SlavePricePerHead));
        }
    }
}
