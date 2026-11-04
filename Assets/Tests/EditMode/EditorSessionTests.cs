using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.LevelEditor;

namespace ShiftRegister.Tests
{
    public class EditorSessionTests
    {
        static EditorSession Blank() => new EditorSession(new LevelData { name = "T" });

        /// <summary>A bit that needs one shift right.</summary>
        static EditorSession OneMove()
        {
            var session = Blank();
            session.Edit.SetBit(EditLayer.Start, 0, 3, true);
            session.Edit.SetBit(EditLayer.Target, 0, 4, true);
            return session;
        }

        static void RunToEnd(EditorSession session)
        {
            while (!session.Tick(50))
            {
            }
        }

        [Test]
        public void ClosingATextField_StillCountsAsTyping_SoItsKeyIsNoShortcut()
        {
            Ui.NoteTypingEnded();
            Assert.That(Ui.IsTyping, Is.True);
        }

        [TestCase(1, "1 move")]
        [TestCase(0, "0 moves")]
        [TestCase(6, "6 moves")]
        public void MoveCount_ReadsRight(int count, string expected) =>
            Assert.That(Ui.Moves(count), Is.EqualTo(expected));

        [Test]
        public void Stroke_FirstCellDecidesPaintOrClear()
        {
            var session = Blank();
            session.BeginPaint(1, 0);
            session.PaintTo(1, 1);
            session.EndPaint();
            Assert.That(session.Edit.Level.start[1], Is.EqualTo(TestLevels.Bits("11000000")));

            session.BeginPaint(1, 0); // on a bit: this stroke clears
            session.PaintTo(1, 2);    // skips over column 1 and an empty cell
            session.EndPaint();
            Assert.That(session.Edit.Level.start[1], Is.Zero);
        }

        [Test]
        public void Stroke_FillsCellsBetweenFastMoves_AndIsOneUndoStep()
        {
            var session = Blank();
            session.BeginPaint(0, 0);
            session.PaintTo(0, 7);
            session.PaintTo(7, 7);
            session.EndPaint();
            Assert.That(session.Edit.Level.start[0], Is.EqualTo(255));
            for (int row = 1; row < 8; row++)
            {
                Assert.That(session.Edit.Level.start[row], Is.EqualTo(1), $"row {row}");
            }

            session.Edit.Undo();
            Assert.That(session.Edit.Level.start[0], Is.Zero);
            Assert.That(session.Edit.CanUndo, Is.False);
        }

        [Test]
        public void Stroke_PaintsTheChosenLayerAndTool()
        {
            var session = Blank();
            session.Layer = EditLayer.Target;
            session.BeginPaint(2, 2);
            session.EndPaint();
            Assert.That(session.Edit.GetBit(EditLayer.Target, 2, 2), Is.True);
            Assert.That(session.Edit.GetBit(EditLayer.Start, 2, 2), Is.False);

            session.Tool = PaintTool.Wall;
            session.BeginPaint(2, 2);
            session.EndPaint();
            Assert.That(session.Edit.IsWall(2, 2), Is.True);
            Assert.That(session.Edit.GetBit(EditLayer.Target, 2, 2), Is.False);

            session.Tool = PaintTool.Erase;
            session.BeginPaint(2, 2);
            session.EndPaint();
            Assert.That(session.Edit.IsWall(2, 2), Is.False);
        }

        [Test]
        public void GateTool_PaintsWithTheChosenOp_AndClearsOnASecondStroke()
        {
            var session = Blank();
            session.Tool = PaintTool.Gate;
            session.Op = GateOp.Xor;
            session.BeginPaint(3, 1);
            session.PaintTo(3, 4);
            session.EndPaint();
            Assert.That(session.Edit.GateOn(3).op, Is.EqualTo(GateOp.Xor));
            Assert.That(session.Edit.GateOn(3).mask, Is.EqualTo(TestLevels.Bits("01111000")));

            session.BeginPaint(3, 1);
            session.PaintTo(3, 4);
            session.EndPaint();
            Assert.That(session.Edit.GateOn(3), Is.Null, "the first cell was on, so this stroke clears");
        }

        [Test]
        public void GateTool_DraggingAcrossRows_GivesEachRowItsOwnGate()
        {
            var session = Blank();
            session.Tool = PaintTool.Gate;
            session.BeginPaint(1, 2);
            session.PaintTo(2, 2);
            session.EndPaint();
            Assert.That(session.Edit.Level.gates.Length, Is.EqualTo(2));
            Assert.That(LevelEdit.AuthoredErrors(session.Edit.Level), Is.Empty);
        }

