using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.CustomLevels;
using ShiftRegister.Game.Progress;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class CustomLevelTests
    {
        const string TestKey = "ShiftRegister.CustomLevels.Tests";

        /// <summary>One shift right solves Simple; these detours take three moves.</summary>
        static readonly Move[] Quick = { Move.Row(0, Direction.Right) };
        static readonly Move[] Detour = { Move.Row(0, Direction.Left), Move.Row(0, Direction.Right),
            Move.Row(0, Direction.Right) };

        [TearDown]
        public void RemoveTestKey() => PlayerPrefs.DeleteKey(TestKey);

        static LevelData Simple() => new LevelData
        {
            name = "Simple",
            start = new[] { 16, 0, 0, 0, 0, 0, 0, 0 },
            target = new[] { 8, 0, 0, 0, 0, 0, 0, 0 },
            par = 1,
            solution = "R1→"
        };

        [Test]
        public void Add_GivesEachLevelItsOwnCustomId()
        {
            var library = new CustomLevelLibrary();
            var first = library.Add(Simple(), LevelProof.Searched, 1);
            var second = library.Add(Simple(), LevelProof.Searched, 2);
            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, first.level.id);
            Assert.That(first.level.id, Is.Not.EqualTo(second.level.id));
        }

        [Test]
        public void Limit_IsEnforced()
        {
            var library = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                Assert.That(library.Add(Simple(), LevelProof.None, i), Is.Not.Null);
            }

            Assert.That(library.IsFull, Is.True);
            Assert.That(library.Add(Simple(), LevelProof.None, 0), Is.Null);
            Assert.That(library.Duplicate(library.levels[0].level.id, 0), Is.Null);
        }

        [Test]
        public void Duplicate_HasNewIdAndNoBest()
        {
            var library = new CustomLevelLibrary();
            var first = library.Add(Simple(), LevelProof.Searched, 1);
            library.RecordSolve(first.level.id, 3);
            var copy = library.Duplicate(first.level.id, 2);
            Assert.That(copy.level.id, Is.Not.EqualTo(first.level.id));
            Assert.That(copy.bestMoves, Is.Zero);
            Assert.That(copy.proof, Is.EqualTo(LevelProof.Searched));
        }

        [Test]
        public void Update_KeepsBestForARename_ClearsItForANewPuzzle()
        {
            var library = new CustomLevelLibrary();
            var first = library.Add(Simple(), LevelProof.Searched, 1);
            string id = first.level.id;
            library.RecordSolve(id, 2);

            var renamed = LevelEdit.Clone(first.level);
            renamed.name = "New name";
            library.Update(renamed, LevelProof.Searched, 2);
            Assert.That(library.Get(id).bestMoves, Is.EqualTo(2));

            var changed = LevelEdit.Clone(first.level);
            changed.start[7] = 1;
            library.Update(changed, LevelProof.None, 3);
            Assert.That(library.Get(id).bestMoves, Is.Zero);
        }

        [Test]
        public void RecordSolve_KeepsTheFewestMoves()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Simple(), LevelProof.Searched, 1).level.id;
            Assert.That(library.RecordSolve(id, 5), Is.True);
            Assert.That(library.RecordSolve(id, 7), Is.False);
            Assert.That(library.RecordSolve(id, 3), Is.True);
            Assert.That(library.Get(id).bestMoves, Is.EqualTo(3));
        }

        static LevelData Unproven()
        {
            var level = Simple();
            level.par = 0;
            level.solution = "";
            return level;
        }

        [Test]
        public void ApplyWin_WorksOnAnEntryOutsideTheLibrary()
        {
            var entry = new CustomLevel { level = Unproven(), proof = LevelProof.Replayed };
            entry.level.par = 3;
            entry.level.solution = "R1← R1→ R1→";

            var result = CustomLevelLibrary.ApplyWin(entry, Quick, out bool proved);

            Assert.That(proved, Is.False);
            Assert.That(entry.level.par, Is.EqualTo(1), "a better solve lowers a par that is only a known solve");
            Assert.That(entry.bestMoves, Is.EqualTo(1));
            Assert.That(result.IsNewBest, Is.True);
        }

        [Test]
        public void SaveCopy_KeepsProofParAndBest_UnderANewId()
        {
            var library = new CustomLevelLibrary();
            var random = new CustomLevel { level = Simple(), proof = LevelProof.Searched, bestMoves = 2 };

            var saved = library.SaveCopy(random, 5);

            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, saved.level.id);
            Assert.That(saved.proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(saved.level.par, Is.EqualTo(1));
            Assert.That(saved.bestMoves, Is.EqualTo(2));
            Assert.That(library.levels, Has.Count.EqualTo(1));
            Assert.That(saved, Is.Not.SameAs(random));
        }

        [Test]
        public void SaveCopy_IsNull_WhenTheLibraryIsFull()
        {
            var library = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                library.Add(Simple(), LevelProof.Searched, i);
            }

            Assert.That(library.SaveCopy(new CustomLevel { level = Simple(), proof = LevelProof.Searched }, 1),
                Is.Null);
        }

        [Test]
        public void RandomSession_MakesALevelOutsideAnyLibrary_ThatSurvivesSaving()
        {
            var session = new RandomSession();
            session.Start(RandomDifficulty.Medium, 11);
            Assert.That(session.IsGenerating, Is.True);
            while (!session.Tick(1.0))
            {
            }

            Assert.That(session.IsGenerating, Is.False);
            Assert.That(session.Failed, Is.False);
            Assert.That(session.Difficulty, Is.EqualTo(RandomDifficulty.Medium));
            Assert.That(session.Level.proof, Is.EqualTo(LevelProof.Searched));

            // The check a saved level has to pass when it is loaded again.
            var library = new CustomLevelLibrary();
            Assert.That(library.SaveCopy(session.Level, 1), Is.Not.Null);
            Assert.That(library.RemoveInvalid(), Is.Zero);
        }

        [Test]
        public void RandomSession_Cancel_StopsIt_AndAStartForgetsTheOldLevel()
        {
            var session = new RandomSession();
            Assert.That(session.Cancel(), Is.False);

            session.Start(RandomDifficulty.Easy, 1);
            Assert.That(session.Cancel(), Is.True);
            Assert.That(session.IsGenerating, Is.False);
            Assert.That(session.Tick(1.0), Is.False);
            Assert.That(session.Level, Is.Null);

            session.Start(RandomDifficulty.Easy, 1);
            while (!session.Tick(1.0))
            {
            }

            Assert.That(session.Level, Is.Not.Null);
            session.Start(RandomDifficulty.Hard, 2);
            Assert.That(session.Level, Is.Null);
        }

        [Test]
        public void RecordWin_ProvesAnUnprovenLevel()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Unproven(), LevelProof.None, 1).level.id;

            var result = library.RecordWin(id, Detour, out bool proved);

            Assert.That(proved, Is.True);
            var entry = library.Get(id);
            Assert.That(entry.proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(entry.level.par, Is.EqualTo(3));
            Assert.That(entry.level.solution, Is.EqualTo("R1← R1→ R1→"));
            Assert.That(result.Par, Is.EqualTo(3));
            Assert.That(result.Stars, Is.EqualTo(3));
            Assert.That(entry.bestMoves, Is.EqualTo(3));
        }

        [Test]
        public void RecordWin_FewerMovesLowerAPlayedPar_MoreDoNot()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Unproven(), LevelProof.None, 1).level.id;
            library.RecordWin(id, Detour, out _);

            library.RecordWin(id, Quick, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(library.Get(id).level.par, Is.EqualTo(1));

            var result = library.RecordWin(id, Detour, out _);
            Assert.That(library.Get(id).level.par, Is.EqualTo(1));
            Assert.That(result.Stars, Is.LessThan(3));
            Assert.That(library.Get(id).bestMoves, Is.EqualTo(1));
        }

        [Test]
        public void RecordWin_KeepsTheSolversPar()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Simple(), LevelProof.Searched, 1).level.id;
            library.RecordWin(id, Detour, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(library.Get(id).proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(library.Get(id).level.par, Is.EqualTo(1));
            Assert.That(library.Get(id).bestMoves, Is.EqualTo(3));
        }

        [Test]
        public void RecordWin_IgnoresMovesThatDoNotSolve()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Unproven(), LevelProof.None, 1).level.id;
            library.RecordWin(id, new[] { Move.Row(0, Direction.Left) }, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(library.Get(id).proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Duplicate_MarksTheNameAsACopy_WithinTheLimit()
        {
            var library = new CustomLevelLibrary();
            var level = Simple();
            level.name = "Sixteen letters!";
            var copy = library.Duplicate(library.Add(level, LevelProof.Searched, 1).level.id, 2);
            Assert.That(copy.level.name.Length, Is.LessThanOrEqualTo(LevelEdit.MaxNameLength));
            StringAssert.EndsWith(" COPY", copy.level.name);
            Assert.That(LevelEdit.AuthoredErrors(copy.level), Is.Empty);
        }

        [Test]
        public void Import_AddsAProvenLevelUnderANewId()
        {
            var library = new CustomLevelLibrary();
            string code = ShareCode.Encode(Simple());
            var first = library.Import(code, 5, out var error);
            var second = library.Import(code, 6, out _);

            Assert.That(error, Is.EqualTo(ShareCodeError.None));
            Assert.That(first.proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(first.level.par, Is.EqualTo(1));
            Assert.That(first.level.name, Is.EqualTo("Simple"));
            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, first.level.id);
            Assert.That(second.level.id, Is.Not.EqualTo(first.level.id), "the same code twice gives two levels");
        }

        [Test]
        public void Import_ReportsABadCode_AndAddsNothing()
        {
            var library = new CustomLevelLibrary();
            Assert.That(library.Import("nonsense", 1, out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ShareCodeError.NotACode));
            Assert.That(library.levels, Is.Empty);
        }

        [Test]
        public void Import_RefusedWhenFull()
        {
            var library = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                library.Add(Simple(), LevelProof.None, i);
            }

            Assert.That(library.Import(ShareCode.Encode(Simple()), 1, out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ShareCodeError.None));
            Assert.That(library.levels.Count, Is.EqualTo(CustomLevelLibrary.MaxLevels));
        }

        [Test]
        public void EveryImportError_HasWords()
        {
            foreach (ShareCodeError error in System.Enum.GetValues(typeof(ShareCodeError)))
            {
                if (error != ShareCodeError.None)
                {
                    Assert.That(CustomLevelLibrary.ImportMessage(error), Is.Not.Empty, error.ToString());
                }
            }
        }

        [Test]
        public void AWonLevel_CanBeExportedAndImported()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Unproven(), LevelProof.None, 1).level.id;
            Assert.That(ShareCode.CanEncode(library.Get(id).level, out _), Is.False, "not proven yet");

            library.RecordWin(id, Detour, out _);
            var copy = library.Import(ShareCode.Encode(library.Get(id).level), 2, out _);
            Assert.That(copy.level.solution, Is.EqualTo("R1← R1→ R1→"));
            Assert.That(copy.level.par, Is.EqualTo(3));
        }

        [Test]
        public void Remove_TakesTheLevelOut()
        {
            var library = new CustomLevelLibrary();
            string id = library.Add(Simple(), LevelProof.Searched, 1).level.id;
            Assert.That(library.Remove(id), Is.True);
            Assert.That(library.Get(id), Is.Null);
            Assert.That(library.Remove(id), Is.False);
        }

        [Test]
        public void Store_RoundTripsEveryField()
        {
            var level = Simple();
            level.gates = new[] { new RowGate(3, GateOp.Xor, 6) };
            level.rotateRows = new[] { 5 };
            level.lossRule = LossRule.AtMost;
            level.lossCount = 2;
            var library = new CustomLevelLibrary();
            var first = library.Add(level, LevelProof.Replayed, 42);
            library.RecordSolve(first.level.id, 4);
            CustomLevelStore.Save(library, TestKey);

            var back = CustomLevelStore.Load(TestKey);
            var entry = back.Get(first.level.id);
            Assert.That(entry, Is.Not.Null);
            Assert.That(LevelEdit.SameLevel(entry.level, first.level), Is.True);
            Assert.That(entry.level.solution, Is.EqualTo("R1→"));
            Assert.That(entry.proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(entry.bestMoves, Is.EqualTo(4));
            Assert.That(entry.editedTicks, Is.EqualTo(42));
        }

        [Test]
        public void Store_MissingKey_GivesAnEmptyLibrary() =>
            Assert.That(CustomLevelStore.Load(TestKey).levels, Is.Empty);

        [Test]
        public void Store_DropsDamagedEntries()
        {
            var library = new CustomLevelLibrary();
            library.Add(Simple(), LevelProof.Searched, 1);
            var fakeProof = library.Add(Simple(), LevelProof.Searched, 2);
            fakeProof.level.solution = "R1←";
            var badRules = library.Add(Simple(), LevelProof.None, 3);
            badRules.level.pins[6] = 1;
            CustomLevelStore.Save(library, TestKey);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("damaged"));
            Assert.That(CustomLevelStore.Load(TestKey).levels.Count, Is.EqualTo(1));
        }

        [Test]
        public void Library_LeavesPlayerProgressAlone()
        {
            var progress = new PlayerProgress();
            var library = new CustomLevelLibrary();
            string id = library.Add(Simple(), LevelProof.Searched, 1).level.id;
            library.RecordSolve(id, 1);
            Assert.That(progress.levels, Is.Empty);
            Assert.That(progress.IsSolved(id), Is.False);
        }
    }
}
