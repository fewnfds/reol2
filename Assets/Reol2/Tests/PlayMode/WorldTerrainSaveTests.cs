using System;
using System.IO;
using NUnit.Framework;
using Reol2.Voxels;
using Reol2.World;
using UnityEngine;

namespace Reol2.Tests
{
    public sealed class WorldTerrainSaveTests
    {
        private const long Seed = 20261121;
        private string directory;

        [SetUp]
        public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "reol2-terrain-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            foreach (string emptyDirectory in Directory.EnumerateDirectories(directory))
                Directory.Delete(emptyDirectory);
            Directory.Delete(directory);
        }

        [Test]
        public void ASecondEditWhileSavingKeepsBothCompleteVersionsAcrossThePageCorner()
        {
            var field = NewField();
            Vector3Int cell = field.Caves.Passage[3].Page * ContinentField.ChunkSize;
            Vector3Int centre = cell * 2 + Vector3Int.one;
            string path;
            using (var save = new WorldTerrainSave(field, directory, Seed, 0))
            {
                path = save.FilePath;
                Assert.That(field.Edit(cell, fill: true, out _), Is.True);
                save.Tick(0f);
                Assert.That(field.Edit(cell, fill: false, out _), Is.True);
                Assert.That(save.Flush(), Is.True);
            }

            var opened = NewField();
            using (var save = new WorldTerrainSave(opened, directory, Seed, 0))
            {
                Assert.That(save.CanEdit, Is.True);
                Assert.That(opened.EditedSamples, Is.EqualTo(field.EditedSamples));
                Assert.That(opened.SampleAt(centre), Is.GreaterThan(0f));
                for (int z = -2; z <= 2; z++)
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    Vector3Int sample = centre + new Vector3Int(x, y, z);
                    Assert.That(opened.SampleAt(sample), Is.EqualTo(field.SampleAt(sample)));
                }
            }

