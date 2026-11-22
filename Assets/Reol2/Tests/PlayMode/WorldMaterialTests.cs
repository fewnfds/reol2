using System;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;

namespace Reol2.Tests
{
    public sealed class WorldMaterialTests
    {
        [Test]
        public void CoarseMeshesDropFineBandsBeforeUndersampling()
        {
            Assert.That(WorldMaterial.BandWeight(8f, 8), Is.EqualTo(1f));
            Assert.That(WorldMaterial.BandWeight(48f, 8), Is.EqualTo(0.5f));
            Assert.That(WorldMaterial.BandWeight(64f, 8), Is.Zero);
            Assert.That(WorldMaterial.BandWeight(64f, 12), Is.EqualTo(1f));
        }

        [Test]
        public void FarEdgeColorsAreStableAcrossOriginsAndBreakTheOldPeriod()
        {
            var point = new WorldPosition(WorldDefinition.Extent - 8192.5, 80, -15555.25);
            var first = new WorldFrame();
            var second = new WorldFrame();
            first.CenterOn(point);
            second.CenterOn(new WorldPosition(point.X + 800, point.Y + 600, point.Z - 750));
            var field = new ContinentField(new WorldDefinition(14376));
            Color32 ColorAt(WorldPosition p) => field.MaterialAt(p, Vector3.up, 80, 8);
            Assert.That(ColorAt(first.ToWorld(first.ToLocal(point))), Is.EqualTo(ColorAt(second.ToWorld(second.ToLocal(point)))));

            int differing = 0;
            for (int i = 0; i < 32; i++)
            {
                var a = new WorldPosition(point.X, point.Y, point.Z + i * 145.5);
                var b = new WorldPosition(a.X + 2 * Math.PI / 0.037, a.Y, a.Z);
                if (!ColorAt(a).Equals(ColorAt(b)))
                    differing++;
            }
            Assert.That(differing, Is.GreaterThan(16));
        }

        [TestCase(-264, -8)]
        [TestCase(4088, 248)]
        [TestCase(67108728, -67108728)]
        public void PreparedPageTintMatchesGlobalSamplingAtCornersAndInside(int x, int z)
        {
            var field = new ContinentField(new WorldDefinition(14376));
            WorldMaterial.PageTint tint = field.PrepareTint(x, z);
            foreach (float u in new[] { 0f, 0.25f, 4.75f, 8f })
            foreach (float v in new[] { 0f, 4.5f, 8f })
            {
                var point = new WorldPosition(x + (double)u, 64, z + (double)v);
                Color32 global = field.MaterialAt(point, Vector3.up, 65, 0.5f);
                Color32 prepared = WorldMaterial.ColorAt(64, Vector3.up, 65, 0.5f,
                    tint.ShadeAt(new Vector3(u, 0, v)), true);
                Assert.That(prepared, Is.EqualTo(global));
            }
        }
    }
}
