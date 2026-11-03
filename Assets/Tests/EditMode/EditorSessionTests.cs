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
