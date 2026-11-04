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
                var level = Make(difficulty, seed).Level;
                Assert.That(Move.TryParseList(level.solution, out var moves), Is.True, $"seed {seed}: {level.solution}");
                Assert.That(moves.Count, Is.EqualTo(level.par), $"seed {seed}");
                Assert.That(Solver.Replays(LevelRules.Compile(level), moves), Is.True, $"seed {seed}: {level.solution}");
            }
        }

        [Test]
        public void EveryLevel_FitsInAShareCode([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var level = Make(difficulty, seed).Level;
                Assert.That(ShareCode.CanEncode(level, out string reason), Is.True, $"seed {seed}: {reason}");
                Assert.That(ShareCode.TryDecode(ShareCode.Encode(level), out var back), Is.EqualTo(ShareCodeError.None), $"seed {seed}");
                Assert.That(LevelEdit.SamePuzzle(level, back), Is.True, $"seed {seed}");
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
                var level = Make(RandomDifficulty.Easy, seed).Level;
                Assert.That(level.walls, Is.All.Zero, $"seed {seed}");
                Assert.That(level.pins, Is.All.Zero, $"seed {seed}");
                Assert.That(level.rotateRows, Is.Empty, $"seed {seed}");
                Assert.That(level.chainTopRows, Is.Empty, $"seed {seed}");
            }
        }

        [Test]
        public void NoLevel_UsesRulesThatCannotBeUndone([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var level = Make(difficulty, seed).Level;
                Assert.That(level.gates, Is.Empty, $"seed {seed}");
                Assert.That(level.fillLeftRows, Is.Empty, $"seed {seed}");
                Assert.That(level.fillRightRows, Is.Empty, $"seed {seed}");
                Assert.That(level.lossRule, Is.EqualTo(LossRule.Any), $"seed {seed}");
            }
        }

        [Test]
        public void SameSeed_GivesTheSameLevel([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var first = Make(difficulty, 7).Level;
            var second = Make(difficulty, 7).Level;
            Assert.That(LevelEdit.SameLevel(first, second), Is.True);
            Assert.That(first.solution, Is.EqualTo(second.solution));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentLevels([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var first = Make(difficulty, 1).Level;
            var second = Make(difficulty, 2).Level;
            Assert.That(LevelEdit.SamePuzzle(first, second), Is.False);
        }

        [Test]
        public void SlicedJob_GivesTheSameLevelAsOneGo([ValueSource(nameof(All))] RandomDifficulty difficulty)
        {
            var job = LevelGenerator.Begin(difficulty, 3);
            while (!job.Step(500))
            {
            }

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
