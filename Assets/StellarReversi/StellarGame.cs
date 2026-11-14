using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

namespace StellarReversi
{
    /// <summary>
    /// 『星空リバーシ』の本体。
    /// シーンに何も置かなくても、再生すると自動で生成される(RuntimeInitializeOnLoadMethod)。
    /// 入力は新しい Input System(Pointer = マウス/タッチ/ペン、Keyboard)。文字(タイトル・設定)だけ IMGUI で描く。
    ///
    /// ・タイトル → ステージ選択 → プレイ → クリア、の流れ。右上の歯車で設定(BGM・SE・ステージセレクト・タイトルへ戻る)。
    /// ・ステージ選択: 28の月相。まだ遊べない面は何も表示されず、前の面をクリアすると薄い黄色で現れ、クリア済みは濃い黄色になる。
    /// ・プレイ: あなたは新月。満月を挟んで新月にする。あなたが打つと、相手(満月)もかならず打ち返す。
    ///         相手は打てる所のどこかにランダムに打つので、どう打たれても解ける面だけが入っている。
    ///         全てのマスを新月にすればクリア。行き詰まっても何も表示されず、戻る/やり直すだけ。
    /// ・盤面の下のアイコン: 左から「ステージ選択へ」「ひとつ戻る(相手の手もまとめて戻る)」「やり直す」。
    /// ・キー: Z = ひとつ戻る、R = やり直す、Esc = ひとつ前の画面へ。
    /// </summary>
    public sealed partial class StellarGame : MonoBehaviour
    {
        enum Mode { Title, Select, Play, Clear }

        struct Particle
        {
            public bool alive;
            public Vector3 pos, vel;
            public float age, life, size, rot, spin;
        }

        struct Star
        {
            public SpriteRenderer sr;
            public float phase, speed, threshold, alpha;
            public Color tint;
        }

        /// <summary>「ひとつ戻る」用: 自分が打つ前の盤面と、打った場所。</summary>
        sealed class HistoryEntry
        {
            public readonly sbyte[] cells = new sbyte[MoonBoard.Count];
            public int move;
        }

        const float DiscSize = 0.9f;
        const float BoardCenterY = 0.4f;
        const float HudY = -5.0f;
        const float HudSpacing = 1.9f;
        const float HudHitRadius = 0.7f;
        const int StarCount = 280;
        const int ParticleCount = 480;
        const string ClearedKey = "Stellar.Cleared3";   // 「3,5,7」のように、クリアした面の番号(0始まり)を並べて保存
        const string BgmKey = "Stellar.Bgm";
        const string SeKey = "Stellar.Se";

        static readonly Color Background = new Color(8f / 255f, 8f / 255f, 31f / 255f, 1f); // #08081F
        static readonly Color Gold = new Color(202f / 255f, 178f / 255f, 82f / 255f, 1f);
        static readonly Color ArtYellow = new Color(240f / 255f, 230f / 255f, 130f / 255f, 1f);   // 友達の絵の月の黄色
        // アイコンや点の色。友達の絵があるときは、絵の黄色に揃える
        static Color Accent { get { return StellarArt.GameReady ? ArtYellow : Gold; } }
        static readonly Color PaleYellow = new Color(246f / 255f, 238f / 255f, 186f / 255f, 1f);

        // ---- 見た目 ----
        Camera cam;
        Material mat, moonMat;
        Transform selectRoot, boardRoot, hudRoot, skyRoot, particleRoot;
        SpriteRenderer[] stageIcons = new SpriteRenderer[0];
        SpriteRenderer[] discs = new SpriteRenderer[MoonBoard.Count];
        SpriteRenderer[] hints = new SpriteRenderer[MoonBoard.Count];
        SpriteRenderer[] hudIcons = new SpriteRenderer[4];      // 0 = ステージ選択へ, 1 = ひとつ戻る, 2 = やり直す, 3 = ヒント
        readonly float[] hudPulse = new float[4];
        SpriteRenderer clearMoon, titleMoon, gear, leftArrow, rightArrow;
        SpriteRenderer shootingStar;
        Star[] stars = new Star[StarCount];
        Particle[] particles = new Particle[ParticleCount];
        SpriteRenderer[] particleSr = new SpriteRenderer[ParticleCount];

        // ---- 盤面 ----
        readonly MoonBoard board = new MoonBoard();
        readonly sbyte[] initial = new sbyte[MoonBoard.Count];
        readonly sbyte[] shown = new sbyte[MoonBoard.Count];     // いま画面に出ている状態
        readonly float[] frame = new float[MoonBoard.Count];     // 月の満ち欠けの段階(0..56)
        readonly float[] scale = new float[MoonBoard.Count];
        readonly float[] alpha = new float[MoonBoard.Count];
        readonly List<HistoryEntry> history = new List<HistoryEntry>();

        // ---- 状態 ----
        Mode mode = Mode.Title;
        int stage;
        readonly HashSet<int> clearedSet = new HashSet<int>();   // クリアした面(0始まり)
        bool busy;
        bool levelCleared;     // いまの面をクリアした(演出の最中でも、次の面への矢印を押せる)
        bool settingsOpen;
        float boardAlpha, selectAlpha, titleAlpha, hudAlpha = 1f, skyProgress, clearMoonAlpha;
        bool skyVisible;
        float clearReadyTime;
        bool bgBoil;           // 背景・女の子のゆらぎ。プレイ中は止めて、最後の一枚が埋まった瞬間から動かす
        bool winSkippable;     // 月が消える演出が始まってから、タップで演出を飛ばせる
        float winSpeed = 1f;   // DevMode: クリア演出の倍速(見た目だけ。音は倍速にしない)
        float shootingAge = -1f, shootingLife, shootingTimer = 2f;
        Vector3 shootingStart, shootingDir;
        float bgmLevel = 0.8f, seLevel = 0.8f;
        Vector3 gearPos;
        Vector3[] stagePos = new Vector3[0];
        float stageSpacing = 1.6f, stageScale = 1f, layoutAspect = -1f;
        DevRunner devRunner;
        // ---- 友達の絵(StellarArt.cs) ----
        Transform artRoot;
        SpriteRenderer girlBlue, girlLit;   // 女の子だけ別の層(プレイ中は青、クリア後は月明かりつき)
        SpriteRenderer bgTitle, bgGame, bgClear, btnContinue, btnRestart, stageHeader, hand;
        readonly SpriteRenderer[] digitSr = new SpriteRenderer[8];
        const float TitleButtonPad = 0.35f;
        // 「はじめから」のあとの導入(目をつむった女の子と、ひとりごと)
        TitleAnimator titleAnim;   // タイトルの動くイラスト(絵が全部あるとき)
        bool creditsOn;             // 15 面をクリアしたあとのクレジット(タイトルの絵のロゴ抜きを、暗くして)
        float creditsStart;
        const float CreditsDim = 0.32f;
        bool introOn;
        float introStart;
        SpriteRenderer bgIntro;
        int introStep;            // 0 = 「月...まぶしいな...」、1 = 「オセロみたいに...」
        float introStepStart;
        const float IntroZoom = 1.6f;                              // 目をつむった女の子に寄る倍率
        static readonly Vector2 IntroFocus = new Vector2(620f, 470f);   // 寄るときの中心(基準の画面 1920x1080 のピクセル)
        const float IntroBarRatio = 2.39f;                         // 映画のような横長(上下を黒く切る)の縦横比
        const float IntroTextSize = 40f;                           // 文字の大きさ
        const float IntroDim = 0.38f;   // 導入の絵の明るさ(0 = 真っ暗、1 = そのまま)
        bool confirmReset;                    // 「はじめから」の確認のバナーを出している
        readonly float[] titleHover = new float[2];   // 0 = つづきから、1 = はじめから: カーソルが乗ると 1 に近づく
        // ステージ選択(絵のレイアウト): 月が手の前を通って並ぶ道。基準の画面(1920x1080)の点。-2, -1, 0(手がつまむ月), +1, +2 番目
        static readonly Vector2[] SlotPoints =
        {
            new Vector2(48f, 464f), new Vector2(499f, 513f), new Vector2(970f, 456f), new Vector2(1457f, 312f), new Vector2(1783f, 14f),
        };
        const float SlotMoonScale = 1.28f;   // 絵の見本で、月の大きさは直径 115 ピクセルほど
        int selStage;                         // いまつまんでいる月(0 始まり)
        float selShown;                       // 画面に出ている位置(滑らかに selStage へ近づく)
        bool dragging, dragMoved;
        Vector2 dragStart;
        float dragProgress;
        // ---- ズーム(プレイ中は盤を大きく、クリアで引いて、木の絵を見せる) ----
        float boardZoom = 1f;
        float girlGrow;                       // 女の子の大きさ: 0 = プレイ中の小さな姿、1 = クリア後の大きな姿
        // プレイ中の女の子(頭と肩だけ)の姿に、クリア後の女の子の絵を重ねる変換。基準の画面で p' = GirlQ0 * p + GirlD0
        const float GirlQ0 = 0.6900f;
        static readonly Vector2 GirlD0 = new Vector2(260.81f, 441.15f);
        float clearBg;                        // クリア後の背景(木に月が実った絵)の濃さ 0..1
        bool winZoom;                         // クリアの引きの最中(盤のズームを自動で決めない)
        // DevMode: ステージ作成
        bool creator;            // ステージ作成中
        bool editMode;           // true = 設置モード、false = 実行モード
        bool awaitOpponent;      // 実行モード: 相手(満月)の手を、自分で置く番
        string creatorMessage = "";
        float creatorMessageUntil;
        int hintCell = -1;                 // DevMode: ヒントで光らせるマス
        string hintMessage = "";           // DevMode: ヒントの結果の文字
        float hintMessageUntil;

        // ---- 音 ----
        AudioSource bgmSource, seSource;
        AudioClip clipPlace, clipTick, clipSparkle, clipSwell;
        readonly AudioClip[] chimes = new AudioClip[8];

