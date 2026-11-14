using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 音の素材もコードで合成する。静かな夜に合う、ゆっくりしたパッドと、ガラスのような鈴の音。
    /// 音階は A マイナー・ペンタトニックなので、どの順に鳴っても濁らない。
    /// 自分の音源を使いたいときは Assets/Resources/Stellar/ に BGM / SE_Place / SE_Tick / SE_Sparkle / SE_Swell / SE_Chime0〜7 を置けば差し替わる。
    /// </summary>
    public static class StellarAudio
    {
        const int SampleRate = 16000;
        const float TwoPi = Mathf.PI * 2f;

        /// <summary>鈴の音階(A4 C5 D5 E5 G5 A5 C6 D6)。裏返る順に上っていく。</summary>
        public static readonly float[] Scale =
        {
            440.00f, 523.25f, 587.33f, 659.25f, 783.99f, 880.00f, 1046.50f, 1174.66f
        };

        // ------------------------------------------------------------------
        // BGM: 32秒でループする、ゆっくりしたパッドとまばらな鈴
        // ------------------------------------------------------------------

        public static AudioClip CreateBgm()
        {
            const float chordSeconds = 8f;
            float[][] chords =
            {
                new[] { 110.00f, 164.81f, 261.63f, 392.00f },  // Am9: A2 E3 C4 G4
                new[] {  87.31f, 130.81f, 220.00f, 329.63f },  // Fmaj7: F2 C3 A3 E4
                new[] { 130.81f, 196.00f, 246.94f, 329.63f },  // Cmaj7: C3 G3 B3 E4
                new[] {  98.00f, 146.83f, 246.94f, 293.66f },  // G6/9: G2 D3 B3 D4
            };
            float loopSeconds = chordSeconds * chords.Length;
            int length = (int)(loopSeconds * SampleRate);
            var buf = new float[length];

            // パッド: 各和音を 16 秒かけて浮かび上がらせ、半分ずつ重ねる(つなぎ目で音量が変わらない)
            for (int c = 0; c < chords.Length; c++)
            {
                for (int v = 0; v < chords[c].Length; v++)
                {
                    AddPad(buf, c * chordSeconds, chordSeconds * 2f, chords[c][v], 0.07f);
                }
            }

            // まばらな鈴(毎回同じ並びになるよう、乱数の種を固定)
            var rnd = new System.Random(42);
            const int bellCount = 20;
            for (int k = 0; k < bellCount; k++)
            {
                float slot = loopSeconds / bellCount;
                float t0 = k * slot + (float)rnd.NextDouble() * slot * 0.8f;
                float f = Scale[rnd.Next(Scale.Length)];
                if (rnd.Next(4) == 0) f *= 0.5f;
                AddBell(buf, t0, 4.5f, f, 0.10f, true);
            }

            CircularEcho(buf, 0.42f, 0.38f);
            Normalize(buf, 0.5f);
            return MakeClip("StellarBgm", buf);
        }

        static void AddPad(float[] buf, float startSec, float durSec, float freq, float amp)
        {
            int start = (int)(startSec * SampleRate);
            int count = (int)(durSec * SampleRate);
            for (int n = 0; n < count; n++)
            {
                float t = (float)n / SampleRate;
                float s = Mathf.Sin(Mathf.PI * t / durSec);
                float env = s * s;
                float ph = TwoPi * freq * t;
                // わずかにずらした 2 つの音で、ゆらぎを出す
                float v = 0.5f * (Mathf.Sin(ph) + Mathf.Sin(TwoPi * (freq + 0.6f) * t)) + 0.12f * Mathf.Sin(2f * ph);
                buf[(start + n) % buf.Length] += amp * env * v;
            }
        }

        static void AddBell(float[] buf, float startSec, float durSec, float freq, float amp, bool wrap)
        {
            int start = (int)(startSec * SampleRate);
            int count = (int)(durSec * SampleRate);
            for (int n = 0; n < count; n++)
            {
                int idx = start + n;
                if (wrap) idx %= buf.Length;
                else if (idx >= buf.Length) break;

                float t = (float)n / SampleRate;
                float env = Mathf.Exp(-1.3f * t) * Mathf.Min(1f, t / 0.004f);
                float v = Mathf.Sin(TwoPi * freq * t)
                        + 0.35f * Mathf.Sin(TwoPi * freq * 2.76f * t) * Mathf.Exp(-3f * t);
                buf[idx] += amp * env * v;
            }
        }

        // 末尾から先頭へ回り込む反響(ループの継ぎ目が目立たない)
        static void CircularEcho(float[] buf, float delaySec, float feedback)
        {
            int d = (int)(delaySec * SampleRate);
            int n = buf.Length;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    buf[i] += feedback * buf[(i - d + n) % n];
                }
            }
        }

        // ------------------------------------------------------------------
        // 効果音
        // ------------------------------------------------------------------

        /// <summary>裏返るときの鈴(音階の i 番目)。</summary>
        public static AudioClip CreateChime(int i)
        {
            float f = Scale[Mathf.Clamp(i, 0, Scale.Length - 1)];
            return Create("StellarChime" + i, 1.2f, t =>
            {
                float attack = Mathf.Min(1f, t / 0.003f);
                return attack * 0.5f * (
                    Mathf.Sin(TwoPi * f * t) * Mathf.Exp(-4.2f * t)
                    + 0.25f * Mathf.Sin(TwoPi * f * 2f * t) * Mathf.Exp(-7f * t)
                    + 0.08f * Mathf.Sin(TwoPi * f * 3f * t) * Mathf.Exp(-10f * t));
            });
        }

        /// <summary>新月を置くときの、やわらかい低い音。</summary>
        public static AudioClip CreatePlace()
        {
            var rnd = new System.Random(3);
            return Create("StellarPlace", 0.5f, t =>
            {
                float body = Mathf.Sin(TwoPi * 174.6f * t) * Mathf.Exp(-9f * t) * 0.7f
                           + Mathf.Sin(TwoPi * 349.2f * t) * Mathf.Exp(-14f * t) * 0.3f;
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-120f * t) * 0.08f;
                return body + noise;
            });
        }

        /// <summary>ボタンなどを押したときの、ごく小さな音。</summary>
        public static AudioClip CreateTick()
        {
            return Create("StellarTick", 0.12f, t =>
                Mathf.Sin(TwoPi * 660f * t) * Mathf.Exp(-40f * t) * 0.5f);
        }

        /// <summary>全消しのときの、きらきらと降る高い鈴。</summary>
        public static AudioClip CreateSparkle()
        {
            const float seconds = 4f;
            int length = (int)(seconds * SampleRate);
            var buf = new float[length];
            var rnd = new System.Random(9);
            for (int k = 0; k < 46; k++)
            {
                float t0 = (float)rnd.NextDouble() * 2.8f;
                float f = Scale[rnd.Next(2, Scale.Length)] * 2f;
                AddBell(buf, t0, 1.1f, f, 0.16f, false);
            }
            for (int i = 0; i < length; i++) buf[i] = Mathf.Clamp(buf[i], -1f, 1f);
            Normalize(buf, 0.6f);
            return MakeClip("StellarSparkle", buf);
        }

        /// <summary>全消しのあと、星空と一緒にゆっくり満ちて消えていく和音。</summary>
        public static AudioClip CreateSwell()
        {
            const float seconds = 9f;
            int length = (int)(seconds * SampleRate);
            var buf = new float[length];
            float[] freqs = { 220.00f, 261.63f, 329.63f, 493.88f, 659.25f };   // Am add9 の広がり
            for (int k = 0; k < freqs.Length; k++)
            {
                float amp = 0.12f / (1f + k * 0.35f);
                for (int n = 0; n < length; n++)
                {
                    float t = (float)n / SampleRate;
                    float s = Mathf.Sin(Mathf.PI * t / seconds);
                    float env = s * s;
                    float v = 0.5f * (Mathf.Sin(TwoPi * freqs[k] * t) + Mathf.Sin(TwoPi * (freqs[k] + 0.5f) * t));
                    buf[n] += amp * env * v;
                }
            }
            Normalize(buf, 0.5f);
            return MakeClip("StellarSwell", buf);
        }

        // ------------------------------------------------------------------
        // 道具
        // ------------------------------------------------------------------

        static AudioClip Create(string name, float seconds, System.Func<float, float> sample)
        {
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                data[i] = Mathf.Clamp(sample((float)i / SampleRate), -1f, 1f);
            }
            return MakeClip(name, data);
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] buf, float peak)
        {
            float max = 0.0001f;
            for (int i = 0; i < buf.Length; i++)
            {
                float a = Mathf.Abs(buf[i]);
                if (a > max) max = a;
            }
            float k = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
        }
    }
}
