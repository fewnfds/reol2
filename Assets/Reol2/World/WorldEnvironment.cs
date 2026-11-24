using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEngine.SceneManagement;

namespace Reol2.World
{
    [DisallowMultipleComponent]
    public sealed class WorldEnvironment : MonoBehaviour
    {
        private static readonly int SkyDaylight = Shader.PropertyToID("_ReolSkyDaylight");
        private static readonly int ShelterLight = Shader.PropertyToID("_ReolShelterLight");
        private static readonly int SkyHorizon = Shader.PropertyToID("_ReolSkyHorizon");
        private static readonly int SkyZenith = Shader.PropertyToID("_Zenith");
        private static readonly int CelestialDirection = Shader.PropertyToID("_SunDirection");
        private static readonly int CloudCover = Shader.PropertyToID("_Cloud");
        private static readonly ProfilerMarker AtmosphereMarker = new("Reol2.World.Environment");
        private static readonly Color CaveDark = new(0.09f, 0.13f, 0.15f);

        [SerializeField] private Material skyTemplate;
        [SerializeField] private Material precipitationMaterial;
        [SerializeField, Min(0f)] private float gameSecondsPerRealSecond = 60f;
        private readonly WorldClock clock = new();
        private Material sky;
        private Material previousSky;
        private AmbientMode previousAmbientMode;
        private Color previousAmbient;
        private Color previousFog;
        private float previousFogStart, previousFogEnd;
        private Camera sceneView;
        private CameraClearFlags previousClearFlags;
        private Color previousBackground;
        private float previousFarClip;
        private Light sun;
        private Color previousSunColor;
        private float previousSunIntensity;
        private LightShadows previousSunShadows;
        private Quaternion previousSunRotation;
        private ParticleSystem rain;
        private ParticleSystem snow;
        private UnderwaterEffects waterEffects;
        private double pendingSeconds;
        private float nextSample;
        private float baseSunIntensity;
        private WorldPosition lastSample;
        private bool sampled;
        private bool sheltered;
        private bool ownsEnvironment;
        private long lastTick;
        private Color horizon;
        private float mediumLight = 1f;
        private WeatherSample weather;
        private WeatherSample? previewWeather;
        internal ClimateSample Climate { get; private set; }
        internal WorldClock Clock => clock;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
        internal WeatherSample RenderWeather => weather;
#endif
        internal WeatherSample? PreviewWeather
        {
            get => previewWeather;
            set
            {
                previewWeather = value;
                sampled = false;
            }
        }
        internal int ActivePrecipitation => (rain?.particleCount ?? 0) + (snow?.particleCount ?? 0);
        internal bool UnderwaterEffectsActive => waterEffects?.Active == true;

        private void Update()
        {
            if (clock.Paused)
                return;
            pendingSeconds += Time.deltaTime * gameSecondsPerRealSecond;
            long seconds = (long)pendingSeconds;
            pendingSeconds -= seconds;
            clock.Advance(seconds);
        }

