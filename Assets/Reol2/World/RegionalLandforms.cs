using System;
using System.Numerics;
using static Reol2.World.LandformProfile;

namespace Reol2.World
{
    internal sealed class RegionalLandforms
    {
        internal const int DrainageSpacing = 250;
        private readonly LandformProfile profile;
        private readonly RegionalClimate[] climate;
        private readonly int half;
        private readonly Vector2 lakeMin, lakeMax;
        private readonly RegionalWaterFeatures waterFeatures;

        internal int Size => half * 2;
        internal int Side { get; }
        internal bool HasLake { get; }
        internal Vector2 LakeMin => lakeMin;
        internal Vector2 LakeMax => lakeMax;
        internal float LakeLevel => profile.LakeLevel;
        internal Vector2 Basin => profile.Basin;
        internal RegionalRivers Rivers { get; }
        internal Vector2 Landing { get; }
        internal Vector2 LandingView { get; }
        internal Vector2 Peak { get; }
        internal Vector2 SeaEntry { get; }

        internal static bool Supports(int kilometres) => kilometres is 16 or 64 or 256;

        internal RegionalLandforms(ulong seed, int kilometres, out RegionalDrainage drainage)
        {
            if (!Supports(kilometres))
                throw new ArgumentOutOfRangeException(nameof(kilometres));
            int size = kilometres * 1000;
            half = size / 2;
            Side = size / DrainageSpacing + 1;
            profile = new LandformProfile(seed, size);
            lakeMin = profile.Basin - profile.LakeExtent;
            lakeMax = profile.Basin + profile.LakeExtent;
            var ground = new float[Side * Side];
            int high = 0;
            for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                int index = x + z * Side;
                ground[index] = profile.InitialHeight(x * DrainageSpacing - half, z * DrainageSpacing - half);
                if (ground[index] > ground[high])
                    high = index;
            }
            Peak = PointAt(high % Side, high / Side);
            climate = RegionalClimate.Generate(profile, ground, Side, DrainageSpacing);
            drainage = new RegionalDrainage(ground, climate, Side, DrainageSpacing, profile);
            // A closed depression may become a salt flat when its catchment cannot supply surface water.
            float inflow = 0f;
            int lakeArea = 0;
            for (int i = 0; i < ground.Length; i++)
            {
                if (drainage.Downstream[i] != RegionalDrainage.Basin)
                    continue;
                inflow += drainage.Runoff[i] - climate[i].Runoff;
                lakeArea++;
            }
            HasLake = lakeArea != 0 && inflow >= lakeArea * 0.36f;
            Rivers = new RegionalRivers(drainage, size, DrainageSpacing, HasLake, profile.InitialHeight);
            waterFeatures = new RegionalWaterFeatures(this, ground);
            (Landing, LandingView) = FindLanding(ground);

            for (int x = -half; x < 0; x += 32)
            {
                if (HeightAt(new Vector2(x, 0)) >= -45f)
                {
                    SeaEntry = new Vector2(x - 32f, 0f);
                    break;
                }
            }
        }

        internal float HeightAt(Vector2 point)
        {
            float height = profile.InitialHeight(point.X, point.Y);
            return Rivers.Sculpt(point, height);
        }

        internal void Sample(Vector2 point, out float ground, out float water)
        {
            ground = profile.InitialHeight(point.X, point.Y);
            water = 0f;
            if (!Contains(point))
                return;
            Rivers.Sample(point, ref ground, out float river, out bool tidal);
            water = ReceivingWater(point, ground, river, tidal);
        }

        internal float WaterAt(Vector2 point) => WaterAt(point, HeightAt(point));

        internal float WaterAt(Vector2 point, float ground)
        {
            if (!Contains(point))
                return 0f;
            float water = Rivers.WaterAt(point, ground, out bool tidal);
            return ReceivingWater(point, ground, water, tidal);
        }

        private float ReceivingWater(Vector2 point, float ground, float water, bool tidal)
        {
            if (HasLake && point.X >= lakeMin.X && point.X <= lakeMax.X && point.Y >= lakeMin.Y && point.Y <= lakeMax.Y)
                water = MathF.Max(water, profile.LakeLevel);
            if (water == 0f && ground < 0f && !tidal && !waterFeatures.NearConnectedSea(point))
                return ground - 0.25f;
            return water;
        }

        internal WaterFeatures WaterFeaturesIn(Vector2 start, int width) => waterFeatures.Query(start, width);

        internal bool NearConnectedSea(Vector2 point)
        {
            if (waterFeatures.NearConnectedSea(point))
                return true;
            Rivers.WaterAt(point, 0f, out bool tidal);
            return tidal;
        }

