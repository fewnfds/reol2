using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Reol2.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Stage = Reol2.World.ContinentStreamProbe.Stage;

namespace Reol2.World
{
    [DisallowMultipleComponent]
    public sealed class ContinentTerrain : MonoBehaviour
    {
        public const int MapWidth = ContinentField.Extent * 2;
        public const int LowestElevation = ContinentField.Bedrock;
        private const int PoolLimit = 32;
        private const int NearRadius = 4;
        private const int LeadRadius = 2;
        private const int PagesPerFrame = 72;
        private const int SurfacesPerFrame = 3;
        private const double BuildBudgetMs = 2.25;
        private const float Reach = 5.5f;

        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material oceanMaterial;
        [SerializeField] private bool largeWorld;
        [SerializeField] private long worldSeed = 20261121;

        private readonly Dictionary<Vector3Int, ResidentPage> ready = new(800);
        private readonly Stack<ChunkView> pool = new();
        private readonly Dictionary<Vector3Int, float> requests = new(512);
        private readonly List<KeyValuePair<Vector3Int, float>> queue = new(512);
        private readonly List<Vector3Int> retired = new(512);
        private readonly HashSet<Vector2Int> changedColumns = new();
        private readonly List<Vector3Int> changedChunks = new();
        private readonly MarchingCubes mesher = new();
        private readonly WorldFrame frame = new();
        private Func<Vector3Int, Vector3, Vector3, Color32> palette;
        private Vector4 paletteHeights;
        private WorldMaterial.PageTint paletteTint;
        private MineralVein? paletteVein;
        private bool paletteEdited;
        private ContinentField field;
        private ContinentHorizon horizon;
        private WorldHorizon worldHorizon;
        private WorldCaveHorizon caveHorizon;
        private WorldInlandWater inlandWater;
        private OceanSurface ocean;
        private Camera horizonView;
        private WorldTerrainSave terrainSave;
        private static readonly int GroundOrigin = Shader.PropertyToID("_ReolGroundOrigin");
        private bool coverageDirty;
        private Vector3Int center;
        private Vector3Int lead;
        private int next;
        private bool scheduled;
        private ContinentStreamProbe probe;
        private WorldPosition? deepSea;
        private WorldPosition? terrainDemoLanding;

        public bool IsLargeWorld => largeWorld;
        public long Seed => worldSeed;
        internal string TerrainSaveStatus => terrainSave?.Status;
        public Vector3 Landing => frame.ToLocal(WorldLanding);
        public WorldPosition WorldLanding => terrainDemoLanding ?? field.Landing;
        internal bool IsTerrainDemo => terrainDemoLanding.HasValue;
        public WorldPosition OceanEntry => field.SeaEntry;
        public WorldPosition WestCaveEntrance => field.Caves.WestEntrance;
        public WorldPosition EastCaveEntrance => field.Caves.EastEntrance;
        public WorldPosition CaveChamber => field.Caves.Chamber;
        public WorldPosition DeepChamber => field.Caves.DeepChamber;
        internal RegionalLandforms Landforms => field.Surface?.Landforms;
        internal WorldPosition BasinEntry => Landforms is RegionalLandforms region
            ? field.Surface.Place(region.Basin, region.LakeLevel + 110)
            : new WorldPosition(field.Surface.Center.x + WorldSurface.BasinEast, WorldSurface.LakeLevel + 24,
                field.Surface.Center.y + WorldSurface.BasinNorth);
        internal WorldPosition DeepSeaEntry
        {
            get
            {
                if (!deepSea.HasValue)
                    deepSea = field.Surface.FindDeepSea();
                return deepSea.Value;
            }
        }
        internal WorldPosition MountainOverview => Landforms is RegionalLandforms region
            ? field.Surface.Place(new System.Numerics.Vector2(-region.Size * 0.38f, -region.Size * 0.38f),
                Math.Min(WorldDefinition.MaxY - 8d, region.Size * 0.21d + region.HeightAt(region.Peak)))
            : new WorldPosition(field.Surface.Center.x - 87600, WorldDefinition.MaxY - 8, field.Surface.Center.y - 34000);
        internal IReadOnlyList<WorldPosition> CavePassage => field.Caves.Passage;
        public int ActivePages => ready.Count;
        public int VisibleChunks { get; private set; }
        public int PendingPages => requests.Count;
        public int EditedSamples => field.EditedSamples;
        public int BuiltPages { get; private set; }
        public double LastStreamMs { get; private set; }
        public int HorizonTiles => worldHorizon?.ResidentTiles ?? 0;
        internal int PendingHorizonTiles => worldHorizon?.PendingTiles ?? 0;
        internal (int Tiles, int Cells, int Longest) RegionalHorizonBuilds => worldHorizon == null ? default
            : (worldHorizon.RegionalTilesBuilt, worldHorizon.RegionalCellVisits, worldHorizon.LongestRegionalTileBuild);
        public int HorizonTriangles => worldHorizon?.VisibleTriangles ?? 0;
        internal int CaveTiles => caveHorizon?.VisibleTiles ?? 0;
        internal bool HasPendingCavePages => caveHorizon?.HasPendingPages == true;
        internal int HorizonLevelAt(WorldPosition point) => worldHorizon.VisibleLevelAt(point);
        internal long EvaluatedDensitySamples => field.EvaluatedDensitySamples;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal Vector3Int FrameOrigin => frame.Origin;
        internal WorldRenderReport.TileState[] RenderReportTiles() => worldHorizon.ReportVisibleTiles(frame);
        internal (int Edges, float OriginalError, float Error) MeasureDryHorizonEdges() => worldHorizon.MeasureDryEdges();
        internal (int Leaves, int WithError, int AboveThreshold, float MaximumPixels) ProjectedHorizonErrors() =>
            worldHorizon.MeasureProjectedErrors();
        internal int RegularHorizonBuilds => worldHorizon?.RegularTilesBuilt ?? 0;
        internal (int Ready, int Pending) CaveEntranceProgress => caveHorizon.EntranceProgress;
        internal int RenderReportInlandWaterTriangles => worldHorizon?.SharedInlandWaterTriangles ?? 0;
        internal int RenderReportSeaTriangles => ocean.TriangleCount - RenderReportInlandWaterTriangles;
        internal int LastEditPublishedPages => changedChunks.Count;
        internal bool TerrainSaveEnabled => terrainSave?.CanEdit == true;
        internal bool TerrainEditsPending => terrainSave?.HasPendingEdits == true;
#endif

