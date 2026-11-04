using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class ValidationTests
    {
        [Test]
        public void DocLevels_AreValid()
        {
            Assert.That(LevelRules.Validate(TestLevels.Heart()), Is.Empty);
            Assert.That(LevelRules.Validate(TestLevels.Domino()), Is.Empty);
            Assert.That(LevelRules.Validate(TestLevels.FourGates()), Is.Empty);
        }

        [Test]
        public void BitOnWall_IsRejected()
        {
            var level = TestLevels.OneRow("00100000", "00#00000");
            Assert.Throws<LevelValidationException>(() => LevelRules.Compile(level));
        }

        [Test]
        public void PinNotInTarget_IsRejected()
        {
            var level = TestLevels.OneRow("00100000");
            level.pins[3] = TestLevels.Bits("00100000");
            Assert.That(LevelRules.Validate(level), Has.Some.Contains("target"));
        }

        [Test]
        public void ChainWithWalls_IsRejected()
        {
            var level = new LevelData { chainTopRows = new[] { 3 } };
            level.walls[4] = 1;
            Assert.That(LevelRules.Validate(level), Has.Some.Contains("walls"));
        }

        [Test]
        public void WrongRowCount_IsRejected()
        {
            var level = new LevelData { start = new int[7] };
            Assert.That(LevelRules.Validate(level), Is.Not.Empty);
        }
    }
}
