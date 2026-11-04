using System;
using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class LevelGeneratorTests
    {
        /// <summary>Few on purpose: searching is slow in the editor, and every test here makes levels.</summary>
        const int Seeds = 5;

        static readonly RandomDifficulty[] All = { RandomDifficulty.Easy, RandomDifficulty.Medium, RandomDifficulty.Hard };

        static GeneratedLevel Make(RandomDifficulty difficulty, int seed)
        {
            var made = LevelGenerator.Generate(difficulty, seed);
            Assert.That(made, Is.Not.Null, $"{difficulty} seed {seed} found no level");
            return made;
        }

        [Test]
        public void EveryLevel_PassesTheEditorsRules_AndHasSomethingToPlay([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var made = Make(difficulty, seed);
                Assert.That(LevelEdit.AuthoredErrors(made.Level), Is.Empty, $"seed {seed}");
                Assert.That(new LevelEdit(made.Level, made.Proof).Problems(), Is.Empty, $"seed {seed}");
                Assert.That(LevelEdit.SanitizeName(made.Level.name), Is.EqualTo(made.Level.name), $"seed {seed}");
            }
        }

        [Test]
        public void EveryLevel_IsSolvedByItsOwnSolution_InParMoves([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var d = Make(difficulty, seed).Level;
                Assert.That(Move.TryParseList(d.solution, out var moves), Is.True, $"seed {seed}: {d.solution}");
                Assert.That(moves.Count, Is.EqualTo(d.par), $"seed {seed}");
                Assert.That(Solver.Replays(LevelRules.Compile(d), moves), Is.True, $"seed {seed}: {d.solution}");
            }
        }

        [Test]
        public void EveryLevel_FitsInAShareCode([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var d = Make(difficulty, seed).Level;
                Assert.That(ShareCode.CanEncode(d, out string why), Is.True, $"seed {seed}: {why}");
                Assert.That(ShareCode.TryDecode(ShareCode.Encode(d), out var back), Is.EqualTo(ShareCodeError.None), $"seed {seed}");
                Assert.That(LevelEdit.SamePuzzle(d, back), Is.True, $"seed {seed}");
            }
        }

        [Test]
        public void EasyAndMedium_ParIsTheTrueMinimum([Values(RandomDifficulty.Easy, RandomDifficulty.Medium)] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var made = Make(difficulty, seed);
                Assert.That(made.Proof, Is.EqualTo(LevelProof.Searched), $"seed {seed}");
                Assert.That(Solver.Solve(made.Level).Par, Is.EqualTo(made.Level.par), $"seed {seed}");
            }
        }

        [Test]
        public void ParStaysInEachDifficultysRange()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                Assert.That(Make(RandomDifficulty.Easy, seed).Level.par, Is.InRange(3, 5), $"easy seed {seed}");
                Assert.That(Make(RandomDifficulty.Medium, seed).Level.par, Is.InRange(5, 7), $"medium seed {seed}");
                Assert.That(Make(RandomDifficulty.Hard, seed).Level.par, Is.InRange(8, 11), $"hard seed {seed}");
            }
        }

        [Test]
        public void EasyLevels_HaveNoExtraRules()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var d = Make(RandomDifficulty.Easy, seed).Level;
                Assert.That(d.walls, Is.All.Zero, $"seed {seed}");
                Assert.That(d.pins, Is.All.Zero, $"seed {seed}");
                Assert.That(d.rotateRows, Is.Empty, $"seed {seed}");
                Assert.That(d.chainTopRows, Is.Empty, $"seed {seed}");
            }
        }

        [Test]
        public void NoLevel_UsesRulesThatCannotBeUndone([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var d = Make(difficulty, seed).Level;
                Assert.That(d.gates, Is.Empty, $"seed {seed}");
                Assert.That(d.fillLeftRows, Is.Empty, $"seed {seed}");
                Assert.That(d.fillRightRows, Is.Empty, $"seed {seed}");
                Assert.That(d.lossRule, Is.EqualTo(LossRule.Any), $"seed {seed}");
            }
        }

        [Test]
        public void SameSeed_GivesTheSameLevel([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var a = Make(difficulty, 7).Level;
            var b = Make(difficulty, 7).Level;
            Assert.That(LevelEdit.SameLevel(a, b), Is.True);
            Assert.That(a.solution, Is.EqualTo(b.solution));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentLevels([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var a = Make(difficulty, 1).Level;
            var b = Make(difficulty, 2).Level;
            Assert.That(LevelEdit.SamePuzzle(a, b), Is.False);
        }

        [Test]
        public void SlicedJob_GivesTheSameLevelAsOneGo([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var job = LevelGenerator.Begin(difficulty, 3);
            while (!job.Step(500)) { }
            Assert.That(job.IsDone, Is.True);
            Assert.That(job.Result, Is.Not.Null);

            var whole = Make(difficulty, 3);
            Assert.That(LevelEdit.SameLevel(job.Result.Level, whole.Level), Is.True);
            Assert.That(job.Result.Proof, Is.EqualTo(whole.Proof));
            Assert.That(job.Result.Attempts, Is.EqualTo(whole.Attempts));
        }

        [Test]
        public void UnknownDifficulty_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerator.Begin((RandomDifficulty)99, 1));
        }
    }
}
