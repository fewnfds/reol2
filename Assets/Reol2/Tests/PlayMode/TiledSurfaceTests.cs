using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Reol2.World;
using UnityEngine;

namespace Reol2.Tests
{
    public sealed class TiledSurfaceTests
    {
        private const int Side = 17;
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Application.temporaryCachePath, $"ReolSurfaceFixture-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            var manifest = new TiledSurfaceData.Manifest
            {
                seed = 88,
                west = -128,
                south = -128,
                tileSize = 128,
                eastCount = 2,
                northCount = 1,
                spawnX = -100d,
                spawnZ = -40d,
                seaX = -160d,
                seaZ = -40d
            };
            File.WriteAllText(Path.Combine(folder, TiledSurfaceData.ManifestName), JsonUtility.ToJson(manifest));
            WriteTile(0);
            WriteTile(1);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.GetFiles(folder))
                File.Delete(file);
            Directory.Delete(folder);
        }

        [Test]
        public void GroundAndWaterInterpolateThroughTheNegativeCoordinateTileSeam()
        {
            var data = TiledSurfaceData.Load(folder, 88, false);
            var surface = new WorldSurface(new WorldDefinition(88), data: data);
            Assert.That(surface.HeightAt(-1, -47), Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(surface.HeightAt(0, -47), Is.EqualTo(3.625f).Within(0.0001f));
            Assert.That(surface.HeightAt(1, -47), Is.EqualTo(3.75f).Within(0.0001f));
            Assert.That(surface.WaterAt(-0.25, -47), Is.EqualTo(8f));
            Assert.That(surface.WaterAt(0.25, -47), Is.EqualTo(8f));
            Assert.That(surface.WaterFeaturesIn(-32, -64, 64), Is.EqualTo(WaterFeatures.River));
        }

        [Test]
        public void WrongSeedOrMismatchedSeamFailsBeforeSampling()
        {
            Assert.Throws<InvalidDataException>(() => TiledSurfaceData.Load(folder, 42, false));
            WriteTile(1, changedEdge: true);
            Assert.Throws<InvalidDataException>(() => TiledSurfaceData.Load(folder, 88, false));
        }

        private void WriteTile(int tile, bool changedEdge = false)
        {
            using var contents = new GZipStream(File.Create(TiledSurfaceData.TilePath(folder, tile, 0)), System.IO.Compression.CompressionLevel.Fastest);
            using var writer = new BinaryWriter(contents);
            for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                int worldX = -128 + tile * 128 + x * 8, worldZ = -128 + z * 8;
                writer.Write(4f + worldX / 8f + (worldZ + 50f) / 8f - 0.75f + (changedEdge && x == 0 ? 1f : 0f));
            }
            for (int i = 0; i < Side * Side; i++)
                writer.Write(8f);
            for (int i = 0; i < Side * Side; i++)
                writer.Write((byte)2);
        }
    }
}
