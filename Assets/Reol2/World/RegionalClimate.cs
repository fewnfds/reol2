using System;
using static Reol2.World.LandformProfile;

namespace Reol2.World
{
    internal enum LandBiome : byte
    {
        Ocean, Lake, Alpine, Tundra, Desert, DrySteppe, Grassland, TemperateForest,
        MontaneForest, TropicalSeasonalForest, TropicalRainforest
    }

    internal readonly struct RegionalClimate
    {
        internal readonly float Temperature, Rainfall, DriestMonth;

        internal RegionalClimate(float temperature, float rainfall, float driestMonth)
        {
            Temperature = temperature;
            Rainfall = rainfall;
            DriestMonth = driestMonth;
        }

        internal float Aridity => Rainfall / MathF.Max(340f, 450f + Temperature * 30f);
        internal float Runoff => MathF.Max(0f, Rainfall - MathF.Max(280f, 400f + Temperature * 17f) * 0.48f) / 1000f;

        internal LandBiome Biome => Temperature < -1f ? LandBiome.Alpine
            : Temperature < 5f ? LandBiome.Tundra
            : Aridity < 0.20f ? LandBiome.Desert
            : Aridity < 0.43f ? LandBiome.DrySteppe
            : Temperature >= 22f && DriestMonth >= 60f && Rainfall >= 1700f ? LandBiome.TropicalRainforest
            : Temperature >= 20f && Aridity >= 0.84f ? LandBiome.TropicalSeasonalForest
            : Temperature < 15f && Aridity >= 0.94f ? LandBiome.MontaneForest
            : Aridity >= 0.81f ? LandBiome.TemperateForest
            : LandBiome.Grassland;

        internal static RegionalClimate Mix(RegionalClimate a, RegionalClimate b, float t) => new(
            LandformProfile.Mix(a.Temperature, b.Temperature, t), LandformProfile.Mix(a.Rainfall, b.Rainfall, t),
            LandformProfile.Mix(a.DriestMonth, b.DriestMonth, t));

        internal static RegionalClimate[] Generate(LandformProfile profile, float[] ground, int side, float step)
        {
            var climate = new RegionalClimate[ground.Length];
            var upwind = new float[side];
            var current = new float[side];
            float half = (side - 1) * step * 0.5f;
            Sweep(0, 1);
            Array.Clear(upwind, 0, side);
            Sweep(side - 1, -1);
            return climate;

            void Sweep(int first, int direction)
            {
                for (int x = first; (uint)x < side; x += direction)
                {
                    int ahead = Math.Clamp(x + 3 * direction, 0, side - 1);
                    int behind = Math.Clamp(x - 4 * direction, 0, side - 1);
                    for (int z = 0; z < side; z++)
                    {
                        int index = x + z * side;
                        float seaTemperature = profile.SeaTemperature(z * step - half);
                        float oceanMoisture = 0.90f + seaTemperature * 0.017f;
                        float available = MixMoisture(upwind, z);
                        float altitude = MathF.Max(0f, ground[index]);
                        float temperature = seaTemperature - altitude * 0.0062f;
                        if (ground[index] <= 0f)
                        {
                            current[z] = LandformProfile.Mix(available, oceanMoisture, 0.27f);
                            climate[index] = new RegionalClimate(temperature, oceanMoisture * 1660f, oceanMoisture * 95f);
                            continue;
                        }

                        // Winds respond to the broader slopes rather than each 250 m rise in the DEM.
                        float rise = (MeanAltitude(ground, side, ahead, z) - MeanAltitude(ground, side, behind, z)) / 7f;
                        float deposited = MathF.Max(0f, rise);
                        float rain = 60f + available * (1480f + 1360f * Ramp(deposited / 92f));
                        float depletion = 1f - MathF.Exp(-step / 55000f - deposited / 910f);
                        current[z] = available * (1f - depletion) + 0.00085f;

                        if (direction > 0)
                        {
                            climate[index] = new RegionalClimate(temperature, rain, 0f);
                            continue;
                        }

                        // Westerlies dominate the annual budget. Seasonal onshore winds also supply the other coast.
                        float west = climate[index].Rainfall;
                        float annual = LandformProfile.Mix(west, rain, 0.16f);
                        float drySeason = MathF.Min(LandformProfile.Mix(west, rain, 0.08f),
                            LandformProfile.Mix(west, rain, 0.42f));
                        climate[index] = new RegionalClimate(temperature, annual, drySeason * (0.64f / 12f));
                    }
                    (upwind, current) = (current, upwind);
                }
            }
        }

        private static float MixMoisture(float[] upwind, int z) => upwind[z] * 0.84f
            + (upwind[Math.Max(0, z - 1)] + upwind[Math.Min(upwind.Length - 1, z + 1)]) * 0.08f;

        private static float MeanAltitude(float[] ground, int side, int x, int z) =>
            MathF.Max(0f, ground[x + z * side]) * 0.5f
            + (MathF.Max(0f, ground[x + Math.Max(0, z - 1) * side])
            + MathF.Max(0f, ground[x + Math.Min(side - 1, z + 1) * side])) * 0.25f;
    }
}
