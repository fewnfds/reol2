using UnityEngine;

namespace Reol2.World
{
    internal enum RockLayer { Soil, Sand, Clay, Sedimentary, Granite, Basalt }
    internal enum Mineral { None, Salt, Coal, Iron, Copper, Sulfur }

    internal static class WorldGeology
    {
        internal static RockLayer LayerAt(float elevation, float ground)
        {
            float depth = ground - elevation;
            if (depth < 2f)
                return Mathf.Abs(ground) < 12f || ground < 0f ? RockLayer.Sand : RockLayer.Soil;
            if (depth < 8f)
                return RockLayer.Clay;
            if (depth < 180f)
                return RockLayer.Sedimentary;
            return depth < 1400f ? RockLayer.Granite : RockLayer.Basalt;
        }

        internal static Color RockTint(float depth, float spacing, Color stone)
        {
            float transition = Mathf.Max(4f, spacing * 1.5f);
            float granite = WorldNoise.Ramp((depth - 180f + transition) / (2f * transition));
            float basalt = WorldNoise.Ramp((depth - 1400f + transition) / (2f * transition));
            var sediment = new Color(stone.r * 1.12f, stone.g * 1.04f, stone.b * 0.89f);
            var igneous = new Color(stone.r * 0.95f, stone.g * 0.94f, stone.b * 1.07f);
            var deep = new Color(stone.r * 0.64f, stone.g * 0.69f, stone.b * 0.84f);
            return Color.Lerp(Color.Lerp(sediment, igneous, granite), deep, basalt);
        }
    }

    internal readonly struct MineralVein
    {
        internal readonly WorldPosition Center;
        private readonly Vector3 axis;
        private readonly int choice;

        internal MineralVein(WorldPosition center, Vector3 axis, int choice)
        {
            Center = center;
            this.axis = axis;
            this.choice = choice;
        }

        internal Mineral In(RockLayer layer)
        {
            return layer switch
            {
                RockLayer.Sedimentary => choice == 0 ? Mineral.Salt : Mineral.Coal,
                RockLayer.Granite => choice == 0 ? Mineral.Copper : Mineral.Iron,
                RockLayer.Basalt => choice == 0 ? Mineral.Sulfur : Mineral.Iron,
                _ => Mineral.None
            };
        }

        internal bool Contains(WorldPosition point)
        {
            var delta = new Vector3((float)(point.X - Center.X), (float)(point.Y - Center.Y), (float)(point.Z - Center.Z));
            float along = Vector3.Dot(delta, axis);
            return along * along / (26f * 26f) + (delta - along * axis).sqrMagnitude / (7f * 7f) <= 1f;
        }

        internal bool IntersectsPage(Vector3Int page)
        {
            const int size = ContinentField.ChunkSize;
            double x = Center.X - page.x * size, y = Center.Y - page.y * size, z = Center.Z - page.z * size;
            return x >= -26 && x <= size + 26 && y >= -26 && y <= size + 26 && z >= -26 && z <= size + 26;
        }
    }

    internal static class WorldResources
    {
        internal const int SearchRange = 96;
        internal static Vector3Int Region(WorldPosition point)
        {
            Vector3Int p = point.Cell;
            return new Vector3Int(p.x >> 7, p.y >> 7, p.z >> 7);
        }

        internal static bool TryVein(Vector3Int region, ulong seed, out MineralVein vein)
        {
            vein = default;
            ulong hash = WorldNoise.Hash(region.x, region.z, WorldNoise.Hash(region.y, 327, seed));
            ulong province = WorldNoise.Hash(region.x >> 5, region.z >> 5, seed + 9221);
            int richness = 1 + (int)(province & 3);
            if ((int)(hash & 15UL) >= richness)
                return false;

            // The longest radius fits inside its owning cube even when tilted.
            var center = new WorldPosition(region.x * 128 + 32 + (int)(hash >> 8 & 63),
                region.y * 128 + 32 + (int)(hash >> 16 & 63),
                region.z * 128 + 32 + (int)(hash >> 24 & 63));
            if (!WorldDefinition.Contains(center))
                return false;
            float angle = (float)(hash >> 40 & 255) * (2f * Mathf.PI / 256f);
            var axis = new Vector3(Mathf.Cos(angle), (int)(hash >> 32 & 15) * 0.05f - 0.35f, Mathf.Sin(angle));
            int choice = (int)((hash ^ province) >> 50 & 1);
            vein = new MineralVein(center, axis.normalized, choice);
            return true;
        }

        internal static Mineral At(WorldPosition point, RockLayer layer, ulong seed) =>
            TryVein(Region(point), seed, out MineralVein vein) && vein.Contains(point) ? vein.In(layer) : Mineral.None;
    }
}
