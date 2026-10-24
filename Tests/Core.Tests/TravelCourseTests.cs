using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class TravelCourseTests
    {
        static Galaxy Create() => Galaxy.CreateDefault(2461324.0);

        /// <summary>The farthest pair of systems by gates, so the course has several legs.</summary>
        static (StarSystem from, StarSystem to) FarPair(Galaxy g)
        {
            var from = g.Find("Sol System");
            var to = g.Systems.OrderByDescending(s => g.Network.Route(from, s)?.Count ?? 0).First();
            return (from, to);
        }

        [Test]
        public void ACourseIsOneGatePerLegEachStartingWhereTheLastEnded()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            Assert.That(course.Legs, Is.GreaterThanOrEqualTo(3));
            Assert.That(course.Gates[0].Home, Is.SameAs(from));
            for (int i = 1; i < course.Legs; i++) Assert.That(course.Gates[i].Home, Is.SameAs(course.Gates[i - 1].Partner.Home));
            Assert.That(course.Gates[course.Legs - 1].Partner.Home, Is.SameAs(to));
        }

        [Test]
        public void PassingEachGateInOrderWalksTheCourseToItsEnd()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            var gates = course.Gates.ToList();
            for (int i = 0; i < gates.Count; i++)
            {
                Assert.That(course.Next, Is.SameAs(gates[i]));
                Assert.That(course.Passed(gates[i], g.Network, null, double.NegativeInfinity), Is.EqualTo(i < gates.Count - 1));
            }
            Assert.That(course.Done, Is.True);
        }

        [Test]
        public void AnotherGateReplansFromWhereTheShipComesOut()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            var course = TravelCourse.Plan(g.Network, from, to);
            var wrong = from.Gates.First(x => x != course.Next);
            Assert.That(course.Passed(wrong, g.Network, null, double.NegativeInfinity), Is.True);
            Assert.That(course.Next.Home, Is.SameAs(wrong.Partner.Home));
        }

        [Test]
        public void TheViewTakesTheSameGatesOneAtATime()
        {
            var g = Create();
            var (from, to) = FarPair(g);
            Assert.That(GateJump.TryPlan(g.Network, from, to, out var route), Is.True);
            Assert.That(route.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(route[0].Home, Is.SameAs(from));
            Assert.That(route.Last().Partner.Home, Is.SameAs(to));
        }
    }
}