        internal void Apply(Camera view, ContinentTerrain terrain, WorldPosition feet, bool underwater, bool underground)
        {
            using var marker = AtmosphereMarker.Auto();
            if (!ownsEnvironment)
            {
                ownsEnvironment = true;
                sun = RenderSettings.sun;
                previousSky = RenderSettings.skybox;
                previousAmbient = RenderSettings.ambientLight;
                previousAmbientMode = RenderSettings.ambientMode;
                previousFog = RenderSettings.fogColor;
                previousFogStart = RenderSettings.fogStartDistance;
                previousFogEnd = RenderSettings.fogEndDistance;
                sceneView = view;
                previousClearFlags = view.clearFlags;
                previousBackground = view.backgroundColor;
                previousFarClip = view.farClipPlane;
                if (sun != null)
                {
                    previousSunColor = sun.color;
                    previousSunIntensity = sun.intensity;
                    previousSunShadows = sun.shadows;
                    previousSunRotation = sun.transform.rotation;
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                if (skyTemplate != null)
                {
                    sky = new Material(skyTemplate);
                    RenderSettings.skybox = sky;
                }
                if (precipitationMaterial != null)
                {
                    rain = CreatePrecipitation(precipitationMaterial, false);
                    snow = CreatePrecipitation(precipitationMaterial, true);
                }
            }

            if (underwater)
                waterEffects ??= new UnderwaterEffects(view);
            waterEffects?.Apply(underwater);
            WorldPosition at = terrain.ToWorld(view.transform.position);
            bool enclosed = underwater || underground;
            bool moved = !sampled || System.Math.Abs(at.X - lastSample.X) > 256
                || System.Math.Abs(at.Y - lastSample.Y) > 128 || System.Math.Abs(at.Z - lastSample.Z) > 256;
            if (moved || enclosed != sheltered || Time.unscaledTime >= nextSample || clock.Tick - lastTick >= 120)
            {
                sampled = true;
                sheltered = enclosed;
                float shelterTarget = underground ? 0.45f : 1f;
                if (moved)
                    mediumLight = shelterTarget;
                lastSample = at;
                lastTick = clock.Tick;
                nextSample = Time.unscaledTime + 0.15f;
                SampleOutdoor(at, terrain);
                bool underRoof = enclosed || ((weather.Rain > 0.015f || weather.Snow > 0.015f)
                    && Physics.Raycast(view.transform.position, Vector3.up, 40f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                SetPrecipitation(rain, underRoof ? 0f : weather.Rain, Climate.Wind, false);
                SetPrecipitation(snow, underRoof ? 0f : weather.Snow, Climate.Wind, true);
            }

            float lightTarget = underground ? 0.45f : 1f;
            mediumLight = Mathf.MoveTowards(mediumLight, lightTarget, Time.deltaTime * 2f);
            Shader.SetGlobalFloat(ShelterLight, mediumLight);
            if (!enclosed)
            {
                PlacePrecipitation(rain, view.transform);
                PlacePrecipitation(snow, view.transform);
            }
            if (sun != null)
            {
                LightShadows shadows = LightShadows.None;
                if (!underground && baseSunIntensity > 0.18f)
                    shadows = terrain.IsLargeWorld ? LightShadows.Soft : LightShadows.Hard;
                if (sun.shadows != shadows)
                    sun.shadows = shadows;
                // A fixed weather slot and world clock give the flash the same timing on return.
                long flashSecond = clock.Tick % 1973;
                float flash = !underground && weather.Storm > 0.6f && flashSecond < 11
                    ? (1f - flashSecond / 11f) * weather.Storm : 0f;
                sun.intensity = baseSunIntensity * mediumLight + flash * 1.8f;
            }
            view.clearFlags = !underground && sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            Color fog = underground ? CaveDark : horizon;
            view.backgroundColor = fog;
            float range = terrain.HorizonDistance(feet.Y);
            float visible = range * Mathf.Lerp(1f, 0.48f, weather.Haze);
            RenderSettings.fogColor = fog;
            if (terrain.IsLargeWorld)
            {
                RenderSettings.fogStartDistance = underground ? 225f : visible * 0.14f;
                RenderSettings.fogEndDistance = underground ? WorldCaveHorizon.VisibleDistance : visible;
                view.farClipPlane = underground ? WorldCaveHorizon.VisibleDistance + 40f : range * 1.07f;
            }
            else
            {
                RenderSettings.fogStartDistance = underground ? 20f : 360f;
                RenderSettings.fogEndDistance = underground ? 145f : Mathf.Lerp(2300f, 1400f, weather.Haze);
            }
        }

        private void SampleOutdoor(WorldPosition at, ContinentTerrain terrain)
        {
            ulong seed = unchecked((ulong)terrain.Seed);
            Climate = WorldClimate.Sample(at, terrain.SurfaceHeight(at), clock.Tick, seed, terrain.GeneratedClimateAt(at));
            weather = PreviewWeather ?? WorldWeather.Sample(at, clock.Tick, Climate, seed + 797);
            float timeAngle = (clock.DayFraction - 0.25f) * (2f * Mathf.PI);
            var direction = new Vector3(Mathf.Cos(timeAngle), Mathf.Sin(timeAngle), 0.29f).normalized;
            float day = WorldNoise.Ramp((direction.y + 0.14f) / 0.52f);
            float twilight = WorldNoise.Ramp(1f - Mathf.Abs(direction.y - 0.03f) * 6f);
            float overcast = weather.Cloud * 0.63f;
            horizon = Color.Lerp(new Color(0.075f, 0.11f, 0.19f), new Color(0.69f, 0.78f, 0.84f), day);
            horizon = Color.Lerp(horizon, new Color(0.92f, 0.48f, 0.30f), twilight * (1f - overcast) * 0.57f);
            horizon = Color.Lerp(horizon, Color.Lerp(new Color(0.12f, 0.14f, 0.19f),
                new Color(0.56f, 0.61f, 0.67f), day), overcast);
            Color zenith = Color.Lerp(new Color(0.025f, 0.048f, 0.12f), new Color(0.24f, 0.51f, 0.72f), day);
            zenith = Color.Lerp(zenith, horizon, overcast);
            if (sun != null)
            {
                bool daylight = direction.y >= 0f;
                Vector3 active = daylight ? direction : -direction;
                sun.transform.rotation = Quaternion.LookRotation(-active, Vector3.up);
                sun.color = daylight ? Color.Lerp(new Color(1f, 0.59f, 0.36f), new Color(1f, 0.96f, 0.88f), day)
                    : new Color(0.51f, 0.66f, 0.94f);
                baseSunIntensity = WorldNoise.Ramp(Mathf.Abs(direction.y) * 5f) * (1f - overcast)
                    * (daylight ? 1.1f : 0.15f);
            }
            float ambient = Mathf.Lerp(0.20f, 1f, day) * (1f - overcast * 0.23f);
            RenderSettings.ambientLight = new Color(0.57f, 0.64f, 0.69f) * ambient;
            Shader.SetGlobalFloat(SkyDaylight, ambient);
            Shader.SetGlobalColor(SkyHorizon, horizon);
            if (sky == null)
                return;
            sky.SetColor(SkyHorizon, horizon);
            sky.SetColor(SkyZenith, zenith);
            sky.SetVector(CelestialDirection, new Vector4(direction.x, direction.y, direction.z, day));
            sky.SetFloat(CloudCover, weather.Cloud);
        }

        private ParticleSystem CreatePrecipitation(Material material, bool snowing)
        {
            var obj = new GameObject(snowing ? "Nearby snow" : "Nearby rain");
            SceneManager.MoveGameObjectToScene(obj, gameObject.scene);
            var particles = obj.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.maxParticles = snowing ? 350 : 720;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = snowing ? 4.7f : 0.65f;
            main.startSpeed = 0f;
            main.startSize = snowing ? new ParticleSystem.MinMaxCurve(0.04f, 0.097f) : 0.024f;
            main.startColor = snowing ? new Color(0.82f, 0.90f, 0.96f, 0.75f) : new Color(0.58f, 0.71f, 0.81f, 0.54f);
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(18f, 6f, 18f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.renderMode = snowing ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = snowing ? 1f : 6f;
            renderer.velocityScale = snowing ? 0f : 0.065f;
            return particles;
        }

        private static void SetPrecipitation(ParticleSystem particles, float strength, Vector2 wind, bool snowing)
        {
            if (particles == null)
                return;
            if (strength <= 0.015f)
            {
                if (particles.isPlaying)
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }
            var emission = particles.emission;
            emission.rateOverTime = strength * (snowing ? 72f : 850f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = wind.x * 0.26f;
            velocity.z = wind.y * 0.26f;
            velocity.y = snowing ? -1.8f : -12f;
            if (!particles.isPlaying)
                particles.Play();
        }

        private static void PlacePrecipitation(ParticleSystem particles, Transform view)
        {
            if (particles != null && particles.isPlaying)
                particles.transform.SetPositionAndRotation(view.position + Vector3.up * 4f, Quaternion.identity);
        }

        private void OnDestroy()
        {
            if (!ownsEnvironment)
                return;
            waterEffects?.Release();
            if (SceneManager.GetActiveScene() == gameObject.scene)
            {
                if (RenderSettings.skybox == sky)
                    RenderSettings.skybox = previousSky;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.fogColor = previousFog;
                RenderSettings.fogStartDistance = previousFogStart;
                RenderSettings.fogEndDistance = previousFogEnd;
            }
            if (sun != null)
            {
                sun.color = previousSunColor;
                sun.intensity = previousSunIntensity;
                sun.shadows = previousSunShadows;
                sun.transform.rotation = previousSunRotation;
            }
            if (sceneView != null)
            {
                sceneView.clearFlags = previousClearFlags;
                sceneView.backgroundColor = previousBackground;
                sceneView.farClipPlane = previousFarClip;
            }
            Shader.SetGlobalFloat(SkyDaylight, 0f);
            Shader.SetGlobalFloat(ShelterLight, 0f);
            Shader.SetGlobalColor(SkyHorizon, Color.clear);
            if (rain != null)
                Destroy(rain.gameObject);
            if (snow != null)
                Destroy(snow.gameObject);
            Destroy(sky);
        }
    }
}
