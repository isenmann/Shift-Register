using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Tests
{
    /// <summary>Chapter 3–6 visuals: row labels, gate / 1-IN overlays and belts.</summary>
    public class MechanicVisualTests
    {
        static LevelRules Mixed()
        {
            var d = new LevelData
            {
                rotateRows = new[] { 1 },
                chainTopRows = new[] { 2 },
                fillLeftRows = new[] { 4 },
                gates = new[]
                {
                    new RowGate(5, GateOp.Xor, 0b00011000),
                    new RowGate(6, GateOp.And, 0b00111100),
                    new RowGate(6, GateOp.Not, 0b00000001)
                }
            };
            return LevelRules.Compile(d);
        }

        [Test]
        public void Labels_NameWhatMakesARowSpecial()
        {
            var rules = Mixed();
            Assert.That(RowHint.Label(rules, 0), Is.EqualTo(""));
            Assert.That(RowHint.Label(rules, 1), Is.EqualTo("wrap"));
            Assert.That(RowHint.Label(rules, 2), Is.EqualTo("carry"));
            Assert.That(RowHint.Label(rules, 3), Is.EqualTo("carry"));
            Assert.That(RowHint.Label(rules, 4), Is.EqualTo("1-IN"));
            Assert.That(RowHint.Label(rules, 5), Is.EqualTo("XOR"));
            Assert.That(RowHint.Label(rules, 6), Is.EqualTo("AND+"));
        }

        [Test]
        public void Overlays_MarkGateCellsAndOneInEdges()
        {
            var go = new GameObject("Board");
            try
            {
                var view = go.AddComponent<BoardView>();
                view.Build(Mixed());
                Assert.That(view.HasOverlay(5, 3) && view.HasOverlay(5, 4), Is.True);
                Assert.That(view.HasOverlay(5, 2), Is.False);
                Assert.That(view.OverlaySymbol(5, 3), Is.EqualTo("^"));
                Assert.That(view.OverlaySymbol(6, 2), Is.EqualTo("&"));
                Assert.That(view.OverlaySymbol(6, 7), Is.EqualTo("¬"));
                Assert.That(view.OverlaySymbol(4, 0), Is.EqualTo("1›"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Belts_ForWrapRowsAndChains()
        {
            var go = new GameObject("Board");
            try
            {
                var controls = go.AddComponent<BoardControls>();
                controls.Build(Mixed());
                var root = go.transform.Find("Controls");
                Assert.That(root.Find("Wrap belt 2"), Is.Not.Null);
                Assert.That(root.Find("Chain belt 3-4"), Is.Not.Null);
                int belts = 0;
                foreach (Transform t in root) if (t.name.Contains("belt")) belts++;
                Assert.That(belts, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
