using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 絵のファイルを使わず、月・星・アイコンをコードで描く。
    /// 月は添付の2枚の画像と同じ色: 満月 #CAB252(縁へ向かって少し透ける)、新月 #05071E に縁取り #CAB252。
    /// </summary>
    public static class MoonArt
    {
        /// <summary>月の満ち欠けの段階数。0 = 新月、28 = 満月、56 = 新月(0 と同じ見た目)。</summary>
        public const int Frames = 57;
        public const int FullFrame = 28;

        const int Tex = 160;          // テクスチャ 160px(円の外に光のにじみ用の余白)
        const float Ppu = 128f;       // 円の直径 128px = ちょうど 1 ユニット
        const float RPx = 64f;        // 円の半径(px)

        static readonly Color Gold = new Color(202f / 255f, 178f / 255f, 82f / 255f);
        static readonly Color Navy = new Color(5f / 255f, 7f / 255f, 30f / 255f);

        static readonly Color Pale = new Color(246f / 255f, 238f / 255f, 186f / 255f);

        static readonly Sprite[] moonCache = new Sprite[Frames];
        static readonly Sprite[] paleCache = new Sprite[Frames];
        static Sprite sparkleSprite, dotSprite, undoSprite, resetSprite, homeSprite, gearSprite, arrowLeftSprite, arrowRightSprite, hintSprite;

        // 「再生のたびに静的変数を作り直さない」設定(Enter Play Mode Options)でも、前回の絵が残って
        // 矢印や歯車が取り違えられないよう、再生を始めるたびに全部捨てて作り直す。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCaches()
        {
            System.Array.Clear(moonCache, 0, moonCache.Length);
            System.Array.Clear(paleCache, 0, paleCache.Length);
            sparkleSprite = dotSprite = undoSprite = resetSprite = homeSprite = gearSprite = arrowLeftSprite = arrowRightSprite = hintSprite = null;
        }

        // ------------------------------------------------------------------
        // 月
        // ------------------------------------------------------------------

        public static Sprite Moon(int frame)
        {
            if (StellarArt.MoonsReady) return StellarArt.MoonFrame(frame);   // 友達の絵の月
            frame = Mathf.Clamp(frame, 0, Frames - 1);
            if (moonCache[frame] == null) moonCache[frame] = BuildMoon(frame, Gold);
            return moonCache[frame];
        }

        /// <summary>ステージ選択で「新しいステージ」を示す、薄い黄色の月。</summary>
        public static Sprite MoonPale(int frame)
        {
            if (StellarArt.MoonsReady) return StellarArt.New();
            frame = Mathf.Clamp(frame, 0, Frames - 1);
            if (paleCache[frame] == null) paleCache[frame] = BuildMoon(frame, Pale);
            return paleCache[frame];
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static Sprite BuildMoon(int i, Color gold)
        {
            float c = Mathf.Cos(2f * Mathf.PI * i / (Frames - 1)); // 1 = 新月 ... -1 = 満月
            float f = (1f - c) * 0.5f;                              // 明るい部分の割合 0..1
            bool waxing = i <= (Frames - 1) / 2;                    // 満ちていく間は右側が明るい
            float gAmp = 0.06f + 0.20f * f;                         // 光のにじみ: 満月は強く、新月はごくわずか
            float half = Tex * 0.5f;
            float center = (Tex - 1) * 0.5f;

            var pixels = new Color32[Tex * Tex];
            for (int py = 0; py < Tex; py++)
            {
                for (int px = 0; px < Tex; px++)
                {
                    float x = px - center, y = py - center;
                    float d = Mathf.Sqrt(x * x + y * y);
                    float xn = x / RPx, yn = y / RPx;
                    float w = Mathf.Sqrt(Mathf.Clamp01(1f - yn * yn));

                    // 明暗の境目(欠けた部分の曲線)
                    float s = waxing ? (xn - c * w) : (-c * w - xn);
                    float L = Mathf.Clamp01(s * RPx / 1.5f + 0.5f);

                    float r = d / RPx;
                    float aLit = 0.96f - 0.16f * SmoothStep(0.5f, 1f, r);
                    float baseA = (1f - L) + L * aLit;
                    float pr = Navy.r * (1f - L) + gold.r * L * aLit;
                    float pg = Navy.g * (1f - L) + gold.g * L * aLit;
                    float pb = Navy.b * (1f - L) + gold.b * L * aLit;

                    // 淡い黄色の縁取り
                    float ringA = 0.82f * Mathf.Clamp01(d - RPx * 0.95f + 0.5f);
                    float opr = gold.r * ringA + pr * (1f - ringA);
                    float opg = gold.g * ringA + pg * (1f - ringA);
                    float opb = gold.b * ringA + pb * (1f - ringA);
                    float outA = ringA + baseA * (1f - ringA);

                    float cov = Mathf.Clamp01(RPx - d + 0.5f);
                    float dpr = opr * cov, dpg = opg * cov, dpb = opb * cov, da = outA * cov;

                    // 円の外のやわらかい光
                    float t = Mathf.Clamp01((d - RPx) / (half - RPx));
                    float ga = d > RPx - 1f ? gAmp * (1f - t) * (1f - t) : 0f;

                    float rpr = dpr + gold.r * ga * (1f - da);
                    float rpg = dpg + gold.g * ga * (1f - da);
                    float rpb = dpb + gold.b * ga * (1f - da);
                    float ra = da + ga * (1f - da);

                    pixels[py * Tex + px] = ToStraight(rpr, rpg, rpb, ra);
                }
            }
            return MakeSprite(pixels, Tex, Ppu);
        }

        // 計算は「アルファを掛けた色」で行っているので、Unity 用に元の色とアルファに戻す。
        static Color32 ToStraight(float pr, float pg, float pb, float a)
        {
            if (a < 0.0001f) return new Color32(0, 0, 0, 0);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Clamp01(pr / a) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(pg / a) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(pb / a) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }

        static Sprite MakeSprite(Color32[] pixels, int size, float ppu, string spriteName = null)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(pixels);
            tex.Apply(false, spriteName == null);   // 名前付き(アイコン)は、中身を確認できるよう読み出し可能のまま残す
            if (spriteName != null) tex.name = spriteName;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            if (spriteName != null) sp.name = spriteName;
            return sp;
        }

        /// <summary>アイコンの絵の中身の指紋(確認用)。3 つの絵が本当に別物かを Console で確かめるのに使う。</summary>
        public static string IconFingerprint(Sprite sp)
        {
            if (sp == null || sp.texture == null || !sp.texture.isReadable) return "?";
            Color32[] px = sp.texture.GetPixels32();
            long sum = 0, sx = 0;
            for (int i = 0; i < px.Length; i++)
            {
                int a = px[i].a;
                sum += a;
                sx += (long)a * (i % sp.texture.width);
            }
            float cx = sum > 0 ? (float)sx / sum / sp.texture.width : 0f;   // 0.5 が中央。0.5 より小さければ左に寄っている
            return sp.name + " 濃さの合計=" + sum + " 重心x=" + cx.ToString("F3");
        }

        // ------------------------------------------------------------------
        // 星・光の点
        // ------------------------------------------------------------------

        /// <summary>四方に光が伸びるきらめき(64px = 1 ユニット)。</summary>
        public static Sprite Sparkle()
        {
            if (sparkleSprite != null) return sparkleSprite;
            const int size = 64;
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px - center) / (size * 0.5f);
                    float y = (py - center) / (size * 0.5f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - (Mathf.Sqrt(Mathf.Abs(x)) + Mathf.Sqrt(Mathf.Abs(y)))), 1.5f);
                    float core = Mathf.Exp(-(x * x + y * y) * 60f);
                    float v = Mathf.Clamp01(a + core);
                    pixels[py * size + px] = new Color32(255, 242, 204, (byte)Mathf.RoundToInt(v * 255f));
                }
            }
            sparkleSprite = MakeSprite(pixels, size, size);
            return sparkleSprite;
        }

        /// <summary>やわらかい光の点(32px = 1 ユニット)。</summary>
        public static Sprite Dot()
        {
            if (dotSprite != null) return dotSprite;
            const int size = 32;
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px - center) / (size * 0.5f);
                    float y = (py - center) / (size * 0.5f);
                    float r = Mathf.Sqrt(x * x + y * y);
                    float v = Mathf.Clamp01(1f - r);
                    v *= v;
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(v * 255f));
                }
            }
            dotSprite = MakeSprite(pixels, size, size);
            return dotSprite;
        }

        // ------------------------------------------------------------------
        // アイコン(白い線。色は SpriteRenderer で付ける)。128px = 1 ユニット。
        // ------------------------------------------------------------------

        const int IconSize = 128;
        const float StrokeHalf = 0.075f;

        public static Sprite UndoIcon()
        {
            if (undoSprite != null) return undoSprite;
            undoSprite = BuildIcon("Undo", (x, y) =>
            {
                float d = SegDist(x, y, 0.18f, 0.40f, -0.42f, 0.40f);
                d = Mathf.Min(d, Chevron(x, y, -0.46f, 0.40f, -1f, 0f));
                d = Mathf.Min(d, ArcDist(x, y, 0.18f, 0f, 0.40f, -90f, 90f));
                d = Mathf.Min(d, SegDist(x, y, 0.18f, -0.40f, -0.30f, -0.40f));
                return d;
            });
            return undoSprite;
        }

        public static Sprite ResetIcon()
        {
            if (resetSprite != null) return resetSprite;
            const float R = 0.42f;
            float tipX = R * Mathf.Cos(50f * Mathf.Deg2Rad);
            float tipY = R * Mathf.Sin(50f * Mathf.Deg2Rad);
            float fwdX = Mathf.Sin(50f * Mathf.Deg2Rad);
            float fwdY = -Mathf.Cos(50f * Mathf.Deg2Rad);
            resetSprite = BuildIcon("Reset", (x, y) =>
            {
                float d = ArcDist(x, y, 0f, 0f, R, 50f, 360f);
                d = Mathf.Min(d, Chevron(x, y, tipX, tipY, fwdX, fwdY));
                return d;
            });
            return resetSprite;
        }

        public static Sprite HomeIcon()
        {
            if (homeSprite != null) return homeSprite;
            homeSprite = BuildIcon("Home", (x, y) =>
            {
                float d = 9f;
                for (int ix = -1; ix <= 1; ix += 2)
                {
                    for (int iy = -1; iy <= 1; iy += 2)
                    {
                        float dd = Mathf.Sqrt((x - 0.30f * ix) * (x - 0.30f * ix) + (y - 0.30f * iy) * (y - 0.30f * iy));
                        d = Mathf.Min(d, dd - 0.17f + StrokeHalf);
                    }
                }
                return d;
            });
            return homeSprite;
        }

        /// <summary>ヒント(電球)。</summary>
        public static Sprite HintIcon()
        {
            if (hintSprite != null) return hintSprite;
            hintSprite = BuildIcon("Hint", (x, y) =>
            {
                const float cy = 0.16f, R = 0.30f;
                float d = ArcDist(x, y, 0f, cy, R, -50f, 230f);                         // 電球のまるい部分(下がすぼまる)
                float ex = R * Mathf.Cos(-50f * Mathf.Deg2Rad), ey = cy + R * Mathf.Sin(-50f * Mathf.Deg2Rad);
                d = Mathf.Min(d, SegDist(x, y, ex, ey, 0.12f, -0.30f));                  // 首(右)
                d = Mathf.Min(d, SegDist(x, y, -ex, ey, -0.12f, -0.30f));                // 首(左)
                d = Mathf.Min(d, SegDist(x, y, -0.12f, -0.30f, 0.12f, -0.30f));          // 口金
                d = Mathf.Min(d, SegDist(x, y, -0.09f, -0.46f, 0.09f, -0.46f));          // 底
                return d;
            });
            return hintSprite;
        }

        /// <summary>ひとつ前のステージへ。</summary>
        public static Sprite ArrowLeftIcon()
        {
            if (arrowLeftSprite != null) return arrowLeftSprite;
            arrowLeftSprite = BuildIcon("ArrowLeft", (x, y) =>
            {
                float d = SegDist(x, y, 0.42f, 0f, -0.42f, 0f);
                return Mathf.Min(d, Chevron(x, y, -0.46f, 0f, -1f, 0f));
            });
            return arrowLeftSprite;
        }

        /// <summary>ひとつ先のステージへ。</summary>
        public static Sprite ArrowRightIcon()
        {
            if (arrowRightSprite != null) return arrowRightSprite;
            arrowRightSprite = BuildIcon("ArrowRight", (x, y) =>
            {
                float d = SegDist(x, y, -0.42f, 0f, 0.42f, 0f);
                return Mathf.Min(d, Chevron(x, y, 0.46f, 0f, 1f, 0f));
            });
            return arrowRightSprite;
        }

        /// <summary>歯車(設定)。</summary>
        public static Sprite GearIcon()
        {
            if (gearSprite != null) return gearSprite;
            gearSprite = BuildIcon("Gear", (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float d = Mathf.Min(Mathf.Abs(r - 0.36f), Mathf.Abs(r - 0.12f));
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    d = Mathf.Min(d, SegDist(x, y, 0.36f * ca, 0.36f * sa, 0.60f * ca, 0.60f * sa));
                }
                return d;
            });
            return gearSprite;
        }

        // dist(x, y) は、図形までの距離(アイコンの座標は -1..1)。線の太さの内側なら不透明にする。
        static Sprite BuildIcon(string name, System.Func<float, float, float> dist)
        {
            var pixels = new Color32[IconSize * IconSize];
            float center = (IconSize - 1) * 0.5f;
            float aa = 1.3f / (IconSize * 0.5f);
            for (int py = 0; py < IconSize; py++)
            {
                for (int px = 0; px < IconSize; px++)
                {
                    float x = (px - center) / (IconSize * 0.5f);
                    float y = -(py - center) / (IconSize * 0.5f);
                    float a = Mathf.Clamp01((StrokeHalf - dist(x, y)) / aa + 0.5f);
                    pixels[py * IconSize + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return MakeSprite(pixels, IconSize, IconSize, name);
        }

        static float SegDist(float x, float y, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
            float qx = ax + t * vx, qy = ay + t * vy;
            return Mathf.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
        }

        // 円弧までの距離。角度は度で、a0 から a1 へ反時計回り。
        static float ArcDist(float x, float y, float cx, float cy, float radius, float a0, float a1)
        {
            float dx = x - cx, dy = y - cy;
            float ang = Mod(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg - a0, 360f);
            if (ang <= a1 - a0)
            {
                return Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius);
            }
            float e0x = cx + radius * Mathf.Cos(a0 * Mathf.Deg2Rad), e0y = cy + radius * Mathf.Sin(a0 * Mathf.Deg2Rad);
            float e1x = cx + radius * Mathf.Cos(a1 * Mathf.Deg2Rad), e1y = cy + radius * Mathf.Sin(a1 * Mathf.Deg2Rad);
            float d0 = Mathf.Sqrt((x - e0x) * (x - e0x) + (y - e0y) * (y - e0y));
            float d1 = Mathf.Sqrt((x - e1x) * (x - e1x) + (y - e1y) * (y - e1y));
            return Mathf.Min(d0, d1);
        }

        static float Mod(float a, float m)
        {
            return a - m * Mathf.Floor(a / m);
        }

        // 矢じりの「く」の字。tip が先端、(fwdX, fwdY) が進む向き。
        static float Chevron(float x, float y, float tipX, float tipY, float fwdX, float fwdY)
        {
            const float length = 0.24f;
            float n = Mathf.Sqrt(fwdX * fwdX + fwdY * fwdY);
            fwdX /= n;
            fwdY /= n;
            float best = 9f;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float a = 40f * sgn * Mathf.Deg2Rad;
                float bx = -(fwdX * Mathf.Cos(a) - fwdY * Mathf.Sin(a)) * length;
                float by = -(fwdX * Mathf.Sin(a) + fwdY * Mathf.Cos(a)) * length;
                best = Mathf.Min(best, SegDist(x, y, tipX, tipY, tipX + bx, tipY + by));
            }
            return best;
        }
    }
}
