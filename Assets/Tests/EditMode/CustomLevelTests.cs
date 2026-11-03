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
            for (int i = 0; i < CustomLevelLibrary.MaxLevels; i++) Assert.That(lib.Add(Simple(), LevelProof.None, i), Is.Not.Null);
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
