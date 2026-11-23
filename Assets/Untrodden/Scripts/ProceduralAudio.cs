using UnityEngine;

namespace Untrodden
{
    /// <summary>오디오 애셋 없이 쓸 임시 사운드(바람, 모닥불, 부딪힘)를 코드로 만든다.</summary>
    public static class ProceduralAudio
    {
        const int Rate = 22050;

        public static AudioClip Wind(float seconds = 6f)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var rng = new System.Random(7);
            float brown = 0f, lfo = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.998f;
                lfo = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * i / n * 3f);
                data[i] = brown * 2.5f * lfo;
            }
            Crossfade(data, Rate / 2);
            return Make("Wind", data);
        }

        public static AudioClip Crackle(float seconds = 4f)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var rng = new System.Random(11);
            float brown = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.01f, -1f, 1f) * 0.999f;
                data[i] = brown * 0.8f;
                if (rng.NextDouble() < 0.0009)
                {
                    int len = rng.Next(80, 400);
                    float amp = 0.3f + (float)rng.NextDouble() * 0.5f;
                    for (int k = 0; k < len && i + k < n; k++)
                        data[i + k] += ((float)rng.NextDouble() * 2f - 1f) * amp * (1f - k / (float)len);
                }
            }
            Crossfade(data, Rate / 4);
            return Make("Crackle", data);
        }

        public static AudioClip Knock()
        {
            int n = Rate / 8;
            var data = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Exp(-i / (Rate * 0.015f));
                float tone = Mathf.Sin(2f * Mathf.PI * 320f * i / Rate);
                data[i] = (tone * 0.6f + ((float)rng.NextDouble() * 2f - 1f) * 0.4f) * env;
            }
            return Make("Knock", data);
        }

        public static AudioClip Beep()
        {
            int n = Rate / 3;
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Sin(Mathf.PI * i / n);
                data[i] = Mathf.Sin(2f * Mathf.PI * 660f * i / Rate) * env * 0.4f;
            }
            return Make("Beep", data);
        }

        static void Crossfade(float[] data, int fade)
        {
            int n = data.Length;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] = data[i] * t + data[n - fade + i] * (1f - t);
            }
            System.Array.Resize(ref data, n);
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
