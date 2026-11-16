using System.Collections.Generic;
using Untrodden.Weather;
using Untrodden.World;
using UnityEngine;

namespace Untrodden.Audio
{
    public enum SoundId
    {
        Wind, Water, Crackle, Growl, BearHurt, BearAttack, Shot, DryFire, Load,
        Eat, Drink, Splash, Pickup, Wood, Strike, Ignite, Warning, Arrive
    }

    /// <summary>
    /// Every sound is synthesised once from noise and simple waves, so the project needs no audio assets.
    /// Good enough for a prototype; swap in recorded clips later by replacing <see cref="Clip"/>.
    /// </summary>
    public static class Sounds
    {
        const int k_Rate = 22050;
        static readonly Dictionary<SoundId, AudioClip> s_Clips = new Dictionary<SoundId, AudioClip>();

        public static void Play(SoundId id, Vector3 position, float volume)
        {
            var go = new GameObject("Sfx " + id);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = Clip(id);
            src.volume = volume;
            src.spatialBlend = 1f;
            src.minDistance = 1.5f;
            src.maxDistance = 60f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            Object.Destroy(go, src.clip.length + 0.1f);
        }

        public static AudioClip Clip(SoundId id)
        {
            if (s_Clips.TryGetValue(id, out var clip) && clip != null)
                return clip;
            clip = Build(id);
            s_Clips[id] = clip;
            return clip;
        }

