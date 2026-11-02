using System.Linq;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Editor;
using ShiftRegister.Game;
using UnityEditor;

namespace ShiftRegister.Tests
{
    public class LevelFileTests
    {
        [Test]
        public void Parse_MissingFields_KeepDefaults()
        {
            var d = LevelJson.Parse("{ \"id\": \"x\", \"start\": [1,2,3,4,5,6,7,8], \"target\": [0,0,0,0,0,0,0,0] }");
            Assert.That(d.start[7], Is.EqualTo(8));
            Assert.That(d.walls, Has.Length.EqualTo(8));
            Assert.That(d.gates, Is.Empty);
            Assert.That(d.lossRule, Is.EqualTo(LossRule.Any));
            Assert.That(LevelRules.Validate(d), Is.Empty);
        }

        [Test]
        public void Parse_GatesAndLossRule()
        {
            var d = LevelJson.Parse("{ \"gates\": [ { \"row\": 6, \"op\": 3, \"mask\": 1 } ], \"lossRule\": 2, \"lossCount\": 3 }");
            Assert.That(d.gates[0].op, Is.EqualTo(GateOp.Not));
            Assert.That(d.lossRule, Is.EqualTo(LossRule.Exactly));
            Assert.That(d.lossCount, Is.EqualTo(3));
        }

        [Test]
        public void SetField_ReplacesExistingValue_KeepsFormatting()
        {
            const string json = "{\n  \"id\": \"2-3\",\n  \"start\":  [0, 1],\n  \"par\": 8,\n  \"solution\": \"old\"\n}\n";
            string a = LevelTools.SetField(json, "par", "6");
            string b = LevelTools.SetField(a, "solution", "\"R5← C4↑\"");
            Assert.That(b, Is.EqualTo("{\n  \"id\": \"2-3\",\n  \"start\":  [0, 1],\n  \"par\": 6,\n  \"solution\": \"R5← C4↑\"\n}\n"));
        }

        [Test]
        public void SetField_AppendsMissingField()
        {
            string s = LevelTools.SetField("{\n  \"id\": \"a\"\n}\n", "par", "3");
            Assert.That(s, Is.EqualTo("{\n  \"id\": \"a\",\n  \"par\": 3\n}\n"));
            Assert.That(LevelTools.SetField("{}", "par", "1"), Is.EqualTo("{\n  \"par\": 1\n}"));
        }

        [Test]
        public void Catalog_AllLevelsValid_ParUpToDate()
        {
            var catalog = AssetDatabase.FindAssets("t:" + nameof(LevelCatalog))
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(g)))
                .SingleOrDefault();
            Assert.That(catalog, Is.Not.Null, "exactly one LevelCatalog asset");

            var levels = catalog.AllLevels().ToList();
            Assert.That(levels, Is.Not.Empty);
            foreach (var (chapter, level, file) in levels)
            {
                Assert.That(file, Is.Not.Null, $"chapter {chapter + 1} level {level + 1} is empty");
                var r = LevelTools.Check(file);
                Assert.That(r.Ok, Is.True, $"{file.name}: {r.Message}");
                Assert.That(r.NewJson, Is.Null,
                    $"{file.name}: par/solution out of date ({r.Message}); run Shift Register > Validate and Solve Levels");
            }
        }
    }
}
