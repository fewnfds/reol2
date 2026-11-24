using System;
using System.Numerics;

namespace Reol2.World
{
    // Coordinates are metres relative to the centre of a bounded coastal region.
    internal sealed class LandformProfile
    {
        internal const float OffshoreHeight = -480f;
        private readonly ulong seed;
        private readonly float half, summit, ridgeWidth, ridgeOffset, ridgeTilt, temperateOffset;
        private readonly Vector2 plateau, basin, basinAxes;
        private readonly float basinFloor, basinRelief, basinAngle;

        internal LandformProfile(ulong seed, int size)
        {
            this.seed = seed;
            half = size * 0.5f;
            summit = 1640f * MathF.Pow(size / 16000f, 0.44f);
            ridgeWidth = MathF.Min(half * 0.29f, 22500f);
            ridgeOffset = half * (-0.21f + Variation(107) * 0.105f);
            ridgeTilt = Variation(109) * 0.28f;
            plateau = half * new Vector2(0.15f + Variation(31) * 0.075f, 0.38f + Variation(37) * 0.11f);
            float basinX = Variation(181) < 0.25f ? -0.47f : 0.35f;
            basin = half * new Vector2(basinX + Variation(41) * 0.075f, -0.27f + Variation(43) * 0.13f);
            basinAxes = half * new Vector2(0.205f + Variation(47) * 0.035f, 0.23f + Variation(53) * 0.04f);
            basinAngle = Variation(59) * 0.65f;
            basinFloor = 26f + summit * (0.075f + Variation(61) * 0.02f);
            basinRelief = summit * 0.29f;
            temperateOffset = Variation(67) * 2.4f;
        }

        internal Vector2 Basin => basin;
        internal float LakeLevel => basinFloor + MathF.Min(85f, basinRelief * 0.08f);
        internal Vector2 LakeExtent => Vector2.One * MathF.Max(basinAxes.X, basinAxes.Y) * 0.4f;
        internal float SeaTemperature(float z) => 26.5f + temperateOffset - 1.65f * (z / half);