        internal ContinentStreamProbe EnableStreamProbe() => probe ??= new ContinentStreamProbe();

        internal float ViewDistanceOverride { get; private set; }

        private void Awake()
        {
            ocean = new OceanSurface(transform, oceanMaterial);
            if (largeWorld)
            {
                string[] arguments = Environment.GetCommandLineArgs();
                IWorldSurfaceData surfaceData = null;
                int seedFlag = Array.IndexOf(arguments, "-world-seed");
                if (seedFlag >= 0 && seedFlag + 1 < arguments.Length
                    && long.TryParse(arguments[seedFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long requested))
                    worldSeed = requested;
                var world = new WorldDefinition(worldSeed);
                RegionalLandforms landforms = null;
                int regionFlag = Array.IndexOf(arguments, "-landforms");
                if (regionFlag >= 0)
                {
                    if (regionFlag + 1 < arguments.Length && int.TryParse(arguments[regionFlag + 1], out int size)
                        && RegionalLandforms.Supports(size))
                    {
                        var timer = Stopwatch.StartNew();
                        landforms = new RegionalLandforms(world.SeedBits, size, out _);
                        UnityEngine.Debug.Log(FormattableString.Invariant($"Regional landforms | seed {worldSeed} | {size} km | {landforms.Side * landforms.Side} samples | {landforms.Rivers.Reaches.Length} river reaches | {timer.Elapsed.TotalMilliseconds:F2} ms"));
                    }
                    else
                        UnityEngine.Debug.LogWarning("Use -landforms 16, -landforms 64 or -landforms 256 to select a test region.");
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (Array.IndexOf(arguments, "-reol-native-resolution") >= 0)
                {
                    DisplayInfo display = Screen.mainWindowDisplayInfo;
                    Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
                }
#endif
                int rangeFlag = Array.IndexOf(arguments, "-world-view-distance-km");
                if (rangeFlag >= 0 && rangeFlag + 1 < arguments.Length
                    && float.TryParse(arguments[rangeFlag + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float range)
                    && range >= 10f && range <= 160f)
                    ViewDistanceOverride = range * 1000f / 1.07f;
                int surfaceFlag = Array.IndexOf(arguments, "-world-surface-bake");
                if (surfaceFlag >= 0)
                {
                    if (surfaceFlag + 1 >= arguments.Length)
                        throw new ArgumentException("-world-surface-bake requires a fixture directory.");
                    surfaceData = TiledSurfaceData.Load(arguments[surfaceFlag + 1], worldSeed, landforms != null);
                }
#endif
                field = new ContinentField(world, landforms, surfaceData);
                if (surfaceData == null)
                    OpenTerrainSave(arguments, landforms?.Size ?? 0);
                if (terrainSave != null && !Application.isEditor)
                    Application.wantsToQuit += terrainSave.TrySaveBeforeQuit;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
                if (surfaceData == null && worldSeed == 20261121 && landforms?.Size == 64000
                    && Array.IndexOf(arguments, "-terrain-demo") >= 0)
                {
                    const double x = 8778.5, z = 6108.5;
                    terrainDemoLanding = new WorldPosition(x, field.HeightAt(x, z) + 0.18, z);
                }
#endif
                worldHorizon = new WorldHorizon(transform, field, terrainMaterial, ocean, oceanMaterial);
                caveHorizon = new WorldCaveHorizon(transform, field, worldHorizon);
                if (!field.Surface.HasWaterTiles)
                    inlandWater = new WorldInlandWater(transform, field.Surface, oceanMaterial);
                palette = GlobalColor;
                frame.CenterOn(WorldLanding);
                inlandWater?.Place(frame);
            }
            else
            {
                field = new ContinentField();
                horizon = new ContinentHorizon(transform, field, terrainMaterial);
                palette = LegacyColor;
                ocean.BuildFixed(field, frame);
            }
            SetGroundOrigin();
        }

        private void OpenTerrainSave(string[] arguments, int regionSize)
        {
            int rootFlag = Array.IndexOf(arguments, "-world-save-root");
            if (rootFlag >= 0)
            {
                if (rootFlag + 1 < arguments.Length && !arguments[rootFlag + 1].StartsWith("-", StringComparison.Ordinal))
                    terrainSave = new WorldTerrainSave(field, arguments[rootFlag + 1], worldSeed, regionSize);
                else
                    UnityEngine.Debug.LogWarning("-world-save-root requires a directory.");
                return;
            }
            if (!Application.isEditor && Array.IndexOf(arguments, "-continent-benchmark") < 0)
                terrainSave = new WorldTerrainSave(field, Path.Combine(Application.persistentDataPath, "WorldSaves"),
                    worldSeed, regionSize);
        }

        private void Update() => terrainSave?.Tick(Time.unscaledTime);

        internal bool TryQuitWithoutTerrainSave()
        {
            if (Application.isEditor || terrainSave?.ConfirmQuitWithoutSaving() != true)
                return false;
            Application.Quit();
            return true;
        }

        public WorldPosition ToWorld(Vector3 local) => frame.ToWorld(local);
        internal void SetHorizonView(Camera view) => horizonView = view;
        public Vector3 ToLocal(WorldPosition point) => frame.ToLocal(point);
        internal WorldPosition PlaceInRegion(System.Numerics.Vector2 point, double height) => field.Surface.Place(point, height);
        public float SurfaceHeight(WorldPosition point) => field.HeightAt(point.X, point.Z);
        internal RegionalClimate? GeneratedClimateAt(WorldPosition point) => field.Surface?.GeneratedClimateAt(point);
        public float SurfaceHeight(float x, float z) => largeWorld
            ? SurfaceHeight(frame.ToWorld(new Vector3(x, 0f, z))) - frame.Origin.y : field.HeightAt(x, z);
        public bool IsOcean(Vector3 point) => field.InOcean(frame.ToWorld(point));
        internal bool IsWater(Vector3 point) => field.InWater(frame.ToWorld(point));
        internal bool TrySampleWaterColumn(WorldPosition feet, out float surface, out float bottom)
        {
            surface = field.WaterSurface(feet);
            bottom = feet.Y < surface ? field.HeightAt(feet.X, feet.Z) : surface;
            return feet.Y < surface && feet.Y >= bottom - 0.25 && surface > bottom;
        }
        internal bool TryGetWaterColumn(WorldPosition point, out float surface, out float bottom)
        {
            surface = field.WaterSurface(point);
            bottom = field.HeightAt(point.X, point.Z);
            return surface > bottom;
        }
        internal bool IsUnderground(Vector3 point)
        {
            WorldPosition world = frame.ToWorld(point);
            return world.Y + 8 < field.HeightAt(world.X, world.Z);
        }
        internal float HorizonDistance(double elevation)
        {
            if (ViewDistanceOverride > 0f)
                return ViewDistanceOverride;
            float height = Mathf.Max(0f, (float)elevation);
            float range = Mathf.Lerp(20000f, 150000f, Mathf.Clamp01(height / WorldDefinition.MaxY));
            return Landforms == null ? range : Mathf.Max(range, Mathf.Min(90000f, 20000f + height * 6f));
        }

        internal bool NeedsShift(Vector3 local) => largeWorld && frame.NeedsShift(local);

        internal bool AllowsEntry(WorldPosition point)
        {
            if (!largeWorld)
                return true;
            if (!WorldDefinition.Contains(point) || point.X < -WorldDefinition.Extent + 0.54
                || point.X > WorldDefinition.Extent - 0.54 || point.Z < -WorldDefinition.Extent + 0.54
                || point.Z > WorldDefinition.Extent - 0.54 || point.Y < WorldDefinition.MinY + 0.57
                || point.Y > WorldDefinition.MaxY - 2.09)
                return false;
            return !field.IsSolidAt(new WorldPosition(point.X, point.Y + 0.25, point.Z))
                && !field.IsSolidAt(new WorldPosition(point.X, point.Y + 1.38, point.Z));
        }

        internal void CenterWorld(WorldPosition point)
        {
            if (!largeWorld)
                return;
            Vector3Int previous = frame.Origin;
            frame.CenterOn(point);
            if (previous == frame.Origin)
                return;
            Vector3Int delta = frame.Origin - previous;
            if (Math.Abs(delta.x) > 8192 || Math.Abs(delta.y) > 8192 || Math.Abs(delta.z) > 8192)
            {
                foreach (ResidentPage page in ready.Values)
                    Recycle(page.View);
                ready.Clear();
                requests.Clear();
                queue.Clear();
                next = 0;
                probe?.SetRequests(requests);
            }
            else
            {
                Vector3Int anchor = frame.PageOrigin;
                foreach (var page in ready)
                    page.Value.View?.Place(page.Key - anchor);
            }
            coverageDirty = true;
            scheduled = false;
            worldHorizon.Reposition(frame);
            caveHorizon.Reposition(frame);
            inlandWater?.Place(frame);
            ocean.Reposition(frame);
            SetGroundOrigin();
        }

        private void SetGroundOrigin() => Shader.SetGlobalVector(GroundOrigin,
            new Vector4(frame.Origin.x / 8, frame.Origin.z / 8, frame.Origin.y / 8, 1f));

        public void PrepareEntry(Vector3 point)
        {
            Vector3Int origin = LocalChunk(point);
            center = origin;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector3Int chunk = origin + new Vector3Int(x, y, z);
                if (InsideWorld(chunk) && !ready.ContainsKey(chunk))
                    Build(chunk);
            }
            scheduled = false;
            UpdateHorizon(point, entry: true);
        }

        public void Stream(Vector3 position, Vector3 intendedVelocity)
        {
            probe?.BeginStep();
            long start = Stopwatch.GetTimestamp();
            float range = largeWorld ? 64f : 44f;
            Vector3 prediction = Vector3.ClampMagnitude(intendedVelocity * 0.68f, range);
            float verticalLead = Mathf.Abs(prediction.y);
            float verticalRange = largeWorld ? 28f : 14f;
            if (verticalLead > verticalRange)
                prediction *= verticalRange / verticalLead;
            Vector3 endpoint = ClampToWorld(position + prediction);
            var path = new ContinentTravelPath(position, endpoint - position, frame.PageOrigin);
            Vector3Int nextCenter = LocalChunk(position);
            Vector3Int nextLead = LocalChunk(endpoint);
            if (largeWorld && field.IsClearSky(frame.ToWorld(position), frame.ToWorld(endpoint), 56))
            {
                if (ready.Count != 0)
                {
                    foreach (ResidentPage page in ready.Values)
                        Recycle(page.View, probe);
                    ready.Clear();
                    coverageDirty = true;
                }
                if (requests.Count != 0)
                {
                    requests.Clear();
                    probe?.SetRequests(requests);
                }
                scheduled = false;
                UpdateHorizon(position, started: start);
                LastStreamMs = Milliseconds(start);
                probe?.ObserveOpenPath(path);
                return;
            }
            if (!scheduled || nextCenter != center || nextLead != lead)
            {
                center = nextCenter;
                lead = nextLead;
                long scheduleStart = ContinentStreamProbe.Start(probe);
                Schedule();
                probe?.End(Stage.Schedule, scheduleStart);
                probe?.SetRequests(requests);
            }
            ServiceRequests(path, start);
            long horizonStart = ContinentStreamProbe.Start(probe);
            UpdateHorizon(position, started: start);
            probe?.End(Stage.Horizon, horizonStart);
            LastStreamMs = Milliseconds(start);
            probe?.ObservePath(path, ready);
        }

        private void ServiceRequests(ContinentTravelPath path, long start)
        {
            int pages = 0;
            int surfaces = 0;
            // A small change within a page can move the swept controller into an adjacent page.
            // Recheck the path without rebuilding the full queue or retiring unrelated views.
            for (int segment = 0; segment < path.SegmentCount; segment++)
                foreach (Vector3Int chunk in path.SegmentPages(segment).allPositionsWithin)
                    if (BuildWithinBudget(chunk, start, ref pages, ref surfaces))
                        return;

            while (next < queue.Count)
                if (BuildWithinBudget(queue[next++].Key, start, ref pages, ref surfaces))
                    return;
        }

        private bool BuildWithinBudget(Vector3Int chunk, long start, ref int pages, ref int surfaces)
        {
            if (ready.ContainsKey(chunk) || !InsideWorld(chunk))
                return false;

            if (Build(chunk, probe))
                surfaces++;
            pages++;
            return pages >= PagesPerFrame || surfaces >= SurfacesPerFrame || Milliseconds(start) >= BuildBudgetMs;
        }

        public Vector3 LimitDisplacement(Vector3 feet, Vector3 displacement)
        {
            Vector3 target = ClampToWorld(feet + displacement);
            if (HasCoverage(feet, target))
                return target - feet;

            float safe = 0f;
            float missing = 1f;
            for (int i = 0; i < 7; i++)
            {
                float fraction = (safe + missing) * 0.5f;
                if (HasCoverage(feet, Vector3.Lerp(feet, target, fraction)))
                    safe = fraction;
                else
                    missing = fraction;
            }
            return (target - feet) * safe;
        }

        public bool HasCoverage(Vector3 fromFeet, Vector3 toFeet)
        {
            if (largeWorld && field.IsClearSky(frame.ToWorld(fromFeet), frame.ToWorld(toFeet), 3))
                return true;
            foreach (Vector3Int chunk in ContinentTravelPath.SweptPages(fromFeet, toFeet, frame.PageOrigin).allPositionsWithin)
                if (!ready.ContainsKey(chunk) && (!largeWorld || InsideWorld(chunk)))
                    return false;
            return true;
        }

        private Vector3Int LocalChunk(Vector3 point) => ContinentField.ChunkAt(point) + frame.PageOrigin;

        public bool Pick(Ray aim, Vector3 eye, out Vector3Int dig, out Vector3Int fill) =>
            Pick(aim, eye, out dig, out fill, out _, out _);

        internal bool Pick(Ray aim, Vector3 eye, out Vector3Int dig, out Vector3Int fill, out WorldPosition sample, out bool placed)
        {
            if (!PickSurface(aim, eye, out RaycastHit hit))
            {
                dig = fill = default;
                sample = default;
                placed = false;
                return false;
            }
            GetEditCells(aim, hit, out dig, out fill, out sample, out placed);
            return field.Editable(dig) || field.Editable(fill);
        }

        internal bool PickSurface(Ray aim, Vector3 eye, out RaycastHit hit)
        {
            if (!Physics.Raycast(aim, out hit, Reach + Vector3.Distance(aim.origin, eye),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || !hit.collider.transform.IsChildOf(transform))
                return false;
            Vector3 line = hit.point - eye;
            return line.sqrMagnitude <= Reach * Reach
                && !Physics.Raycast(eye, line, Mathf.Max(0f, line.magnitude - 0.06f),
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        internal void GetEditCells(Ray aim, RaycastHit hit, out Vector3Int dig, out Vector3Int fill,
            out WorldPosition sample, out bool placed)
        {
            // Keep the ray on the material side without skipping a thin layer at a grid plane.
            sample = frame.ToWorld(hit.point + aim.direction * 0.002f);
            dig = field.DigCellAt(sample, out placed);
            fill = frame.ToWorld(hit.point + hit.normal * 0.4f).Cell;
        }

        internal string Prospect(WorldPosition point, bool placed)
        {
            if (placed)
                return "Placed earth";
            float ground = field.HeightAt(point.X, point.Z);
            RockLayer layer = WorldGeology.LayerAt((float)point.Y, ground);
            ulong seed = unchecked((ulong)worldSeed);
            Mineral exposed = WorldResources.At(point, layer, seed);
            if (exposed != Mineral.None)
                return $"{layer}  |  Exposed {exposed.ToString().ToLowerInvariant()}";

            Vector3Int region = WorldResources.Region(point);
            Mineral nearby = Mineral.None;
            double closest = WorldResources.SearchRange * WorldResources.SearchRange;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!WorldResources.TryVein(region + new Vector3Int(x, y, z), seed, out MineralVein vein))
                    continue;
                WorldPosition center = vein.Center;
                double dx = center.X - point.X;
                double dy = center.Y - point.Y;
                double dz = center.Z - point.Z;
                double distance = dx * dx + dy * dy + dz * dz;
                if (distance >= closest)
                    continue;
                Mineral mineral = vein.In(WorldGeology.LayerAt((float)center.Y, field.HeightAt(center.X, center.Z)));
                if (mineral == Mineral.None || !field.IsSolidAt(center))
                    continue;
                closest = distance;
                nearby = mineral;
            }
            if (nearby == Mineral.None)
                return $"{layer}  |  No nearby mineral trace";
            string strength = closest < 35 * 35 ? "Strong" : closest < 65 * 65 ? "Moderate" : "Weak";
            return $"{layer}  |  {strength} {nearby.ToString().ToLowerInvariant()} trace";
        }

        public bool TryEdit(Vector3Int cell, bool fill, CharacterController occupant = null)
        {
            if (terrainSave?.CanEdit == false)
                return false;
            if (fill && occupant != null
                && occupant.bounds.Intersects(new Bounds((Vector3)(cell - frame.Origin) + Vector3.one * 0.5f, Vector3.one * 2f)))
                return false;
            if (!field.Edit(cell, fill, out BoundsInt samples))
                return false;
            PublishEditedBounds(samples, fill);
            return true;
        }

        public bool TryEditSphere(WorldPosition centerPoint, bool fill, CharacterController occupant = null)
        {
            if (terrainSave?.CanEdit == false)
                return false;
            float clearance = ContinentField.BrushRadius + ContinentField.Spacing * 0.5f;
            if (fill && occupant != null && occupant.bounds.SqrDistance(frame.ToLocal(centerPoint)) < clearance * clearance)
                return false;
            if (!field.EditSphere(centerPoint, fill, out BoundsInt samples))
                return false;
            PublishEditedBounds(samples, fill);
            return true;
        }

        public bool TryGrade(WorldPosition start, WorldPosition end, CharacterController occupant = null)
        {
            if (terrainSave?.CanEdit == false)
                return false;
            if (occupant != null)
            {
                Vector3 first = frame.ToLocal(start), last = frame.ToLocal(end);
                Bounds body = occupant.bounds;
                var along = new Vector2(last.x - first.x, last.z - first.z);
                var delta = new Vector2(body.center.x - first.x, body.center.z - first.z);
                float t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(delta, along) / along.sqrMagnitude) : 0f;
                float height = Mathf.Lerp(first.y, last.y, t);
                float width = ContinentField.GradeHalfWidth + ContinentField.GradeShoulder + occupant.radius;
                if ((delta - along * t).sqrMagnitude <= width * width && height > body.min.y + 0.02f
                    && height - ContinentField.GradeMaxCutFill - ContinentField.Spacing <= body.max.y)
                    return false;
            }
            if (!field.EditGrade(start, end, out _))
                return false;
            changedChunks.Clear();
            foreach (Vector3Int chunk in field.GradeChangedChunks)
                PrepareEditedChunk(chunk, null);
            PublishEditedChunks();
            return true;
        }

        private void PublishEditedBounds(BoundsInt samples, bool fill)
        {
            changedChunks.Clear();
            foreach (Vector3Int chunk in ContinentField.AffectedChunks(samples).allPositionsWithin)
                PrepareEditedChunk(chunk, fill);
            PublishEditedChunks();
        }

        private void PrepareEditedChunk(Vector3Int chunk, bool? fill)
        {
            if (!ready.TryGetValue(chunk, out ResidentPage page))
                return;
            if (page.View == null && ((fill == true && page.Content == PageContent.Solid)
                || (fill == false && page.Content == PageContent.Air)))
                return;
            ready[chunk] = Prepare(chunk, page.View, editing: true);
            changedChunks.Add(chunk);
        }

        private void PublishEditedChunks()
        {
            foreach (Vector3Int chunk in changedChunks)
                Publish(chunk);
            if (largeWorld)
                UpdateCoverage(center);
            else
                FlushColumns();
        }

        private void Schedule()
        {
            scheduled = true;
            retired.Clear();
            foreach (Vector3Int chunk in ready.Keys)
            {
                if (!InRange(chunk, center, NearRadius + 2, 3) && !InRange(chunk, lead, LeadRadius + 2, 2))
                    retired.Add(chunk);
            }
            foreach (Vector3Int chunk in retired)
            {
                Recycle(ready[chunk].View, probe);
                ready.Remove(chunk);
                MarkColumn(chunk);
            }

            requests.Clear();
            RequestBox(center, NearRadius, 2, 0f);
            RequestBox(lead, LeadRadius, 1, 2f);
            queue.Clear();
            queue.AddRange(requests);
            queue.Sort(ComparePriority);
            next = 0;
        }

        private void RequestBox(Vector3Int focus, int horizontal, int vertical, float penalty)
        {
            for (int z = -horizontal; z <= horizontal; z++)
            for (int y = -vertical; y <= vertical; y++)
            for (int x = -horizontal; x <= horizontal; x++)
            {
                Vector3Int chunk = focus + new Vector3Int(x, y, z);
                Request(chunk, x * x + z * z + y * y * 3f + penalty);
            }
        }

        private void Request(Vector3Int chunk, float priority)
        {
            if (InsideWorld(chunk) && !ready.ContainsKey(chunk)
                && (!requests.TryGetValue(chunk, out float previous) || priority < previous))
                requests[chunk] = priority;
        }

        private static int ComparePriority(KeyValuePair<Vector3Int, float> a, KeyValuePair<Vector3Int, float> b) =>
            a.Value.CompareTo(b.Value);

        private bool Build(Vector3Int chunk, ContinentStreamProbe metrics = null)
        {
            ResidentPage view = Prepare(chunk, null, metrics);
            long publishStart = ContinentStreamProbe.Start(metrics);
            ready.Add(chunk, view);
            requests.Remove(chunk);
            Publish(chunk);
            metrics?.End(Stage.Publish, publishStart);
            metrics?.BuildCompleted(chunk);
            BuiltPages++;
            bool hasSurface = ready[chunk].View != null;
            if (metrics != null)
            {
                if (hasSurface)
                    metrics.SurfacePages++;
                else
                    metrics.PagesWithoutSurface++;
            }
            return hasSurface;
        }

        private ResidentPage Prepare(Vector3Int chunk, ChunkView view, ContinentStreamProbe metrics = null, bool editing = false)
        {
            long sampleStart = ContinentStreamProbe.Start(metrics);
            PageContent content = editing ? field.CopyEditedSurfaceSamples(chunk, mesher.SampleBuffer)
                : field.CopySurfaceSamples(chunk, mesher.SampleBuffer);
            bool hasSurface = content == PageContent.Mixed;
            metrics?.End(Stage.Sample, sampleStart);
            if (hasSurface)
            {
                long extractStart = ContinentStreamProbe.Start(metrics);
                const int size = ContinentField.ChunkSize;
                int x = chunk.x * size, z = chunk.z * size;
                paletteTint = field.PrepareTint(x, z);
                if (largeWorld)
                {
                    paletteHeights = new Vector4(field.HeightNode(x, z), field.HeightNode(x + size, z),
                        field.HeightNode(x, z + size), field.HeightNode(x + size, z + size));
                    var region = new Vector3Int(chunk.x >> 4, chunk.y >> 4, chunk.z >> 4);
                    paletteVein = WorldResources.TryVein(region, unchecked((ulong)worldSeed), out MineralVein vein)
                        && vein.IntersectsPage(chunk) ? vein : null;
                    paletteEdited = paletteVein.HasValue && field.HasEdits(chunk);
                }
                mesher.Extract(chunk, palette);
                metrics?.End(Stage.Extract, extractStart);
                hasSurface = mesher.TriangleCount != 0;
                if (!hasSurface)
                    content = mesher.SampleBuffer[mesher.SampleBuffer.Length / 2] < 0f ? PageContent.Solid : PageContent.Air;
            }
            if (hasSurface && view == null)
            {
                long viewStart = ContinentStreamProbe.Start(metrics);
                if (pool.TryPop(out view))
                {
                    if (metrics != null)
                        metrics.ReusedViews++;
                }
                else
                {
                    view = new ChunkView(transform, largeWorld ? worldHorizon.ResidentMaterial : terrainMaterial);
                    if (metrics != null)
                        metrics.CreatedViews++;
                }
                view.Place(chunk - frame.PageOrigin);
                VisibleChunks++;
                metrics?.End(Stage.View, viewStart);
            }
            view?.Prepare(hasSurface, mesher, metrics);
            return new ResidentPage(content, view);
        }

        private void Publish(Vector3Int chunk)
        {
            ResidentPage page = ready[chunk];
            ChunkView view = page.View;
            if (view != null)
            {
                if (view.HasSurface)
                    view.Publish();
                else
                {
                    Recycle(view);
                    ready[chunk] = new ResidentPage(page.Content, null);
                }
            }
            MarkColumn(chunk);
        }

        private void MarkColumn(Vector3Int chunk)
        {
            if (largeWorld)
                coverageDirty = true;
            else
                changedColumns.Add(new Vector2Int(chunk.x, chunk.z));
        }

        private void UpdateHorizon(Vector3 local, bool entry = false, long started = 0)
        {
            if (!largeWorld)
            {
                FlushColumns();
                return;
            }
            UpdateCoverage(LocalChunk(local));
            WorldPosition world = frame.ToWorld(local);
            double budget = entry ? 3 : Math.Max(0, 2.85 - Milliseconds(started));
            caveHorizon.Stream(world, frame, budget);
            budget = entry ? 3 : Math.Max(0, 2.85 - Milliseconds(started));
            WorldPosition eye = world;
            float pixelScale = WorldHorizon.PixelScale(Screen.height, 64f);
            if (horizonView != null)
            {
                pixelScale = WorldHorizon.PixelScale(horizonView.pixelHeight, horizonView.fieldOfView);
                // The camera has not caught up on the frame that rebases or teleports the player.
                if (!entry && (horizonView.transform.position - local).sqrMagnitude < 32f * 32f)
                    eye = frame.ToWorld(horizonView.transform.position);
            }
            worldHorizon.Stream(world, eye, frame, entry, budget, HorizonDistance(world.Y), pixelScale);
        }

        private void UpdateCoverage(Vector3Int focus)
        {
            if (!coverageDirty && (ready.Count == 0 || worldHorizon.CoverageCenter == focus))
                return;
            coverageDirty = false;
            worldHorizon.UpdateCoverage(ready, focus, frame);
        }

        private void FlushColumns()
        {
            foreach (Vector2Int column in changedColumns)
            {
                Vector2Int band = field.SurfaceBand(column);
                bool complete = true;
                for (int y = band.x; y <= band.y; y++)
                    complete &= ready.ContainsKey(new Vector3Int(column.x, y, column.y));
                horizon.SetCovered(column, complete);
                for (int y = band.x; y <= band.y; y++)
                    if (ready.TryGetValue(new Vector3Int(column.x, y, column.y), out ResidentPage page))
                        page.View?.Show(complete);
            }
            changedColumns.Clear();
            horizon.Flush();
        }

        private void Recycle(ChunkView view, ContinentStreamProbe metrics = null)
        {
            if (view == null)
                return;
            VisibleChunks--;
            if (pool.Count < PoolLimit)
            {
                view.Hide();
                pool.Push(view);
            }
            else
            {
                view.Release();
                if (metrics != null)
                    metrics.ReleasedViews++;
            }
        }

        private void OnDestroy()
        {
            if (terrainSave != null)
            {
                Application.wantsToQuit -= terrainSave.TrySaveBeforeQuit;
                terrainSave.Dispose();
            }
            Shader.SetGlobalVector(GroundOrigin, Vector4.zero);
            foreach (ResidentPage page in ready.Values)
                page.View?.Release();
            foreach (ChunkView view in pool)
                view.Release();
            horizon?.Release();
            caveHorizon?.Release();
            inlandWater?.Release();
            worldHorizon?.Release();
            ocean?.Release();
        }

        private bool InsideWorld(Vector3Int chunk) => largeWorld ? WorldDefinition.ContainsPage(chunk) :
            chunk.x >= ContinentField.FirstColumn && chunk.x < -ContinentField.FirstColumn
            && chunk.z >= ContinentField.FirstColumn && chunk.z < -ContinentField.FirstColumn
            && chunk.y >= ContinentField.Bedrock / ContinentField.ChunkSize
            && chunk.y < ContinentField.Ceiling / ContinentField.ChunkSize;

        private static bool InRange(Vector3Int chunk, Vector3Int origin, int horizontal, int vertical) =>
            Mathf.Abs(chunk.x - origin.x) <= horizontal && Mathf.Abs(chunk.z - origin.z) <= horizontal
            && Mathf.Abs(chunk.y - origin.y) <= vertical;

        private Vector3 ClampToWorld(Vector3 point)
        {
            if (largeWorld)
            {
                Vector3Int origin = frame.Origin;
                point.x = Mathf.Clamp(point.x, (-WorldDefinition.Extent - origin.x) + 0.55f, (WorldDefinition.Extent - origin.x) - 0.55f);
                point.y = Mathf.Clamp(point.y, (WorldDefinition.MinY - origin.y) + 0.65f, (WorldDefinition.MaxY - origin.y) - 2.1f);
                point.z = Mathf.Clamp(point.z, (-WorldDefinition.Extent - origin.z) + 0.55f, (WorldDefinition.Extent - origin.z) - 0.55f);
                return point;
            }
            const float edge = ContinentField.Extent - 2f;
            point.x = Mathf.Clamp(point.x, -edge, edge);
            point.y = Mathf.Clamp(point.y, ContinentField.Bedrock + 0.65f, ContinentField.Ceiling - 2.1f);
            point.z = Mathf.Clamp(point.z, -edge, edge);
            return point;
        }

        private static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);

        private Color32 LegacyColor(Vector3Int chunk, Vector3 local, Vector3 normal)
        {
            float height = chunk.y * ContinentField.ChunkSize + local.y;
            return WorldMaterial.ColorAt(height, normal, height, ContinentField.Spacing, paletteTint.ShadeAt(local), false);
        }

        private Color32 GlobalColor(Vector3Int chunk, Vector3 local, Vector3 normal)
        {
            const int size = ContinentField.ChunkSize;
            float ground = WorldSurface.Interpolate(paletteHeights.x, paletteHeights.y, paletteHeights.z, paletteHeights.w,
                local.x / size, local.z / size);
            float height = chunk.y * size + local.y;
            Color32 color = WorldMaterial.ColorAt(height, normal, ground, ContinentField.Spacing, paletteTint.ShadeAt(local), true);
            if (!paletteVein.HasValue && Landforms == null)
                return color;
            var point = new WorldPosition(chunk.x * (double)size + local.x, height, chunk.z * (double)size + local.z);
            color = field.TintForRegion(point, normal, ground, color, ContinentField.Spacing);
            if (paletteVein is not MineralVein vein)
                return color;
            if (!vein.Contains(point))
                return color;
            Mineral mineral = vein.In(WorldGeology.LayerAt(height, ground));
            if (mineral == Mineral.None)
                return color;
            if (paletteEdited)
            {
                var inside = new WorldPosition(point.X - normal.x * 0.05, point.Y - normal.y * 0.05,
                    point.Z - normal.z * 0.05);
                field.DigCellAt(inside, out bool placed);
                if (placed)
                    return color;
            }
            return WorldMaterial.OreTint(color, mineral);
        }

        private readonly struct ResidentPage
        {
            internal readonly PageContent Content;
            internal readonly ChunkView View;

            internal ResidentPage(PageContent content, ChunkView view)
            {
                Content = content;
                View = view;
            }
        }

        private sealed class ChunkView
        {
            private readonly GameObject root;
            private readonly MeshFilter filter;
            private readonly MeshRenderer renderer;
            private readonly MeshCollider collider;
            private Mesh displayed;
            private Mesh pending;

            internal bool HasSurface { get; private set; }

            internal ChunkView(Transform parent, Material material)
            {
                root = new GameObject("Resident terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                root.transform.SetParent(parent, false);
                filter = root.GetComponent<MeshFilter>();
                renderer = root.GetComponent<MeshRenderer>();
                collider = root.GetComponent<MeshCollider>();
                renderer.sharedMaterial = material;
                displayed = CreateMesh();
                pending = CreateMesh();
            }

            internal void Place(Vector3Int chunk) => root.transform.localPosition = (Vector3)chunk * ContinentField.ChunkSize;

            internal void Prepare(bool hasSurface, MarchingCubes mesher, ContinentStreamProbe probe)
            {
                HasSurface = hasSurface;
                if (hasSurface)
                {
                    long start = ContinentStreamProbe.Start(probe);
                    mesher.Upload(pending);
                    probe?.End(Stage.Upload, start);
                    start = ContinentStreamProbe.Start(probe);
                    Physics.BakeMesh(pending.GetEntityId(), convex: false, collider.cookingOptions);
                    probe?.End(Stage.Bake, start);
                }
            }

            internal void Publish()
            {
                collider.sharedMesh = pending;
                filter.sharedMesh = pending;
                (displayed, pending) = (pending, displayed);
                renderer.enabled = true;
                root.SetActive(true);
            }

            internal void Show(bool visible) => renderer.enabled = visible;

            internal void Hide()
            {
                root.SetActive(false);
                collider.sharedMesh = null;
                filter.sharedMesh = null;
            }

            internal void Release()
            {
                Hide();
                Object.Destroy(root);
                Object.Destroy(displayed);
                Object.Destroy(pending);
            }

            private static Mesh CreateMesh()
            {
                var mesh = new Mesh { name = "Continent surface", indexFormat = IndexFormat.UInt16 };
                mesh.MarkDynamic();
                return mesh;
            }
        }
    }
}
