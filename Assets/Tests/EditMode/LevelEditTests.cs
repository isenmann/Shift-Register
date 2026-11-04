using System;
using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class LevelEditTests
    {
        static LevelEdit Blank() => LevelEdit.Blank("Test");

        static void AssertValid(LevelEdit edit) =>
            Assert.That(LevelEdit.AuthoredErrors(edit.Level), Is.Empty, string.Join("; ", LevelEdit.AuthoredErrors(edit.Level)));

        [Test]
        public void Wall_ClearsWhatIsUnderIt()
        {
            var edit = Blank();
            edit.SetBit(EditLayer.Start, 2, 3, true);
            edit.SetBit(EditLayer.Target, 2, 3, true);
            edit.SetPin(2, 3, true);
            edit.SetGateCell(2, 3, GateOp.Or);

            edit.SetWall(2, 3, true);

            Assert.That(edit.GetBit(EditLayer.Start, 2, 3), Is.False);
            Assert.That(edit.GetBit(EditLayer.Target, 2, 3), Is.False);
            Assert.That(edit.Level.pins[2], Is.Zero);
            Assert.That(edit.GateOn(2), Is.Null, "the gate's last cell went");
            AssertValid(edit);
        }

        [Test]
        public void Bit_CannotBePaintedOnAWall()
        {
            var edit = Blank();
            edit.SetWall(1, 1, true);
            edit.SetBit(EditLayer.Start, 1, 1, true);
            Assert.That(edit.GetBit(EditLayer.Start, 1, 1), Is.False);
        }

        [Test]
        public void Pin_SetsBothPictures_AndErasingABitRemovesIt()
        {
            var edit = Blank();
            edit.SetPin(4, 4, true);
            Assert.That(edit.GetBit(EditLayer.Start, 4, 4) && edit.GetBit(EditLayer.Target, 4, 4), Is.True);

            edit.SetBit(EditLayer.Target, 4, 4, false);
            Assert.That(edit.Level.pins[4], Is.Zero);
            Assert.That(edit.GetBit(EditLayer.Start, 4, 4), Is.True, "only the pin and that picture's bit go");
            AssertValid(edit);
        }

        [Test]
        public void Wall_BreaksChainOnItsRow_AndEndCellWallTakesFillBack()
        {
            var edit = Blank();
            edit.SetChain(2, true);
            edit.SetWall(3, 4, true);
            Assert.That(edit.ChainTopOf(2), Is.EqualTo(-1));

            edit.SetFill(5, true, true);
            edit.SetWall(5, 0, true);
            Assert.That(edit.HasFill(5, true), Is.False);
            AssertValid(edit);
        }

        [Test]
        public void Chain_TakesBackOverlappingChainWallsAndInnerFills()
        {
            var edit = Blank();
            edit.SetChain(2, true);
            edit.SetWall(4, 0, true);
            edit.SetFill(3, false, true);
            edit.SetChain(3, true);

            Assert.That(edit.ChainTopOf(2), Is.EqualTo(-1));
            Assert.That(edit.ChainTopOf(3), Is.EqualTo(3));
            Assert.That(edit.Level.walls[4], Is.Zero);
            Assert.That(edit.HasFill(3, false), Is.False, "the top row's right edge is inside the chain");
            AssertValid(edit);
        }

        [Test]
        public void Chain_RotatesTogether()
        {
            var edit = Blank();
            edit.SetRotate(1, true);
            edit.SetChain(1, true);
            Assert.That(edit.IsRotating(1) && edit.IsRotating(2), Is.True);

            edit.SetRotate(2, false);
            Assert.That(edit.IsRotating(1) || edit.IsRotating(2), Is.False);
            AssertValid(edit);
        }

        [Test]
        public void RotateAndFill_TakeEachOtherBack()
        {
            var edit = Blank();
            edit.SetFill(0, true, true);
            edit.SetRotate(0, true);
            Assert.That(edit.HasFill(0, true), Is.False);

            edit.SetFill(0, false, true);
            Assert.That(edit.IsRotating(0), Is.False);
            AssertValid(edit);
        }

        [Test]
        public void Gate_OnePerRow_LastOpWins()
        {
            var edit = Blank();
            edit.SetGateCell(3, 2, GateOp.And);
            edit.SetGateCell(3, 5, GateOp.Xor);
            Assert.That(edit.Level.gates.Length, Is.EqualTo(1));
            Assert.That(edit.GateOn(3).op, Is.EqualTo(GateOp.Xor));
            Assert.That(edit.GateOn(3).mask, Is.EqualTo(TestLevels.Bits("00100100")));

            edit.ClearGateCell(3, 2);
            edit.ClearGateCell(3, 5);
            Assert.That(edit.GateOn(3), Is.Null);
        }

        [Test]
        public void Gate_And1In_NeverShareACell()
        {
            var edit = Blank();
            edit.SetFill(1, true, true);
            edit.SetGateCell(1, 0, GateOp.Or);
            Assert.That(edit.HasFill(1, true), Is.False);

            edit.SetFill(1, true, true);
            Assert.That(edit.GateOn(1), Is.Null, "the 1-IN edge took the gate's only cell");
            AssertValid(edit);
        }

        [Test]
        public void AnyEditSequence_StaysValid()
        {
            var random = new Random(12345);
            var edit = Blank();
            for (int i = 0; i < 20000; i++)
            {
                int row = random.Next(8), column = random.Next(8);
                bool on = random.Next(2) == 0;
                switch (random.Next(14))
                {
                    case 0:
                        edit.SetBit(EditLayer.Start, row, column, on);
                        break;
                    case 1:
                        edit.SetBit(EditLayer.Target, row, column, on);
                        break;
                    case 2:
                        edit.SetWall(row, column, on);
                        break;
                    case 3:
                        edit.SetPin(row, column, on);
                        break;
                    case 4:
                        edit.Erase(row, column);
                        break;
                    case 5:
                        edit.SetRotate(row, on);
                        break;
                    case 6:
                        edit.SetChain(random.Next(7), on);
                        break;
                    case 7:
                        edit.SetFill(row, on, random.Next(2) == 0);
                        break;
                    case 8:
                        edit.SetGateCell(row, column, (GateOp)random.Next(4));
                        break;
                    case 9:
                        edit.ClearGateCell(row, column);
                        break;
                    case 10:
                        edit.SetGateOp(row, (GateOp)random.Next(4));
                        break;
                    case 11:
                        edit.RemoveGate(row);
                        break;
                    case 12:
                        edit.SetLoss((LossRule)random.Next(3), random.Next(100));
                        break;
                    case 13:
                        if (on)
                        {
                            edit.Undo();
                        }
                        else
                        {
                            edit.Redo();
                        }

                        break;
                }

                var errors = LevelEdit.AuthoredErrors(edit.Level);
                Assert.That(errors, Is.Empty, $"step {i}: {string.Join("; ", errors)}");
            }
        }

        [Test]
        public void RealChangesClearProof_NoOpsAndRenamesDoNot()
        {
            var edit = Blank();
            edit.SetBit(EditLayer.Start, 0, 0, true);
            edit.SetBit(EditLayer.Target, 0, 1, true);
            edit.SetSolution(new[] { Move.Row(0, Direction.Right) }, LevelProof.Searched);
            Assert.That(edit.Level.par, Is.EqualTo(1));

            edit.SetBit(EditLayer.Start, 0, 0, true);
            edit.SetName("New name");
            Assert.That(edit.Level.par, Is.EqualTo(1));

            edit.SetBit(EditLayer.Start, 7, 7, true);
            Assert.That(edit.Level.par, Is.Zero);
            Assert.That(edit.Level.solution, Is.Empty);
            Assert.That(edit.Proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Undo_BringsTheProofBack_RedoTakesItAway()
        {
            var edit = Blank();
            edit.SetBit(EditLayer.Start, 0, 0, true);
            edit.SetBit(EditLayer.Target, 0, 1, true);
            edit.SetSolution(new[] { Move.Row(0, Direction.Right) }, LevelProof.Replayed);

            edit.SetBit(EditLayer.Start, 7, 7, true);
            edit.Undo();
            Assert.That(edit.Proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(edit.Level.solution, Is.EqualTo("R1→"));

            edit.Redo();
            Assert.That(edit.Proof, Is.EqualTo(LevelProof.None));
            Assert.That(edit.Level.solution, Is.Empty);
        }

        [Test]
        public void UndoRedo_RestoreExactly()
        {
            var edit = Blank();
            edit.SetBit(EditLayer.Start, 0, 0, true);
            var one = LevelEdit.Clone(edit.Level);
            edit.SetWall(0, 0, true);
            edit.SetRotate(3, true);

            Assert.That(edit.Undo(), Is.True);
            Assert.That(edit.Undo(), Is.True);
            Assert.That(LevelEdit.SameLevel(edit.Level, one), Is.True);

            Assert.That(edit.Redo(), Is.True);
            Assert.That(edit.Level.walls[0], Is.Not.Zero);

            edit.SetBit(EditLayer.Target, 5, 5, true);
            Assert.That(edit.CanRedo, Is.False, "a new edit drops the redo steps");
        }

        [Test]
        public void Stroke_IsOneUndoStep()
        {
            var edit = Blank();
            edit.BeginStroke();
            for (int column = 0; column < 8; column++)
            {
                edit.SetBit(EditLayer.Start, 2, column, true);
            }

            edit.EndStroke();
            Assert.That(edit.Level.start[2], Is.EqualTo(255));

            edit.Undo();
            Assert.That(edit.Level.start[2], Is.Zero);
            Assert.That(edit.CanUndo, Is.False);
        }

        [Test]
        public void EmptyStroke_LeavesNoUndoStep()
        {
            var edit = Blank();
            edit.BeginStroke();
            edit.SetBit(EditLayer.Start, 2, 2, false);
            edit.EndStroke();
            Assert.That(edit.CanUndo, Is.False);
        }

        [Test]
        public void Problems_ReportSameBoardsAndNoMoves()
        {
            var edit = Blank();
            Assert.That(edit.Problems(), Does.Contain(LevelProblem.SameStartAndTarget));
            edit.SetBit(EditLayer.Target, 0, 0, true);
            Assert.That(edit.Problems(), Is.Empty);

            for (int i = 0; i < 8; i++)
            {
                edit.SetPin(i, i, true);
            }

            Assert.That(edit.Problems(), Does.Contain(LevelProblem.NoMoves));
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
            var edit = new LevelEdit(source);
            edit.SetWall(0, 0, true);
            Assert.That(source.walls[0], Is.Zero);
        }
    }
}
