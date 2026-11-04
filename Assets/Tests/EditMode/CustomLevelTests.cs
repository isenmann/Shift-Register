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
            var lib = new CustomLevelLibrary();
            var a = lib.Add(Simple(), LevelProof.Searched, 1);
            var b = lib.Add(Simple(), LevelProof.Searched, 2);
            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, a.level.id);
            Assert.That(a.level.id, Is.Not.EqualTo(b.level.id));
        }

        [Test]
        public void Limit_IsEnforced()
        {
            var lib = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                Assert.That(lib.Add(Simple(), LevelProof.None, i), Is.Not.Null);
            }

            Assert.That(lib.IsFull, Is.True);
            Assert.That(lib.Add(Simple(), LevelProof.None, 0), Is.Null);
            Assert.That(lib.Duplicate(lib.levels[0].level.id, 0), Is.Null);
        }

        [Test]
        public void Duplicate_HasNewIdAndNoBest()
        {
            var lib = new CustomLevelLibrary();
            var a = lib.Add(Simple(), LevelProof.Searched, 1);
            lib.RecordSolve(a.level.id, 3);
            var copy = lib.Duplicate(a.level.id, 2);
            Assert.That(copy.level.id, Is.Not.EqualTo(a.level.id));
            Assert.That(copy.bestMoves, Is.Zero);
            Assert.That(copy.proof, Is.EqualTo(LevelProof.Searched));
        }

        [Test]
        public void Update_KeepsBestForARename_ClearsItForANewPuzzle()
        {
            var lib = new CustomLevelLibrary();
            var a = lib.Add(Simple(), LevelProof.Searched, 1);
            string id = a.level.id;
            lib.RecordSolve(id, 2);

            var renamed = LevelEdit.Clone(a.level);
            renamed.name = "New name";
            lib.Update(renamed, LevelProof.Searched, 2);
            Assert.That(lib.Get(id).bestMoves, Is.EqualTo(2));

            var changed = LevelEdit.Clone(a.level);
            changed.start[7] = 1;
            lib.Update(changed, LevelProof.None, 3);
            Assert.That(lib.Get(id).bestMoves, Is.Zero);
        }

        [Test]
        public void RecordSolve_KeepsTheFewestMoves()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Simple(), LevelProof.Searched, 1).level.id;
            Assert.That(lib.RecordSolve(id, 5), Is.True);
            Assert.That(lib.RecordSolve(id, 7), Is.False);
            Assert.That(lib.RecordSolve(id, 3), Is.True);
            Assert.That(lib.Get(id).bestMoves, Is.EqualTo(3));
        }

        static LevelData Unproven()
        {
            var d = Simple();
            d.par = 0;
            d.solution = "";
            return d;
        }

        /// <summary>One shift right solves Simple; these detours take three moves.</summary>
        static readonly Move[] Quick = { Move.Row(0, Direction.Right) };
        static readonly Move[] Detour = { Move.Row(0, Direction.Left), Move.Row(0, Direction.Right), Move.Row(0, Direction.Right) };

        [Test]
        public void ApplyWin_WorksOnAnEntryOutsideTheLibrary()
        {
            var entry = new CustomLevel { level = Unproven(), proof = LevelProof.Replayed };
            entry.level.par = 3;
            entry.level.solution = "R1← R1→ R1→";

            var r = CustomLevelLibrary.ApplyWin(entry, Quick, out bool proved);

            Assert.That(proved, Is.False);
            Assert.That(entry.level.par, Is.EqualTo(1), "a better solve lowers a par that is only a known solve");
            Assert.That(entry.bestMoves, Is.EqualTo(1));
            Assert.That(r.IsNewBest, Is.True);
        }

        [Test]
        public void SaveCopy_KeepsProofParAndBest_UnderANewId()
        {
            var lib = new CustomLevelLibrary();
            var random = new CustomLevel { level = Simple(), proof = LevelProof.Searched, bestMoves = 2 };

            var saved = lib.SaveCopy(random, 5);

            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, saved.level.id);
            Assert.That(saved.proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(saved.level.par, Is.EqualTo(1));
            Assert.That(saved.bestMoves, Is.EqualTo(2));
            Assert.That(lib.levels, Has.Count.EqualTo(1));
            Assert.That(saved, Is.Not.SameAs(random));
        }

        [Test]
        public void SaveCopy_IsNull_WhenTheLibraryIsFull()
        {
            var lib = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                lib.Add(Simple(), LevelProof.Searched, i);
            }

            Assert.That(lib.SaveCopy(new CustomLevel { level = Simple(), proof = LevelProof.Searched }, 1), Is.Null);
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
            var lib = new CustomLevelLibrary();
            Assert.That(lib.SaveCopy(session.Level, 1), Is.Not.Null);
            Assert.That(lib.RemoveInvalid(), Is.Zero);
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
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Unproven(), LevelProof.None, 1).level.id;

            var r = lib.RecordWin(id, Detour, out bool proved);

            Assert.That(proved, Is.True);
            var entry = lib.Get(id);
            Assert.That(entry.proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(entry.level.par, Is.EqualTo(3));
            Assert.That(entry.level.solution, Is.EqualTo("R1← R1→ R1→"));
            Assert.That(r.Par, Is.EqualTo(3));
            Assert.That(r.Stars, Is.EqualTo(3));
            Assert.That(entry.bestMoves, Is.EqualTo(3));
        }

        [Test]
        public void RecordWin_FewerMovesLowerAPlayedPar_MoreDoNot()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Unproven(), LevelProof.None, 1).level.id;
            lib.RecordWin(id, Detour, out _);

            lib.RecordWin(id, Quick, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(lib.Get(id).level.par, Is.EqualTo(1));

            var r = lib.RecordWin(id, Detour, out _);
            Assert.That(lib.Get(id).level.par, Is.EqualTo(1));
            Assert.That(r.Stars, Is.LessThan(3));
            Assert.That(lib.Get(id).bestMoves, Is.EqualTo(1));
        }

        [Test]
        public void RecordWin_KeepsTheSolversPar()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Simple(), LevelProof.Searched, 1).level.id;
            lib.RecordWin(id, Detour, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(lib.Get(id).proof, Is.EqualTo(LevelProof.Searched));
            Assert.That(lib.Get(id).level.par, Is.EqualTo(1));
            Assert.That(lib.Get(id).bestMoves, Is.EqualTo(3));
        }

        [Test]
        public void RecordWin_IgnoresMovesThatDoNotSolve()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Unproven(), LevelProof.None, 1).level.id;
            lib.RecordWin(id, new[] { Move.Row(0, Direction.Left) }, out bool proved);
            Assert.That(proved, Is.False);
            Assert.That(lib.Get(id).proof, Is.EqualTo(LevelProof.None));
        }

        [Test]
        public void Duplicate_MarksTheNameAsACopy_WithinTheLimit()
        {
            var lib = new CustomLevelLibrary();
            var d = Simple();
            d.name = "Sixteen letters!";
            var copy = lib.Duplicate(lib.Add(d, LevelProof.Searched, 1).level.id, 2);
            Assert.That(copy.level.name.Length, Is.LessThanOrEqualTo(LevelEdit.MaxNameLength));
            StringAssert.EndsWith(" COPY", copy.level.name);
            Assert.That(LevelEdit.AuthoredErrors(copy.level), Is.Empty);
        }

        [Test]
        public void Import_AddsAProvenLevelUnderANewId()
        {
            var lib = new CustomLevelLibrary();
            string code = ShareCode.Encode(Simple());
            var a = lib.Import(code, 5, out var error);
            var b = lib.Import(code, 6, out _);

            Assert.That(error, Is.EqualTo(ShareCodeError.None));
            Assert.That(a.proof, Is.EqualTo(LevelProof.Replayed));
            Assert.That(a.level.par, Is.EqualTo(1));
            Assert.That(a.level.name, Is.EqualTo("Simple"));
            StringAssert.StartsWith(CustomLevelLibrary.IdPrefix, a.level.id);
            Assert.That(b.level.id, Is.Not.EqualTo(a.level.id), "the same code twice gives two levels");
        }

        [Test]
        public void Import_ReportsABadCode_AndAddsNothing()
        {
            var lib = new CustomLevelLibrary();
            Assert.That(lib.Import("nonsense", 1, out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ShareCodeError.NotACode));
            Assert.That(lib.levels, Is.Empty);
        }

        [Test]
        public void Import_RefusedWhenFull()
        {
            var lib = new CustomLevelLibrary();
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++)
            {
                lib.Add(Simple(), LevelProof.None, i);
            }

            Assert.That(lib.Import(ShareCode.Encode(Simple()), 1, out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ShareCodeError.None));
            Assert.That(lib.levels.Count, Is.EqualTo(CustomLevelLibrary.MaxLevels));
        }

        [Test]
        public void EveryImportError_HasWords()
        {
            foreach (ShareCodeError e in System.Enum.GetValues(typeof(ShareCodeError)))
            {
                if (e != ShareCodeError.None)
                {
                    Assert.That(CustomLevelLibrary.ImportMessage(e), Is.Not.Empty, e.ToString());
                }
            }
        }

        [Test]
        public void AWonLevel_CanBeExportedAndImported()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Unproven(), LevelProof.None, 1).level.id;
            Assert.That(ShareCode.CanEncode(lib.Get(id).level, out _), Is.False, "not proven yet");

            lib.RecordWin(id, Detour, out _);
            var copy = lib.Import(ShareCode.Encode(lib.Get(id).level), 2, out _);
            Assert.That(copy.level.solution, Is.EqualTo("R1← R1→ R1→"));
            Assert.That(copy.level.par, Is.EqualTo(3));
        }

        [Test]
        public void Remove_TakesTheLevelOut()
        {
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Simple(), LevelProof.Searched, 1).level.id;
            Assert.That(lib.Remove(id), Is.True);
            Assert.That(lib.Get(id), Is.Null);
            Assert.That(lib.Remove(id), Is.False);
        }

        [Test]
        public void Store_RoundTripsEveryField()
        {
            var d = Simple();
            d.gates = new[] { new RowGate(3, GateOp.Xor, 6) };
            d.rotateRows = new[] { 5 };
            d.lossRule = LossRule.AtMost;
            d.lossCount = 2;
            var lib = new CustomLevelLibrary();
            var a = lib.Add(d, LevelProof.Replayed, 42);
            lib.RecordSolve(a.level.id, 4);
            CustomLevelStore.Save(lib, TestKey);

            var back = CustomLevelStore.Load(TestKey);
            var entry = back.Get(a.level.id);
            Assert.That(entry, Is.Not.Null);
            Assert.That(LevelEdit.SameLevel(entry.level, a.level), Is.True);
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
            var lib = new CustomLevelLibrary();
            lib.Add(Simple(), LevelProof.Searched, 1);
            var fakeProof = lib.Add(Simple(), LevelProof.Searched, 2);
            fakeProof.level.solution = "R1←";
            var badRules = lib.Add(Simple(), LevelProof.None, 3);
            badRules.level.pins[6] = 1;
            CustomLevelStore.Save(lib, TestKey);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("damaged"));
            Assert.That(CustomLevelStore.Load(TestKey).levels.Count, Is.EqualTo(1));
        }

        [Test]
        public void Library_LeavesPlayerProgressAlone()
        {
            var progress = new PlayerProgress();
            var lib = new CustomLevelLibrary();
            string id = lib.Add(Simple(), LevelProof.Searched, 1).level.id;
            lib.RecordSolve(id, 1);
            Assert.That(progress.levels, Is.Empty);
            Assert.That(progress.IsSolved(id), Is.False);
        }
    }
}
