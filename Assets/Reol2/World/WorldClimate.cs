using System;
using UnityEngine;

namespace Reol2.World
{
    internal readonly struct ClimateSample
    {
        internal readonly float Temperature;
        internal readonly float Humidity;
        internal readonly Vector2 Wind;

        internal ClimateSample(float temperature, float humidity, Vector2 wind)
        {
            Temperature = temperature;
            Humidity = humidity;
            Wind = wind;
        }
    }

    internal static class WorldClimate
    {
        internal static ClimateSample Sample(WorldPosition point, float ground, long tick, ulong seed)
        {
            Vector3Int cell = point.Cell;
            float continental = WorldNoise.Value(cell.x, cell.z, 19, seed + 751);
            float local = WorldNoise.Value(cell.x, cell.z, 16, seed + 761);
            float latitude = Mathf.Clamp01(Mathf.Abs(cell.z) / (float)WorldDefinition.Extent);
            float season = Mathf.Cos((tick % WorldClock.Year) / (float)WorldClock.Year * (2f * Mathf.PI));
            float day = Mathf.Cos(((tick % WorldClock.Day) / (float)WorldClock.Day - 0.62f) * (2f * Mathf.PI));
            float height = Mathf.Max(0f, (float)point.Y);
            float temperature = 21f - 24f * latitude - height * 0.0058f
                + continental * 8f + local * 4f + season * 6f + day * 3f;
            float coast = 1f - WorldNoise.Ramp(Mathf.Max(ground, 0f) / 400f);
            float humidity = Mathf.Clamp01(0.48f + continental * 0.18f + local * 0.23f + coast * 0.18f - season * 0.08f);
            float heading = WorldNoise.Value(cell.x, cell.z, 18, seed + 769) * Mathf.PI;
            Vector2 wind = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * (3f + 6f * humidity);
            return new ClimateSample(temperature, humidity, wind);
        }
    }

    internal readonly struct WeatherSample
    {
        internal readonly float Cloud;
        internal readonly float Rain;
        internal readonly float Snow;
        internal readonly float Haze;
        internal readonly float Storm;

        internal WeatherSample(float cloud, float rain, float snow, float haze, float storm)
        {
            Cloud = cloud;
            Rain = rain;
            Snow = snow;
            Haze = haze;
            Storm = storm;
        }
    }

    internal static class WorldWeather
    {
        internal const int Interval = 3 * WorldClock.Hour;

        internal static WeatherSample Sample(WorldPosition point, long tick, ClimateSample climate, ulong seed)
        {
            Vector3Int cell = point.Cell;
            ulong period = (ulong)(tick / Interval);
            float transition = WorldNoise.Ramp((tick % Interval) / (float)Interval);
            float previous = WorldNoise.Value(cell.x, cell.z, 15, unchecked(seed + period * 12582917UL));
            float next = WorldNoise.Value(cell.x, cell.z, 15, unchecked(seed + (period + 1) * 12582917UL));
            float moisture = Mathf.Clamp01(climate.Humidity * 0.83f + Mathf.LerpUnclamped(previous, next, transition) * 0.47f);
            float cloud = WorldNoise.Ramp((moisture - 0.20f) / 0.61f);
            float precipitation = WorldNoise.Ramp((moisture - 0.55f) / 0.37f);
            float frozen = 1f - WorldNoise.Ramp((climate.Temperature + 2f) / 5f);
            float haze = cloud * 0.23f + precipitation * 0.66f;
            float storm = WorldNoise.Ramp((precipitation - 0.65f) / 0.35f) * (1f - frozen);
            return new WeatherSample(cloud, precipitation * (1f - frozen), precipitation * frozen, haze, storm);
        }
    }
}
