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
            var s = Blank();
            s.Edit.SetBit(EditLayer.Start, 0, 3, true);
            s.Edit.SetBit(EditLayer.Target, 0, 4, true);
            return s;
        }

        static void RunToEnd(EditorSession s)
        {
            while (!s.Tick(50)) { }
        }

        [Test]
        public void ClosingATextField_StillCountsAsTyping_SoItsKeyIsNoShortcut()
        {
            Ui.NoteTypingEnded();
            Assert.That(Ui.IsTyping, Is.True);
        }

        [Test]
        public void Stroke_FirstCellDecidesPaintOrClear()
        {
            var s = Blank();
            s.BeginPaint(1, 0);
            s.PaintTo(1, 1);
            s.EndPaint();
            Assert.That(s.Edit.Level.start[1], Is.EqualTo(TestLevels.Bits("11000000")));

            s.BeginPaint(1, 0); // on a bit: this stroke clears
            s.PaintTo(1, 2);    // skips over column 1 and an empty cell
            s.EndPaint();
            Assert.That(s.Edit.Level.start[1], Is.Zero);
        }

        [Test]
        public void Stroke_FillsCellsBetweenFastMoves_AndIsOneUndoStep()
        {
            var s = Blank();
            s.BeginPaint(0, 0);
            s.PaintTo(0, 7);
            s.PaintTo(7, 7);
            s.EndPaint();
            Assert.That(s.Edit.Level.start[0], Is.EqualTo(255));
            for (int r = 1; r < 8; r++) Assert.That(s.Edit.Level.start[r], Is.EqualTo(1), $"row {r}");

            s.Edit.Undo();
            Assert.That(s.Edit.Level.start[0], Is.Zero);
            Assert.That(s.Edit.CanUndo, Is.False);
        }

        [Test]
        public void Stroke_PaintsTheChosenLayerAndTool()
        {
            var s = Blank();
            s.Layer = EditLayer.Target;
            s.BeginPaint(2, 2);
            s.EndPaint();
            Assert.That(s.Edit.GetBit(EditLayer.Target, 2, 2), Is.True);
            Assert.That(s.Edit.GetBit(EditLayer.Start, 2, 2), Is.False);

            s.Tool = PaintTool.Wall;
            s.BeginPaint(2, 2);
            s.EndPaint();
            Assert.That(s.Edit.IsWall(2, 2), Is.True);
            Assert.That(s.Edit.GetBit(EditLayer.Target, 2, 2), Is.False);

            s.Tool = PaintTool.Erase;
            s.BeginPaint(2, 2);
            s.EndPaint();
            Assert.That(s.Edit.IsWall(2, 2), Is.False);
        }

        [Test]
        public void GateTool_PaintsWithTheChosenOp_AndClearsOnASecondStroke()
        {
            var s = Blank();
            s.Tool = PaintTool.Gate;
            s.Op = GateOp.Xor;
            s.BeginPaint(3, 1);
            s.PaintTo(3, 4);
            s.EndPaint();
            Assert.That(s.Edit.GateOn(3).op, Is.EqualTo(GateOp.Xor));
            Assert.That(s.Edit.GateOn(3).mask, Is.EqualTo(TestLevels.Bits("01111000")));

            s.BeginPaint(3, 1);
            s.PaintTo(3, 4);
            s.EndPaint();
            Assert.That(s.Edit.GateOn(3), Is.Null, "the first cell was on, so this stroke clears");
        }

        [Test]
        public void GateTool_DraggingAcrossRows_GivesEachRowItsOwnGate()
        {
            var s = Blank();
            s.Tool = PaintTool.Gate;
            s.BeginPaint(1, 2);
            s.PaintTo(2, 2);
            s.EndPaint();
            Assert.That(s.Edit.Level.gates.Length, Is.EqualTo(2));
            Assert.That(LevelEdit.AuthoredErrors(s.Edit.Level), Is.Empty);
        }

        [Test]
        public void NextOp_GoesRoundAllFour()
        {
            var op = GateOp.And;
            for (int i = 0; i < 4; i++) op = EditorSession.NextOp(op);
            Assert.That(op, Is.EqualTo(GateOp.And));
            Assert.That(EditorSession.NextOp(GateOp.And), Is.Not.EqualTo(GateOp.And));
        }

        [Test]
        public void AFullyDressedLevel_SolvesAndSharesThroughTheSession()
        {
            var s = OneMove(); // row 1: one shift right
            s.Edit.SetRotate(2, true);
            s.Edit.SetChain(4, true);
            s.Edit.SetFill(7, true, true);
            s.Edit.SetGateCell(6, 3, GateOp.Xor);
            s.Edit.SetGatesOnColumnMoves(true);
            s.Edit.SetLoss(LossRule.AtMost, 2);
            foreach (var layer in new[] { EditLayer.Start, EditLayer.Target })
            {
                s.Layer = layer;
                Assert.DoesNotThrow(() => LevelRules.Compile(s.DisplayLevel()), layer.ToString());
            }

            s.StartSolve();
            RunToEnd(s);
            Assert.That(s.Edit.Proof, Is.EqualTo(LevelProof.Searched), s.ProofText);

            string code = ShareCode.Encode(s.Edit.Level);
            Assert.That(ShareCode.TryDecode(code, out var back), Is.EqualTo(ShareCodeError.None));
            Assert.That(LevelEdit.SameLevel(back, s.Edit.Level), Is.True);
        }

        [Test]
        public void DisplayLevel_ShowsThePaintedPictureAsBits_AndAlwaysCompiles()
        {
            var s = OneMove();
            s.Tool = PaintTool.Pin;
            s.BeginPaint(6, 6);
            s.EndPaint();

            Assert.That(s.DisplayLevel().start, Is.EqualTo(s.Edit.Level.start));
            s.SwapLayer();
            Assert.That(s.DisplayLevel().start, Is.EqualTo(s.Edit.Level.target));
            Assert.That(s.DisplayLevel().target, Is.EqualTo(s.Edit.Level.start));
            Assert.DoesNotThrow(() => LevelRules.Compile(s.DisplayLevel()));
        }

        [Test]
        public void Solve_StoresParSolutionAndProof()
        {
            var s = OneMove();
            Assert.That(s.ProofTitle, Is.EqualTo("NOT PROVEN"));
            s.StartSolve();
            Assert.That(s.IsSolving, Is.True);
            Assert.That(s.ProofTitle, Is.EqualTo("SEARCHING"));
            RunToEnd(s);

            Assert.That(s.IsSolving, Is.False);
            Assert.That(s.Edit.Proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(s.Edit.Level.par, Is.EqualTo(1));
            Assert.That(s.ProofTitle, Is.EqualTo("PAR 1"));
        }

        [Test]
        public void Solve_ReportsNoSolution_ThenAnEditClearsIt()
        {
            var s = Blank();
            s.Edit.SetBit(EditLayer.Target, 0, 0, true); // nothing can create the bit
            s.StartSolve();
            RunToEnd(s);
            Assert.That(s.Outcome, Is.EqualTo(SolveOutcome.NoSolution));
            Assert.That(s.ProofTitle, Is.EqualTo("NO SOLUTION"));
            Assert.That(s.Status(out _), Does.Not.Contain("Ready"), "the status line must agree with the proof panel");

            s.Edit.SetBit(EditLayer.Start, 0, 1, true);
            Assert.That(s.Outcome, Is.EqualTo(SolveOutcome.None));
            Assert.That(s.Status(out _), Does.Contain("Ready"));
        }

        [Test]
        public void Edit_StopsARunningSearch_AndClearsTheProof()
        {
            var s = OneMove();
            s.StartSolve();
            s.Edit.SetBit(EditLayer.Start, 5, 5, true);
            Assert.That(s.IsSolving, Is.False);

            s = OneMove();
            s.StartSolve();
            RunToEnd(s);
            s.Edit.SetBit(EditLayer.Start, 5, 5, true);
            Assert.That(s.Edit.Proof, Is.EqualTo(LevelProof.None));
            Assert.That(s.Edit.Level.par, Is.Zero);
        }

        [Test]
        public void Cancel_StopsTheSearch_WithoutAResult()
        {
            var s = OneMove();
            Assert.That(s.CancelSolve(), Is.False);
            s.StartSolve();
            Assert.That(s.CancelSolve(), Is.True);
            Assert.That(s.IsSolving, Is.False);
            Assert.That(s.Edit.Proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Solve_RefusedWhileAGoalProblemStands()
        {
            var s = Blank();
            s.Status(out bool problem);
            Assert.That(problem, Is.True);
            Assert.That(s.CanSolve, Is.False);
            s.StartSolve();
            Assert.That(s.IsSolving, Is.False);
        }
    }
}