        // ---- 文字(TextMeshPro / NotoSansJP の SDF フォント。StellarUi.cs) ----
        StellarUi ui;
        int settingsOpenedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<StellarGame>() != null) return;
            new GameObject("StellarReversi").AddComponent<StellarGame>();
        }

        // ------------------------------------------------------------------
        // 初期化
        // ------------------------------------------------------------------

        void Awake()
        {
            // シーンに元からある駒など(Black/White プレハブ)は、この作品では使わないので描画だけ止める。
            foreach (SpriteRenderer sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                sr.enabled = false;
            }

            SetupCamera();
            ui = new StellarUi(transform, MoonArt.Dot());
            ui.Prewarm(UiCharacters);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                mat = new Material(shader);
                moonMat = new Material(shader);   // 月だけ輝度を変えられるよう、材質を分けておく
            }

            LoadCleared();
            Loc.Load();
            LoadRankStats();
            LoadNodes();
            hintsOn = PlayerPrefs.GetInt(HintOnKey, 1) == 1;
            DevMode.Load();
            devRunner = gameObject.AddComponent<DevRunner>();
            bgmLevel = PlayerPrefs.GetFloat(BgmKey, 0.8f);
            seLevel = PlayerPrefs.GetFloat(SeKey, 0.8f);
            SetupAudio();

            BuildSky();
            BuildSelect();
            BuildBoard();
            BuildParticles();
            BuildTitleAndGear();
            BuildArt();
            BuildLives();
            ApplyFullscreenOnStart();

            ShowTitle();
            StartCoroutine(WarmUp());
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                cam.gameObject.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            UpdateCamera();

            // 表示領域(16:9)の外を黒く塗るためのカメラ
            var bars = new GameObject("LetterboxCamera");
            bars.transform.SetParent(transform, false);
            var barCam = bars.AddComponent<Camera>();
            barCam.clearFlags = CameraClearFlags.SolidColor;
            barCam.backgroundColor = Color.black;
            barCam.cullingMask = 0;
            barCam.depth = -100f;
            barCam.orthographic = true;
        }

        // 縦長の画面でも盤面とステージ一覧が収まるようにする。
        void UpdateCamera()
        {
            cam.rect = StellarView.Normalized;   // 16:9 の領域に収める。余りは、黒い別のカメラが塗る
            float aspect = Mathf.Max(0.1f, cam.aspect);
            cam.orthographicSize = Mathf.Max(6f, 5.4f / aspect);
        }

        // 月の絵を先に少しずつ作っておく(アニメーション中のカクつきを防ぐ)。
        IEnumerator WarmUp()
        {
            if (StellarArt.MoonsReady) yield break;   // 友達の絵の月を使うので、コードで描く月は作らない
            for (int i = 0; i < MoonArt.Frames; i++)
            {
                MoonArt.Moon(i);
                if (i % 4 == 3) yield return null;
            }
        }

        SpriteRenderer MakeSr(string objName, Transform parent, int order, Sprite sprite, Material material = null)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            Material m = material != null ? material : mat;
            if (m != null) sr.sharedMaterial = m;
            return sr;
        }

        Transform MakeRoot(string rootName, Transform parent)
        {
            var go = new GameObject(rootName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        void BuildSky()
        {
            skyRoot = MakeRoot("Sky", transform);
            var rnd = new System.Random(1234);
            Color[] palette =
            {
                new Color(0.86f, 0.90f, 1f), new Color(1f, 0.93f, 0.72f), new Color(1f, 1f, 1f), new Color(0.78f, 0.85f, 1f)
            };
            for (int i = 0; i < StarCount; i++)
            {
                bool big = rnd.NextDouble() < 0.14;
                float r = (float)rnd.NextDouble();
                SpriteRenderer sr = MakeSr("Star" + i, skyRoot, 0, big ? MoonArt.Sparkle() : MoonArt.Dot());
                float size = big ? 0.25f + 0.40f * r : 0.06f + 0.16f * r;
                sr.transform.localPosition = new Vector3(((float)rnd.NextDouble() * 2f - 1f) * 18f,
                                                         ((float)rnd.NextDouble() * 2f - 1f) * 10f, 0f);
                sr.transform.localScale = new Vector3(size, size, 1f);
                sr.enabled = false;
                stars[i] = new Star
                {
                    sr = sr,
                    phase = (float)rnd.NextDouble() * 6.28f,
                    speed = 0.6f + (float)rnd.NextDouble() * 1.8f,
                    threshold = (float)rnd.NextDouble() * 0.6f,
                    alpha = big ? 0.95f : 0.45f + 0.5f * (float)rnd.NextDouble(),
                    tint = palette[rnd.Next(palette.Length)],
                };
            }

            shootingStar = MakeSr("ShootingStar", skyRoot, 1, MoonArt.Dot());
            shootingStar.enabled = false;
        }

        void BuildSelect()
        {
            selectRoot = MakeRoot("StageSelect", transform);
            int n = Stages.FixedCount;
            stageIcons = new SpriteRenderer[n];
            for (int k = 0; k < n; k++)
            {
                // 新月から満月、また新月へ。面の数だけ月が並ぶ。
                SpriteRenderer sr = MakeSr("Stage" + k, selectRoot, 3, MoonArt.Moon(PhaseFrame(k)), moonMat);
                sr.enabled = false;
                stageIcons[k] = sr;
            }
            EnsureNodeVisuals();
            LayoutSelect();
        }

        // k 番目のステージの月の形。0 番目が新月で、真ん中あたりで満月になり、また新月に近づいていく。
        static int PhaseFrame(int k)
        {
            int n = Mathf.Max(1, Stages.Count);
            return Mathf.RoundToInt(k * (MoonArt.Frames - 1) / (float)n) % (MoonArt.Frames - 1);
        }

        // 面の数と画面の広さから、月がいちばん大きく並ぶ行・列の数を決める。
        void LayoutSelect()
        {
            layoutAspect = cam.aspect;
            int n = NodeCount;
            stagePos = new Vector3[n];
            if (n == 0) return;

            float h = cam.orthographicSize, w = h * cam.aspect;
            float areaW = Mathf.Max(2f, Mathf.Min(2f * w - 2.4f, 13f));
            bool art = StellarArt.MoonsReady;   // 友達の絵では、上に「ステージ ○」の文字が入るので、月は少し下に寄せる
            float areaH = Mathf.Max(2f, Mathf.Min(2f * h - 3f - (art ? 1.4f : 0f), 9.6f));
            int bestCols = 1;
            float bestSp = 0f;
            for (int c = 1; c <= n; c++)
            {
                int r = (n + c - 1) / c;
                float sp = Mathf.Min(areaW / c, areaH / r);
                if (sp > bestSp + 0.001f)
                {
                    bestSp = sp;
                    bestCols = c;
                }
            }

            stageSpacing = Mathf.Min(bestSp, 1.75f);
            stageScale = Mathf.Min(1f, stageSpacing / 1.45f);
            int rows = (n + bestCols - 1) / bestCols;
            for (int k = 0; k < n; k++)
            {
                int col = k % bestCols, row = k / bestCols;
                int inRow = row == rows - 1 ? n - row * bestCols : bestCols;   // 最後の行は真ん中に寄せる
                stagePos[k] = new Vector3((col - (inRow - 1) * 0.5f) * stageSpacing,
                                          ((rows - 1) * 0.5f - row) * stageSpacing + (art ? -0.7f : 0.2f), 0f);
            }
        }

        static Vector3 CellPosition(int i)
        {
            return new Vector3(i % MoonBoard.Size - 3.5f, BoardCenterY + 3.5f - i / MoonBoard.Size, 0f);
        }

        void BuildBoard()
        {
            boardRoot = MakeRoot("Board", transform);
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                SpriteRenderer hint = MakeSr("Hint" + i, boardRoot, 1, MoonArt.Dot());
                hint.transform.localPosition = CellPosition(i);
                hint.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                hint.enabled = false;
                hints[i] = hint;

                SpriteRenderer disc = MakeSr("Disc" + i, boardRoot, 3, MoonArt.Moon(0), moonMat);
                disc.transform.localPosition = CellPosition(i);
                disc.enabled = false;
                discs[i] = disc;
            }

            hudRoot = MakeRoot("Hud", boardRoot);
            Sprite[] icons = { MoonArt.HomeIcon(), MoonArt.UndoIcon(), MoonArt.ResetIcon(), MoonArt.HintIcon() };
            for (int n = 0; n < 4; n++)
            {
                SpriteRenderer sr = MakeSr("Hud" + n, hudRoot, 5, icons[n]);
                sr.transform.localPosition = HudPosition(n);
                hudIcons[n] = sr;
            }

            clearMoon = MakeSr("ClearMoon", transform, 4, MoonArt.Moon(0), moonMat);
            clearMoon.transform.localPosition = new Vector3(0f, BoardCenterY, 0f);
            clearMoon.gameObject.SetActive(false);

            boardRoot.gameObject.SetActive(false);
        }

        // 絵ごとに専用の材質を作り、その絵の画像をはっきり結びつける(材質を共有すると、同じ絵に見えてしまうことがあった)
        Material OwnMaterial(Sprite sprite)
        {
            if (mat == null) return null;
            var m = new Material(mat);
            m.mainTexture = sprite.texture;
            return m;
        }

        void BuildTitleAndGear()
        {
            titleMoon = MakeSr("TitleMoon", transform, 3, MoonArt.Moon(0), moonMat);
            titleMoon.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            titleMoon.gameObject.SetActive(false);

            Sprite gearArt = StellarArt.Get("ui_settings");
            Sprite gearSprite = gearArt != null ? gearArt : MoonArt.GearIcon();
            gear = MakeSr("Gear", transform, 12, gearSprite, OwnMaterial(gearSprite));
            gear.enabled = false;

            // 歯車の左に、ひとつ前/ひとつ先のステージへ移る矢印(ステージ選択を通らずに行ける)
            leftArrow = MakeSr("ArrowLeft", transform, 12, MoonArt.ArrowLeftIcon(), OwnMaterial(MoonArt.ArrowLeftIcon()));
            leftArrow.enabled = false;
            rightArrow = MakeSr("ArrowRight", transform, 12, MoonArt.ArrowRightIcon(), OwnMaterial(MoonArt.ArrowRightIcon()));
            rightArrow.enabled = false;

            // 確認用: 歯車・左矢印・右矢印が別の絵になっているか(重心x が 左矢印<0.5<右矢印、歯車は 0.5 付近)
            Debug.Log("[星空リバーシ] アイコン確認: " + MoonArt.IconFingerprint(gear.sprite) + " / "
                + MoonArt.IconFingerprint(leftArrow.sprite) + " / " + MoonArt.IconFingerprint(rightArrow.sprite));
        }

        // ホーム・ひとつ戻す・やり直し。盤の左にすき間があれば縦に並べ、無ければ下に並べる(ワールドの位置)
        Vector3 HudPosition(int n)
        {
            float w = cam.orthographicSize * cam.aspect;
            float edge = 4f * boardZoom;
            if (w - edge >= 2.2f)
            {
                float x = -(edge + (w - edge) * 0.5f);
                return new Vector3(x, 1.9f - 1.4f * n, 0f);
            }
            // 友達の絵では、真ん中の下に女の子がいるので、アイコンは左下にまとめる
            if (StellarArt.GameReady) return new Vector3(n < 3 ? -3.7f + 1.3f * n : -5.0f, -4.65f, 0f);   // ヒント(3)は、いちばん左
            return new Vector3((n - 1) * HudSpacing, HudY, 0f);
        }

        // プレイ中の盤の拡大率(画面に収まる範囲で、できるだけ大きく)
        float PlayZoom()
        {
            float h = cam.orthographicSize, w = h * cam.aspect;
            float z = Mathf.Min(1.25f, (w - 0.6f) / 4f, (h - 0.7f) / 4f);
            return Mathf.Max(1f, z);
        }

        void BuildParticles()
        {
            particleRoot = MakeRoot("Particles", transform);
            for (int i = 0; i < ParticleCount; i++)
            {
                SpriteRenderer sr = MakeSr("P" + i, particleRoot, 6, MoonArt.Sparkle());
                sr.enabled = false;
                particleSr[i] = sr;
            }
        }

        void SetupAudio()
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.volume = 0.7f * bgmLevel;
            bgmSource.clip = LoadClip("Stellar/BGM", StellarAudio.CreateBgm);

            seSource = gameObject.AddComponent<AudioSource>();
            seSource.playOnAwake = false;
            seSource.spatialBlend = 0f;

            for (int i = 0; i < chimes.Length; i++)
            {
                int index = i;
                chimes[i] = LoadClip("Stellar/SE_Chime" + i, () => StellarAudio.CreateChime(index));
            }
            clipPlace = LoadClip("Stellar/SE_Place", StellarAudio.CreatePlace);
            clipTick = LoadClip("Stellar/SE_Tick", StellarAudio.CreateTick);
            clipSparkle = LoadClip("Stellar/SE_Sparkle", StellarAudio.CreateSparkle);
            clipSwell = LoadClip("Stellar/SE_Swell", StellarAudio.CreateSwell);

            bgmSource.Play();
        }

        // Resources に自前の音源があればそれを使い、無ければ合成した音を使う。
        static AudioClip LoadClip(string resourcePath, System.Func<AudioClip> fallback)
        {
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            return clip != null ? clip : fallback();
        }

        void PlaySe(AudioClip clip, float volume = 1f)
        {
            if (clip != null) seSource.PlayOneShot(clip, volume * seLevel);
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------

        void Update()
        {
            UpdateCamera();
            bgmSource.volume = 0.7f * bgmLevel;

            // DevMode: 背景の明るさと月の輝度
            Color bg = StellarArt.GameReady ? Color.black : Background * DevMode.BackgroundScale;
            bg.a = 1f;
            cam.backgroundColor = bg;
            if (moonMat != null)
            {
                float m = DevMode.MoonScale;
                moonMat.SetColor("_Color", new Color(m, m, m, 1f));
            }
            if (Mathf.Abs(cam.aspect - layoutAspect) > 0.001f) LayoutSelect();

            bool pressed = false;
            Vector2 world = Vector2.zero;
            Vector2 screenPos = Vector2.zero;
            Pointer pointer = Pointer.current;   // マウス・タッチ・ペンのうち最後に使われたもの
            if (pointer != null && pointer.press.wasPressedThisFrame && StellarView.Pixels.Contains(pointer.position.ReadValue()))
            {
                Vector2 p = pointer.position.ReadValue();
                Vector3 w = cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -cam.transform.position.z));
                pressed = true;
                screenPos = p;
                world = new Vector2(w.x, w.y);
                if (IsOverDevUi(p)) pressed = false;   // DevMode: ボタンの上のクリックは盤面に通さない
            }

            Keyboard kb = Keyboard.current;
            bool undoKey = kb != null && kb.zKey.wasPressedThisFrame;
            bool resetKey = kb != null && kb.rKey.wasPressedThisFrame;
            bool escKey = kb != null && kb.escapeKey.wasPressedThisFrame;
            bool backKey = escKey || (kb != null && kb.backspaceKey.wasPressedThisFrame);
            if (DevMode.Active && mode == Mode.Play && !settingsOpen && kb != null && kb.hKey.wasPressedThisFrame) ShowHint();   // DevMode

            UpdateTopBar();
            bool nearGear = pressed && (mode != Mode.Title || TitleMenuVisible || settingsOpen) && Vector2.Distance(world, gearPos) < 0.75f;
            bool nearLeft = pressed && leftArrow.enabled && Vector2.Distance(world, leftArrow.transform.position) < 0.65f;
            bool nearRight = pressed && rightArrow.enabled && Vector2.Distance(world, rightArrow.transform.position) < 0.65f;

            if (settingsOpen)
            {
                // 設定を開いている間は、盤面などには触れない。歯車か Esc で閉じる。
                if (nearGear || escKey) CloseSettings();
            }
            else if (nearGear)
            {
                OpenSettings();
            }
            else if (CutinOn)
            {
                UpdateCutin(pressed);   // カットインの間は、ほかの入力を受けつけない(どこを押しても次へ)
            }
            else if (nearLeft || nearRight)
            {
                // ステージ選択を通らずに、ひとつ前/ひとつ先へ
                if ((!busy || levelCleared) && (nearLeft || CanPlay(curNode + 1)))
                {
                    PlaySe(clipTick);
                    StartStage(curNode + (nearLeft ? -1 : 1));
                }
            }
            else
            {
                switch (mode)
                {
                    case Mode.Title:
                        if (creditsOn)
                        {
                            if ((pressed || escKey) && Time.time - creditsStart > 1f) { creditsOn = false; if (creditsFromTitle) creditsFromTitle = false; else ShowSelect(); }
                            break;
                        }
                        if (introOn)
                        {
                            // 1 回目のタップ: 「月...まぶしいな...」が消えて「オセロみたいに...」が出る / 2 回目: 1 面へ
                            // 文字が出きる前のタップは、まず出しきる
                            if (pressed && Time.time - introStart > 0.5f)
                            {
                                float shown = IntroTextAlpha(introStep == 0 ? 1.2f : 0.6f);
                                if (shown < 0.99f) introStepStart = Time.time - (introStep == 0 ? 1.2f : 0.6f) - IntroFadeIn;
                                else if (introStep == 0) { introStep = 1; introStepStart = Time.time; }
                                else { introOn = false; StartStage(0); }
                            }
                            break;
                        }
                        if (confirmReset && escKey) confirmReset = false;
                        if (pressed && titleAlpha > 0.5f && !PointerOverTitleMenu())
                        {
                            if (StellarArt.TitleReady) { if (!confirmReset) HandleTitleClick(world); }
                            else
                            {
                                PlaySe(clipTick);
                                ShowSelect();
                            }
                        }
                        break;
                    case Mode.Select:
                        if (escKey) ShowTitle();
                        else if (StellarArt.MoonsReady) HandleSelectInput(world);
                        else if (pressed) HandleSelectClick(world);
                        break;
                    case Mode.Play:
                        if (winSkippable && pressed) SkipWin();
                        else HandlePlayInput(pressed, world, undoKey, resetKey, backKey);
                        break;
                    case Mode.Clear:
                        if ((pressed || backKey) && Time.time >= clearReadyTime) { if (stage == 14 && !backKey) ShowStage15Cutin(); else ShowSelect(); }
                        break;
                }
            }

            UpdateTitleVisuals();
            UpdateSelectVisuals();
            UpdateBoardVisuals();
            UpdateSky();
            UpdateParticles();
            UpdateShootingStar();
            UpdateClearMoon();
            UpdateArt();
            UpdateLives();
        }

        // ------------------------------------------------------------------
        // タイトル
        // ------------------------------------------------------------------

        void ShowTitle()
        {
            StopAllCoroutines();
            HideCutin();
            introOn = false;
            creditsOn = false;
            creditsFromTitle = false;
            sideUntil = 0f;
            bgBoil = false;
            winSkippable = false;
            creator = false;   // DevMode
            editMode = false;
            awaitOpponent = false;
            busy = false;
            settingsOpen = false;
            ClearParticles();
            mode = Mode.Title;
            confirmReset = false;
            winZoom = false;
            clearBg = 0f;
            girlGrow = 0f;
            boardZoom = 1f;
            selectRoot.gameObject.SetActive(false);
            boardRoot.gameObject.SetActive(false);
            clearMoon.gameObject.SetActive(false);
            titleMoon.gameObject.SetActive(true);
            titleAlpha = 0f;
            StartCoroutine(Lerp(0f, 1f, 1.6f, v => titleAlpha = v));
            StartCoroutine(Lerp(skyProgress, 0.45f, 2.5f, v => skyProgress = v));   // 星がほんのり浮かぶ
        }

        void UpdateTitleVisuals()
        {
            bool on = mode == Mode.Title && !StellarArt.TitleReady;   // 友達の絵のタイトルでは、月は絵に含まれている
            if (titleMoon.gameObject.activeSelf != on) titleMoon.gameObject.SetActive(on);
            if (!on) return;

            // いまは満月で止めてある(以前は、ゆっくり満ちて欠けていた:
            //   float f = (Time.time * 1.6f) % (MoonArt.Frames - 1); titleMoon.sprite = MoonArt.Moon(Mathf.RoundToInt(f));)
            titleMoon.sprite = MoonArt.Moon(MoonArt.FullFrame);
            float s = 3.4f * (1f + 0.015f * Mathf.Sin(Time.time * 0.9f));
            titleMoon.transform.localScale = new Vector3(s, s, 1f);
            titleMoon.color = new Color(1f, 1f, 1f, titleAlpha);
        }

        // ------------------------------------------------------------------
        // 設定(右上の歯車)
        // ------------------------------------------------------------------

        // 矢印(右)の位置: 画面の右端・下端からの距離(ワールド単位)
        static readonly Vector2 ArrowBottomRight = new Vector2(1.0f, 1.0f);

        void UpdateTopBar()
        {
            bool inGame = (mode == Mode.Play || mode == Mode.Clear) && !creator;
            gear.enabled = mode != Mode.Title || TitleMenuVisible || settingsOpen;   // タイトルでも、右上に歯車(設定)

            // 左向き: 2 面目以降。右向き: 最後の面ではなく、行ける面(クリア済みの次)があるとき
            leftArrow.enabled = inGame && curNode >= 1;
            rightArrow.enabled = inGame && curNode + 1 < NodeCount;   // 次の面があれば出す(最後の面では消える)
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            if (StellarArt.Has("ui_settings"))
            {
                // 友達の絵の歯車: 絵のとおり、画面の右上の角に置く(回さない)
                Vector2 gc = StellarArt.Center("ui_settings");
                gearPos = StellarArt.FromTopRight(cam, gc.x, gc.y);
                gear.transform.position = gearPos;
                Vector2 gf = StellarArt.Fix("ui_settings");
                float gs = StellarArt.Fit(cam);
                gear.transform.localScale = new Vector3(gs * gf.x, gs * gf.y, 1f);
            }
            else
            {
                gearPos = new Vector3(w - 0.9f, h - 0.9f, 0f);
                gear.transform.position = gearPos;
                gear.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                if (settingsOpen) gear.transform.Rotate(0f, 0f, -30f * Time.deltaTime);
            }
            // タイトルでは、絵の色のまま(青い背景に、金色の色味を掛けるとくすんで見える)
            gear.color = mode == Mode.Title
                ? new Color(1f, 1f, 1f, Mathf.Clamp01(titleAlpha))
                : new Color(Accent.r, Accent.g, Accent.b, settingsOpen ? 1f : 0.8f);
            if (mode == Mode.Title) return;   // 矢印はタイトルには出さない

            Color arrowColor = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            // ステージ遷移の矢印は画面の下(右下)に置く。ArrowBottomRight で位置を調整できる
            Vector3 arrowBase = new Vector3(w - ArrowBottomRight.x, -h + ArrowBottomRight.y, 0f);
            rightArrow.transform.position = arrowBase;
            leftArrow.transform.position = arrowBase + new Vector3(-1.25f, 0f, 0f);
            leftArrow.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            rightArrow.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            leftArrow.color = arrowColor;
            // まだ行けない次の面(前の面をクリアしていない)なら、薄く表示して押しても進まない
            rightArrow.color = CanPlay(curNode + 1) ? arrowColor : new Color(Accent.r, Accent.g, Accent.b, 0.25f);
        }

        void OpenSettings()
        {
            settingsOpen = true;
            settingsOpenedFrame = Time.frameCount;   // 歯車を押した同じフレームの押下を、設定画面のボタンに通さない
            PlaySe(clipTick);
        }

        void CloseSettings()
        {
            settingsOpen = false;
            rankingOpen = false;
            gear.transform.localRotation = Quaternion.identity;
            PlayerPrefs.SetFloat(BgmKey, bgmLevel);
            PlayerPrefs.SetFloat(SeKey, seLevel);
            DevMode.Save();   // PlayerPrefs.Save() も呼ぶ
            PlaySe(clipTick);
        }

        // 画面に出す文字を先に入れておく(動的フォントなので、初めて出る文字は描くときに追加される)
        const string UiCharacters =
            "星空リバーシクリックしてはじめるステージセレクトタイトルへ戻る背景の明るさ月の輝度デベロッパーモード検証結果出ますを作る" +
            "あなたは新月です。すべての満月を裏がえして消してください。一つでも残してはいけません空白。ヒント実行編集盤面コピー設置打てる場所がありません探索が大きすぎて判定できませんでしたこの盤面からは解けませんかやり直してください" +
            "相手どう打っても勝てる最善の手通りあと最大全マス新月にできましたこの手順で解けますを置いてください中あなたが打ったあと満月も自分で置きますコピーしました貼れますクリックで" +
            "なし最初に打てる場所が空きる作成ONOFF0123456789()×:/ ,.-+→" +
            "今までの内容がすべて消えますがよろしいですかはいいえ消して始める" +
            "クリア演出" +
            "月まぶしいなオセロみたいに消えたらいのに." +
            "タイトルの動き速さ" +
            "月もオセロみたいに消えたらいのに相手が月を置くことがあります赤い月のステージは難易度が高くなっています裏返" +
            "ディレクター林凌平デザイナーぶりけステージデザインばるすThank you for playing!：:" +
            "フルスクリーン言語日本語今日あと回使い切りました明日また設定クレジット割れてしまいました次の面進めませんステージセレクトで現れますここに置いてみましょうこの盤面からは解けません途中で出たのでやり直しとしてライフが減りました月は修復されましたただし" +
            "ContinueNewGameStarrySkyReversiSettingsCreditsFullscreenHintsLanguageStageYesNo" +
            "協力ドパミン研究所制作ヘイルズhails_gameルナティックLUNATICここまでプレイしていただきありがとうございますゲームに慣れてきましたか今までのルールを軽く振り返りますまで進んだらエキシビションマッチに挑戦する権利を差し上げます難易度が高くなっています▼!？（）" +
            "ExhibitionMatchYouhave3lives.SpecialThanksProducedbyHailsThankyousomuchforplayingthisfarGettingusedtothegame?Let'squicklookbackattherulesReachandyou'llearntherighttochallengethe";

        void FillRect(Rect r, Color c)
        {
            ui.Fill(r, c);
        }

        void LateUpdate()
        {
            bool devHintUi = DevMode.Active && mode == Mode.Play && !settingsOpen;   // DevMode

            ui.Begin(Time.frameCount != settingsOpenedFrame);
            float vw = ui.Width;
            const float vh = StellarUi.Height;

            if (mode == Mode.Title && !StellarArt.TitleReady) DrawTitle(vw);
            DrawEnglishOverlays(vw);
            DrawTitleMenu();
            if (mode == Mode.Title && creditsOn) DrawCredits(vw, vh);
            else if (mode == Mode.Title && introOn) DrawIntro(vw, vh);
            else if (mode == Mode.Title && confirmReset) DrawResetConfirm(vw, vh);
            if (mode == Mode.Play && !settingsOpen && !creator)
            {
                DrawTutorialText(vw);
                DrawSideMessage(vw);
                DrawHintCount(vw);
            }
            if (mode == Mode.Clear && !settingsOpen) DrawClearNote(vw, vh);
            if (devHintUi) DrawDevHint(vw);
            if ((mode == Mode.Play || mode == Mode.Clear) && !settingsOpen) DrawStageNumber(vw);
            DrawCutin(vw, vh);   // メニュー(設定)より下
            if (settingsOpen) DrawSettings(vw, vh);
            ui.End();
        }

        // チュートリアル(1〜3面目)だけ、盤の右横に目的を書く。行の長さは、盤の右の余白に収まるよう手で改行している
        static readonly string[] TutorialTexts =
        {
            "あなたは新月です。\n裏返してすべての\n満月を新月に\nしてください。",
            "一つでも満月を\n残してはいけません。",
            "一つでも空白を\n残してはいけません。",
            "相手が月を\n置くことが\nあります。",
            null,   // 5 面目(最初の LUNATIC)の説明は、カットインで出す
        };

        static readonly string[] TutorialTextsEn =
        {
            "You are the new moon.\nFlip every full moon\ninto a new moon.",
            "Don't leave even\none full moon.",
            "Don't leave even\none empty space.",
            "Your opponent may\nplace moons, too.",
            null,   // 5 面目: カットイン
        };


        void DrawTutorialText(float vw)
        {
            if (stage < 0 || stage >= TutorialTexts.Length) return;
            float unit = StellarUi.Height / (2f * cam.orthographicSize);   // 1 ユニットが仮想画面で何ピクセルか
            float x = vw * 0.5f + 4f * boardZoom * unit + 40f;   // 盤の右端のすぐ外
            float width = vw - x - 20f;
            float cy = StellarUi.Height * 0.5f - BoardCenterY * unit;
            string text = Loc.En ? TutorialTextsEn[stage] : TutorialTexts[stage];
            if (text == null) return;
            ui.Label(new Rect(x, cy - 150f, width, 300f), text, stage >= 3 ? 34f : 38f, TextAlignmentOptions.Left,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, hudAlpha));   // 月が消え始めると、いっしょに消える
        }

        // ステージ番号を、ステージを移動する矢印の左に出す
        void DrawStageNumber(float vw)
        {
            float unit = StellarUi.Height / (2f * cam.orthographicSize);
            Vector3 p = leftArrow.transform.position;
            float sx = vw * 0.5f + p.x * unit, sy = StellarUi.Height * 0.5f - p.y * unit;
            float a = Mathf.Min(boardAlpha, 0.9f);
            ui.Label(new Rect(sx - 55f - 200f, sy - 40f, 200f, 80f), NodeLabel(curNode), 52f, TextAlignmentOptions.Right,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, a));
        }

        void SmallText(Rect r, string t)
        {
            ui.Label(r, t, 34f, TextAlignmentOptions.Center, PaleYellow);
        }

        void LabelText(Rect r, string t)
        {
            ui.Label(r, t, 44f, TextAlignmentOptions.Left, PaleYellow);
        }

        // DevMode: 左上の「ヒント」ボタンと、結果の文字
        void DrawDevHint(float vw)
        {
            if (creator)
            {
                DrawCreator(vw);
                return;
            }
            if (ui.Button(new Rect(24f, 24f, 220f, 70f), "ヒント (H)", 32f)) ShowHint();
            if (Time.time < hintMessageUntil && hintMessage.Length > 0)
            {
                SmallText(new Rect(24f, 100f, 900f, 50f), hintMessage);
            }
        }

        // ------------------------------------------------------------------
        // DevMode: ステージ作成
        //   設置モード: 初めは全て新月。マスをクリックするたびに 新月 → 満月 → 何もなし → 新月 …と変わる。
        //   「実行」で実行モードへ。その盤面から遊べる。ふだんは自動で打つ相手(満月)の手も、自分で置く
        //   (相手の手の分岐を、全て試せるように)。
        // ------------------------------------------------------------------

        static Rect ExecRect(float vw) { return new Rect(vw - 300f, 440f, 270f, 90f); }
        static Rect CopyRect(float vw) { return new Rect(vw - 300f, 550f, 270f, 90f); }
        static readonly Rect HintRect = new Rect(24f, 24f, 220f, 70f);

        // 画面上の位置 p(ピクセル)が、開発用ボタンの上かどうか
        bool IsOverDevUi(Vector2 p)
        {
            if (!DevMode.Active || mode != Mode.Play || settingsOpen) return false;
            Rect view = StellarView.Pixels;
            float s = view.height / 1080f;
            float vx = (p.x - view.x) / s, vy = (view.yMax - p.y) / s;
            float vw = view.width / s;
            var pt = new Vector2(vx, vy);
            if (creator) return ExecRect(vw).Contains(pt) || CopyRect(vw).Contains(pt);
            return HintRect.Contains(pt);
        }

        void EnterCreator()
        {
            StopAllCoroutines();
            creator = true;
            editMode = true;
            awaitOpponent = false;
            stage = 0;
            hintCell = -1;
            creatorMessage = "マスをクリックすると 新月 → 満月 → なし と変わります";
            creatorMessageUntil = Time.time + 10f;

            var cells = new sbyte[MoonBoard.Count];
            for (int i = 0; i < MoonBoard.Count; i++) cells[i] = MoonBoard.New;
            board.SetCells(cells);
            board.CopyCellsTo(initial);
            history.Clear();
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                shown[i] = board[i];
                SnapVisual(i);
            }

            busy = false;
            settingsOpen = false;
            hudAlpha = 1f;
            clearMoonAlpha = 0f;
            clearMoon.gameObject.SetActive(false);
            ClearParticles();
            StartCoroutine(Lerp(skyProgress, 0f, 1.2f, v => skyProgress = v));

            mode = Mode.Play;
            selectRoot.gameObject.SetActive(false);
            boardRoot.gameObject.SetActive(true);
            selectAlpha = 0f;
            boardAlpha = 0f;
            StartCoroutine(Lerp(0f, 1f, 0.9f, v => boardAlpha = v));
            PlaySe(clipTick);
        }

        void HandleEditInput(bool pressed, Vector2 world)
        {
            if (!pressed) return;

            Vector3 hp = HudPosition(0);
            float dx = world.x - hp.x, dy = world.y - hp.y;
            if (dx * dx + dy * dy < HudHitRadius * HudHitRadius)
            {
                hudPulse[0] = 1f;
                PlaySe(clipTick);
                ShowSelect();
                return;
            }

            int cell = WorldToCell(world);
            if (cell < 0) return;
            var cells = new sbyte[MoonBoard.Count];
            board.CopyCellsTo(cells);
            sbyte c = cells[cell];
            cells[cell] = c == MoonBoard.New ? MoonBoard.Full : c == MoonBoard.Full ? MoonBoard.Empty : MoonBoard.New;
            board.SetCells(cells);
            shown[cell] = cells[cell];
            SnapVisual(cell);
            PlaySe(clipTick);
        }

        void RunCreator()
        {
            if (busy) return;
            if (board.IsSolved())
            {
                creatorMessage = "全マスが新月です。満月か空きを置いてください";
                creatorMessageUntil = Time.time + 6f;
                return;
            }
            board.CopyCellsTo(initial);
            history.Clear();
            editMode = false;
            awaitOpponent = false;
            creatorMessage = board.HasMove(MoonBoard.New)
                ? "実行中: あなたが打ったあと、相手(満月)の手も自分で置きます"
                : "最初に打てる場所がありません";
            creatorMessageUntil = Time.time + 8f;
            PlaySe(clipTick);
        }

        void BackToEdit()
        {
            if (busy) return;
            hintCell = -1;
            awaitOpponent = false;
            editMode = true;
            history.Clear();
            board.SetCells(initial);
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                shown[i] = board[i];
                SnapVisual(i);
            }
            creatorMessage = "設置モード: マスをクリックで 新月 → 満月 → なし";
            creatorMessageUntil = Time.time + 6f;
            PlaySe(clipTick);
        }

        // 作った盤面を 8 行 x 8 文字(Stages.txt の書き方)にして、クリップボードと Console に出す
        void CopyCreatorBoard()
        {
            var cells = new sbyte[MoonBoard.Count];
            if (editMode) board.CopyCellsTo(cells);
            else System.Array.Copy(initial, cells, MoonBoard.Count);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                sb.Append(cells[i] == MoonBoard.New ? 'B' : cells[i] == MoonBoard.Full ? 'W' : '.');
                if (i % MoonBoard.Size == MoonBoard.Size - 1) sb.Append('\n');
            }
            string text = sb.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log("[星空リバーシ] 作ったステージ(Stages.txt に貼れます):\n" + text);
            creatorMessage = "盤面をコピーしました(Stages.txt に貼れます)";
            creatorMessageUntil = Time.time + 6f;
        }

        IEnumerator DoManualOpponent(int cell)
        {
            busy = true;
            board.Play(cell, MoonBoard.Full);
            var target = new sbyte[MoonBoard.Count];
            board.CopyCellsTo(target);
            yield return AnimateToState(target, cell);

            // あなたに打てる所が無ければ、相手がもう一度打つ
            awaitOpponent = !board.HasMove(MoonBoard.New) && board.HasMove(MoonBoard.Full);
            busy = false;
        }

        void DrawCreator(float vw)
        {
            string head = editMode ? "ステージ作成: 設置モード" : "ステージ作成: 実行モード";
            SmallText(new Rect(24f, 24f, 900f, 50f), head);
            if (Time.time < creatorMessageUntil && creatorMessage.Length > 0)
            {
                SmallText(new Rect(24f, 74f, 1300f, 50f), creatorMessage);
            }
            if (editMode)
            {
                if (ui.Button(ExecRect(vw), "実行", 44f)) RunCreator();
            }
            else
            {
                if (ui.Button(ExecRect(vw), "編集に戻る", 32f)) BackToEdit();
            }
            if (ui.Button(CopyRect(vw), "盤面をコピー", 32f)) CopyCreatorBoard();
        }

        // DevMode: いまの盤面から勝てる最短の一手を探して光らせる
        void ShowHint()
        {
            if (!DevMode.Active || mode != Mode.Play || busy || creator) return;
            int move, depth, wins;
            bool truncated;
            if (board.IsSolved() || !board.HasMove(MoonBoard.New))
            {
                hintCell = -1;
                hintMessage = "打てる場所がありません(戻るかやり直してください)";
            }
            else if (DevMode.FindHint(board, out move, out depth, out wins, out truncated))
            {
                hintCell = move;
                hintMessage = "ヒント: " + DevMode.CellName(move) + "(相手がどう打っても、あと最大 " + depth + " 手 / 勝てる手 " + wins + " 通り)";
            }
            else
            {
                hintCell = -1;
                hintMessage = truncated ? "探索が大きすぎて判定できませんでした" : "この盤面からは解けません(戻るかやり直してください)";
            }
            hintMessageUntil = Time.time + 8f;
            PlaySe(clipTick);
        }

        void DrawTitle(float vw)
        {
            Rect view = StellarView.Pixels;
            float s = view.height / 1080f;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(0f, -1.7f, 0f));
            float y = (view.yMax - sp.y) / s;

            ui.Label(new Rect(0f, y - 80f, vw, 160f), "星空リバーシ", 104f, TextAlignmentOptions.Center,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, titleAlpha));

            float pulse = 0.45f + 0.35f * Mathf.Sin(Time.time * 2f);
            ui.Label(new Rect(0f, y + 120f, vw, 60f), "クリックして はじめる", 34f, TextAlignmentOptions.Center,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, titleAlpha * pulse));
        }

        void DrawSettings(float vw, float vh)
        {
            if (rankingOpen)
            {
                DrawRankingPanel(vw, vh);
                return;
            }
            FillRect(new Rect(0f, 0f, vw, vh), new Color(0.02f, 0.02f, 0.1f, 0.72f));

            bool dev = DevMode.Available;
            bool devOn = DevMode.Active;

            // 左の列: ふつうの設定。デベロッパーモードが ON のときは、右にもう 1 列(開発用の項目)を足す
            bool inTitle = mode == Mode.Title;
            float colW = 800f;
            float leftH = 275f + (dev && !devOn ? 85f : 0f) + 340f + (RankingUnlocked ? 85f : 0f) + (inTitle ? 0f : 120f + 100f) + 50f;
            float devH = 75f + 5f * 90f + 3f * 85f + 50f;
            float ph = devOn ? Mathf.Max(leftH, devH) : leftH;
            float pw = devOn ? colW + 760f : colW;
            var panel = new Rect((vw - pw) * 0.5f, (vh - ph) * 0.5f, pw, ph);
            FillRect(panel, new Color(Background.r, Background.g, Background.b, 0.98f));
            Color edge = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
            FillRect(new Rect(panel.x, panel.y, pw, 3f), edge);
            FillRect(new Rect(panel.x, panel.yMax - 3f, pw, 3f), edge);
            FillRect(new Rect(panel.x, panel.y, 3f, ph), edge);
            FillRect(new Rect(panel.xMax - 3f, panel.y, 3f, ph), edge);

            float x0 = panel.x + 70f;
            float sliderX = x0 + 320f;
            float sliderW = panel.x + colW - 70f - sliderX;
            float y = panel.y;

            LabelText(new Rect(x0, y + 75f, 310f, 64f), "BGM");
            bgmLevel = Slider(new Rect(sliderX, y + 95f, sliderW, 24f), bgmLevel);

            LabelText(new Rect(x0, y + 165f, 310f, 64f), "SE");
            seLevel = Slider(new Rect(sliderX, y + 185f, sliderW, 24f), seLevel);
            if (ui.SliderReleased)
            {
                PlaySe(chimes[3], 0.8f);   // 音量の確かめ
            }

            float cursor = 255f;
            if (dev && devOn)
            {
                // 右の列: DevMode だけの項目
                FillRect(new Rect(panel.x + colW, y + 30f, 3f, ph - 60f), edge);
                float dx = panel.x + colW + 40f;
                float dsx = dx + 320f;
                float dw = panel.xMax - 60f - dsx;
                float bw2 = panel.xMax - 60f - dx;
                float dc = 75f;
                LabelText(new Rect(dx, y + dc, 310f, 64f), "背景の明るさ");
                DevMode.BackgroundLevel = Slider(new Rect(dsx, y + dc + 20f, dw, 24f), DevMode.BackgroundLevel);
                dc += 90f;
                LabelText(new Rect(dx, y + dc, 310f, 64f), "月の輝度");
                DevMode.MoonLevel = Slider(new Rect(dsx, y + dc + 20f, dw, 24f), DevMode.MoonLevel);
                dc += 90f;
                LabelText(new Rect(dx, y + dc, 310f, 64f), "クリア演出 ×" + DevMode.WinSpeed.ToString("0.00"));
                DevMode.WinSpeedLevel = Slider(new Rect(dsx, y + dc + 20f, dw, 24f), DevMode.WinSpeedLevel);
                dc += 90f;
                LabelText(new Rect(dx, y + dc, 310f, 64f), "タイトルの動き ×" + DevMode.TitleMove.ToString("0.0"));
                DevMode.TitleMoveLevel = Slider(new Rect(dsx, y + dc + 20f, dw, 24f), DevMode.TitleMoveLevel);
                dc += 90f;
                LabelText(new Rect(dx, y + dc, 310f, 64f), "タイトルの速さ ×" + DevMode.TitleSpeed.ToString("0.0"));
                DevMode.TitleSpeedLevel = Slider(new Rect(dsx, y + dc + 20f, dw, 24f), DevMode.TitleSpeedLevel);
                dc += 90f;

                if (ui.Button(new Rect(dx, y + dc, bw2, 70f), "デベロッパーモード: ON", 32f))
                {
                    DevMode.Active = false;
                    DevMode.Save();
                    return;
                }
                dc += 85f;
                if (ui.Button(new Rect(dx, y + dc, bw2, 70f), "ステージを検証(結果は Console に出ます)", 32f))
                {
                    devRunner.StartCoroutine(DevMode.CheckStages());
                }
                dc += 85f;
                if (ui.Button(new Rect(dx, y + dc, bw2, 70f), "ステージを作る", 32f))
                {
                    CloseSettings();
                    EnterCreator();
                    return;
                }
            }
            else if (dev)
            {
                if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), "デベロッパーモード: OFF", 32f))
                {
                    DevMode.Active = true;
                    DevMode.Save();
                    return;
                }
                cursor += 85f;
            }

            if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), Loc.T("フルスクリーン: ", "Fullscreen: ") + OnOff(FullscreenNow), 34f))
            {
                ToggleFullscreen();
                PlaySe(clipTick);
            }
            cursor += 85f;
            string hintLabel = Loc.T("ヒント: ", "Hints: ") + OnOff(hintsOn) +
                               (hintsOn ? Loc.T("(今日あと" + HintsLeft() + "回)", " (" + HintsLeft() + " left today)") : "");
            if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), hintLabel, 34f))
            {
                SetHintsOn(!hintsOn);
                PlaySe(clipTick);
            }
            cursor += 85f;
            if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), Loc.T("言語 / Language: 日本語", "言語 / Language: English"), 34f))
            {
                Loc.Toggle();
                PlaySe(clipTick);
            }
            cursor += 85f;
            if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), Loc.T("クレジット", "Credits"), 34f))
            {
                CloseSettings();
                if (inTitle)
                {
                    creditsFromTitle = true;
                    creditsOn = true;
                    creditsStart = Time.time;
                }
                else
                {
                    ShowCredits();
                }
                return;
            }
            cursor += 85f;
            if (RankingUnlocked)
            {
                if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 70f), Loc.T("ランキング", "Ranking"), 34f))
                {
                    PlaySe(clipTick);
                    OpenRanking();
                    return;
                }
                cursor += 85f;
            }

            cursor += 20f;
            if (!inTitle)
            {
                if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 100f), Loc.T("ステージセレクト", "Stage Select"), 44f))
                {
                    CloseSettings();
                    if (mode != Mode.Select) ShowSelect();
                    return;
                }
                cursor += 120f;
                if (ui.Button(new Rect(x0, y + cursor, colW - 140f, 100f), Loc.T("タイトルへ戻る", "Back to Title"), 44f))
                {
                    CloseSettings();
                    ShowTitle();
                    return;
                }
            }
            if (ui.Button(new Rect(panel.xMax - 90f, panel.y + 12f, 78f, 78f), "×", 52f))
            {
                CloseSettings();
            }
        }

        // スライダー(ゆっくり光る点がつまみ)。0..1。
        float Slider(Rect r, float value)
        {
            return ui.Slider(r, value, Gold, PaleYellow);
        }

        // ------------------------------------------------------------------
        // ステージ選択
        // ------------------------------------------------------------------

        // k はノード番号(決まった面のあとに、ランダム面が続く)。ランダム面は「まだ」以外なら、結果が出ている
        bool IsCleared(int k)
        {
            if (k < Stages.FixedCount) return clearedSet.Contains(k);
            return k < NodeCount && RN(k).state != StateNew;
        }

        // 最初の面か、ひとつ前の面をクリアしていれば遊べる(ランダム面は、現れた時点で遊べる)。
        bool IsUnlocked(int k)
        {
            if (k >= Stages.FixedCount) return true;
            return k == 0 || IsCleared(k - 1);
        }

        // 行ける面か(DevMode が ON のときは、クリアしていない面にも行ける)
        bool CanPlay(int k)
        {
            if (k < 0 || k >= NodeCount) return false;
            if (IsRandomNode(k) && RN(k).waitReveal) return false;   // 割れる演出が終わるまで、次の月は現れない
            return IsUnlocked(k) || DevMode.Active;
        }

        void LoadCleared()
        {
            clearedSet.Clear();
            foreach (string part in PlayerPrefs.GetString(ClearedKey, "").Split(','))
            {
                int v;
                if (int.TryParse(part, out v) && v >= 0) clearedSet.Add(v);
            }
        }

        void SaveCleared()
        {
            PlayerPrefs.SetString(ClearedKey, string.Join(",", clearedSet));
            PlayerPrefs.Save();
        }

        void ShowSelect()
        {
            StopAllCoroutines();
            HideCutin();
            introOn = false;
            creditsOn = false;
            creditsFromTitle = false;
            sideUntil = 0f;
            bgBoil = false;
            winSkippable = false;
            creator = false;   // DevMode
            editMode = false;
            awaitOpponent = false;
            busy = false;
            settingsOpen = false;
            ClearParticles();
            mode = Mode.Select;
            winZoom = false;
            clearBg = 0f;
            girlGrow = 0f;
            boardZoom = 1f;
            dragging = false;
            selStage = StellarArt.MoonsReady ? FirstToPlay() : selStage;
            selShown = selStage;
            LayoutSelect();
            selectRoot.gameObject.SetActive(true);
            boardRoot.gameObject.SetActive(false);
            clearMoon.gameObject.SetActive(false);
            selectAlpha = 0f;
            StartCoroutine(Lerp(0f, 1f, 0.9f, v => selectAlpha = v));
            StartCoroutine(Lerp(skyProgress, 0f, 1.6f, v => skyProgress = v));   // 星空は静かに消えていく
        }

        void HandleSelectClick(Vector2 world)
        {
            if (selectAlpha < 0.5f) return;
            float radius = stageSpacing * 0.45f;
            for (int k = 0; k < stagePos.Length; k++)
            {
                if (!CanPlay(k)) continue;   // まだ現れていない面は押せない
                float dx = world.x - stagePos[k].x, dy = world.y - stagePos[k].y;
                if (dx * dx + dy * dy < radius * radius)
                {
                    StartStage(k);
                    return;
                }
            }
        }

        void UpdateSelectVisuals()
        {
            if (!selectRoot.gameObject.activeSelf) return;
            if (mode != Mode.Select) return;
            if (StellarArt.MoonsReady) return;   // 友達の絵のレイアウトは UpdateSelectArt

            float t = Time.time;
            bool art = StellarArt.MoonsReady;
            for (int k = 0; k < stageIcons.Length; k++)
            {
                SpriteRenderer sr = stageIcons[k];
                if (!CanPlay(k))
                {
                    sr.enabled = false;           // まだ: 何もなし
                    continue;
                }

                sr.enabled = true;
                sr.transform.localPosition = stagePos[k];
                float s = stageScale;
                float a;
                if (IsCleared(k))
                {
                    sr.sprite = art ? StellarArt.Full() : MoonArt.Moon(PhaseFrame(k));          // クリア済み: 濃い黄色(絵では、満ちた月)
                    a = 1f;
                }
                else if (IsUnlocked(k))
                {
                    sr.sprite = art ? StellarArt.New() : MoonArt.MoonPale(PhaseFrame(k));      // 新しい面: 薄い黄色(絵では、輪だけの月)。ゆっくり呼吸する
                    a = 0.8f + 0.2f * Mathf.Sin(t * 2f);
                    s *= 1f + 0.04f * Mathf.Sin(t * 2f);
                }
                else
                {
                    sr.sprite = art ? StellarArt.New() : MoonArt.Moon(PhaseFrame(k));          // DevMode だけ: まだ行けないはずの面を、うっすら見せる
                    a = 0.3f;
                }
                Color tint = IsLunatic(k) ? LunaticTint : Color.white;
                sr.color = new Color(tint.r, tint.g, tint.b, a * selectAlpha);
                sr.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        void StartStage(int k)
        {
            if (k < 0 || k >= NodeCount) return;
            if (IsRandomNode(k) && RN(k).waitReveal) return;

            StopAllCoroutines();
            HideCutin();
            introOn = false;
            creditsOn = false;
            bgBoil = false;
            winSkippable = false;
            creator = false;   // DevMode
            editMode = false;
            awaitOpponent = false;
            curNode = k;
            levelCleared = false;
            stage = NodeStage(k);
            sideUntil = 0f;
            winZoom = false;
            clearBg = 0f;
            girlGrow = 0f;
            boardZoom = PlayZoom();
            board.Load(k < Stages.FixedCount ? Stages.Data[k] : RandomBoard(RN(k)));
            board.CopyCellsTo(initial);
            history.Clear();
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                shown[i] = board[i];
                SnapVisual(i);
            }

            busy = false;
            hintCell = -1;   // DevMode
            hintMessage = "";
            settingsOpen = false;
            hudAlpha = 1f;
            clearMoonAlpha = 0f;
            clearMoon.gameObject.SetActive(false);
            ClearParticles();
            StartCoroutine(Lerp(skyProgress, 0f, 1.2f, v => skyProgress = v));   // 星空があれば、静かに消える

            mode = Mode.Play;
            selectRoot.gameObject.SetActive(false);
            boardRoot.gameObject.SetActive(true);
            selectAlpha = 0f;
            boardAlpha = 0f;
            StartCoroutine(Lerp(0f, 1f, 0.9f, v => boardAlpha = v));
            PlaySe(clipTick);
            OnEnterNode();
            if (!creator)
            {
                if (IsLunatic(stage)) ShowLunaticCutin();   // LUNATIC の面は、カットインで知らせる
                else ShowRuleCutins();                      // 16 面の前: ルールの振り返り / 26 面の前: エキシビションマッチの説明(それぞれ 1 回だけ)
            }
        }

        // ------------------------------------------------------------------
        // プレイ中の入力
        // ------------------------------------------------------------------

        void HandlePlayInput(bool pressed, Vector2 world, bool undoKey, bool resetKey, bool backKey)
        {
            if (backKey)
            {
                ShowSelect();
                PlaySe(clipTick);
                return;
            }
            if (busy || boardAlpha < 0.5f) return;

            if (creator && editMode)
            {
                HandleEditInput(pressed, world);   // DevMode
                return;
            }

            if (undoKey) Undo();
            if (resetKey) ResetStage();
            if (!pressed) return;

            for (int n = 0; n < (hintsOn ? 4 : 3); n++)
            {
                Vector3 p = HudPosition(n);
                float dx = world.x - p.x, dy = world.y - p.y;
                if (dx * dx + dy * dy < HudHitRadius * HudHitRadius)
                {
                    hudPulse[n] = 1f;
                    PlaySe(clipTick);
                    if (n == 0) ShowSelect();
                    else if (n == 1) Undo();
                    else if (n == 2) ResetStage();
                    else UseHint();
                    return;
                }
            }

            int cell = WorldToCell(world);
            if (creator && awaitOpponent)
            {
                // DevMode: 相手(満月)の手を自分で置く
                if (cell >= 0 && board.IsLegal(cell, MoonBoard.Full)) StartCoroutine(DoManualOpponent(cell));
                return;
            }
            if (cell >= 0 && board.IsLegal(cell, MoonBoard.New)) StartCoroutine(DoMove(cell));
        }

        int WorldToCell(Vector2 w)
        {
            w /= boardZoom;   // 盤は boardZoom 倍に拡大して置いてある
            int col = Mathf.FloorToInt(w.x + 4f);
            int row = Mathf.FloorToInt(4f - (w.y - BoardCenterY));
            if (col < 0 || col >= MoonBoard.Size || row < 0 || row >= MoonBoard.Size) return -1;
            return row * MoonBoard.Size + col;
        }

        /// <summary>
        /// あなたが打つ → 全て新月ならクリア → そうでなければ相手(満月)が打つ。
        /// あなたに打てる所が無ければ、相手がもう一度打つ(パス)。どちらも打てなければ、何も起きずそのまま。
        /// </summary>
        IEnumerator DoMove(int cell)
        {
            busy = true;
            hintCell = -1;   // DevMode

            var entry = new HistoryEntry { move = cell };
            board.CopyCellsTo(entry.cells);       // 打つ前の盤面を覚えておく(戻すときは相手の手もまとめて戻る)
            history.Add(entry);
            MarkDirty();

            board.Play(cell, MoonBoard.New);
            if (!creator && board.IsSolved())
            {
                RecordNodeClear();   // 打った瞬間にクリアとして記録する(矢印が押せるようになるのは、演出が始まってから)
            }
            var target = new sbyte[MoonBoard.Count];
            board.CopyCellsTo(target);
            yield return AnimateToState(target, cell);

            if (board.IsSolved())
            {
                if (creator)
                {
                    // DevMode: ステージ作成の実行中は、クリア演出を出さず、解けたことだけ知らせる
                    creatorMessage = "全マス新月にできました(この手順で解けます)";
                    creatorMessageUntil = Time.time + 8f;
                    busy = false;
                    yield break;
                }
                bgBoil = true;   // 最後の一枚が埋まった瞬間から、背景が動き出す
                yield return WinSequence(cell);
                yield break;
            }

            if (creator)
            {
                // DevMode: 実行モードでは、相手(満月)の手も自分で置く
                awaitOpponent = board.HasMove(MoonBoard.Full);
                busy = false;
                yield break;
            }

            while (board.HasMove(MoonBoard.Full))
            {
                yield return new WaitForSeconds(0.45f);

                int oc = board.ChooseOpponentMove();
                board.Play(oc, MoonBoard.Full);
                board.CopyCellsTo(target);
                yield return AnimateToState(target, oc);

                if (board.HasMove(MoonBoard.New)) break;   // あなたの番
                // あなたに打てる所が無いので、相手がもう一度打つ
            }

            busy = false;   // 行き詰まっても、何も表示しない。残った満月はそのまま。
        }

        void Undo()
        {
            if (busy || history.Count == 0) return;
            ChargeLife();
            StartCoroutine(DoUndo());
        }

        IEnumerator DoUndo()
        {
            busy = true;
            hintCell = -1;   // DevMode
            awaitOpponent = false;
            HistoryEntry entry = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            if (history.Count == 0) ClearDirty();
            board.SetCells(entry.cells);
            yield return AnimateToState(entry.cells, entry.move);
            busy = false;
        }

        void ResetStage()
        {
            if (busy || history.Count == 0) return;
            ChargeLife();
            StartCoroutine(DoReset());
        }

        IEnumerator DoReset()
        {
            busy = true;
            hintCell = -1;   // DevMode
            awaitOpponent = false;
            history.Clear();
            ClearDirty();
            board.SetCells(initial);
            StartCoroutine(SpinIcon(2, -360f, 0.7f));
            yield return AnimateToState(initial, 27);
            busy = false;
        }

        IEnumerator SpinIcon(int n, float degrees, float duration)
        {
            Transform t = hudIcons[n].transform;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                float k = EaseInOut(time / duration);
                t.localRotation = Quaternion.Euler(0f, 0f, degrees * k);
                yield return null;
            }
            t.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------
        // 盤面のアニメーション(月の満ち欠け)
        // ------------------------------------------------------------------

        void SnapVisual(int i)
        {
            frame[i] = shown[i] == MoonBoard.Full ? MoonArt.FullFrame : 0f;
            scale[i] = shown[i] == MoonBoard.Empty ? 0f : 1f;
            alpha[i] = shown[i] == MoonBoard.Empty ? 0f : 1f;
        }

        static float Dist(int i, int ox, int oy)
        {
            float dx = i % MoonBoard.Size - ox, dy = i / MoonBoard.Size - oy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float EaseInOut(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        static float EaseOut(float k)
        {
            k = 1f - Mathf.Clamp01(k);
            return 1f - k * k * k;
        }

        /// <summary>
        /// 画面の状態を target に合わせる。置いた場所から波紋のように、
        /// 満月は欠けて新月に、新月は満ちて満月に変わり、石は現れ、消える。
        /// </summary>
        IEnumerator AnimateToState(sbyte[] target, int origin)
        {
            int ox = origin % MoonBoard.Size, oy = origin / MoonBoard.Size;
            var changes = new List<int>();
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                if (shown[i] != target[i]) changes.Add(i);
            }
            changes.Sort((a, b) => Dist(a, ox, oy).CompareTo(Dist(b, ox, oy)));

            float end = 0f;
            int rank = 0;
            bool placeSound = false, vanishSound = false;
            foreach (int i in changes)
            {
                sbyte from = shown[i], to = target[i];
                float delay, duration;
                if (from == MoonBoard.Empty)
                {
                    delay = 0f;
                    duration = 0.35f;
                    if (!placeSound) { PlaySe(clipPlace); placeSound = true; }
                }
                else if (to == MoonBoard.Empty)
                {
                    delay = 0.03f * Dist(i, ox, oy);
                    duration = 0.3f;
                    if (!vanishSound) { PlaySe(clipTick, 0.8f); vanishSound = true; }
                }
                else
                {
                    delay = 0.16f + 0.075f * Dist(i, ox, oy);
                    duration = 0.6f;
                }
                if (delay + duration > end) end = delay + duration;
                StartCoroutine(TweenCell(i, from, to, delay, duration, rank));
                if (from != MoonBoard.Empty && to != MoonBoard.Empty) rank++;
            }

            yield return new WaitForSeconds(end + 0.05f);

            for (int i = 0; i < MoonBoard.Count; i++)
            {
                shown[i] = target[i];
                SnapVisual(i);
            }
        }

        IEnumerator TweenCell(int i, sbyte from, sbyte to, float delay, float duration, int rank)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            if (from == MoonBoard.Empty)
            {
                // 現れる
                frame[i] = to == MoonBoard.Full ? MoonArt.FullFrame : 0f;
                for (float time = 0f; time < duration; time += Time.deltaTime)
                {
                    float k = EaseOut(time / duration);
                    scale[i] = Mathf.Lerp(0.3f, 1f, k);
                    alpha[i] = k;
                    yield return null;
                }
            }
            else if (to == MoonBoard.Empty)
            {
                // 消える
                for (float time = 0f; time < duration; time += Time.deltaTime)
                {
                    float k = EaseInOut(time / duration);
                    scale[i] = Mathf.Lerp(1f, 0.4f, k);
                    alpha[i] = 1f - k;
                    yield return null;
                }
            }
            else
            {
                // 満月 → 新月は欠けていき(28 → 56)、新月 → 満月は満ちていく(0 → 28)
                bool waning = to == MoonBoard.New;
                // 新月になるときは音階が上っていき、満月に戻されるときは下っていく
                int ci = waning ? Mathf.Min(rank, chimes.Length - 1) : Mathf.Max(0, 4 - rank);
                PlaySe(chimes[ci], 0.55f);
                float a = waning ? MoonArt.FullFrame : 0f;
                float b = waning ? MoonArt.Frames - 1 : MoonArt.FullFrame;
                for (float time = 0f; time < duration; time += Time.deltaTime)
                {
                    float k = EaseInOut(time / duration);
                    frame[i] = Mathf.Lerp(a, b, k);
                    scale[i] = 1f + 0.07f * Mathf.Sin(Mathf.PI * k);
                    yield return null;
                }
            }

            FinishCell(i, to);
        }

        // アニメーションの最後の姿に揃える(新月は 0 番目、満月は 28 番目の絵)。
        void FinishCell(int i, sbyte to)
        {
            frame[i] = to == MoonBoard.Full ? MoonArt.FullFrame : 0f;
            scale[i] = to == MoonBoard.Empty ? 0f : 1f;
            alpha[i] = to == MoonBoard.Empty ? 0f : 1f;
        }

        // ------------------------------------------------------------------
        // 全消し: 星のようにスパークして消え、満天の星空が現れる
        // ------------------------------------------------------------------

        IEnumerator WinSequence(int origin)
        {
            // DevMode: 見た目の時間だけを縮める。音は倍速にせず、鳴らすタイミングが早まるだけ
            float sp = winSpeed = DevMode.WinSpeed;

            yield return new WaitForSeconds(0.5f / sp);

            PlaySe(clipSwell, 0.9f);
            winSkippable = true;   // ここから、タップで演出を飛ばせる
            StartCoroutine(EnableArrowsAfter(ArrowReadyDelay / sp));
            StartCoroutine(Lerp(0f, 1f, 7f / sp, v => skyProgress = v));
            // 順番: 月が消え始める → 画面が引く → 引き終わる → 月が消え終わる → 背景が game から clear に変わる
            winZoom = true;
            StartCoroutine(ZoomOutAfter(0.3f / sp, 1.6f / sp));
            StartCoroutine(Lerp(1f, 0f, 1.2f / sp, v => hudAlpha = v));

            int ox = origin % MoonBoard.Size, oy = origin / MoonBoard.Size;
            var order = new List<int>();
            for (int i = 0; i < MoonBoard.Count; i++) order.Add(i);
            order.Sort((a2, b2) => Dist(a2, ox, oy).CompareTo(Dist(b2, ox, oy)));

            const float step = 0.03f;
            for (int r = 0; r < order.Count; r++)
            {
                StartCoroutine(SparkleOut(order[r], (step * r + Random.Range(0f, 0.08f)) / sp, r));
            }
            yield return new WaitForSeconds((step * order.Count + 1.2f) / sp);

            PlaySe(clipSparkle, 0.8f);
            StartCoroutine(Lerp(0f, 1f, 2.5f / sp, v => clearBg = v));   // 月が消え終わってから、背景が clear へゆっくり変わる

            // 静かなクリア画面: 星空と、このステージの月だけ
            if (!StellarArt.GameReady)
            {
                clearMoon.sprite = MoonArt.Moon(PhaseFrame(stage));
                clearMoon.gameObject.SetActive(true);
                clearMoonAlpha = 0f;
                StartCoroutine(Lerp(0f, 1f, 4f / sp, v => clearMoonAlpha = v));
            }
            clearReadyTime = Time.time + 3f / sp;
            winSkippable = false;
            mode = Mode.Clear;
            busy = false;
        }

        // タップで演出を飛ばして、月明りに照らされた女の子の画面にする(次のタップでステージ選択)
        // 演出が始まって(音が鳴り始めて)から、次の面への矢印を押せるようにするまでの時間(秒)。調整用
        const float ArrowReadyDelay = 0.35f;

        IEnumerator EnableArrowsAfter(float d)
        {
            yield return new WaitForSeconds(d);
            levelCleared = true;
        }

        void SkipWin()
        {
            levelCleared = true;
            winSkippable = false;
            StopAllCoroutines();
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                scale[i] = 0f;
                alpha[i] = 0f;
            }
            ClearParticles();
            winZoom = true;
            boardZoom = 1f;
            girlGrow = 1f;
            clearBg = 1f;
            skyProgress = 1f;
            hudAlpha = 0f;
            if (!StellarArt.GameReady)
            {
                clearMoon.sprite = MoonArt.Moon(PhaseFrame(stage));
                clearMoon.gameObject.SetActive(true);
                clearMoonAlpha = 1f;
            }
            PlaySe(clipSparkle, 0.8f);
            clearReadyTime = Time.time + 0.3f;
            mode = Mode.Clear;
            busy = false;
        }

        // 月が消え始めてから delay 秒後に、duration 秒かけて盤を 1 倍まで引く
        IEnumerator ZoomOutAfter(float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            float from = boardZoom;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                boardZoom = Mathf.Lerp(from, 1f, EaseInOut(time / duration));
                girlGrow = EaseInOut(time / duration);   // 引きながら、女の子も大きくなる
                yield return null;
            }
            boardZoom = 1f;
            girlGrow = 1f;
        }

        IEnumerator SparkleOut(int i, float delay, int rank)
        {
            yield return new WaitForSeconds(delay);

            if (rank % 4 == 0) PlaySe(chimes[(rank / 4) % chimes.Length], 0.45f);
            Burst(CellPosition(i) * boardZoom, 6);

            float grow = 0.16f / winSpeed, vanish = 0.45f / winSpeed;
            for (float time = 0f; time < grow; time += Time.deltaTime)
            {
                scale[i] = Mathf.Lerp(1f, 1.35f, time / grow);
                yield return null;
            }
            for (float time = 0f; time < vanish; time += Time.deltaTime)
            {
                float k = EaseInOut(time / vanish);
                scale[i] = Mathf.Lerp(1.35f, 0f, k);
                alpha[i] = 1f - k;
                yield return null;
            }
            scale[i] = 0f;
            alpha[i] = 0f;
        }

        void Burst(Vector3 pos, int count)
        {
            for (int c = 0; c < count; c++)
            {
                int slot = -1;
                for (int i = 0; i < ParticleCount; i++)
                {
                    if (!particles[i].alive) { slot = i; break; }
                }
                if (slot < 0) return;

                float angle = Random.value * Mathf.PI * 2f;
                float speed = Random.Range(0.8f, 2.2f);
                particles[slot] = new Particle
                {
                    alive = true,
                    pos = pos,
                    vel = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
                    age = 0f,
                    life = Random.Range(0.7f, 1.4f),
                    size = Random.Range(0.18f, 0.38f),
                    rot = Random.Range(0f, 90f),
                    spin = Random.Range(-120f, 120f),
                };
            }
        }

        void ClearParticles()
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                particles[i].alive = false;
                particleSr[i].enabled = false;
            }
        }

        // ------------------------------------------------------------------
        // 毎フレームの描画の更新
        // ------------------------------------------------------------------

        void UpdateBoardVisuals()
        {
            if (!boardRoot.gameObject.activeSelf) return;
            float t = Time.time;

            // ズーム: 盤を拡大し、アイコンの列は大きさと位置を保つ
            if (mode == Mode.Play && !winZoom) boardZoom = PlayZoom();
            boardRoot.localScale = new Vector3(boardZoom, boardZoom, 1f);
            hudRoot.localScale = new Vector3(1f / boardZoom, 1f / boardZoom, 1f);
            for (int n = 0; n < 4; n++) hudIcons[n].transform.localPosition = HudPosition(n);

            for (int i = 0; i < MoonBoard.Count; i++)
            {
                SpriteRenderer sr = discs[i];
                float a = alpha[i] * boardAlpha;
                float s = scale[i];
                if (a < 0.002f || s < 0.002f)
                {
                    sr.enabled = false;
                }
                else
                {
                    sr.enabled = true;
                    sr.sprite = MoonArt.Moon(Mathf.RoundToInt(frame[i]));
                    Color tint = IsLunatic(stage) && !creator ? LunaticTint : Color.white;
                    sr.color = new Color(tint.r, tint.g, tint.b, a);
                    sr.transform.localScale = new Vector3(DiscSize * s, DiscSize * s, 1f);
                }

                // 空きマスのしるし。置ける場所は、ゆっくり脈打つ。
                float ha = 0f;
                bool editing = creator && editMode;
                sbyte side = awaitOpponent ? MoonBoard.Full : MoonBoard.New;
                if (mode == Mode.Play && !busy && !editing && shown[i] == MoonBoard.Empty)
                {
                    ha = board.IsLegal(i, side) ? 0.38f + 0.22f * Mathf.Sin(t * 2.2f + i * 0.7f) : 0.12f;
                }
                float hs = 0.55f;
                if (i == hintCell && mode == Mode.Play && !busy)
                {
                    // DevMode: ヒントのマスは、大きく明るく脈打つ
                    ha = 0.85f + 0.15f * Mathf.Sin(t * 5f);
                    hs = 0.8f + 0.1f * Mathf.Sin(t * 5f);
                }
                ha *= boardAlpha;
                hints[i].transform.localScale = new Vector3(hs, hs, 1f);
                hints[i].enabled = ha > 0.003f;
                Color hc = awaitOpponent ? new Color(0.78f, 0.86f, 1f, 1f) : Accent;   // 相手の手を置く番は、青白い点
                hints[i].color = new Color(hc.r, hc.g, hc.b, ha);
            }

            // 下のアイコン。戻す・やり直すは、できないときは薄くなる。
            for (int n = 0; n < 4; n++)
            {
                float factor = 1f;
                if (n == 0) factor = 0.75f;
                else if (n == 3)
                {
                    hudIcons[3].enabled = hintsOn && mode == Mode.Play && !creator;
                    factor = ((HintsLeft() > 0 || DevMode.Active) && !busy) ? 0.9f : 0.28f;
                }
                else if (history.Count == 0 || busy) factor = 0.28f;
                hudPulse[n] = Mathf.MoveTowards(hudPulse[n], 0f, Time.deltaTime * 4f);
                float s = 0.85f * (1f + 0.2f * hudPulse[n]);
                hudIcons[n].color = new Color(Accent.r, Accent.g, Accent.b, 0.9f * factor * hudAlpha * boardAlpha);
                hudIcons[n].transform.localScale = new Vector3(s, s, 1f);
            }
        }

        void UpdateSky()
        {
            if (StellarArt.GameReady)
            {
                // 友達の絵の背景に星空が描かれているので、コードの星は出さない
                if (skyVisible)
                {
                    for (int i = 0; i < StarCount; i++) stars[i].sr.enabled = false;
                    skyVisible = false;
                }
                return;
            }
            if (skyProgress <= 0.0005f)
            {
                if (skyVisible)
                {
                    for (int i = 0; i < StarCount; i++) stars[i].sr.enabled = false;
                    skyVisible = false;
                }
                return;
            }

            skyVisible = true;
            float t = Time.time;
            for (int i = 0; i < StarCount; i++)
            {
                Star st = stars[i];
                // 暗い星から順に、徐々に現れる。そのあとは瞬く。
                float reveal = Mathf.Clamp01(skyProgress * 1.6f - st.threshold);
                float a = reveal * st.alpha * (0.55f + 0.45f * Mathf.Sin(t * st.speed + st.phase));
                st.sr.enabled = a > 0.003f;
                st.sr.color = new Color(st.tint.r, st.tint.g, st.tint.b, a);
            }
        }

        void UpdateClearMoon()
        {
            if (!clearMoon.gameObject.activeSelf) return;
            float s = 2.6f * (1f + 0.015f * Mathf.Sin(Time.time * 0.8f));
            clearMoon.transform.localScale = new Vector3(s, s, 1f);
            clearMoon.color = new Color(1f, 1f, 1f, 0.95f * clearMoonAlpha);
        }

        void UpdateParticles()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < ParticleCount; i++)
            {
                if (!particles[i].alive) continue;

                Particle p = particles[i];
                p.age += dt;
                if (p.age >= p.life)
                {
                    particles[i].alive = false;
                    particleSr[i].enabled = false;
                    continue;
                }
                p.pos += p.vel * dt;
                p.vel *= 0.96f;
                p.rot += p.spin * dt;
                particles[i] = p;

                float k = p.age / p.life;
                float size = p.size * (1f - 0.5f * k);
                SpriteRenderer sr = particleSr[i];
                sr.enabled = true;
                sr.transform.position = p.pos;
                sr.transform.localScale = new Vector3(size, size, 1f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, p.rot);
                sr.color = new Color(1f, 0.95f, 0.78f, 1f - k);
            }
        }

        void UpdateShootingStar()
        {
            if (mode == Mode.Clear && skyProgress > 0.7f && shootingAge < 0f)
            {
                shootingTimer -= Time.deltaTime;
                if (shootingTimer <= 0f)
                {
                    shootingTimer = Random.Range(3.5f, 7f);
                    shootingAge = 0f;
                    shootingLife = 0.9f;
                    shootingStart = new Vector3(Random.Range(-9f, 2f), Random.Range(3f, 6.5f), 0f);
                    shootingDir = new Vector3(0.85f, -0.5f, 0f).normalized;
                    shootingStar.transform.localRotation =
                        Quaternion.Euler(0f, 0f, Mathf.Atan2(shootingDir.y, shootingDir.x) * Mathf.Rad2Deg);
                    shootingStar.transform.localScale = new Vector3(1.6f, 0.05f, 1f);
                }
            }

            if (shootingAge >= 0f)
            {
                shootingAge += Time.deltaTime;
                float k = shootingAge / shootingLife;
                if (k >= 1f)
                {
                    shootingAge = -1f;
                    shootingStar.enabled = false;
                    return;
                }
                shootingStar.enabled = true;
                shootingStar.transform.position = shootingStart + shootingDir * (9f * shootingAge);
                shootingStar.color = new Color(1f, 0.96f, 0.82f, 0.9f * Mathf.Sin(Mathf.PI * k));
            }
        }

        // ------------------------------------------------------------------
        // 友達の絵(StellarArt.cs)。絵が無いときは、何も作らず、これまでの見た目のまま。
        // ------------------------------------------------------------------

        // 絵ごとに専用の材質を持たせる(材質を共有すると、別の絵が同じに見えることがあった)
        SpriteRenderer ArtSr(string objName, int order, string spriteName)
        {
            Sprite sp = StellarArt.Get(spriteName);
            if (sp == null) return null;
            SpriteRenderer sr = MakeSr(objName, artRoot, order, sp, mat != null ? new Material(mat) : null);
            sr.enabled = false;
            return sr;
        }

        void BuildArt()
        {
            artRoot = MakeRoot("Art", transform);
            bgGame = ArtSr("BgGame", -20, "game1");
            girlBlue = ArtSr("GirlBlue", -17, "girl_blue1");
            girlLit = ArtSr("GirlLit", -16, "girl_lit1");
            bgClear = ArtSr("BgClear", -19, "clear1");
            bgTitle = ArtSr("BgTitle", -18, "title_background1");
            bgIntro = ArtSr("BgIntro", -15, "intro");
            titleAnim = gameObject.AddComponent<TitleAnimator>();
            titleAnim.Init(mat, artRoot);
            btnContinue = ArtSr("BtnContinue", 2, "title_continue");
            btnRestart = ArtSr("BtnRestart", 2, "title_restart");
            stageHeader = ArtSr("StageHeader", 2, "stageselect_stage1");
            hand = ArtSr("Hand", 4, "stageselect_hand");
            for (int i = 0; i < digitSr.Length; i++) digitSr[i] = ArtSr("Digit" + i, 2, "font_0");
        }

        // 画面いっぱいの背景(2 枚を交互に出して、手描きのゆらぎにする)
        void ShowBackground(SpriteRenderer sr, string a, string b, float alpha, float zoom = 1f, bool still = false)
        {
            if (sr == null) return;
            if (alpha <= 0.003f)
            {
                sr.enabled = false;
                return;
            }
            sr.enabled = true;
            Sprite sp = StellarArt.Alternate(a, b, still);
            sr.sprite = sp;
            float c = StellarArt.Cover(cam) * zoom;
            Vector2 f = StellarArt.Fix(sp.name);
            sr.transform.position = Vector3.zero;
            sr.transform.localScale = new Vector3(c * f.x, c * f.y, 1f);
            float v = Mathf.Clamp01(DevMode.BackgroundScale);   // DevMode: 背景の明るさ(暗くする方向だけ)
            sr.color = new Color(v, v, v, alpha);
        }

        // 基準の画面で (cx, cy) が中心の位置に、文字などの絵を置く
        void ShowLayer(SpriteRenderer sr, Sprite sp, float cx, float cy, float alpha, float extraScale = 1f)
        {
            if (sr == null) return;
            if (sp == null || alpha <= 0.003f)
            {
                sr.enabled = false;
                return;
            }
            sr.enabled = true;
            sr.sprite = sp;
            float fit = StellarArt.Fit(cam) * extraScale;
            Vector2 f = StellarArt.Fix(sp.name);
            sr.transform.position = StellarArt.FromCenter(cam, cx, cy);
            sr.transform.localScale = new Vector3(fit * f.x, fit * f.y, 1f);
            sr.color = new Color(1f, 1f, 1f, alpha);
        }

        // 5 の倍数の面は LUNATIC(難しい面)。月が赤くなる
        static bool IsLunatic(int k) { return k >= 0 && (k + 1) % 5 == 0; }
        static readonly Color LunaticTint = new Color(1f, 0.52f, 0.88f, 1f);   // 黄色の月に掛けて、うすい赤(サーモンピンク)にする

        bool HasProgress { get { return clearedSet.Count > 0; } }

        void UpdateArt()
        {
            if (artRoot == null) return;

            // 背景
            bool credits = mode == Mode.Title && creditsOn;
            bool title = mode == Mode.Title && !introOn && !creditsOn;
            bool inGame = mode == Mode.Play || mode == Mode.Clear;
            bool layered = titleAnim != null && TitleAnimator.Ready;
            ShowBackground(bgTitle, "title_background1", "title_background2", (title || credits) && !layered ? titleAlpha : 0f);
            if (titleAnim != null)
            {
                bool introShown = mode == Mode.Title && introOn && layered;
                if (introShown) titleAnim.ApplyStill(cam, Mathf.Clamp01((Time.time - introStart) / 0.8f), IntroDim, IntroFocus, IntroZoom);   // 目をつむった静止画を、暗くして
                else titleAnim.Apply(cam, (title || credits) && layered ? titleAlpha : 0f, !credits, credits ? CreditsDim : 1f);
            }
            ShowBackground(bgGame, "game1", "game2", inGame ? boardAlpha : 0f, boardZoom, !bgBoil);
            // クリアすると、木に月が実った絵がゆっくり現れる(コードの星空のかわり)
            ShowBackground(bgClear, "clear1", "clear2", inGame ? clearBg * boardAlpha : 0f, boardZoom, !bgBoil);

            UpdateGirl(inGame);
            ShowBackground(bgIntro, "intro", "intro", mode == Mode.Title && introOn && !layered ? Mathf.Clamp01((Time.time - introStart) / 0.8f) : 0f, 1f, true);

            // タイトルの「つづきから」「はじめから」: カーソルが乗ると、少し小さくなる
            Vector2 cursor = CursorWorld();
            for (int n = 0; n < 2; n++)
            {
                SpriteRenderer sr = n == 0 ? btnContinue : btnRestart;
                string nm = n == 0 ? "title_continue" : "title_restart";
                if (sr == null) continue;
                if (!title)
                {
                    sr.enabled = false;
                    titleHover[n] = 0f;
                    continue;
                }
                bool usable = n == 1 || HasProgress;
                if (!usable)
                {
                    sr.enabled = false;   // まだ遊んでいないときは、「つづきから」を出さない
                    titleHover[n] = 0f;
                    continue;
                }
                bool over = !confirmReset && HitTitleButton(nm, cursor);
                titleHover[n] = Mathf.MoveTowards(titleHover[n], over ? 1f : 0f, Time.deltaTime * 8f);
                if (Loc.En)
                {
                    sr.enabled = false;   // 英語のときは、絵(日本語)のかわりに文字を重ねる
                    continue;
                }
                Vector2 c = StellarArt.Center(nm);
                float a = titleAlpha * (usable ? 1f : 0.3f);
                ShowLayer(sr, StellarArt.Get(nm), c.x, c.y, a, 1f - 0.06f * titleHover[n]);
            }

            UpdateSelectArt();
        }

        // ポインタ(マウス・タッチ)のワールドの位置
        Vector2 CursorWorld()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null) return new Vector2(9999f, 9999f);
            Vector2 p = pointer.position.ReadValue();
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -cam.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        // 遊べる面のうち、いちばん先のもの
        int MaxPlayable()
        {
            int m = 0;
            for (int k = 0; k < NodeCount; k++) if (CanPlay(k)) m = k;
            return m;
        }

        // 最初に選ばれている月: まだクリアしていない、いちばん若い面(全部クリア済みなら最後の面)
        int FirstToPlay()
        {
            // 割れた/修復されたばかりの月があれば、まずそこを見せる(演出のため)
            for (int n = Stages.FixedCount; n < NodeCount; n++)
            {
                if (RN(n).crackAnim || RN(n).repairAnim) return n;
            }
            for (int k = 0; k < NodeCount; k++)
            {
                if (IsUnlocked(k) && !IsCleared(k) && CanPlay(k)) return k;
            }
            return Mathf.Max(0, NodeCount - 1);
        }

        // 月の道の上の位置。u = 0 が手のつまむ場所、+1 が右上の次の月、-1 が左下の前の月
        Vector3 SlotWorld(float u)
        {
            u = Mathf.Clamp(u, -2.6f, 2.6f);
            // 点は -2..+2 の 5 つ。両端は、となりの点から延ばしてなめらかにつなぐ(Catmull-Rom)
            int i = Mathf.Clamp(Mathf.FloorToInt(u) + 2, 0, 3);
            float t = u - (i - 2);
            Vector2 p1 = SlotPoints[i], p2 = SlotPoints[i + 1];
            Vector2 p0 = i > 0 ? SlotPoints[i - 1] : 2f * p1 - p2;
            Vector2 p3 = i < 3 ? SlotPoints[i + 2] : 2f * p2 - p1;
            float t2 = t * t, t3 = t2 * t;
            Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            return StellarArt.FromCenter(cam, q.x, q.y);
        }

        void StepStage(int d)
        {
            int next = Mathf.Clamp(selStage + d, 0, MaxPlayable());
            if (next == selStage) return;
            selStage = next;
            PlaySe(clipTick, 0.7f);
        }

        // ステージ選択の操作: 月を道に沿ってスワイプ(右上へ = 前、左下へ = 次)、右上を押す = 次、左下を押す = 前
        // つまんでいる月を押すと始まり、ほかの月を押すと、その月がつままれる。キーは ←↓ が前、→↑ が次、Enter で始まる
        void HandleSelectInput(Vector2 world)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) StepStage(1);
                if (kb.leftArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) StepStage(-1);
                if ((kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) && CanPlay(selStage)) { StartStage(selStage); return; }
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && selectAlpha > 0.5f)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel < -0.1f) StepStage(1);
                else if (wheel > 0.1f) StepStage(-1);
            }

            Pointer pointer = Pointer.current;
            if (pointer == null || selectAlpha < 0.5f) return;

            if (pointer.press.wasPressedThisFrame)
            {
                dragging = true;
                dragMoved = false;
                dragProgress = 0f;
                dragStart = CursorWorld();
            }
            if (!dragging) return;

            Vector3 here = SlotWorld(0f), nextPos = SlotWorld(1f);
            Vector2 axis = new Vector2(here.x - nextPos.x, here.y - nextPos.y);   // 次の月が手へ向かう向き(左下)
            float len = axis.magnitude;
            axis /= Mathf.Max(0.001f, len);
            Vector2 delta = CursorWorld() - dragStart;
            if (delta.magnitude > 0.35f) dragMoved = true;
            dragProgress = dragMoved ? Mathf.Clamp(Vector2.Dot(delta, axis) / len, -1f, 1f) : 0f;

            if (pointer.press.wasReleasedThisFrame)
            {
                dragging = false;
                if (dragMoved)
                {
                    if (dragProgress > 0.2f) StepStage(1);
                    else if (dragProgress < -0.2f) StepStage(-1);
                    dragProgress = 0f;
                    return;
                }

                // 動かさずに離した = 押した
                float radius = 1.5f * SlotMoonScale * StellarArt.Fit(cam);   // 当たり判定: 月の直径の 2 倍
                int hit = -1;
                float best = radius * radius;
                for (int k = 0; k < NodeCount; k++)
                {
                    if (!CanPlay(k) || Mathf.Abs(NodeU(k, selShown)) > 2.3f) continue;
                    Vector3 sp = NodeWorld(k, selShown);
                    float dx = dragStart.x - sp.x, dy = dragStart.y - sp.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best)
                    {
                        best = d2;
                        hit = k;
                    }
                }
                if (hit == selStage)
                {
                    StartStage(hit);
                }
                else if (hit >= 0)
                {
                    selStage = hit;
                    PlaySe(clipTick, 0.7f);
                }
                else if (dragStart.x > 0f && (dragStart.y > 0f || (dragStart.y < 0f && NextIsRetry()))) StepStage(1);       // 右上(割れた月のあとは右下)を押す = 次
                else if (dragStart.x < 0f && dragStart.y < 0f) StepStage(-1);      // 左下を押す = 前
            }
        }

        // 女の子: 背景とは別の層。プレイ中は小さな青い姿で、クリアで引くときに大きくなり、そのあと月明かりつきの絵へ溶けていく
        void UpdateGirl(bool inGame)
        {
            if (girlBlue == null || girlLit == null) return;
            if (!inGame)
            {
                girlBlue.enabled = false;
                girlLit.enabled = false;
                return;
            }
            float c = StellarArt.Cover(cam) * boardZoom;   // 背景と同じ拡大
            // クリアの演出が始まったら、女の子が月より手前に来る(プレイ中は盤のうしろ)
            girlBlue.sortingOrder = winZoom ? 9 : -17;
            girlLit.sortingOrder = winZoom ? 10 : -16;
            PlaceGirl(girlBlue, StellarArt.Alternate("girl_blue1", "girl_blue2", !bgBoil), c, boardAlpha);
            PlaceGirl(girlLit, StellarArt.Alternate("girl_lit1", "girl_lit2", !bgBoil), c, clearBg * boardAlpha);
        }

        void PlaceGirl(SpriteRenderer sr, Sprite sp, float c, float alpha)
        {
            if (sp == null || alpha <= 0.003f)
            {
                sr.enabled = false;
                return;
            }
            Vector2 center = StellarArt.Center(sp.name);
            float q = Mathf.Lerp(GirlQ0, 1f, girlGrow);
            // 横には動かさない(大きくなるときに、右へずれて見えないように)。縦だけ、プレイ中の姿の高さから
            Vector2 small = new Vector2(center.x, GirlQ0 * center.y + GirlD0.y);
            Vector2 cur = Vector2.Lerp(small, center, girlGrow);
            Vector2 f = StellarArt.Fix(sp.name);
            sr.enabled = true;
            sr.sprite = sp;
            sr.transform.position = new Vector3((cur.x - StellarArt.DesignWidth * 0.5f) * c / StellarArt.PixelsPerUnit,
                                                (StellarArt.DesignHeight * 0.5f - cur.y) * c / StellarArt.PixelsPerUnit, 0f);
            sr.transform.localScale = new Vector3(q * c * f.x, q * c * f.y, 1f);
            float v = Mathf.Clamp01(DevMode.BackgroundScale);
            sr.color = new Color(v, v, v, alpha);
        }

        // ステージ選択: 「ステージ」と数字、動かない手、手の前を通る月
        void UpdateSelectArt()
        {
            bool on = mode == Mode.Select && selectRoot.gameObject.activeSelf;
            if (!on)
            {
                if (stageHeader != null) stageHeader.enabled = false;
                if (hand != null) hand.enabled = false;
                for (int i = 0; i < digitSr.Length; i++) if (digitSr[i] != null) digitSr[i].enabled = false;
                dashOn = false;
                HideAllFx();
                return;
            }

            int maxK = MaxPlayable();
            selStage = Mathf.Clamp(selStage, 0, maxK);
            selShown = Mathf.Lerp(selShown, selStage, 1f - Mathf.Exp(-10f * Time.deltaTime));
            float pos = selShown + (dragging && dragMoved ? dragProgress : 0f);
            pos = Mathf.Clamp(pos, -0.5f, maxK + 0.5f);

            // 月
            float t = Time.time;
            float fit = StellarArt.Fit(cam);
            for (int k = 0; k < stageIcons.Length; k++)
            {
                SpriteRenderer sr = stageIcons[k];
                float u = NodeU(k, pos);
                float vis = Mathf.Clamp01((2.6f - Mathf.Abs(u)) / 0.6f);
                if (!CanPlay(k) || vis <= 0.003f)
                {
                    sr.enabled = false;
                    HideFx(k);
                    continue;
                }
                float s = SlotMoonScale * fit * (1f + 0.05f * Mathf.Clamp01(1f - Mathf.Abs(u)));
                Color tint = IsLunatic(NodeStage(k)) ? LunaticTint : Color.white;
                if (IsRandomNode(k))
                {
                    // 50 面以降: 新月・クリア・割れた月・修復された月。割れた月のあとの月は、右下に出る
                    float reveal = RN(k).attempt > 1 ? Mathf.Clamp01((Time.time - RN(k).revealStart) / 0.8f) : 1f;
                    DrawRandomNode(k, NodeWorld(k, pos), s, vis * selectAlpha * reveal, tint);
                    continue;
                }
                sr.enabled = true;
                sr.transform.position = NodeWorld(k, pos);
                sr.transform.localScale = new Vector3(s, s, 1f);
                float a;
                if (IsCleared(k))
                {
                    sr.sprite = StellarArt.Full();         // クリア済み: 満ちた月
                    a = 1f;
                }
                else if (IsUnlocked(k))
                {
                    sr.sprite = StellarArt.New();          // 次に遊ぶ面: 輪だけの月(ゆっくり呼吸する)
                    a = 0.85f + 0.15f * Mathf.Sin(t * 2f);
                }
                else
                {
                    sr.sprite = StellarArt.New();          // DevMode だけ: まだ行けないはずの面
                    a = 0.35f;
                }
                sr.color = new Color(tint.r, tint.g, tint.b, a * vis * selectAlpha);
            }

            // 「ステージ」と数字(50 面以降は、52-1 のような番号)。英語のときは、「Stage」の文字を重ねる(LateUpdate)
            if (stageHeader != null)
            {
                Sprite sp = StellarArt.Alternate("stageselect_stage1", "stageselect_stage2");
                Vector2 c = StellarArt.Center(sp.name);
                ShowLayer(stageHeader, sp, c.x, c.y, Loc.En ? 0f : selectAlpha);
            }
            dashOn = false;
            string num = NodeLabel(Mathf.RoundToInt(pos));
            float left = StellarArt.DesignRect("font_0").x;
            int di = 0;
            foreach (char ch in num)
            {
                if (ch == '-')
                {
                    dashRect = new Rect(left + 10f, 67f + 40f - 5f, 34f, 10f);
                    dashOn = true;
                    dashAlpha = selectAlpha;
                    left += 54f;
                    continue;
                }
                if (di >= digitSr.Length) break;
                string nm = "font_" + (ch - '0');
                Rect r = StellarArt.DesignRect(nm);
                ShowLayer(digitSr[di], StellarArt.Get(nm), left + r.width * 0.5f, r.y + r.height * 0.5f, selectAlpha);
                left += r.width + 6f;
                di++;
            }
            for (; di < digitSr.Length; di++)
            {
                if (digitSr[di] != null) digitSr[di].enabled = false;
            }

            // 手は動かない。絵の見本のとおりの場所で、月をつまんでいる
            if (hand != null)
            {
                Vector2 hc = StellarArt.Center("stageselect_hand");
                ShowLayer(hand, StellarArt.Get("stageselect_hand"), hc.x, hc.y, selectAlpha);
            }
        }

        // タイトル: 「はじめから」は 1 面から、「つづきから」は(クリアした面があれば)ステージ選択へ
        void HandleTitleClick(Vector2 world)
        {
            if (HitTitleButton("title_restart", world))
            {
                PlaySe(clipTick);
                if (HasProgress)
                {
                    // 今までの記録が消えるので、確認のバナーを出す
                    confirmReset = true;
                    settingsOpenedFrame = Time.frameCount;   // 押した同じフレームの押下を、バナーのボタンに通さない
                }
                else
                {
                    StartIntro();
                }
            }
            else if (HasProgress && HitTitleButton("title_continue", world))
            {
                PlaySe(clipTick);
                ShowSelect();
            }
        }

        // 「はじめから」: 今までの記録(クリアした面)をすべて消して、1 面目から
        void ResetProgressAndStart()
        {
            clearedSet.Clear();
            SaveCleared();
            ResetNodes();
            confirmReset = false;
            StartIntro();
        }

        // 15 面をクリアしたあと: タイトルの絵(ロゴ抜き・少し暗く)の上に、黄色い文字でクレジット
        void ShowCredits()
        {
            ShowTitle();
            creditsOn = true;
            creditsStart = Time.time;
        }

        static readonly string[] CreditLines =
        {
            "ディレクター：林凌平",
            "デザイナー：ぶりけ(@sugiru_yaoyoros)",
            "ステージデザイン：Barusu",
            "協力：ドパミン研究所",
            "制作：ヘイルズ（hails_game）",
            "Thank you for playing!",
        };

        static readonly string[] CreditLinesEn =
        {
            "Director: Ryohei Hayashi (林凌平)",
            "Designer: ぶりけ (@sugiru_yaoyoros)",
            "Stage Design: Barusu",
            "Special Thanks: ドパミン研究所",
            "Produced by: Hails (hails_game)",
            "Thank you for playing!",
        };

        void DrawCredits(float vw, float vh)
        {
            float t = Time.time - creditsStart;
            string[] lines = Loc.En ? CreditLinesEn : CreditLines;
            float x = vw * 0.5f, w = vw * 0.5f - 60f;
            float y = vh * 0.2f;
            for (int i = 0; i < lines.Length; i++)
            {
                bool last = i == lines.Length - 1;
                float a = Mathf.Clamp01((t - 1.0f - i * 0.9f) / 0.9f);
                if (last) y += 60f;
                float size = last ? 58f : 46f;
                ui.Label(new Rect(x, y, w, 100f), lines[i], size, TextAlignmentOptions.Left, new Color(ArtYellow.r, ArtYellow.g, ArtYellow.b, a));
                y += 110f;
            }
        }

        void StartIntro()
        {
            introOn = true;
            introStart = Time.time;
            introStep = 0;
            introStepStart = Time.time;
        }

        const float IntroFadeIn = 0.8f, IntroFadeOut = 0.4f;

        // 今の文の、出ている濃さ(delay 秒待ってから、ゆっくり出る)
        float IntroTextAlpha(float delay)
        {
            return Mathf.Clamp01((Time.time - introStepStart - delay) / IntroFadeIn);
        }

        // 導入: 目をつむった女の子(絵 "intro")を背景に、右側にひとりごとを順に出す
        void DrawIntro(float vw, float vh)
        {
            float t = Time.time - introStart;
            float fade = Mathf.Clamp01(t / 0.8f);
            if (!StellarArt.Has("intro") && !TitleAnimator.Ready) FillRect(new Rect(0f, 0f, vw, vh), new Color(0.02f, 0.02f, 0.08f, fade));

            // 映画のように、上と下を黒く切る
            float bar = Mathf.Max(0f, (vh - vw / IntroBarRatio) * 0.5f);
            FillRect(new Rect(0f, 0f, vw, bar), new Color(0f, 0f, 0f, fade));
            FillRect(new Rect(0f, vh - bar, vw, bar), new Color(0f, 0f, 0f, fade));

            float x = vw * 0.63f, w = vw * 0.37f - 50f;
            float a1 = introStep == 0 ? IntroTextAlpha(1.2f) : 1f - Mathf.Clamp01((Time.time - introStepStart) / IntroFadeOut);
            float a2 = introStep == 0 ? 0f : IntroTextAlpha(0.6f);
            Color c = PaleYellow;
            float h = 220f, y = (vh - h) * 0.5f;
            if (a1 > 0.003f) ui.Label(new Rect(x, y, w, h), Loc.T("月...まぶしいな...", "Moon... so bright..."), IntroTextSize, TextAlignmentOptions.Left, new Color(c.r, c.g, c.b, a1));
            if (a2 > 0.003f) ui.Label(new Rect(x, y, w, h), Loc.T("月もオセロみたいに\n消えたらいいのにな...", "I wish the moon could vanish\nlike Othello pieces do..."), IntroTextSize, TextAlignmentOptions.Left, new Color(c.r, c.g, c.b, a2));
        }

        // 確認のバナー
        void DrawResetConfirm(float vw, float vh)
        {
            FillRect(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.7f));
            float pw = 1000f, ph = 420f;
            var panel = new Rect((vw - pw) * 0.5f, (vh - ph) * 0.5f, pw, ph);
            FillRect(panel, new Color(0f, 0f, 0f, 0.96f));
            Color edge = new Color(ArtYellow.r, ArtYellow.g, ArtYellow.b, 0.8f);
            FillRect(new Rect(panel.x, panel.y, pw, 4f), edge);
            FillRect(new Rect(panel.x, panel.yMax - 4f, pw, 4f), edge);
            FillRect(new Rect(panel.x, panel.y, 4f, ph), edge);
            FillRect(new Rect(panel.xMax - 4f, panel.y, 4f, ph), edge);

            ui.Label(new Rect(panel.x, panel.y + 60f, pw, 150f), Loc.T("今までの内容がすべて消えますが\nよろしいですか?", "All your progress will be erased.\nAre you sure?"), 52f,
                     TextAlignmentOptions.Center, PaleYellow);
            if (ui.Button(new Rect(panel.x + 70f, panel.y + 250f, 420f, 100f), Loc.T("いいえ", "No"), 44f))
            {
                confirmReset = false;
                PlaySe(clipTick);
            }
            if (ui.Button(new Rect(panel.xMax - 490f, panel.y + 250f, 420f, 100f), Loc.T("はい(消して始める)", "Yes (start over)"), 40f))
            {
                PlaySe(clipTick);
                ResetProgressAndStart();
            }
        }

        bool HitTitleButton(string name, Vector2 world)
        {
            Vector2 c = StellarArt.Center(name);
            Vector3 p = StellarArt.FromCenter(cam, c.x, c.y);
            Rect r = StellarArt.DesignRect(name);
            float fit = StellarArt.Fit(cam) / StellarArt.PixelsPerUnit;
            float hw = r.width * 0.5f * fit + TitleButtonPad, hh = r.height * 0.5f * fit + TitleButtonPad;
            return Mathf.Abs(world.x - p.x) <= hw && Mathf.Abs(world.y - p.y) <= hh;
        }

        // ------------------------------------------------------------------
        // 道具
        // ------------------------------------------------------------------

        IEnumerator Lerp(float from, float to, float duration, System.Action<float> apply)
        {
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                apply(Mathf.Lerp(from, to, time / duration));
                yield return null;
            }
            apply(to);
        }
    }
}