            byte[] backup = File.ReadAllBytes(path + ".bak");
            File.WriteAllBytes(path + ".tmp", new byte[] { 1, 2, 3 });
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var recovered = NewField();
            using (var save = new WorldTerrainSave(recovered, directory, Seed, 0))
            {
                Assert.That(save.CanEdit, Is.True);
                Assert.That(recovered.SampleAt(centre), Is.LessThan(0f));
                var point = new WorldPosition(cell.x + 0.5, cell.y + 0.5, cell.z + 0.5);
                Assert.That(recovered.DigCellAt(point, out bool placed), Is.EqualTo(cell));
                Assert.That(placed, Is.True);
                Assert.That(recovered.Edit(cell, fill: false, out _), Is.True);
                Assert.That(save.Flush(), Is.True);
            }
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backup));
        }

        [Test]
        public void ACompletelyExcavatedPageStaysAirAfterReopen()
        {
            var field = NewField();
            Vector3Int page = new(-4, -2048, 3);
            Vector3Int first = page * ContinentField.ChunkSize;
            int width = DensityVolume.SampleWidth;
            var density = new float[width * width * width];
            Assert.That(field.CopySurfaceSamples(page, density), Is.EqualTo(PageContent.Solid));
            using (var save = new WorldTerrainSave(field, directory, Seed, 0))
            {
                for (int z = -1; z <= ContinentField.ChunkSize; z++)
                for (int y = -1; y <= ContinentField.ChunkSize; y++)
                for (int x = -1; x <= ContinentField.ChunkSize; x++)
                    field.Edit(first + new Vector3Int(x, y, z), fill: false, out _);
                Assert.That(field.CopySurfaceSamples(page, density), Is.EqualTo(PageContent.Air));
                Assert.That(save.Flush(), Is.True);
            }

            var restored = NewField();
            using var loaded = new WorldTerrainSave(restored, directory, Seed, 0);
            Assert.That(restored.EditedSamples, Is.EqualTo(field.EditedSamples));
            Assert.That(restored.CopySurfaceSamples(page, density), Is.EqualTo(PageContent.Air));
            Assert.That(restored.CopySurfaceSamples(page + 3 * Vector3Int.right, density), Is.EqualTo(PageContent.Solid));
        }

        [Test]
        public void AFailedWriteBlocksQuitAndKeepsTheLastCommitUntilRetry()
        {
            var field = NewField();
            Vector3Int cell = new(16, 12000, -32);
            using var save = new WorldTerrainSave(field, directory, Seed, 0);
            Assert.That(field.Edit(cell, fill: true, out _), Is.True);
            Assert.That(save.Flush(), Is.True);
            byte[] previous = File.ReadAllBytes(save.FilePath);
            Assert.That(field.Edit(cell, fill: false, out _), Is.True);
            // A directory at the temporary path reliably fails the write before replacement.
            Directory.CreateDirectory(save.FilePath + ".tmp");
            Assert.That(save.TrySaveBeforeQuit(), Is.False);
            Assert.That(save.HasPendingEdits, Is.True);
            Assert.That(save.Status, Does.Contain("F9"));
            Assert.That(File.ReadAllBytes(save.FilePath), Is.EqualTo(previous));
            Directory.Delete(save.FilePath + ".tmp");
            Assert.That(save.TrySaveBeforeQuit(), Is.True);
            Assert.That(save.HasPendingEdits, Is.False);
            Assert.That(File.ReadAllBytes(save.FilePath + ".bak"), Is.EqualTo(previous));
        }

        [Test]
        public void LeavingAfterARejectedQuitKeepsTheLastSavedVersion()
        {
            var field = NewField();
            Vector3Int cell = new(16, 12000, -32);
            string path;
            byte[] previous;
            using (var save = new WorldTerrainSave(field, directory, Seed, 0))
            {
                path = save.FilePath;
                Assert.That(field.Edit(cell, fill: true, out _), Is.True);
                Assert.That(save.Flush(), Is.True);
                previous = File.ReadAllBytes(path);
                Assert.That(field.Edit(cell, fill: false, out _), Is.True);
                Assert.That(save.ConfirmQuitWithoutSaving(), Is.False);
                Directory.CreateDirectory(path + ".tmp");
                Assert.That(save.TrySaveBeforeQuit(), Is.False);
                Assert.That(save.ConfirmQuitWithoutSaving(), Is.True);
                Directory.Delete(path + ".tmp");
                Assert.That(save.TrySaveBeforeQuit(), Is.True);
            }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(previous));
        }

        [Test]
        public void AnOccupiedOrWrongWorldSaveIsKeptAndRegionIdentityIsSeparate()
        {
            var field = NewField();
            string path;
            using (var save = new WorldTerrainSave(field, directory, Seed, 0))
            {
                path = save.FilePath;
                Assert.That(field.Edit(new Vector3Int(16, 12000, -32), fill: true, out _), Is.True);
                Assert.That(save.Flush(), Is.True);
                using var duplicate = new WorldTerrainSave(NewField(), directory, Seed, 0);
                Assert.That(duplicate.CanEdit, Is.False);
                using var regional = new WorldTerrainSave(NewField(), directory, Seed, 64000);
                Assert.That(regional.CanEdit, Is.True);
                Assert.That(regional.FilePath, Is.Not.EqualTo(path));
            }

            string foreignPath = path.Replace($"terrain-{Seed}-", $"terrain-{Seed + 1}-");
            File.Copy(path, foreignPath);
            byte[] foreign = File.ReadAllBytes(foreignPath);
            var otherField = NewField(Seed + 1);
            using (var save = new WorldTerrainSave(otherField, directory, Seed + 1, 0))
            {
                Assert.That(save.CanEdit, Is.False);
                Assert.That(save.Status, Does.Contain("different world, generator or format"));
                Assert.That(otherField.EditedSamples, Is.Zero);
            }
            Assert.That(File.ReadAllBytes(foreignPath), Is.EqualTo(foreign));
        }

        private static ContinentField NewField(long seed = Seed) => new(new WorldDefinition(seed));
    }
}
