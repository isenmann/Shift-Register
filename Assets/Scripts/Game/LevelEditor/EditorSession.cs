using System;
using System.Diagnostics;
using ShiftRegister.Core;

namespace ShiftRegister.Game.LevelEditor
{
    public enum PaintTool { Bit, Wall, Pin, Gate, Erase }

    /// <summary>What the last search found out, when that gave no proof.</summary>
    public enum SolveOutcome { None, TooBig, NoSolution }

    /// <summary>
    /// The level editor without its view: the level, which picture and tool are in use, paint strokes,
    /// and the search that runs in slices across frames. Any edit stops a running search.
    /// </summary>
    public sealed class EditorSession
    {
        /// <summary>Nodes between clock checks; one node expands a few dozen moves at most.</summary>
        const int NodesPerCheck = 64;

        bool strokeOn, strokeValue, storingResult;
        int lastRow, lastColumn;

        public LevelEdit Edit { get; }
        public EditLayer Layer { get; set; } = EditLayer.Start;
        public PaintTool Tool { get; set; } = PaintTool.Bit;

        /// <summary>The operator the gate tool paints with.</summary>
        public GateOp Op { get; set; } = GateOp.And;

        /// <summary>The search in progress, or null.</summary>
        public Solver.SolveJob Job { get; private set; }
        public SolveOutcome Outcome { get; private set; }

        public EditorSession(LevelData level, LevelProof proof = LevelProof.None)
        {
            Edit = new LevelEdit(level, proof);
            Edit.Changed += OnEdited;
        }

        void OnEdited()
        {
            if (storingResult)
            {
                return;
            }

            Job = null;
            Outcome = SolveOutcome.None;
        }

        public static GateOp NextOp(GateOp op) => (GateOp)(((int)op + 1) % 4);

        public void SwapLayer() => Layer = Layer == EditLayer.Start ? EditLayer.Target : EditLayer.Start;

        /// <summary>
        /// The level as the board should show it: the picture being painted as bits, the other one as
        /// dashed ghosts. The view draws start as bits and target as ghosts, so painting the target swaps them.
        /// </summary>
        public LevelData DisplayLevel()
        {
            var level = LevelEdit.Clone(Edit.Level);
            if (Layer == EditLayer.Target)
            {
                var start = level.start;
                level.start = level.target;
                level.target = start;
            }

            return level;
        }

        // ----- Painting -----

        /// <summary>Starts a stroke. The first cell decides whether the stroke paints or clears.</summary>
        public void BeginPaint(int row, int column)
        {
            EndPaint();
            Edit.BeginStroke();
            strokeOn = true;
            strokeValue = !IsOn(row, column);
            lastRow = row;
            lastColumn = column;
            Apply(row, column);
        }

        /// <summary>Continues the stroke to a cell, filling the cells between when the pointer moved fast.</summary>
        public void PaintTo(int row, int column)
        {
            if (!strokeOn || (row == lastRow && column == lastColumn))
            {
                return;
            }

            int steps = Math.Max(Math.Abs(row - lastRow), Math.Abs(column - lastColumn));
            for (int i = 1; i <= steps; i++)
            {
                int stepRow = lastRow
                    + (int)Math.Round((row - lastRow) * (double)i / steps, MidpointRounding.AwayFromZero);
                int stepColumn = lastColumn
                    + (int)Math.Round((column - lastColumn) * (double)i / steps, MidpointRounding.AwayFromZero);
                Apply(stepRow, stepColumn);
            }

            lastRow = row;
            lastColumn = column;
        }

        public void EndPaint()
        {
            if (!strokeOn)
            {
                return;
            }

            strokeOn = false;
            Edit.EndStroke();
        }

        bool IsOn(int row, int column)
        {
            switch (Tool)
            {
                case PaintTool.Bit:
                    return Edit.GetBit(Layer, row, column);
                case PaintTool.Wall:
                    return Edit.IsWall(row, column);
                case PaintTool.Pin:
                    return Edit.IsPin(row, column);
                case PaintTool.Gate:
                    var gate = Edit.GateOn(row);
                    return gate != null && (gate.mask & (0x80 >> column)) != 0;
                default:
                    return false;
            }
        }

