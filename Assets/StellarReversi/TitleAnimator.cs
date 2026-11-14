using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// タイトル画面の動くイラスト。友達の絵を、パーツ(背景・体・腕・頭・顔・手・ロゴ)に分けて重ね、
    /// それぞれをゆっくり動かす。2 枚組のパーツは、手描きのゆらぎとして交互に出す。まばたきもする。
    ///
    /// 【動きの調整のしかた】
    ///   ・再生中に、ヒエラルキーの「StellarReversi」を選ぶと、この TitleAnimator の値が Inspector に出る。動かしながら変えられる。
    ///     (再生を止めると元に戻る。気に入った値は、下の「= 数字」のところに書き写すと、ずっと残る)
    ///   ・設定画面のデベロッパー項目にも、「タイトルの動き(大きさ)」「タイトルの動き(速さ)」のスライダーがある。全パーツをまとめて強弱できる。
    ///   ・動きの大きさの単位は、基準の画面(1920x1080)のピクセル。角度は度。
    ///   ・周期(period)は、行って戻ってくるまでの秒数。位相(phase)は 0〜1 で、ずらすと動きの始まりがずれる。
    /// </summary>
    public sealed class TitleAnimator : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Part
        {
            [Tooltip("横に動く幅(ピクセル)")] public float moveX;
            [Tooltip("縦に動く幅(ピクセル)")] public float moveY;
            [Tooltip("回る角度(度)。+ が反時計まわり")] public float rotate;
            [Tooltip("行って戻るまでの秒数")] public float period = 4f;
            [Tooltip("0〜1。動きの始まりのずれ")] public float phase;

            public Part() { }

            public Part(float moveX, float moveY, float rotate, float period, float phase)
            {
                this.moveX = moveX;
                this.moveY = moveY;
                this.rotate = rotate;
                this.period = period;
                this.phase = phase;
            }

            public void Eval(float clock, float move, out Vector2 offset, out float deg)
            {
                float t = clock / Mathf.Max(0.05f, period) + phase;
                float s = Mathf.Sin(t * Mathf.PI * 2f);
                float c = Mathf.Sin((t + 0.25f) * Mathf.PI * 2f);   // 縦は少しずらして、円を描くような動きにする
                offset = new Vector2(moveX * c, moveY * s) * move;
                deg = rotate * s * move;
            }
        }

        [Header("全体")]
        [Tooltip("手描きのゆらぎの切り替え(1 秒に何回)")] public float boilFps = 3f;

        [Header("パーツの動き")]
        public Part background = new Part(3f, 2f, 0f, 9f, 0f);
        public Part body = new Part(0f, 4f, 0.25f, 3.8f, 0f);       // 呼吸
        public Part head = new Part(1f, 2f, 1.4f, 4.6f, 0.15f);      // 首を支点に、ゆっくり傾く
        public Part arm = new Part(0f, 2f, 0.9f, 5.2f, 0.3f);        // 肩を支点に、腕全体がゆれる
        public Part hand = new Part(0f, 0f, 2.6f, 3.1f, 0.5f);       // 手首を支点に、手がひらひら
        public Part logo = new Part(0f, 5f, 0f, 3.4f, 0.1f);         // ロゴがふわふわ浮く

        [Header("まばたき")]
        public float blinkIntervalMin = 2.5f;
        public float blinkIntervalMax = 5.5f;
        public float blinkTime = 0.14f;
        [Range(0f, 1f)] [Tooltip("続けて 2 回まばたきする確率")] public float doubleBlink = 0.25f;

        [Header("支点(基準の画面 1920x1080 のピクセル。左上が原点)")]
        public Vector2 bodyPivot = new Vector2(480f, 1000f);
        public Vector2 neckPivot = new Vector2(440f, 490f);
        public Vector2 shoulderPivot = new Vector2(200f, 520f);
        public Vector2 wristPivot = new Vector2(130f, 400f);

        // ---- ここから下は中身 ----

        const int Count = 7;
        const int Bg = 0, Body = 1, Arm = 2, Head = 3, Face = 4, Hand = 5, Logo = 6;
        static readonly string[,] Names =
        {
            { "tl_bg1", "tl_bg2" },
            { "tl_body2", "tl_body1" },
            { "tl_arm1", "tl_arm2" },
            { "tl_head1", "tl_head2" },
            { "tl_face_yellow", "tl_face_b" },
            { "tl_hand1", "tl_hand2" },
            { "tl_logo1", "tl_logo2" },
        };
        static readonly int[] Orders = { -18, -17, -16, -15, -14, -13, -12 };

        readonly SpriteRenderer[] sr = new SpriteRenderer[Count];
        float clock;
        float blinkStart = -10f, nextBlink = 2f;
        int blinksLeft;
        bool built;
        Material baseMat;
        Transform parent;

        /// <summary>全部の絵が置いてあるか(無ければ、これまでの一枚絵のタイトルを使う)</summary>
        public static bool Ready
        {
            get
            {
                for (int i = 0; i < Count; i++)
                    for (int k = 0; k < 2; k++)
                        if (!StellarArt.Has(Names[i, k])) return false;
                return StellarArt.Has("tl_face_closed");
            }
        }

        public void Init(Material material, Transform root)
        {
            baseMat = material;
            parent = root;
        }

        void Build()
        {
            built = true;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Title_" + Names[i, 0]);
                go.transform.SetParent(parent, false);
                sr[i] = go.AddComponent<SpriteRenderer>();
                sr[i].sortingOrder = Orders[i];
                if (baseMat != null) sr[i].sharedMaterial = new Material(baseMat);
                sr[i].enabled = false;
            }
        }

        void Hide()
        {
            if (!built) return;
            for (int i = 0; i < Count; i++) sr[i].enabled = false;
        }

        // 基準の画面の点 p を、pivot のまわりに deg 度(画面で反時計まわりが +)回す
        static Vector2 Rot(Vector2 p, Vector2 pivot, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector2 v = p - pivot;
            return pivot + new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        static void Step(ref Vector2 p, ref float rot, Vector2 pivot, float deg, Vector2 off)
        {
            p = Rot(p, pivot, deg) + off;
            rot += deg;
        }

        /// <summary>毎フレーム呼ぶ。alpha が 0 なら隠す。</summary>
        public void Apply(Camera cam, float alpha, bool showLogo = true, float dim = 1f)
        {
            if (alpha <= 0.003f || !Ready)
            {
                Hide();
                return;
            }
            if (!built) Build();

            float move = DevMode.TitleMove;
            clock += Time.deltaTime * DevMode.TitleSpeed;

            int frame = ((int)(Time.time * Mathf.Max(0.1f, boilFps))) & 1;
            bool blinking = UpdateBlink();

            Vector2 bOff, hOff, aOff, wOff, gOff, lOff;
            float bRot, hRot, aRot, wRot, gRot, lRot;
            body.Eval(clock, move, out bOff, out bRot);
            head.Eval(clock, move, out hOff, out hRot);
            arm.Eval(clock, move, out aOff, out aRot);
            hand.Eval(clock, move, out wOff, out wRot);
            background.Eval(clock, move, out gOff, out gRot);
            logo.Eval(clock, move, out lOff, out lRot);

            float c = StellarArt.Cover(cam);
            float v = Mathf.Clamp01(DevMode.BackgroundScale);
            // 出るときは、透明にせず「黒から明るくなる」。透けると、重なったパーツが二重に見えてしまう
            float k = v * dim * Mathf.Clamp01(alpha);
            var color = new Color(k, k, k, 1f);

            for (int i = 0; i < Count; i++)
            {
                if (i == Logo && !showLogo)
                {
                    sr[i].enabled = false;
                    continue;
                }
                string name = Names[i, frame];
                if (i == Face && blinking) name = "tl_face_closed";
                Sprite sp = StellarArt.Get(name);
                if (sp == null) continue;

                Vector2 p = StellarArt.Center(name);
                float rot = 0f;
                float zoom = 1f;
                switch (i)
                {
                    case Bg:
                        p = new Vector2(StellarArt.DesignWidth * 0.5f, StellarArt.DesignHeight * 0.5f) + gOff;
                        zoom = 1f + 2f * Mathf.Max(Mathf.Abs(background.moveX), Mathf.Abs(background.moveY)) * move / (StellarArt.DesignHeight * 0.5f);
                        break;
                    case Body:
                        Step(ref p, ref rot, bodyPivot, bRot, bOff);
                        break;
                    case Arm:
                        Step(ref p, ref rot, shoulderPivot, aRot, aOff);
                        Step(ref p, ref rot, bodyPivot, bRot, bOff);
                        break;
                    case Hand:
                        Step(ref p, ref rot, wristPivot, wRot, wOff);
                        Step(ref p, ref rot, shoulderPivot, aRot, aOff);
                        Step(ref p, ref rot, bodyPivot, bRot, bOff);
                        break;
                    case Head:
                    case Face:
                        Step(ref p, ref rot, neckPivot, hRot, hOff);
                        Step(ref p, ref rot, bodyPivot, bRot, bOff);
                        break;
                    case Logo:
                        p += lOff;
                        break;
                }

                Vector2 f = StellarArt.Fix(name);
                sr[i].enabled = true;
                sr[i].sprite = sp;
                sr[i].transform.position = new Vector3((p.x - StellarArt.DesignWidth * 0.5f) * c / StellarArt.PixelsPerUnit,
                                                       (StellarArt.DesignHeight * 0.5f - p.y) * c / StellarArt.PixelsPerUnit, 0f);
                sr[i].transform.rotation = Quaternion.Euler(0f, 0f, rot);
                sr[i].transform.localScale = new Vector3(c * f.x * zoom, c * f.y * zoom, 1f);
                sr[i].color = color;
            }
        }

        /// <summary>
        /// 導入用の静止画: 目をつむった顔で、動かさず、ロゴも出さず、dim の明るさに暗くする。
        /// </summary>
        public void ApplyStill(Camera cam, float alpha, float dim, Vector2 focus, float zoom)
        {
            if (alpha <= 0.003f || !Ready)
            {
                Hide();
                return;
            }
            if (!built) Build();

            float c = StellarArt.Cover(cam);
            float cz = c * zoom;   // zoom 倍に寄る。focus(基準の画面の点)が画面の中心に来る
            float v = Mathf.Clamp01(DevMode.BackgroundScale) * dim * Mathf.Clamp01(alpha);
            var color = new Color(v, v, v, 1f);
            for (int i = 0; i < Count; i++)
            {
                if (i == Logo)
                {
                    sr[i].enabled = false;
                    continue;
                }
                string name = i == Face ? "tl_face_closed" : Names[i, 0];
                Sprite sp = StellarArt.Get(name);
                if (sp == null) continue;
                Vector2 p = i == Bg ? new Vector2(StellarArt.DesignWidth * 0.5f, StellarArt.DesignHeight * 0.5f) : StellarArt.Center(name);
                Vector2 f = StellarArt.Fix(name);
                sr[i].enabled = true;
                sr[i].sprite = sp;
                sr[i].transform.position = new Vector3((p.x - focus.x) * cz / StellarArt.PixelsPerUnit,
                                                       (focus.y - p.y) * cz / StellarArt.PixelsPerUnit, 0f);
                sr[i].transform.rotation = Quaternion.identity;
                sr[i].transform.localScale = new Vector3(cz * f.x, cz * f.y, 1f);
                sr[i].color = color;
            }
        }

        // 目をつむっている間だけ true
        bool UpdateBlink()
        {
            float now = Time.time;
            if (now >= nextBlink && now > blinkStart + blinkTime)
            {
                blinkStart = now;
                blinksLeft = Random.value < doubleBlink ? 1 : 0;
                nextBlink = now + Random.Range(blinkIntervalMin, blinkIntervalMax);
            }
            float since = now - blinkStart;
            if (since < blinkTime) return true;
            if (blinksLeft > 0 && since >= blinkTime * 2f)
            {
                blinksLeft--;
                blinkStart = now;
                return true;
            }
            return false;
        }
    }
}