        internal bool Contains(Vector2 point) => MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)) < half;

        internal RegionalClimate ClimateNode(int index) => climate[index];

        internal RegionalClimate ClimateAt(Vector2 point)
        {
            float x = Math.Clamp((point.X + half) / DrainageSpacing, 0f, Side - 1f);
            float z = Math.Clamp((point.Y + half) / DrainageSpacing, 0f, Side - 1f);
            int a = Math.Min((int)x, Side - 2), b = Math.Min((int)z, Side - 2);
            int index = a + b * Side;
            return RegionalClimate.Mix(RegionalClimate.Mix(climate[index], climate[index + 1], x - a),
                RegionalClimate.Mix(climate[index + Side], climate[index + Side + 1], x - a), z - b);
        }

        internal LandBiome BiomeAt(Vector2 point, float ground)
        {
            if (ground <= 0f)
                return LandBiome.Ocean;
            if (WaterAt(point, ground) > ground && HasLake && point.X >= lakeMin.X && point.X <= lakeMax.X
                && point.Y >= lakeMin.Y && point.Y <= lakeMax.Y)
                return LandBiome.Lake;
            return ClimateAt(point).Biome;
        }

        private Vector2 PointAt(int x, int z) => new(x * DrainageSpacing - half, z * DrainageSpacing - half);

        private (Vector2 point, Vector2 view) FindLanding(float[] ground)
        {
            float best = float.NegativeInfinity;
            var landing = Vector2.Zero;
            var view = Vector2.UnitY;
            int stride = Math.Max(1, Side / 180);
            for (int z = 10; z < Side - 10; z += stride)
            for (int x = 10; x < Side - 10; x += stride)
            {
                int index = x + z * Side;
                float height = ground[index];
                if (height < 14f || height > 950f)
                    continue;
                float riseX = ground[index + 1] - ground[index - 1];
                float riseZ = ground[index + Side] - ground[index - Side];
                float slope = MathF.Sqrt(riseX * riseX + riseZ * riseZ) / (2 * DrainageSpacing);
                if (slope > 0.24f)
                    continue;

                float nearby = MathF.Max(MathF.Max(MathF.Abs(ground[index + 2] - height), MathF.Abs(ground[index - 2] - height)),
                    MathF.Max(MathF.Abs(ground[index + 2 * Side] - height), MathF.Abs(ground[index - 2 * Side] - height)));
                Vector2 point = PointAt(x, z) + new Vector2(0.5f);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int direction = dx + Side * dz;
                    float distance = dx != 0 && dz != 0 ? DrainageSpacing * 1.41421356f : DrainageSpacing;
                    float foreground = (ground[index + direction] - height) / distance;
                    float middle = (ground[index + 2 * direction] - height) / (2f * distance);
                    float ridge = MathF.Max((ground[index + 4 * direction] - height) / (4f * distance),
                        (ground[index + 8 * direction] - height) / (8f * distance));
                    float visibleRidge = ridge - MathF.Max(0f, MathF.Max(foreground, middle));
                    float score = Math.Clamp(visibleRidge, -0.5f, 0.55f) * 390f
                        + Math.Clamp(-foreground, -0.4f, 0.28f) * 255f + MathF.Min(nearby, 140f) * 0.62f
                        - slope * 255f - MathF.Max(0f, height - 150f) * 0.12f;
                    if (score <= best)
                        continue;
                    Vector2 towards = Vector2.Normalize(new Vector2(dx, dz));
                    if (!HasClearForeground(point, towards) || !HasWalkableApproach(point))
                        continue;
                    best = score;
                    landing = point;
                    view = towards;
                }
            }

            if (float.IsNegativeInfinity(best))
                throw new InvalidOperationException("The landform region had no dry walkable landing within its search area.");
            return (landing, view);
        }

        private bool HasClearForeground(Vector2 point, Vector2 view)
        {
            float eye = HeightAt(point) + 1.6f;
            for (int distance = 8; distance <= 128; distance *= 2)
            {
                if (HeightAt(point + view * distance) > eye + distance * 0.21f)
                    return false;
            }
            return true;
        }

        private bool HasWalkableApproach(Vector2 point)
        {
            float start = HeightAt(point);
            if (start < WaterAt(point, start) + 3f)
                return false;
            // Check a dry, continuous 24 m route. The player entrance still tests real mesh clearance.
            foreach (Vector2 direction in ApproachDirections)
            {
                float previous = start;
                bool valid = true;
                for (int distance = 4; distance <= 24; distance += 4)
                {
                    Vector2 sample = point + direction * distance;
                    float height = HeightAt(sample);
                    if (height < WaterAt(sample, height) + 1.8f || MathF.Abs(height - previous) > 1.15f)
                    {
                        valid = false;
                        break;
                    }
                    previous = height;
                }
                if (valid)
                    return true;
            }
            return false;
        }

        private static readonly Vector2[] ApproachDirections = { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY };
    }
}