        void Apply(int row, int column)
        {
            switch (Tool)
            {
                case PaintTool.Bit:
                    Edit.SetBit(Layer, row, column, strokeValue);
                    break;
                case PaintTool.Wall:
                    Edit.SetWall(row, column, strokeValue);
                    break;
                case PaintTool.Pin:
                    Edit.SetPin(row, column, strokeValue);
                    break;
                case PaintTool.Gate:
                    if (strokeValue)
                    {
                        Edit.SetGateCell(row, column, Op);
                    }
                    else
                    {
                        Edit.ClearGateCell(row, column);
                    }

                    break;
                default:
                    Edit.Erase(row, column);
                    break;
            }
        }

        // ----- Solving -----

        public bool IsSolving => Job != null;
        public bool CanSolve => !IsSolving && Edit.Problems().Count == 0;

        public void StartSolve()
        {
            if (!CanSolve)
            {
                return;
            }

            Outcome = SolveOutcome.None;
            Job = Solver.Begin(LevelRules.Compile(Edit.Level));
        }

        /// <summary>False if no search was running.</summary>
        public bool CancelSolve()
        {
            if (Job == null)
            {
                return false;
            }

            Job = null;
            return true;
        }

        /// <summary>Runs the search for about this long. Returns true on the call that finishes it.</summary>
        public bool Tick(double budgetMs)
        {
            if (Job == null)
            {
                return false;
            }

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (!Job.Step(NodesPerCheck))
                {
                    continue;
                }

                Finish(Job.Result);
                return true;
            }

            return false;
        }

        void Finish(SolveResult result)
        {
            Job = null;
            switch (result.Status)
            {
                case SolveStatus.Solved:
                    storingResult = true;
                    Edit.SetSolution(result.Solution, LevelProof.Searched);
                    storingResult = false;
                    break;
                case SolveStatus.Unsolvable:
                    Outcome = SolveOutcome.NoSolution;
                    break;
                default:
                    Outcome = SolveOutcome.TooBig;
                    break;
            }
        }

        // ----- Words for the view -----

        /// <summary>The goal problem, or what to do next.</summary>
        public string Status(out bool isProblem)
        {
            var problems = Edit.Problems();
            isProblem = problems.Count > 0;
            if (problems.Contains(LevelProblem.SameStartAndTarget))
            {
                return "Start and target are the same picture.";
            }

            if (problems.Contains(LevelProblem.NoMoves))
            {
                return "Pins and walls leave no move at all.";
            }

            if (IsSolving)
            {
                return "Searching. An edit stops the search.";
            }

            if (Outcome == SolveOutcome.TooBig)
            {
                return "The solver gave up. Only test play can prove this level.";
            }

            if (Outcome == SolveOutcome.NoSolution)
            {
                return "Change the start or the target, then solve again.";
            }

            return Edit.Proof == LevelProof.None ? "Ready to solve." : "Proven. Any edit clears the proof.";
        }

        public string ProofTitle
        {
            get
            {
                if (IsSolving)
                {
                    return "SEARCHING";
                }

                if (Outcome == SolveOutcome.TooBig)
                {
                    return "TOO BIG";
                }

                if (Outcome == SolveOutcome.NoSolution)
                {
                    return "NO SOLUTION";
                }

                return Edit.Proof == LevelProof.None ? "NOT PROVEN" : $"PAR {Edit.Level.par}";
            }
        }

        public string ProofText
        {
            get
            {
                if (IsSolving)
                {
                    return $"{Job.StatesVisited:N0} of {Job.MaxStates:N0} states.";
                }

                if (Outcome == SolveOutcome.TooBig)
                {
                    return "Too many states to search. Beat it in test play to prove it.";
                }

                if (Outcome == SolveOutcome.NoSolution)
                {
                    return "The solver tried every state. This target cannot be reached.";
                }

                if (Edit.Proof == LevelProof.Searched)
                {
                    return "Shortest possible. The solver proved it.";
                }

                if (Edit.Proof == LevelProof.Replayed)
                {
                    return "Your best, not proven shortest. Beat it again to lower par.";
                }

                return "Solve finds the shortest way. You can also beat it yourself in test play.";
            }
        }

        /// <summary>How far the state limit is used up, 0..1.</summary>
        public float SolveProgress => Job == null ? 0f : (float)Job.StatesVisited / Job.MaxStates;
    }
}
