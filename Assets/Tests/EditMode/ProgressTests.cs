using System.Collections.Generic;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game;
using ShiftRegister.Game.Progress;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class ProgressTests
    {
        [TestCase(6, 6, 3)]
        [TestCase(5, 6, 3)]
        [TestCase(9, 6, 2)]  // par + 3 (⌈6/2⌉)
        [TestCase(10, 6, 1)]
        [TestCase(7, 5, 2)]  // par + 2 is the minimum slack
        [TestCase(8, 5, 2)]  // ⌈5/2⌉ = 3
        [TestCase(9, 5, 1)]
        [TestCase(4, 0, 1)]  // no par yet
        public void Stars(int moves, int par, int expected) =>
            Assert.That(Scoring.Stars(moves, par), Is.EqualTo(expected));

        [Test]
        public void Record_KeepsBestMovesAndMostStars()
        {
            var p = new PlayerProgress();
            var first = p.Record("2-3", 10, 6);
            Assert.That(first.IsNewBest, Is.True);
            Assert.That(first.Stars, Is.EqualTo(1));

            var better = p.Record("2-3", 6, 6);
            Assert.That(better.IsNewBest, Is.True);
            Assert.That(better.PreviousBest, Is.EqualTo(10));

            var worse = p.Record("2-3", 9, 6);
            Assert.That(worse.IsNewBest, Is.False);
            Assert.That(p.Get("2-3").bestMoves, Is.EqualTo(6));
            Assert.That(p.StarsFor("2-3"), Is.EqualTo(3));
            Assert.That(p.TotalStars(), Is.EqualTo(3));
        }

        [Test]
        public void Json_RoundTrip()
        {
            var p = new PlayerProgress { lastLevelId = "1-5" };
            p.Record("1-5", 6, 6);
            p.Record("2-3", 8, 6);
            var back = JsonUtility.FromJson<PlayerProgress>(JsonUtility.ToJson(p));
            Assert.That(back.lastLevelId, Is.EqualTo("1-5"));
            Assert.That(back.IsSolved("2-3"), Is.True);
            Assert.That(back.Get("2-3").bestMoves, Is.EqualTo(8));
            Assert.That(back.TotalStars(), Is.EqualTo(5));
        }

        [Test]
        public void Catalog_NextAndPrevious_SkipEmptyChapters()
        {
            var cat = ScriptableObject.CreateInstance<LevelCatalog>();
            try
            {
                cat.chapters = new List<LevelCatalog.Chapter>
                {
                    Chapter(2), Chapter(0), Chapter(1)
                };
                Assert.That(cat.TryFirst(out int c, out int l) && (c, l) == (0, 0), Is.True);
                Assert.That(cat.TryNext(0, 0, out c, out l) && (c, l) == (0, 1), Is.True);
                Assert.That(cat.TryNext(0, 1, out c, out l) && (c, l) == (2, 0), Is.True, "skips the empty chapter");
                Assert.That(cat.TryNext(2, 0, out _, out _), Is.False);
                Assert.That(cat.TryPrevious(2, 0, out c, out l) && (c, l) == (0, 1), Is.True);
                Assert.That(cat.TryPrevious(0, 0, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(cat);
            }
        }

        static LevelCatalog.Chapter Chapter(int levels)
        {
            var ch = new LevelCatalog.Chapter();
            for (int i = 0; i < levels; i++)
            {
                ch.levels.Add(new TextAsset("{}"));
            }

            return ch;
        }
    }
}