        static AudioClip Build(SoundId id)
        {
            var rng = new System.Random((int)id * 7919 + 13);
            float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

            switch (id)
            {
                case SoundId.Wind: return Loop(id, 6f, (t, s) => Brown(ref s.a, Noise(), 0.02f) * (0.7f + 0.3f * Mathf.Sin(t * 0.9f)) * 3f);
                case SoundId.Water: return Loop(id, 3f, (t, s) => (Noise() - s.Low(Noise(), 0.3f)) * 0.25f + Brown(ref s.b, Noise(), 0.05f) * 1.2f);
                case SoundId.Crackle: return Loop(id, 3f, (t, s) => Brown(ref s.a, Noise(), 0.03f) * 0.8f + (rng.NextDouble() < 0.0015 ? Noise() * 0.9f : 0f) + s.Decay(0.985f) * Noise());
                case SoundId.Growl: return OneShot(id, 1.6f, (t, s) =>
                {
                    var env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.6f));
                    var rough = 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 28f);
                    var saw = Mathf.Repeat(t * 68f, 1f) * 2f - 1f;
                    return (saw * 0.35f + s.Low(Noise(), 0.08f) * 1.5f) * rough * env;
                });
                case SoundId.BearHurt: return OneShot(id, 0.9f, (t, s) =>
                {
                    var env = Mathf.Exp(-t * 3f);
                    var saw = Mathf.Repeat(t * (110f - t * 50f), 1f) * 2f - 1f;
                    return (saw * 0.4f + s.Low(Noise(), 0.15f)) * env;
                });
                case SoundId.BearAttack: return OneShot(id, 0.5f, (t, s) => (Noise() * 0.6f + Mathf.Sin(t * 2f * Mathf.PI * 60f)) * Mathf.Exp(-t * 9f));
                case SoundId.Shot: return OneShot(id, 0.9f, (t, s) => (Noise() * Mathf.Exp(-t * 18f) + s.Low(Noise(), 0.05f) * 3f * Mathf.Exp(-t * 4f)) * 0.9f);
                case SoundId.DryFire: return OneShot(id, 0.06f, (t, s) => Noise() * Mathf.Exp(-t * 120f));
                case SoundId.Load: return OneShot(id, 0.25f, (t, s) => Noise() * (Mathf.Exp(-t * 80f) + Mathf.Exp(-Mathf.Max(0f, t - 0.12f) * 80f) * (t > 0.12f ? 1f : 0f)));
                case SoundId.Eat: return OneShot(id, 0.45f, (t, s) => Noise() * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 7f)) * Mathf.Exp(-t * 4f) * 0.7f);
                case SoundId.Drink: return OneShot(id, 0.5f, (t, s) => Mathf.Sin(t * 2f * Mathf.PI * (200f + 300f * Mathf.Repeat(t * 6f, 1f))) * Mathf.Exp(-Mathf.Repeat(t * 6f, 1f) * 6f) * 0.5f);
                case SoundId.Splash: return OneShot(id, 0.5f, (t, s) => Noise() * Mathf.Exp(-t * 7f) * 0.6f);
                case SoundId.Pickup: return OneShot(id, 0.1f, (t, s) => Mathf.Sin(t * 2f * Mathf.PI * 900f) * Mathf.Exp(-t * 40f) * 0.5f);
                case SoundId.Wood: return OneShot(id, 0.2f, (t, s) => (Mathf.Sin(t * 2f * Mathf.PI * 180f) + Noise() * 0.3f) * Mathf.Exp(-t * 25f));
                case SoundId.Strike: return OneShot(id, 0.18f, (t, s) => (Noise() - s.Low(Noise(), 0.5f)) * Mathf.Exp(-t * 15f));
                case SoundId.Ignite: return OneShot(id, 1.2f, (t, s) => s.Low(Noise(), 0.1f + t * 0.2f) * 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f)));
                case SoundId.Warning: return OneShot(id, 3f, (t, s) => Brown(ref s.a, Noise(), 0.03f) * 4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 3f)));
                case SoundId.Arrive: return OneShot(id, 2.5f, (t, s) =>
                    (Mathf.Sin(t * 2f * Mathf.PI * 523f) + Mathf.Sin(t * 2f * Mathf.PI * 659f) * 0.7f + Mathf.Sin(t * 2f * Mathf.PI * 784f) * 0.5f) * Mathf.Exp(-t * 1.5f) * 0.3f);
                default: return OneShot(id, 0.1f, (t, s) => 0f);
            }
        }

        class FilterState
        {
            public float a, b, low, decay = 1f;
            public float Low(float x, float k) => low += (x - low) * k;
            public float Decay(float k) => decay *= k;
        }

        static float Brown(ref float state, float white, float step)
        {
            state = Mathf.Clamp(state + white * step, -1f, 1f);
            return state * 0.5f;
        }

        static AudioClip OneShot(SoundId id, float seconds, System.Func<float, FilterState, float> f) => Make(id, seconds, f, false);
        static AudioClip Loop(SoundId id, float seconds, System.Func<float, FilterState, float> f) => Make(id, seconds, f, true);

        static AudioClip Make(SoundId id, float seconds, System.Func<float, FilterState, float> f, bool loop)
        {
            var n = Mathf.CeilToInt(seconds * k_Rate);
            var data = new float[n];
            var state = new FilterState();
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)k_Rate;
                data[i] = Mathf.Clamp(f(t, state), -1f, 1f);
            }

            if (loop)
            {
                // crossfade the ends so the loop has no click
                var fade = Mathf.Min(n / 4, k_Rate / 4);
                for (var i = 0; i < fade; i++)
                {
                    var w = i / (float)fade;
                    data[i] = data[i] * w + data[n - fade + i] * (1f - w);
                }
                System.Array.Resize(ref data, n - fade);
                n -= fade;
            }

            var clip = AudioClip.Create(id.ToString(), n, 1, k_Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>Wind follows the weather; a looping water source sits on the nearest riverbank.</summary>
    public class AmbientAudio : MonoBehaviour
    {
        AudioSource m_Wind;
        AudioSource m_Water;
        WeatherController m_Weather;
        WorldMap m_World;
        Transform m_Head;
        float m_Timer;

        public void Init(WorldMap world, WeatherController weather, Transform head)
        {
            m_World = world;
            m_Weather = weather;
            m_Head = head;

            m_Wind = gameObject.AddComponent<AudioSource>();
            m_Wind.clip = Sounds.Clip(SoundId.Wind);
            m_Wind.loop = true;
            m_Wind.spatialBlend = 0f;
            m_Wind.volume = 0.2f;
            m_Wind.Play();

            var waterGo = new GameObject("River Sound");
            waterGo.transform.SetParent(transform, false);
            m_Water = waterGo.AddComponent<AudioSource>();
            m_Water.clip = Sounds.Clip(SoundId.Water);
            m_Water.loop = true;
            m_Water.spatialBlend = 1f;
            m_Water.minDistance = 3f;
            m_Water.maxDistance = 45f;
            m_Water.rolloffMode = AudioRolloffMode.Linear;
            m_Water.volume = 0f;
            m_Water.Play();
        }

        void Update()
        {
            if (m_World == null)
                return;

            var storm = m_Weather != null ? m_Weather.Intensity : 0f;
            m_Wind.volume = Mathf.Lerp(0.18f, 0.9f, storm);
            m_Wind.pitch = Mathf.Lerp(0.9f, 1.25f, storm);

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f)
                return;
            m_Timer = 0.3f;

            var p = m_Head.position;
            var r = m_World.NearestRiver(p.x, p.z);
            if (r.valid && r.distance < 45f)
            {
                m_Water.transform.position = new Vector3(r.point.x, r.surface, r.point.y);
                m_Water.volume = Mathf.Lerp(0.25f, 0.8f, Mathf.Clamp01(r.halfWidth / 6f));
            }
            else
            {
                m_Water.volume = 0f;
            }
        }
    }
}
