using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T524: a colony ship is a real hull in the fleet, not a record with a timetable. It is fitted out at its port,
    /// flies the voyage itself, its colonists land only when it actually gets there, and it can be lost on the way.
    /// </summary>
    [TestFixture]
    public class ColonyHullTests
    {
        const double StartJd = 2461324.0;

        static World Sol()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static ColonyShip Launch(World w)
        {
            // One free site to go to, wherever Sol happens to have room.
            var free = w.Colonisation.Sites.FirstOrDefault(c => c.Score > 0 && !Colonisation.Occupied(w.Economy, c.Body, w.Jd));
            if (free == null)
            {
                var body = w.Catalog.Bodies.First(b => b.IsVisible && !b.IsStar && b.Index != w.Catalog["Earth"].Index && w.Economy.Settlements.All(s => s.Body != b.Index));
                free = new ColonySite { Body = body.Index, Name = body.Name, Zone = HabitableZone.Conservative, Resources = new[] { Good.Water }, Capacity = 1e6 };
            }
            w.Colonisation.Sites.Clear();
            w.Colonisation.Sites.Add(free);
            var earth = w.Economy.Settlements.First(s => s.Name == "Earth Orbital");
            earth.Overflow = Colonisation.PartySize * 2;
            var ship = w.Colonisation.TryLaunch(w.Economy, earth.Owner, w.Jd);
            Assert.That(ship, Is.Not.Null, "Earth's crowded people sail");
            return ship;
        }

        [Test]
        public void AColonyShipIsAFreighterFittedOutInTheFleet()
        {
            var w = Sol();
            var v = Launch(w);
            var hull = v.Hull;
            Assert.That(hull, Is.Not.Null, "a real hull, not a number");
            Assert.That(w.Fleet.Ships, Does.Contain(hull));
            Assert.That(hull.Role, Is.EqualTo(ShipRole.Colonist));
            Assert.That(hull.Spec.Class, Is.EqualTo(ShipClass.Freighter));
            Assert.That(hull.Faction, Is.SameAs(v.Owner));
            Assert.That(hull.Colony, Is.SameAs(v));
            Assert.That(hull.Name, Is.EqualTo(v.Name));
            Assert.That(hull.Destroyed, Is.False);
            Assert.That(v.Of, Is.SameAs(w.Colonisation));
        }

        [Test]
        public void ItsColonistsLandOnlyWhenTheHullGetsThere()
        {
            var w = Sol();
            var v = Launch(w);
            var hull = v.Hull;
            int settlements = w.Economy.Settlements.Count;

            // Its reckoned arrival comes and goes: the timetable lands nobody.
            w.Colonisation.Review(w.Economy, v.ArriveJd + 1);
            Assert.That(w.Colonisation.Voyages, Does.Contain(v), "still on its way until the hull arrives");
            Assert.That(w.Economy.Settlements.Count(s => s.Body == v.Body && Colonisation.IsFounded(s)), Is.EqualTo(0));

            // Somewhere else, it lands nobody either.
            Assert.That(w.Colonisation.HullArrived(hull, w.Colonisation, w.Economy, v.ArriveJd), Is.EqualTo(ColonyLanding.None));

            // At the site, they found their outpost.
            hull.Orbit.Body = v.Body;
            Assert.That(Colonisation.HullDestination(hull, w.Colonisation), Is.EqualTo(v.Body));
            Assert.That(w.Colonisation.HullArrived(hull, w.Colonisation, w.Economy, v.ArriveJd), Is.EqualTo(ColonyLanding.Founded));
            Assert.That(w.Colonisation.Voyages, Does.Not.Contain(v));
            Assert.That(hull.Colony, Is.Null);
            var outpost = w.Economy.Settlements.Single(s => s.Body == v.Body && Colonisation.IsFounded(s));
            Assert.That(outpost.Owner, Is.SameAs(v.Owner));
            Assert.That(outpost.Population, Is.GreaterThan(Colonisation.PartySize * 0.9));
            Assert.That(w.Economy.Settlements.Count, Is.EqualTo(settlements + 1));
        }

        [Test]
        public void AColonyShipDestroyedOnTheWayIsLostWithItsColonists()
        {
            var w = Sol();
            var v = Launch(w);
            v.Hull.Destroyed = true;
            w.Colonisation.Review(w.Economy, w.Jd + 1);
            Assert.That(w.Colonisation.Voyages, Does.Not.Contain(v));
            Assert.That(w.Economy.Settlements.Count(s => s.Body == v.Body && Colonisation.IsFounded(s)), Is.EqualTo(0), "nobody lands");
            Assert.That(w.Events.Any(e => e.Text.Contains("is lost with all")), Is.True);
        }

        [Test]
        public void AColonyShipTakenOnTheWayIsLostToItsCaptors()
        {
            var w = Sol();
            var v = Launch(w);
            v.Hull.Faction = w.Pirates;
            w.Colonisation.Review(w.Economy, w.Jd + 1);
            Assert.That(w.Colonisation.Voyages, Does.Not.Contain(v));
            Assert.That(v.Hull.Colony, Is.Null);
            Assert.That(v.Hull.Role, Is.Not.EqualTo(ShipRole.Colonist));
            Assert.That(w.Events.Any(e => e.Text.Contains("is taken by the " + w.Pirates.Name)), Is.True);
        }
    }
}
