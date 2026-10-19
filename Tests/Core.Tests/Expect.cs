using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>Assertions that hand back what they checked, which NUnit's constraint model doesn't.</summary>
    static class Expect
    {
        public static T Single<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            Assert.That(list, Has.Exactly(1).Items);
            return list[0];
        }

        public static T Single<T>(IEnumerable<T> items, Func<T, bool> match)
        {
            var list = items.Where(match).ToList();
            Assert.That(list, Has.Exactly(1).Items, "exactly one item should match");
            return list[0];
        }

        public static void All<T>(IEnumerable<T> items, Action<T> check)
        {
            foreach (var item in items) check(item);
        }
    }
}
