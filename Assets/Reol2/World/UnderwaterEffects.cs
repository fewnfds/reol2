using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Reol2.World
{
    internal sealed class UnderwaterEffects
    {
        private readonly GameObject root;
        private readonly UniversalAdditionalCameraData cameraData;
        private readonly bool previousPostProcessing;
        private readonly VolumeProfile profile;
        private readonly AudioLowPassFilter lowPass;
        private readonly AudioSource ambience;
        private readonly AudioClip loop;

        internal bool Active => root.activeSelf;

        internal UnderwaterEffects(Camera view)
        {
            cameraData = view.GetUniversalAdditionalCameraData();
            previousPostProcessing = cameraData.renderPostProcessing;
            root = new GameObject("Underwater effects") { layer = view.gameObject.layer };
            root.SetActive(false);
            root.transform.SetParent(view.transform, false);

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<ColorAdjustments>().colorFilter.Override(new Color(0.87f, 0.97f, 1f));
            var volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            if (!view.TryGetComponent<AudioListener>(out _))
                view.gameObject.AddComponent<AudioListener>();
            lowPass = view.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 1200f;
            lowPass.enabled = false;
            loop = CreateAmbience();
            ambience = root.AddComponent<AudioSource>();
            ambience.clip = loop;
            ambience.loop = true;
            ambience.playOnAwake = false;
            ambience.volume = 0.15f;
        }

        internal void Apply(bool underwater)
        {
            if (Active == underwater)
                return;
            cameraData.renderPostProcessing = previousPostProcessing || underwater;
            lowPass.enabled = underwater;
            root.SetActive(underwater);
            if (underwater)
                ambience.Play();
        }

        internal void Release()
        {
            if (cameraData != null)
                cameraData.renderPostProcessing = previousPostProcessing;
            if (lowPass != null)
                Object.Destroy(lowPass);
            if (root != null)
                Object.Destroy(root);
            Object.Destroy(profile);
            Object.Destroy(loop);
        }

        private static AudioClip CreateAmbience()
        {
            const int rate = 24000;
            const int fadeSamples = rate / 30;
            var samples = new float[rate * 4];
            var random = new System.Random(61);
            float low = 0f, flow = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                low += (noise - low) * 0.008f;
                flow += (noise - flow) * 0.041f;
                float time = i / (float)rate;
                float wash = 0.12f * Mathf.Sin(time * (2f * Mathf.PI * 39f))
                    + 0.06f * Mathf.Sin(time * (2f * Mathf.PI * 56.5f));
                float fade = Mathf.Clamp01(Mathf.Min(i, samples.Length - 1 - i) / (float)fadeSamples);
                samples[i] = (low * 1.6f + flow * 0.7f + wash) * fade;
            }
            AudioClip clip = AudioClip.Create("Underwater ambience", samples.Length, 1, rate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
