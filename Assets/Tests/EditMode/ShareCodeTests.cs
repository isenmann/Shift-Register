using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game;
using UnityEditor;

namespace ShiftRegister.Tests
{
    public class ShareCodeTests
    {
        static List<LevelData> BuiltIns()
        {
            var catalog = AssetDatabase.FindAssets("t:" + nameof(LevelCatalog))
                .Select(guid => AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
                .Single();
            return catalog.AllLevels().Select(entry => LevelJson.Parse(entry.file)).ToList();
        }

        static LevelData Simple() => new LevelData
        {
            name = "Simple",
            start = new[] { 16, 0, 0, 0, 0, 0, 0, 0 },
            target = new[] { 8, 0, 0, 0, 0, 0, 0, 0 },
            par = 1,
            solution = "R1→"
        };

        [Test]
        public void EveryBuiltInSolution_Replays()
        {
            foreach (var level in BuiltIns())
            {
                Assert.That(Move.TryParseList(level.solution, out var moves), Is.True, level.id);
                Assert.That(moves.Count, Is.EqualTo(level.par), level.id);
                Assert.That(Solver.Replays(LevelRules.Compile(level), moves), Is.True, level.id);
            }
        }

        [Test]
        public void EveryBuiltIn_RoundTrips()
        {
            foreach (var level in BuiltIns())
            {
                string code = ShareCode.Encode(level);
                Assert.That(ShareCode.TryDecode(code, out var back), Is.EqualTo(ShareCodeError.None), level.id);
                Assert.That(LevelEdit.SameLevel(level, back), Is.True, level.id);
                Assert.That(back.solution, Is.EqualTo(level.solution), level.id);
                Assert.That(back.par, Is.EqualTo(level.par), level.id);
            }
        }

        [Test]
        public void BuiltInCodes_StayShort()
        {
            int longest = BuiltIns().Max(level => ShareCode.Encode(level).Length);
            TestContext.WriteLine($"longest built-in code: {longest} characters");
            Assert.That(longest, Is.LessThanOrEqualTo(80));
        }

        [Test]
        public void Decode_IgnoresWhitespace_AndHasNoId()
        {
            string code = ShareCode.Encode(Simple());
            string spaced = "  " + code.Insert(8, "\n ") + "\t";
            Assert.That(ShareCode.TryDecode(spaced, out var back), Is.EqualTo(ShareCodeError.None));
            Assert.That(back.id, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("hello")]
        [TestCase("SRx-AAAA")]
        [TestCase("SR1-***")]
        public void Decode_RejectsNonCodes(string text) =>
            Assert.That(ShareCode.TryDecode(text, out _), Is.EqualTo(ShareCodeError.NotACode));

        [Test]
        public void Decode_RejectsNewerVersion() =>
            Assert.That(ShareCode.TryDecode("SR2-AAAA", out _), Is.EqualTo(ShareCodeError.UnknownVersion));

        [Test]
        public void Decode_RejectsEveryChangedCharacter()
        {
            string code = ShareCode.Encode(Simple());
            for (int i = ShareCode.Prefix.Length; i < code.Length; i++)
            {
                char other = code[i] == 'A' ? 'B' : 'A';
                string changed = code.Substring(0, i) + other + code.Substring(i + 1);
                Assert.That(ShareCode.TryDecode(changed, out _), Is.Not.EqualTo(ShareCodeError.None), $"character {i}");
            }
        }

        [Test]
        public void Decode_RejectsCutOffCodes()
        {
            string code = ShareCode.Encode(Simple());
            for (int length = ShareCode.Prefix.Length; length < code.Length; length++)
            {
                Assert.That(ShareCode.TryDecode(code.Substring(0, length), out _), Is.Not.EqualTo(ShareCodeError.None),
                    $"length {length}");
            }
        }

        [Test]
        public void Decode_RejectsAWrongSolution_EvenWithAGoodChecksum()
        {
            // A hand-made code: the checksum is right, the solution moves the bit the wrong way.
            var bytes = new List<byte> { 0, 0, 1, 16, 1, 8, 1, Move.Row(0, Direction.Left).ToByte(), 0 };
            Assert.That(ShareCode.TryDecode(Wrap(bytes), out _), Is.EqualTo(ShareCodeError.BadSolution));
        }

        [Test]
        public void Decode_RejectsARuleBreakingLevel_EvenWithAGoodChecksum()
        {
            // Flags: pins. A pin on a cell that is off in start and target.
            var bytes = new List<byte> { 2, 0, 1, 16, 1, 8, 1, 1, 1, Move.Row(0, Direction.Right).ToByte(), 0 };
            Assert.That(ShareCode.TryDecode(Wrap(bytes), out _), Is.EqualTo(ShareCodeError.InvalidLevel));
        }

        [Test]
        public void Decode_RemovesTagsFromNames()
        {
            var bytes = new List<byte> { 0, 0, 1, 16, 1, 8, 1, Move.Row(0, Direction.Right).ToByte() };
            string name = "<b>Hi";
            bytes.Add((byte)name.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
            Assert.That(ShareCode.TryDecode(Wrap(bytes), out var level), Is.EqualTo(ShareCodeError.None));
            Assert.That(level.name, Is.EqualTo("bHi"));
        }

        [Test]
        public void Encode_RefusesALevelWithoutAWorkingSolution()
        {
            var level = Simple();
            level.solution = "R1←";
            Assert.That(ShareCode.CanEncode(level, out _), Is.False);
            Assert.Throws<System.ArgumentException>(() => ShareCode.Encode(level));
        }

        /// <summary>Adds prefix and checksum to raw bytes.</summary>
        static string Wrap(List<byte> payload)
        {
            int crc = 0xFFFF;
            foreach (byte value in payload)
            {
                crc ^= value << 8;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
                }
            }

            var all = new List<byte>(payload) { (byte)(crc >> 8), (byte)crc };
            string text = System.Convert.ToBase64String(all.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return ShareCode.Prefix + text;
        }
    }
}
