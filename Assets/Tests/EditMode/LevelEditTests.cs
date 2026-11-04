using System;
using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class LevelEditTests
    {
        static LevelEdit Blank() => LevelEdit.Blank("Test");

        static void AssertValid(LevelEdit e) =>
            Assert.That(LevelEdit.AuthoredErrors(e.Level), Is.Empty, string.Join("; ", LevelEdit.AuthoredErrors(e.Level)));

        [Test]
        public void Wall_ClearsWhatIsUnderIt()
        {
            var e = Blank();
            e.SetBit(EditLayer.Start, 2, 3, true);
            e.SetBit(EditLayer.Target, 2, 3, true);
            e.SetPin(2, 3, true);
            e.SetGateCell(2, 3, GateOp.Or);

            e.SetWall(2, 3, true);

            Assert.That(e.GetBit(EditLayer.Start, 2, 3), Is.False);
            Assert.That(e.GetBit(EditLayer.Target, 2, 3), Is.False);
            Assert.That(e.Level.pins[2], Is.Zero);
            Assert.That(e.GateOn(2), Is.Null, "the gate's last cell went");
            AssertValid(e);
        }

        [Test]
        public void Bit_CannotBePaintedOnAWall()
        {
            var e = Blank();
            e.SetWall(1, 1, true);
            e.SetBit(EditLayer.Start, 1, 1, true);
            Assert.That(e.GetBit(EditLayer.Start, 1, 1), Is.False);
        }

        [Test]
        public void Pin_SetsBothPictures_AndErasingABitRemovesIt()
        {
            var e = Blank();
            e.SetPin(4, 4, true);
            Assert.That(e.GetBit(EditLayer.Start, 4, 4) && e.GetBit(EditLayer.Target, 4, 4), Is.True);

            e.SetBit(EditLayer.Target, 4, 4, false);
            Assert.That(e.Level.pins[4], Is.Zero);
            Assert.That(e.GetBit(EditLayer.Start, 4, 4), Is.True, "only the pin and that picture's bit go");
            AssertValid(e);
        }

        [Test]
        public void Wall_BreaksChainOnItsRow_AndEndCellWallTakesFillBack()
        {
            var e = Blank();
            e.SetChain(2, true);
            e.SetWall(3, 4, true);
            Assert.That(e.ChainTopOf(2), Is.EqualTo(-1));

            e.SetFill(5, true, true);
            e.SetWall(5, 0, true);
            Assert.That(e.HasFill(5, true), Is.False);
            AssertValid(e);
        }

        [Test]
        public void Chain_TakesBackOverlappingChainWallsAndInnerFills()
        {
            var e = Blank();
            e.SetChain(2, true);
            e.SetWall(4, 0, true);
            e.SetFill(3, false, true);
            e.SetChain(3, true);

            Assert.That(e.ChainTopOf(2), Is.EqualTo(-1));
            Assert.That(e.ChainTopOf(3), Is.EqualTo(3));
            Assert.That(e.Level.walls[4], Is.Zero);
            Assert.That(e.HasFill(3, false), Is.False, "the top row's right edge is inside the chain");
            AssertValid(e);
        }

        [Test]
        public void Chain_RotatesTogether()
        {
            var e = Blank();
            e.SetRotate(1, true);
            e.SetChain(1, true);
            Assert.That(e.IsRotating(1) && e.IsRotating(2), Is.True);

            e.SetRotate(2, false);
            Assert.That(e.IsRotating(1) || e.IsRotating(2), Is.False);
            AssertValid(e);
        }

        [Test]
        public void RotateAndFill_TakeEachOtherBack()
        {
            var e = Blank();
            e.SetFill(0, true, true);
            e.SetRotate(0, true);
            Assert.That(e.HasFill(0, true), Is.False);

            e.SetFill(0, false, true);
            Assert.That(e.IsRotating(0), Is.False);
            AssertValid(e);
        }

        [Test]
        public void Gate_OnePerRow_LastOpWins()
        {
            var e = Blank();
            e.SetGateCell(3, 2, GateOp.And);
            e.SetGateCell(3, 5, GateOp.Xor);
            Assert.That(e.Level.gates.Length, Is.EqualTo(1));
            Assert.That(e.GateOn(3).op, Is.EqualTo(GateOp.Xor));
            Assert.That(e.GateOn(3).mask, Is.EqualTo(TestLevels.Bits("00100100")));

            e.ClearGateCell(3, 2);
            e.ClearGateCell(3, 5);
            Assert.That(e.GateOn(3), Is.Null);
        }

        [Test]
        public void Gate_And1In_NeverShareACell()
        {
            var e = Blank();
            e.SetFill(1, true, true);
            e.SetGateCell(1, 0, GateOp.Or);
            Assert.That(e.HasFill(1, true), Is.False);

            e.SetFill(1, true, true);
            Assert.That(e.GateOn(1), Is.Null, "the 1-IN edge took the gate's only cell");
            AssertValid(e);
        }

        [Test]
        public void AnyEditSequence_StaysValid()
        {
            var rnd = new Random(12345);
            var e = Blank();
            for (int i = 0; i < 20000; i++)
            {
                int r = rnd.Next(8), c = rnd.Next(8);
                bool on = rnd.Next(2) == 0;
                switch (rnd.Next(14))
                {
                    case 0:
                        e.SetBit(EditLayer.Start, r, c, on);
                        break;
                    case 1:
                        e.SetBit(EditLayer.Target, r, c, on);
                        break;
                    case 2:
                        e.SetWall(r, c, on);
                        break;
                    case 3:
                        e.SetPin(r, c, on);
                        break;
                    case 4:
                        e.Erase(r, c);
                        break;
                    case 5:
                        e.SetRotate(r, on);
                        break;
                    case 6:
                        e.SetChain(rnd.Next(7), on);
                        break;
                    case 7:
                        e.SetFill(r, on, rnd.Next(2) == 0);
                        break;
                    case 8:
                        e.SetGateCell(r, c, (GateOp)rnd.Next(4));
                        break;
                    case 9:
                        e.ClearGateCell(r, c);
                        break;
                    case 10:
                        e.SetGateOp(r, (GateOp)rnd.Next(4));
                        break;
                    case 11:
                        e.RemoveGate(r);
                        break;
                    case 12:
                        e.SetLoss((LossRule)rnd.Next(3), rnd.Next(100));
                        break;
                    case 13:
                        if (on)
                        {
                            e.Undo();
                        }
                        else
                        {
                            e.Redo();
                        }

                        break;
                }

                var errors = LevelEdit.AuthoredErrors(e.Level);
                Assert.That(errors, Is.Empty, $"step {i}: {string.Join("; ", errors)}");
            }
        }

        [Test]
        public void RealChangesClearProof_NoOpsAndRenamesDoNot()
        {
            var e = Blank();
            e.SetBit(EditLayer.Start, 0, 0, true);
            e.SetBit(EditLayer.Target, 0, 1, true);
            e.SetSolution(new[] { Move.Row(0, Direction.Right) }, LevelProof.Searched);
            Assert.That(e.Level.par, Is.EqualTo(1));

            e.SetBit(EditLayer.Start, 0, 0, true);
            e.SetName("New name");
            Assert.That(e.Level.par, Is.EqualTo(1));

            e.SetBit(EditLayer.Start, 7, 7, true);
            Assert.That(e.Level.par, Is.Zero);
            Assert.That(e.Level.solution, Is.Empty);
            Assert.That(e.Proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Undo_BringsTheProofBack_RedoTakesItAway()
        {
            var e = Blank();
            e.SetBit(EditLayer.Start, 0, 0, true);
            e.SetBit(EditLayer.Target, 0, 1, true);
            e.SetSolution(new[] { Move.Row(0, Direction.Right) }, LevelProof.Replayed);

            e.SetBit(EditLayer.Start, 7, 7, true);
            e.Undo();
            Assert.That(e.Proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(e.Level.solution, Is.EqualTo("R1→"));

            e.Redo();
            Assert.That(e.Proof, Is.EqualTo(LevelProof.None));
            Assert.That(e.Level.solution, Is.Empty);
        }

        [Test]
        public void UndoRedo_RestoreExactly()
        {
            var e = Blank();
            e.SetBit(EditLayer.Start, 0, 0, true);
            var one = LevelEdit.Clone(e.Level);
            e.SetWall(0, 0, true);
            e.SetRotate(3, true);

            Assert.That(e.Undo(), Is.True);
            Assert.That(e.Undo(), Is.True);
            Assert.That(LevelEdit.SameLevel(e.Level, one), Is.True);

            Assert.That(e.Redo(), Is.True);
            Assert.That(e.Level.walls[0], Is.Not.Zero);

            e.SetBit(EditLayer.Target, 5, 5, true);
            Assert.That(e.CanRedo, Is.False, "a new edit drops the redo steps");
        }

        [Test]
        public void Stroke_IsOneUndoStep()
        {
            var e = Blank();
            e.BeginStroke();
            for (int c = 0; c < 8; c++)
            {
                e.SetBit(EditLayer.Start, 2, c, true);
            }

            e.EndStroke();
            Assert.That(e.Level.start[2], Is.EqualTo(255));

            e.Undo();
            Assert.That(e.Level.start[2], Is.Zero);
            Assert.That(e.CanUndo, Is.False);
        }

        [Test]
        public void EmptyStroke_LeavesNoUndoStep()
        {
            var e = Blank();
            e.BeginStroke();
            e.SetBit(EditLayer.Start, 2, 2, false);
            e.EndStroke();
            Assert.That(e.CanUndo, Is.False);
        }

        [Test]
        public void Problems_ReportSameBoardsAndNoMoves()
        {
            var e = Blank();
            Assert.That(e.Problems(), Does.Contain(LevelProblem.SameStartAndTarget));
            e.SetBit(EditLayer.Target, 0, 0, true);
            Assert.That(e.Problems(), Is.Empty);

            for (int i = 0; i < 8; i++)
            {
                e.SetPin(i, i, true);
            }

            Assert.That(e.Problems(), Does.Contain(LevelProblem.NoMoves));
        }

        [TestCase("<size=500>Boom", "size500Boom")]
        [TestCase("  Four Gates  ", "Four Gates")]
        [TestCase("Exactly sixteen chars and more", "Exactly sixteen")]
        [TestCase(null, "")]
        public void SanitizeName(string input, string expected) =>
            Assert.That(LevelEdit.SanitizeName(input), Is.EqualTo(expected));

        [Test]
        public void AuthoredErrors_RejectTwoGatesOnARow_AndAGateOnA1InCell()
        {
            var two = new LevelData { gates = new[] { new RowGate(1, GateOp.Or, 1), new RowGate(1, GateOp.Xor, 2) } };
            Assert.That(LevelRules.Validate(two), Is.Empty, "still fine for the rules");
            Assert.That(LevelEdit.AuthoredErrors(two), Is.Not.Empty);

            var onFill = new LevelData { fillLeftRows = new[] { 1 }, gates = new[] { new RowGate(1, GateOp.Or, 128) } };
            Assert.That(LevelEdit.AuthoredErrors(onFill), Is.Not.Empty);
        }

        [Test]
        public void Constructor_CopiesTheLevel()
        {
            var source = TestLevels.Heart();
            var e = new LevelEdit(source);
            e.SetWall(0, 0, true);
            Assert.That(source.walls[0], Is.Zero);
        }
    }
}
