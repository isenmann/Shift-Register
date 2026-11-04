using System.Linq;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.Progress;

namespace ShiftRegister.Tests
{
    public class HelpTextTests
    {
        static string[] Titles(LevelData level) => HelpText.Mechanics(level).Select(entry => entry.Title).ToArray();

        [Test]
        public void PlainLevel_OnlyTheGoal()
        {
            Assert.That(Titles(new LevelData()), Is.EqualTo(new[] { "GOAL" }));
        }

        [Test]
        public void ListsEveryMechanicOnTheBoard_InChapterOrder()
        {
            var level = new LevelData
            {
                walls = new[] { 0, 16, 0, 0, 0, 0, 0, 0 },
                pins = new[] { 0, 0, 0, 8, 0, 0, 0, 0 },
                rotateRows = new[] { 2 },
                chainTopRows = new[] { 5 },
                fillRightRows = new[] { 7 },
                gates = new[] { new RowGate(4, GateOp.Xor, 24), new RowGate(6, GateOp.And, 60) },
                lossRule = LossRule.Exactly,
                lossCount = 3
            };
            Assert.That(Titles(level), Is.EqualTo(new[]
            {
                "GOAL", "WALLS", "PINS", "WRAP BELT", "CARRY BELT", "1-IN EDGE", "GATES", "BIT BUCKET"
            }));
            var gates = HelpText.Mechanics(level).First(entry => entry.Title == "GATES").Text;
            Assert.That(gates.IndexOf("&"), Is.LessThan(gates.IndexOf("^")), "ops listed in enum order");
            Assert.That(gates, Does.Not.Contain("|"), "only the ops on this board");
            Assert.That(HelpText.Mechanics(level).Last().Text, Does.Contain("exactly 3"));
        }

        [Test]
        public void ColumnGates_SayColumnsToo()
        {
            var level = new LevelData { gates = new[] { new RowGate(0, GateOp.Or, 1) }, gatesOnColumnMoves = true };
            Assert.That(HelpText.Mechanics(level).Last().Text, Does.Contain("column"));
        }

        [TestCase(LossRule.AtMost, 0, "no bits")]
        [TestCase(LossRule.AtMost, 2, "at most 2")]
        [TestCase(LossRule.Any, 0, null)]
        public void BucketRule(LossRule rule, int count, string expected)
        {
            var entries = HelpText.Mechanics(new LevelData { lossRule = rule, lossCount = count });
            var bucket = entries.Where(entry => entry.Title == "BIT BUCKET").ToList();
            if (expected == null)
            {
                Assert.That(bucket, Is.Empty);
            }
            else
            {
                Assert.That(bucket.Single().Text, Does.Contain(expected));
            }
        }

        [Test]
        public void MarkHelpSeen_OnlyFirstTime()
        {
            var progress = new PlayerProgress();
            Assert.That(progress.MarkHelpSeen("3-1"), Is.True);
            Assert.That(progress.MarkHelpSeen("3-1"), Is.False);
            Assert.That(progress.MarkHelpSeen("4-1"), Is.True);
            progress.helpSeen = null; // progress saved before help existed
            Assert.That(progress.MarkHelpSeen("3-1"), Is.True);
        }
    }
}
