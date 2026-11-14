using System.Collections.Generic;
using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 友達の描いた絵(Assets/Resources/Stellar/Art/*.png)を読み込む。絵が無いときは、これまでどおりコードで描く絵を使う。
    ///
    /// 絵は 1920x1080 の画面を基準にしている(1 ユニット = 画面の 90 ピクセル)。
    /// 背景は画面いっぱい、文字や UI は「基準の画面のどこにあったか」を Layout に持っていて、同じ場所に置く。
    /// </summary>
    public static class StellarArt
    {
        public const string Folder = "Stellar/Art/";
        public const float PixelsPerUnit = 90f;          // 基準の画面(1920x1080)の 90 ピクセルが 1 ユニット
        public const float DesignWidth = 1920f, DesignHeight = 1080f;
        public const float MoonDiameter = 234f / 512f;   // 月の絵(512px)のうち、月の円の大きさ

        /// <summary>絵の名前 → 基準の画面での位置と大きさ(左上が原点、ピクセル)。</summary>
        static readonly Dictionary<string, Rect> Layout = new Dictionary<string, Rect>
        {
            { "title_background1", new Rect(0, 0, 1920, 1080) },
            { "title_background2", new Rect(0, 0, 1920, 1080) },
            { "intro", new Rect(0, 0, 1920, 1080) },
            { "tl_bg1", new Rect(0, 0, 1920, 1080) },
            { "tl_bg2", new Rect(0, 0, 1920, 1080) },
            { "tl_arm1", new Rect(0, 319, 289, 406) },
            { "tl_arm2", new Rect(0, 316, 285, 406) },
            { "tl_face_a", new Rect(314, 311, 229, 153) },
            { "tl_face_closed", new Rect(317, 327, 223, 137) },
            { "tl_face_yellow", new Rect(317, 302, 229, 162) },
            { "tl_face_b", new Rect(314, 304, 229, 160) },
            { "tl_hand1", new Rect(78, 82, 309, 337) },
            { "tl_hand2", new Rect(76, 80, 309, 339) },
            { "tl_head1", new Rect(190, 160, 435, 356) },
            { "tl_head2", new Rect(187, 160, 437, 355) },
            { "tl_body1", new Rect(16, 239, 1136, 841) },
            { "tl_body2", new Rect(14, 239, 1137, 841) },
            { "tl_logo1", new Rect(900, 98, 948, 539) },
            { "tl_logo2", new Rect(900, 98, 948, 539) },
            { "game1", new Rect(0, 0, 1920, 1080) },
            { "game2", new Rect(0, 0, 1920, 1080) },
            { "clear1", new Rect(0, 0, 1920, 1080) },
            { "clear2", new Rect(0, 0, 1920, 1080) },
            { "title_continue", new Rect(1336, 791, 273, 72) },
            { "title_restart", new Rect(1341, 892, 263, 62) },
            { "stageselect_stage1", new Rect(759, 63, 350, 91) },
            { "stageselect_stage2", new Rect(761, 66, 346, 87) },
            { "stageselect_hand", new Rect(877, 389, 471, 691) },
            { "girl_lit1", new Rect(848, 662, 229, 418) },
            { "girl_blue1", new Rect(848, 662, 229, 418) },
            { "girl_lit2", new Rect(839, 662, 225, 418) },
            { "girl_blue2", new Rect(839, 662, 225, 418) },
            { "ui_settings", new Rect(1718, 24, 149, 143) },
            { "font_0", new Rect(1131, 67, 56, 80) },
            { "font_1", new Rect(1131, 68, 57, 79) },
            { "font_2", new Rect(1129, 67, 59, 81) },
            { "font_3", new Rect(1131, 66, 58, 82) },
            { "font_4", new Rect(1130, 64, 61, 82) },
            { "font_5", new Rect(1131, 67, 62, 82) },
            { "font_6", new Rect(1129, 68, 60, 79) },
            { "font_7", new Rect(1130, 69, 57, 80) },
            { "font_8", new Rect(1129, 67, 63, 82) },
            { "font_9", new Rect(1128, 66, 60, 83) },
        };

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Vector2> fix = new Dictionary<string, Vector2>();
        static readonly HashSet<string> missing = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCaches()
        {
            cache.Clear();
            fix.Clear();
            missing.Clear();
        }

        /// <summary>この絵が置いてあるか。</summary>
        public static bool Has(string name)
        {
            return Get(name) != null;
        }

        public static bool TitleReady { get { return Has("title_background1") && Has("title_restart"); } }
        public static bool MoonsReady { get { return Has("moon_on1") && Has("moon_off1") && Has("moon_turn1"); } }
        public static bool GameReady { get { return Has("game1"); } }

        /// <summary>基準の画面で、この絵がある場所(ピクセル。左上が原点)。</summary>
        public static Rect DesignRect(string name)
        {
            Rect r;
            return Layout.TryGetValue(name, out r) ? r : new Rect(0f, 0f, DesignWidth, DesignHeight);
        }

        /// <summary>
        /// スプライトを返す(無ければ null)。大きさは、基準の画面での大きさ ÷ 90 ユニットになる。
        /// 月の絵(512px)は、月の円がちょうど 1 ユニットになる大きさにしてある。
        /// </summary>
        public static Sprite Get(string name)
        {
            Sprite sp;
            if (cache.TryGetValue(name, out sp)) return sp;
            if (missing.Contains(name)) return null;

            var tex = Resources.Load<Texture2D>(Folder + name);
            if (tex == null)
            {
                missing.Add(name);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float wantW, wantH;
            if (name.StartsWith("moon_"))
            {
                wantW = wantH = 1f / MoonDiameter;                 // 月の円が 1 ユニット
            }
            else
            {
                Rect r = DesignRect(name);
                wantW = r.width / PixelsPerUnit;
                wantH = r.height / PixelsPerUnit;
            }
            float ppu = tex.width / wantW;
            sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            sp.name = name;
            // 取り込み設定で縦横の大きさが変わってしまったときのため、縦だけ合わせる倍率を覚えておく
            float haveH = tex.height / ppu;
            fix[name] = new Vector2(1f, wantH / haveH);
            cache[name] = sp;
            return sp;
        }

        /// <summary>Get した絵を、基準どおりの形に直す倍率(ふつうは 1,1)。</summary>
        public static Vector2 Fix(string name)
        {
            Vector2 v;
            return fix.TryGetValue(name, out v) ? v : Vector2.one;
        }

        // ------------------------------------------------------------------
        // 画面との対応
        // ------------------------------------------------------------------

        /// <summary>カメラの見える幅と高さ(ユニット)。</summary>
        public static Vector2 View(Camera cam)
        {
            float h = cam.orthographicSize * 2f;
            return new Vector2(h * cam.aspect, h);
        }

        /// <summary>背景を画面いっぱいに広げる倍率(はみ出す分は切れる)。</summary>
        public static float Cover(Camera cam)
        {
            Vector2 v = View(cam);
            return Mathf.Max(v.x / (DesignWidth / PixelsPerUnit), v.y / (DesignHeight / PixelsPerUnit));
        }

        /// <summary>文字や UI の倍率。縦長の画面では全体が収まるよう小さくする。</summary>
        public static float Fit(Camera cam)
        {
            Vector2 v = View(cam);
            return Mathf.Min(v.x / (DesignWidth / PixelsPerUnit), v.y / (DesignHeight / PixelsPerUnit));
        }

        /// <summary>基準の画面の点(dx, dy)(左上が原点)を、画面の中心を基準にしてワールドに直す。</summary>
        public static Vector3 FromCenter(Camera cam, float dx, float dy)
        {
            float f = Fit(cam) / PixelsPerUnit;
            return new Vector3((dx - DesignWidth * 0.5f) * f, (DesignHeight * 0.5f - dy) * f, 0f);
        }

        /// <summary>基準の画面の点を、画面の右上の角を基準にしてワールドに直す(歯車など、角に付くもの用)。</summary>
        public static Vector3 FromTopRight(Camera cam, float dx, float dy)
        {
            Vector2 v = View(cam);
            float f = Fit(cam) / PixelsPerUnit;
            return new Vector3(v.x * 0.5f - (DesignWidth - dx) * f, v.y * 0.5f - dy * f, 0f);
        }

        /// <summary>絵のなかの点(中心ではなく左上基準)の位置。</summary>
        public static Vector2 Center(string name)
        {
            Rect r = DesignRect(name);
            return new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
        }

        // ------------------------------------------------------------------
        // 月の絵(満ち欠けの 5 段階)
        // ------------------------------------------------------------------

        /// <summary>
        /// MoonArt の frame(0 = 新月、28 = 満月、56 = 新月)に対応する月の絵。
        /// 新月(輪)・欠けた月 3 枚・満月の 5 段階。輪と満月は、2 枚を交互に出して手描きのゆらぎにする。
        /// </summary>
        public static Sprite MoonFrame(int frame)
        {
            int f = Mathf.Clamp(frame, 0, 56);
            float t = f <= 28 ? f / 28f : (56 - f) / 28f;     // 0 = 新月、1 = 満月
            int step = Mathf.RoundToInt(t * 4f);
            switch (step)
            {
                case 0: return New();
                case 1: return Get("moon_turn3");
                case 2: return Get("moon_turn2");
                case 3: return Get("moon_turn1");
                default: return Full();
            }
        }

        /// <summary>ゆらぎの切り替え(1 秒に 3 回)。off1 と on2、off2 と on1 は、同じ輪郭。</summary>
        static int Boil() { return ((int)(Time.time * 3f)) & 1; }

        public static Sprite New() { return Get(Boil() == 0 ? "moon_off1" : "moon_off2"); }
        public static Sprite Full() { return Get(Boil() == 0 ? "moon_on2" : "moon_on1"); }

        /// <summary>背景などの 2 枚組を、交互に出す(1 秒に 3 回)。</summary>
        public static Sprite Alternate(string a, string b, bool still = false)
        {
            Sprite sp = Get(!still && Boil() != 0 ? b : a);
            return sp != null ? sp : Get(a);
        }
    }
}