        [Test]
        public void NextOp_GoesRoundAllFour()
        {
            var op = GateOp.And;
            for (int i = 0; i < 4; i++)
            {
                op = EditorSession.NextOp(op);
            }

            Assert.That(op, Is.EqualTo(GateOp.And));
            Assert.That(EditorSession.NextOp(GateOp.And), Is.Not.EqualTo(GateOp.And));
        }

        [Test]
        public void AFullyDressedLevel_SolvesAndSharesThroughTheSession()
        {
            var session = OneMove(); // row 1: one shift right
            session.Edit.SetRotate(2, true);
            session.Edit.SetChain(4, true);
            session.Edit.SetFill(7, true, true);
            session.Edit.SetGateCell(6, 3, GateOp.Xor);
            session.Edit.SetGatesOnColumnMoves(true);
            session.Edit.SetLoss(LossRule.AtMost, 2);
            foreach (var layer in new[] { EditLayer.Start, EditLayer.Target })
            {
                session.Layer = layer;
                Assert.DoesNotThrow(() => LevelRules.Compile(session.DisplayLevel()), layer.ToString());
            }

            session.StartSolve();
            RunToEnd(session);
            Assert.That(session.Edit.Proof, Is.EqualTo(LevelProof.Searched), session.ProofText);

            string code = ShareCode.Encode(session.Edit.Level);
            Assert.That(ShareCode.TryDecode(code, out var back), Is.EqualTo(ShareCodeError.None));
            Assert.That(LevelEdit.SameLevel(back, session.Edit.Level), Is.True);
        }

        [Test]
        public void DisplayLevel_ShowsThePaintedPictureAsBits_AndAlwaysCompiles()
        {
            var session = OneMove();
            session.Tool = PaintTool.Pin;
            session.BeginPaint(6, 6);
            session.EndPaint();

            Assert.That(session.DisplayLevel().start, Is.EqualTo(session.Edit.Level.start));
            session.SwapLayer();
            Assert.That(session.DisplayLevel().start, Is.EqualTo(session.Edit.Level.target));
            Assert.That(session.DisplayLevel().target, Is.EqualTo(session.Edit.Level.start));
            Assert.DoesNotThrow(() => LevelRules.Compile(session.DisplayLevel()));
        }

        [Test]
        public void Solve_StoresParSolutionAndProof()
        {
            var session = OneMove();
            Assert.That(session.ProofTitle, Is.EqualTo("NOT PROVEN"));
            session.StartSolve();
            Assert.That(session.IsSolving, Is.True);
            Assert.That(session.ProofTitle, Is.EqualTo("SEARCHING"));
            RunToEnd(session);

            Assert.That(session.IsSolving, Is.False);
            Assert.That(session.Edit.Proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(session.Edit.Level.par, Is.EqualTo(1));
            Assert.That(session.ProofTitle, Is.EqualTo("PAR 1"));
        }

        [Test]
        public void Solve_ReportsNoSolution_ThenAnEditClearsIt()
        {
            var session = Blank();
            session.Edit.SetBit(EditLayer.Target, 0, 0, true); // nothing can create the bit
            session.StartSolve();
            RunToEnd(session);
            Assert.That(session.Outcome, Is.EqualTo(SolveOutcome.NoSolution));
            Assert.That(session.ProofTitle, Is.EqualTo("NO SOLUTION"));
            Assert.That(session.Status(out _), Does.Not.Contain("Ready"),
                "the status line must agree with the proof panel");

            session.Edit.SetBit(EditLayer.Start, 0, 1, true);
            Assert.That(session.Outcome, Is.EqualTo(SolveOutcome.None));
            Assert.That(session.Status(out _), Does.Contain("Ready"));
        }

        [Test]
        public void Edit_StopsARunningSearch_AndClearsTheProof()
        {
            var session = OneMove();
            session.StartSolve();
            session.Edit.SetBit(EditLayer.Start, 5, 5, true);
            Assert.That(session.IsSolving, Is.False);

            session = OneMove();
            session.StartSolve();
            RunToEnd(session);
            session.Edit.SetBit(EditLayer.Start, 5, 5, true);
            Assert.That(session.Edit.Proof, Is.EqualTo(LevelProof.None));
            Assert.That(session.Edit.Level.par, Is.Zero);
        }

        [Test]
        public void Cancel_StopsTheSearch_WithoutAResult()
        {
            var session = OneMove();
            Assert.That(session.CancelSolve(), Is.False);
            session.StartSolve();
            Assert.That(session.CancelSolve(), Is.True);
            Assert.That(session.IsSolving, Is.False);
            Assert.That(session.Edit.Proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Solve_RefusedWhileAGoalProblemStands()
        {
            var session = Blank();
            session.Status(out bool problem);
            Assert.That(problem, Is.True);
            Assert.That(session.CanSolve, Is.False);
            session.StartSolve();
            Assert.That(session.IsSolving, Is.False);
        }
    }
}
