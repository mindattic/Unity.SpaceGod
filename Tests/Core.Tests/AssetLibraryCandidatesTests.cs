using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class AssetLibraryCandidatesTests
    {
        [Test]
        public void ListsEveryTicketCandidateOnceWithItsOwnDemoScene()
        {
            var names = AssetLibraryCandidates.All.Select(c => c.Name).ToList();
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "Space Combat Kit", "Ingame Spaceship Builder", "Modular Starship Constructor",
                "Space Satellite Kit", "Modular Space Station",
            }));
            Assert.That(AssetLibraryCandidates.All.Select(c => c.DemoScenePath).Distinct().Count(), Is.EqualTo(5));
            Assert.That(AssetLibraryCandidates.All.Select(c => c.Id).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void EveryRoleHasACandidateAndNothingIsClaimedCheckedBeforeSomeoneChecks()
        {
            foreach (CandidateRole role in System.Enum.GetValues(typeof(CandidateRole)))
                Assert.That(AssetLibraryCandidates.For(role).Any(), Is.True, role.ToString());
            Assert.That(AssetLibraryCandidates.All.Any(c => c.FullyChecked), Is.False);
        }

        [Test]
        public void FullyCheckedNeedsAllThreeTargets()
        {
            var partial = new AssetCandidate("X", "X", CandidateRole.StaticStructures, "", Compatibility.Works, Compatibility.Works);
            var full = new AssetCandidate("Y", "Y", CandidateRole.StaticStructures, "", Compatibility.Works, Compatibility.Works, Compatibility.Fails);
            Assert.That(partial.FullyChecked, Is.False);
            Assert.That(full.FullyChecked, Is.True);
        }
    }
}
