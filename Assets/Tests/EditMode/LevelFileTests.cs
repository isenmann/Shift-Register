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
            var level =
                LevelJson.Parse("{ \"id\": \"x\", \"start\": [1,2,3,4,5,6,7,8], \"target\": [0,0,0,0,0,0,0,0] }");
            Assert.That(level.start[7], Is.EqualTo(8));
            Assert.That(level.walls, Has.Length.EqualTo(8));
            Assert.That(level.gates, Is.Empty);
            Assert.That(level.lossRule, Is.EqualTo(LossRule.Any));
            Assert.That(LevelRules.Validate(level), Is.Empty);
        }

        [Test]
        public void Parse_GatesAndLossRule()
        {
            var level =
                LevelJson.Parse(
                "{ \"gates\": [ { \"row\": 6, \"op\": 3, \"mask\": 1 } ], \"lossRule\": 2, \"lossCount\": 3 }");
            Assert.That(level.gates[0].op, Is.EqualTo(GateOp.Not));
            Assert.That(level.lossRule, Is.EqualTo(LossRule.Exactly));
            Assert.That(level.lossCount, Is.EqualTo(3));
        }

        [Test]
        public void SetField_ReplacesExistingValue_KeepsFormatting()
        {
            const string json =
                "{\n  \"id\": \"2-3\",\n  \"start\":  [0, 1],\n  \"par\": 8,\n  \"solution\": \"old\"\n}\n";
            string withPar = LevelTools.SetField(json, "par", "6");
            string withSolution = LevelTools.SetField(withPar, "solution", "\"R5← C4↑\"");
            Assert.That(withSolution,
                Is.EqualTo(
                "{\n  \"id\": \"2-3\",\n  \"start\":  [0, 1],\n  \"par\": 6,\n  \"solution\": \"R5← C4↑\"\n}\n"));
        }

        [Test]
        public void SetField_AppendsMissingField()
        {
            string updated = LevelTools.SetField("{\n  \"id\": \"a\"\n}\n", "par", "3");
            Assert.That(updated, Is.EqualTo("{\n  \"id\": \"a\",\n  \"par\": 3\n}\n"));
            Assert.That(LevelTools.SetField("{}", "par", "1"), Is.EqualTo("{\n  \"par\": 1\n}"));
        }

        [Test]
        public void Catalog_AllLevelsValid_ParUpToDate()
        {
            var catalog = AssetDatabase.FindAssets("t:" + nameof(LevelCatalog))
                .Select(guid => AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
                .SingleOrDefault();
            Assert.That(catalog, Is.Not.Null, "exactly one LevelCatalog asset");

            var levels = catalog.AllLevels().ToList();
            Assert.That(levels, Is.Not.Empty);
            foreach (var (chapter, level, file) in levels)
            {
                Assert.That(file, Is.Not.Null, $"chapter {chapter + 1} level {level + 1} is empty");
                var result = LevelTools.Check(file);
                Assert.That(result.Ok, Is.True, $"{file.name}: {result.Message}");
                Assert.That(result.NewJson, Is.Null,
                    $"{file.name}: par/solution out of date ({result.Message}); " +
                    "run Shift Register > Validate and Solve Levels");
            }
        }
    }
}
