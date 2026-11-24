#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Reol2.World
{
    internal sealed class TiledSurfaceData : IWorldSurfaceData
    {
        internal const int Step = ContinentField.ChunkSize;
        internal const string ManifestName = "surface.json";
        private const byte Sea = 1;
        private const byte River = 2;
        private const byte Lake = 4;
        private const int FeatureSize = 128;

        [Serializable]
        internal sealed class Manifest
        {
            public int version = 1;
            public long seed;
            public bool regionalFallback;
            public int west, south, tileSize, eastCount, northCount;
            public double spawnX, spawnZ, seaX, seaZ;
        }

        private readonly Manifest manifest;
        private readonly Tile[,] tiles;
        private readonly int side, east, north;
        private readonly int featureColumns, featureRows;
        private readonly int[] coasts, rivers, lakes;

        public WorldPosition Spawn => new(manifest.spawnX, 0d, manifest.spawnZ);
        public WorldPosition SeaEntry => new(manifest.seaX, -0.8d, manifest.seaZ);

        internal static TiledSurfaceData Load(string folder, long seed, bool regionalFallback)
        {
            var watch = Stopwatch.StartNew();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(folder, ManifestName)));
            if (manifest == null || manifest.version != 1 || manifest.seed != seed
                || manifest.regionalFallback != regionalFallback
                || manifest.tileSize < FeatureSize || manifest.tileSize > 4096 || manifest.tileSize % FeatureSize != 0
                || manifest.west % Step != 0 || manifest.south % Step != 0
                || manifest.eastCount < 1 || manifest.eastCount > 4 || manifest.northCount < 1 || manifest.northCount > 4)
                throw new InvalidDataException("Invalid baked surface bounds, version, seed, or fallback settings.");
            long east = (long)manifest.west + (long)manifest.tileSize * manifest.eastCount;
            long north = (long)manifest.south + (long)manifest.tileSize * manifest.northCount;
            if (manifest.west < -WorldDefinition.Extent || manifest.south < -WorldDefinition.Extent
                || east > WorldDefinition.Extent || north > WorldDefinition.Extent
                || !WorldDefinition.Contains(new WorldPosition(manifest.spawnX, 0d, manifest.spawnZ))
                || !WorldDefinition.Contains(new WorldPosition(manifest.seaX, 0d, manifest.seaZ)))
                throw new InvalidDataException("Baked surface coordinates are outside the world.");
            var data = new TiledSurfaceData(folder, manifest);
            UnityEngine.Debug.Log(FormattableString.Invariant(
                $"Baked surface | {manifest.eastCount * manifest.northCount} tiles | decode {data.DecodeMs:F2} ms total | decode slowest {data.SlowestTileMs:F2} ms | loaded and verified {watch.Elapsed.TotalMilliseconds:F2} ms"));
            return data;
        }

        private double DecodeMs { get; }
        private double SlowestTileMs { get; }

        private TiledSurfaceData(string folder, Manifest manifest)
        {
            this.manifest = manifest;
            east = manifest.west + manifest.tileSize * manifest.eastCount;
            north = manifest.south + manifest.tileSize * manifest.northCount;
            side = manifest.tileSize / Step + 1;
            tiles = new Tile[manifest.eastCount, manifest.northCount];
            for (int z = 0; z < manifest.northCount; z++)
            for (int x = 0; x < manifest.eastCount; x++)
            {
                long start = Stopwatch.GetTimestamp();
                Tile tile = Tile.Read(TilePath(folder, x, z), side);
                double ms = (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);
                DecodeMs += ms;
                SlowestTileMs = Math.Max(SlowestTileMs, ms);
                tiles[x, z] = tile;
                if (x > 0)
                    CheckEdge(tiles[x - 1, z], tile, horizontal: true);
                if (z > 0)
                    CheckEdge(tiles[x, z - 1], tile, horizontal: false);
            }

            if (manifest.regionalFallback)
                return;
            if (manifest.spawnX < manifest.west || manifest.spawnX >= east
                || manifest.spawnZ < manifest.south || manifest.spawnZ >= north)
                throw new InvalidDataException("The baked spawn is outside the loaded tiles.");
            featureColumns = (east - manifest.west) / FeatureSize;
            featureRows = (north - manifest.south) / FeatureSize;
            int areas = (featureColumns + 1) * (featureRows + 1);
            coasts = new int[areas];
            rivers = new int[areas];
            lakes = new int[areas];
            BuildFeatures();
        }

        internal static string TilePath(string folder, int x, int z) => Path.Combine(folder, $"surface-{x}-{z}.bin.gz");

        public bool TryRead(int x, int z, out float ground, out float water)
        {
            int px = Math.Clamp(x, manifest.west, east);
            int pz = Math.Clamp(z, manifest.south, north);
            bool inside = px == x && pz == z;
            if (!inside && manifest.regionalFallback)
            {
                ground = water = 0f;
                return false;
            }
            int sx = px - manifest.west, sz = pz - manifest.south;
            int nodeX = manifest.west + (sx / Step) * Step, nodeZ = manifest.south + (sz / Step) * Step;
            float u = (sx % Step) / (float)Step, v = (sz % Step) / (float)Step;
            Node(nodeX, nodeZ, out ground, out water);
            if (u != 0f || v != 0f)
            {
                int nextX = Math.Min(nodeX + Step, east), nextZ = Math.Min(nodeZ + Step, north);
                Node(nextX, nodeZ, out float bg, out float bw);
                Node(nodeX, nextZ, out float cg, out float cw);
                Node(nextX, nextZ, out float dg, out float dw);
                ground = WorldSurface.Interpolate(ground, bg, cg, dg, u, v);
                water = WorldSurface.Interpolate(water, bw, cw, dw, u, v);
            }
            if (!inside)
            {
                float fade = Math.Min(1f, Math.Max(Math.Abs(x - px), Math.Abs(z - pz)) / 256f);
                ground += (-90f - ground) * fade;
                water = 0f;
            }
            return true;
        }

        public bool NearSea(double x, double z)
        {
            if (x < manifest.west || x > east || z < manifest.south || z > north)
                return true;
            int west = manifest.west + (int)((x - manifest.west) / Step) * Step;
            int south = manifest.south + (int)((z - manifest.south) / Step) * Step;
            int nextX = Math.Min(west + Step, east), nextZ = Math.Min(south + Step, north);
            return ((Flags(west, south) | Flags(nextX, south) | Flags(west, nextZ) | Flags(nextX, nextZ)) & Sea) != 0;
        }

        public WaterFeatures FeaturesIn(int x, int z, int width)
        {
            if (coasts == null || x > east || z > north || x + width < manifest.west || z + width < manifest.south)
                return WaterFeatures.None;
            int a = Math.Clamp((x - manifest.west) / FeatureSize, 0, featureColumns - 1);
            int b = Math.Clamp((x + width - manifest.west) / FeatureSize, 0, featureColumns - 1) + 1;
            int c = Math.Clamp((z - manifest.south) / FeatureSize, 0, featureRows - 1) * (featureColumns + 1);
            int d = (Math.Clamp((z + width - manifest.south) / FeatureSize, 0, featureRows - 1) + 1) * (featureColumns + 1);
            return (HasFeature(coasts, a, b, c, d) ? WaterFeatures.Coast : WaterFeatures.None)
                | (HasFeature(rivers, a, b, c, d) ? WaterFeatures.River : WaterFeatures.None)
                | (HasFeature(lakes, a, b, c, d) ? WaterFeatures.Lake : WaterFeatures.None);
        }

        private void BuildFeatures()
        {
            int stride = featureColumns + 1;
            for (int z = 0; z < featureRows; z++)
            for (int x = 0; x < featureColumns; x++)
            {
                int west = manifest.west + x * FeatureSize, south = manifest.south + z * FeatureSize;
                byte flags = 0;
                bool haveLand = false;
                for (int nz = south; nz <= south + FeatureSize; nz += Step)
                for (int nx = west; nx <= west + FeatureSize; nx += Step)
                {
                    byte tag = Flags(nx, nz);
                    flags |= tag;
                    haveLand |= (tag & Sea) == 0;
                }
                int i = x + 1 + (z + 1) * stride;
                SetArea(coasts, i, stride, haveLand && (flags & Sea) != 0);
                SetArea(rivers, i, stride, (flags & River) != 0);
                SetArea(lakes, i, stride, (flags & Lake) != 0);
            }
        }

        private static void SetArea(int[] sums, int i, int row, bool exists) =>
            sums[i] = sums[i - 1] + sums[i - row] - sums[i - row - 1] + (exists ? 1 : 0);

        private static bool HasFeature(int[] sums, int a, int b, int c, int d) =>
            sums[b + d] - sums[a + d] - sums[b + c] + sums[a + c] != 0;

        private Tile Locate(int x, int z, out int i)
        {
            x -= manifest.west;
            z -= manifest.south;
            int tx = Math.Min(x / manifest.tileSize, manifest.eastCount - 1);
            int tz = Math.Min(z / manifest.tileSize, manifest.northCount - 1);
            i = (x - tx * manifest.tileSize) / Step + side * ((z - tz * manifest.tileSize) / Step);
            return tiles[tx, tz];
        }

        private void Node(int x, int z, out float ground, out float water)
        {
            Tile tile = Locate(x, z, out int i);
            ground = tile.Ground[i];
            water = tile.Level[i];
        }

        private byte Flags(int x, int z) => Locate(x, z, out int i).Tags[i];

        private void CheckEdge(Tile first, Tile next, bool horizontal)
        {
            for (int i = 0; i < side; i++)
            {
                int a = horizontal ? i * side + side - 1 : (side - 1) * side + i;
                int b = horizontal ? i * side : i;
                if (first.Ground[a] != next.Ground[b] || first.Level[a] != next.Level[b] || first.Tags[a] != next.Tags[b])
                    throw new InvalidDataException("Adjacent baked tiles disagree on the shared samples.");
            }
        }

        private sealed class Tile
        {
            internal readonly float[] Ground, Level;
            internal readonly byte[] Tags;

            private Tile(int count)
            {
                Ground = new float[count];
                Level = new float[count];
                Tags = new byte[count];
            }

            internal static Tile Read(string path, int side)
            {
                int count = side * side, planeBytes = count * sizeof(float);
                var tile = new Tile(count);
                using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
                var plane = new byte[planeBytes];
                ReadPlane(stream, plane, path);
                Buffer.BlockCopy(plane, 0, tile.Ground, 0, plane.Length);
                ReadPlane(stream, plane, path);
                Buffer.BlockCopy(plane, 0, tile.Level, 0, plane.Length);
                ReadPlane(stream, tile.Tags, path);
                if (stream.ReadByte() != -1)
                    throw new InvalidDataException($"Unexpected data after the last plane in {path}.");
                for (int i = 0; i < count; i++)
                    if (!float.IsFinite(tile.Ground[i]) || !float.IsFinite(tile.Level[i]) || tile.Tags[i] > 7)
                        throw new InvalidDataException($"Invalid ground or water sample in {path}.");
                return tile;
            }

            private static void ReadPlane(Stream stream, byte[] bytes, string path)
            {
                for (int offset = 0; offset < bytes.Length;)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0)
                        throw new EndOfStreamException($"Incomplete baked tile: {path}.");
                    offset += count;
                }
            }
        }
    }
}
#endif
