using NUnit.Framework;
using ShiftRegister.Game.Progress;

namespace ShiftRegister.Tests
{
    public class UnlockTests
    {
        [Test]
        public void FirstTwoLevels_OpenOnFreshSave()
        {
            var solved = new[] { false, false, false, false };
            Assert.That(Unlocks.IsUnlocked(solved, 0), Is.True);
            Assert.That(Unlocks.IsUnlocked(solved, 1), Is.True, "one level may be skipped");
            Assert.That(Unlocks.IsUnlocked(solved, 2), Is.False);
        }

        [Test]
        public void SolvingOpensTheNext()
        {
            var solved = new[] { true, false, false, false };
            Assert.That(Unlocks.IsUnlocked(solved, 2), Is.True);
            Assert.That(Unlocks.IsUnlocked(solved, 3), Is.False);
        }

        [Test]
        public void OnlyOneSkipAtATime()
        {
            var solved = new[] { true, false, true, true, false, false };
            Assert.That(Unlocks.IsUnlocked(solved, 4), Is.True, "level 1 is the single skip");
            Assert.That(Unlocks.IsUnlocked(solved, 5), Is.False, "levels 1 and 4 are both unsolved");
        }

        [Test]
        public void NoSkips_IsStrictlyLinear()
        {
            var solved = new[] { true, false, false };
            Assert.That(Unlocks.IsUnlocked(solved, 1, skips: 0), Is.True);
            Assert.That(Unlocks.IsUnlocked(solved, 2, skips: 0), Is.False);
        }
    }
}
