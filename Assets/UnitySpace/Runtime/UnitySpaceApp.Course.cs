using System.Globalization;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The piloted ship's plotted course over the gates (U.SPC-T516): a trip to a system several gates away is a list of
    /// real legs (<see cref="TravelCourse"/>), each flown across the system the hull is in to that system's gate for the
    /// next link and gone through by hand (Y); the HUD names the next gate and how far it is, and the map draws the way.
    /// Nothing jumps the hull to the far system.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>The course the helm is flying, or null.</summary>
        public TravelCourse Course { get; private set; }

        /// <summary>Plots the quickest open way over the gates to <paramref name="to"/> for the piloted hull.</summary>
        public void PlotCourse(StarSystem to)
        {
            var ship = Piloted.Ship;
            if (ship == null || to == null) return;
            if (to == ActiveSystem) { Course = null; return; }
            var course = TravelCourse.Plan(Galaxy.Network, ActiveSystem, to, ship.Faction, Clock.Jd);
            if (course == null)
            {
                ShowToast("No open way through the gates to " + to.Name);
                return;
            }
            Course = course;
            ShowToast("Course plotted to " + to.Name.ToUpperInvariant() + ": " + course.Legs + (course.Legs == 1 ? " gate" : " gates") +
                      ", first " + course.Next.Name.ToUpperInvariant());
        }

        /// <summary>The hull has gone through <paramref name="gate"/>: on to the next leg, replotted if it was some other gate, ended on arrival.</summary>
        void AdvanceCourse(JumpGate gate, Ship ship)
        {
            if (Course == null) return;
            bool more = Course.Passed(gate, Galaxy.Network, ship.Faction, Clock.Jd);
            if (!more)
            {
                Course = null;
                return;
            }
            ShowToast("Next gate: " + Course.Next.Name.ToUpperInvariant() + ", leg " + (Course.Taken + 1) + " of " + Course.Legs);
        }

        /// <summary>The HUD's line for the course being flown, or null: the way by its systems, the next gate and how far off it is.</summary>
        public string CourseLine()
        {
            if (Course == null || Course.Done || !IsPiloting) return null;
            var next = Course.Next;
            string line = "COURSE  " + Course.Describe().ToUpperInvariant();
            if (next.Home == ActiveSystem)
            {
                var at = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
                line += "   ·   " + DistanceText((next.PositionKm - at).Length) + " TO GATE";
            }
            return line;
        }
    }
}
