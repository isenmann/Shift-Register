using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Hud;

namespace ShiftRegister.Tests
{
    public class RowHintTests
    {
        [TestCase(63, 126, "need ×2")]
        [TestCase(15, 60, "need ×4")]
        [TestCase(3, 24, "need ×8")]
        [TestCase(48, 12, "need ÷4")]
        [TestCase(255, 255, "OK")]
        [TestCase(0, 0, "OK")]
        [TestCase(5, 7, "")]
        [TestCase(0, 8, "")]
        [TestCase(255, 254, "")] // ×2 would push a bit off
        [TestCase(7, 3, "")]     // ÷2 would drop a bit
        public void Heart_StyleHints(int now, int target, string expected) =>
            Assert.That(RowHint.For(now, target, true), Is.EqualTo(expected));

        [Test]
        public void NoArithmetic_OnlyOk()
        {
            Assert.That(RowHint.For(63, 126, false), Is.EqualTo(""));
            Assert.That(RowHint.For(9, 9, false), Is.EqualTo("OK"));
        }

        [Test]
        public void ArithmeticHints_OnlyForPlainLevels()
        {
            Assert.That(RowHint.ArithmeticHintsApply(LevelRules.Compile(TestLevels.Heart())), Is.True);
            Assert.That(RowHint.ArithmeticHintsApply(LevelRules.Compile(TestLevels.FourGates())), Is.False);
            var rotating = TestLevels.OneRow("10000000");
            rotating.rotateRows = new[] { 3 };
            Assert.That(RowHint.ArithmeticHintsApply(LevelRules.Compile(rotating)), Is.False);
        }
    }
}