        internal float InitialHeight(float x, float z)
        {
            if (MathF.Max(MathF.Abs(x), MathF.Abs(z)) >= half)
                return OffshoreHeight;

            float coast = 0.855f - Radius(x / half, z / (half * 1.03f))
                + 0.162f * Noise(x, z, half * 0.48f, 73) + 0.058f * Noise(x, z, half * 0.16f, 79)
                + 0.023f * Noise(x, z, MathF.Min(half * 0.071f, 6000f), 81);
            // A mudflat or a cliff can occur on either side of the same coastline.
            float coastSlope = 85f + 315f * Ramp((Noise(x, z, half * 0.32f, 83) + 0.55f) / 1.1f);
            float edge = Ramp((half - MathF.Max(MathF.Abs(x), MathF.Abs(z))) / (half * 0.075f));
            if (coast <= 0f)
            {
                float shelf = coast * coastSlope + OffshoreHeight * Ramp((-coast - 0.035f) / 0.23f);
                return Mix(OffshoreHeight, MathF.Max(OffshoreHeight, shelf), edge);
            }

            float inland = Ramp(coast / 0.16f);
            float foothills = Ramp((Noise(x, z, half * 0.42f, 89) + 0.22f) / 0.91f);
            float hills = 0.54f + 0.29f * Noise(x, z, MathF.Min(half * 0.23f, 8000f), 97)
                + 0.12f * Noise(x, z, 1200f, 101) + 0.05f * Noise(x, z, 475f, 103);
            float coastalRelief = Ramp(coast / 0.093f);
            float lowland = coast * coastSlope + coastalRelief * hills * (16f + summit * 0.12f * foothills);

            float ridgeAxis = ridgeOffset + z * ridgeTilt
                + ridgeWidth * (0.90f * Noise(230f, z, half * 0.49f, 113)
                + 0.28f * Noise(230f, z, MathF.Min(half * 0.24f, 11500f), 127));
            float slope = (x - ridgeAxis) / ridgeWidth;
            float belt = MathF.Exp(-slope * slope * 1.6f) * Ramp((0.86f - MathF.Abs(z / half)) / 0.28f);
            float spurAxis = ridgeAxis - ridgeWidth * (1.00f + 0.61f * Noise(800f, z, half * 0.51f, 131));
            float spur = (x - spurAxis) / (ridgeWidth * 0.82f);
            belt = MathF.Max(belt, 0.47f * MathF.Exp(-spur * spur * 1.6f)
                * Ramp((0.74f - MathF.Abs(z / half + 0.14f)) / 0.42f));

            float ridgeNoise = Noise(x, z, MathF.Min(half * 0.32f, 9400f), 137);
            float peak = 1f - MathF.Abs(ridgeNoise + 0.28f * Noise(x, z, 2400f, 139));
            float crags = 0.72f * Noise(x, z, 1260f, 149) + 0.28f * Noise(x, z, 530f, 151);
            float mountains = summit * belt * (0.47f + 0.51f * peak * peak + crags * 0.24f);

            float plateauX = (x - plateau.X) / half, plateauZ = (z - plateau.Y) / half;
            float u = (plateauX + plateauZ * 0.21f) / 0.34f + Noise(x, z, half * 0.24f, 157) * 0.27f;
            float v = (plateauZ - plateauX * 0.34f) / 0.25f + Noise(x, z, half * 0.19f, 159) * 0.22f;
            float mesaRadius = 0.73f * MathF.Max(MathF.Abs(u), MathF.Abs(v)) + 0.27f * Radius(u, v);
            mesaRadius += 0.14f * Noise(x, z, half * 0.088f, 161);
            float mesa = Ramp((1.18f - mesaRadius) / 0.54f);
            float elevatedPlain = summit * (0.30f + 0.027f * Noise(x, z, 2300f, 163));
            float height = lowland + inland * Mix(mountains, MathF.Max(mountains, elevatedPlain), mesa);

            float bowl = BasinRadius(x, z);
            if (bowl < 1.25f)
            {
                float profile = basinFloor + basinRelief * MathF.Min(1.25f, bowl * bowl)
                    * (1f + Ramp((bowl - 0.40f) / 0.6f) * crags * 0.36f);
                height = Mix(height, profile, Ramp((1.25f - bowl) / 0.42f));
            }

            // Keep the small-scale relief weaker on floodplains, near the lake, and at the shoreline.
            float roughness = inland * MathF.Max(belt * (1f - mesa * 0.65f), foothills * 0.26f);
            float detail = 6.5f * Noise(x, z, 175f, 167) + 1.4f * Noise(x, z, 63f, 173);
            height += roughness * Ramp((bowl - 0.38f) / 0.31f) * detail;
            return Mix(OffshoreHeight, MathF.Max(0.02f, height), edge);
        }

        private float BasinRadius(float x, float z)
        {
            float dx = x - basin.X, dz = z - basin.Y;
            float radius = Radius((dx - dz * basinAngle) / basinAxes.X, (dz + dx * basinAngle) / basinAxes.Y);
            if (radius < 1.5f)
                radius *= 1f + 0.17f * Noise(x, z, half * 0.11f, 191) + 0.06f * Noise(x, z, half * 0.037f, 193);
            return radius;
        }

        private float Variation(int offset) => WorldNoise.Unit(WorldNoise.Hash(offset, 0, seed)) * 2f - 1f;

        private float Noise(float x, float z, float wavelength, int offset)
        {
            // Fixed-point conversion preserves seed parity with the existing world noise at fractional wavelengths.
            const int spanShift = 12;
            float scale = (1 << spanShift) / wavelength;
            return WorldNoise.Value((int)MathF.Floor(x * scale), (int)MathF.Floor(z * scale), spanShift,
                unchecked(seed + (ulong)offset));
        }

        private static float Radius(float x, float z) => MathF.Sqrt(x * x + z * z);
        internal static float Ramp(float t) => WorldNoise.Ramp(t);
        internal static float Mix(float a, float b, float t) => a + (b - a) * t;
    }
}
