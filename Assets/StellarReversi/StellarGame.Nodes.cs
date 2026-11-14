using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

namespace StellarReversi
{
    /// <summary>
    /// 『星空リバーシ』のうち、次のものをまとめたファイル(StellarGame の続き)。
    ///   ・エキシビションマッチ(26 面以降のランダム面)と「ライフ」、割れた月・修復された月
    ///   ・ヒント(設定でオンオフ、1 日 3 回まで)
    ///   ・フルスクリーン、タイトル左下の「クレジット」「設定」、英語の文字
    ///
    /// ステージ選択に並ぶ月を「ノード」と呼ぶ。最初の Stages.FixedCount 個は、Stages.txt の決まった面。
    /// そのあとは、ランダム面のノードが、遊んだ結果にしたがって 1 つずつ増えていく。
    ///   ・ライフが残ったままクリア → その月は「クリア済み」。次の面(52 → 53)の月が、右上に現れる。
    ///   ・ライフが 0 になった → 月が「割れる」。同じ面の次の挑戦(52-1 → 52-2)の月が、右下に現れる。
    ///     割れた月は、好きなときにやり直せる。クリアすると「修復された月」になるが、次の面へは進めない。
    /// </summary>
    public sealed partial class StellarGame
    {
        // ------------------------------------------------------------------
        // ランダム面のノード
        // ------------------------------------------------------------------

        const int StateNew = 0, StateClear = 1, StateCracked = 2, StateRepaired = 3;
        const int MaxLives = 3;
        const string NodesKey = "Stellar.RandomNodes1";
        const string HintOnKey = "Stellar.HintOn";
        const string HintDayKey = "Stellar.HintDay";
        const string HintUsedKey = "Stellar.HintUsed";
        const string FullscreenKey = "Stellar.Fullscreen";
        const int HintsPerDay = 3;
        const float CrackSeconds = 1.9f, RepairSeconds = 1.6f;

        sealed class RNode
        {
            public int k;            // 面(0 始まり。52 面なら 51)
            public int attempt;      // 何回目の挑戦か(52-2 なら 2)
            public int pick;         // RandomStages の抽選結果(盤面)
            public int state;        // StateNew / StateClear / StateCracked / StateRepaired
            public int lives = MaxLives;
            public bool dirty;       // 打ちはじめたまま出ていった(次に入ると、やり直しと同じ扱い)
            // 以下は保存しない(見た目の進行)
            public bool crackAnim, repairAnim, crackFx;
            public float animT0 = -1f;
            public bool waitReveal;
            public float revealStart = -1000f;
        }

        sealed class NodeFx
        {
            public SpriteRenderer left, right, seam;
        }

        static readonly System.Random Rng = new System.Random();
        readonly List<RNode> rnodes = new List<RNode>();
        NodeFx[] fx = new NodeFx[0];
        int curNode;                    // いま遊んでいる月(ノード番号)
        bool lastClearRepaired;
        SpriteRenderer[] lifeSr = new SpriteRenderer[MaxLives];
        float lifePulse;

        bool hintsOn = true;
        bool fullscreenWanted;
        float fsToggleTime = -10f;
        bool creditsFromTitle;

        string sideJa = "", sideEn = "";
        float sideUntil;

        int NodeCount { get { return Stages.FixedCount + rnodes.Count; } }
        bool IsRandomNode(int n) { return n >= Stages.FixedCount && n < NodeCount; }
        RNode RN(int n) { return rnodes[n - Stages.FixedCount]; }
        int NodeStage(int n) { return n < Stages.FixedCount ? n : RN(n).k; }

        int NodeState(int n)
        {
            if (n < Stages.FixedCount) return clearedSet.Contains(n) ? StateClear : StateNew;
            return RN(n).state;
        }

        string NodeLabel(int n)
        {
            n = Mathf.Clamp(n, 0, Mathf.Max(0, NodeCount - 1));
            if (n < Stages.FixedCount) return (n + 1).ToString();
            RNode rn = RN(n);
            return (rn.k + 1) + "-" + rn.attempt;
        }

        // いま、ライフが働いている(はじめて挑戦中のランダム面)
        bool LivesActive
        {
            get
            {
                return mode == Mode.Play && !creator && IsRandomNode(curNode) && RN(curNode).state == StateNew;
            }
        }

        void LoadNodes()
        {
            rnodes.Clear();
            string saved = PlayerPrefs.GetString(NodesKey, "");
            if (saved.Length > 0)
            {
                foreach (string part in saved.Split(';'))
                {
                    string[] f = part.Split(',');
                    if (f.Length < 6) continue;
                    int k, a, p, s, l, d;
                    if (!int.TryParse(f[0], out k) || !int.TryParse(f[1], out a) || !int.TryParse(f[2], out p) ||
                        !int.TryParse(f[3], out s) || !int.TryParse(f[4], out l) || !int.TryParse(f[5], out d)) continue;
                    if (k < Stages.FixedCount || a < 1 || p < 0) continue;
                    // 並びが合わない保存(エキシビションマッチの始まりの面を変えたときの古いデータなど)は、そこから先を捨てる:
                    //   はじめの月は FixedCount の面、そのあとは「同じ面の次の挑戦」か「次の面」でなければならない
                    if (rnodes.Count == 0 ? k != Stages.FixedCount : !(k == rnodes[rnodes.Count - 1].k + 1 && a == 1 || k == rnodes[rnodes.Count - 1].k && a == rnodes[rnodes.Count - 1].attempt + 1)) break;
                    rnodes.Add(new RNode
                    {
                        k = k, attempt = a, pick = p,
                        state = Mathf.Clamp(s, StateNew, StateRepaired),
                        lives = Mathf.Clamp(l, 0, MaxLives),
                        dirty = d != 0,
                    });
                }
            }
            SyncNodes();
        }

        void SaveNodes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < rnodes.Count; i++)
            {
                RNode n = rnodes[i];
                if (i > 0) sb.Append(';');
                sb.Append(n.k).Append(',').Append(n.attempt).Append(',').Append(n.pick).Append(',')
                  .Append(n.state).Append(',').Append(n.lives).Append(',').Append(n.dirty ? 1 : 0);
            }
            PlayerPrefs.SetString(NodesKey, sb.ToString());
            PlayerPrefs.Save();
        }

        // 最後の月の結果に合わせて、次の月を足す(クリア → 次の面、割れた/修復 → 同じ面の次の挑戦)
        void SyncNodes()
        {
            if (Stages.FixedCount == 0) return;
            bool changed = false;
            if (rnodes.Count == 0)
            {
                if (!clearedSet.Contains(Stages.FixedCount - 1)) return;
                AddNode(Stages.FixedCount, 1, false);
                changed = true;
            }
            for (int guard = 0; guard < 8; guard++)
            {
                RNode last = rnodes[rnodes.Count - 1];
                if (last.state == StateClear) AddNode(last.k + 1, 1, false);
                else if (last.state == StateCracked || last.state == StateRepaired) AddNode(last.k, last.attempt + 1, last.crackAnim);
                else break;
                changed = true;
            }
            if (changed) SaveNodes();
            UpdateReached();
            EnsureNodeVisuals();
        }

        void AddNode(int k, int attempt, bool waitReveal)
        {
            bool lunatic = IsLunatic(k);
            var avoid = new HashSet<int>();
            for (int i = Mathf.Max(0, rnodes.Count - 40); i < rnodes.Count; i++)
            {
                if (IsLunatic(rnodes[i].k) == lunatic) avoid.Add(RandomStages.BaseIndex(rnodes[i].pick));
            }
            rnodes.Add(new RNode
            {
                k = k, attempt = attempt,
                pick = RandomStages.Choose(lunatic, Rng, avoid),
                waitReveal = waitReveal,
            });
        }

        string RandomBoard(RNode rn)
        {
            return RandomStages.Board(IsLunatic(rn.k), rn.pick);
        }

        // ステージ選択の月の絵を、ノードの数だけ用意する(ランダム面のノードには、割れた月・修復の飾りもつける)
        void EnsureNodeVisuals()
        {
            if (selectRoot == null) return;
            int n = NodeCount;
            int old = stageIcons.Length;
            if (old >= n && fx.Length >= n) return;
            System.Array.Resize(ref stageIcons, Mathf.Max(n, old));
            System.Array.Resize(ref fx, Mathf.Max(n, fx.Length));
            for (int k = old; k < n; k++)
            {
                SpriteRenderer sr = MakeSr("Stage" + k, selectRoot, 3, MoonArt.Moon(PhaseFrame(k)), moonMat);
                sr.enabled = false;
                stageIcons[k] = sr;
            }
            for (int k = Stages.FixedCount; k < n; k++)
            {
                if (fx[k] != null) continue;
                var f = new NodeFx
                {
                    left = MakeSr("CrackL" + k, selectRoot, 3, null, moonMat),
                    right = MakeSr("CrackR" + k, selectRoot, 3, null, moonMat),
                    seam = MakeSr("Seam" + k, selectRoot, 4, SeamSprite(), mat),
                };
                f.left.enabled = false;
                f.right.enabled = false;
                f.seam.enabled = false;
                fx[k] = f;
            }
        }

        // ------------------------------------------------------------------
        // ライフ
        // ------------------------------------------------------------------

        // 「ひとつ戻る」「やり直す」を押したとき。はじめて挑戦中のランダム面だけ、ライフが 1 減る
        void ChargeLife()
        {
            if (!LivesActive) return;
            RNode rn = RN(curNode);
            rn.lives = Mathf.Max(0, rn.lives - 1);
            lifePulse = 1f;
            if (rn.lives <= 0)
            {
                rn.state = StateCracked;
                rn.crackAnim = true;
                rn.dirty = false;
                OnExhibitionCrack();   // 連続記録は、ここで終わり
                SyncNodes();   // 同じ面の次の挑戦の月が、右下に現れる(割れる演出のあとで)
                SideMessage("月が割れてしまいました。\nこのままクリアしても、\n次の面へは進めません。\n(ステージセレクトで\n次の月が現れます)",
                            "The moon has cracked.\nEven if you clear it,\nyou can't advance.\n(The next moon will\nappear on the stage select.)", 10f);
            }
            SaveNodes();
        }

        // 月に入った。打ちかけで出ていた面は、やり直しと同じ(ライフが 1 減る)
        void OnEnterNode()
        {
            lastClearRepaired = false;
            if (!IsRandomNode(curNode)) return;
            RNode rn = RN(curNode);
            if (rn.state == StateNew && rn.dirty)
            {
                rn.dirty = false;
                ChargeLife();
                if (rn.state == StateNew)
                {
                    SideMessage("途中で出たので、\nやり直しとして\nライフが1つ減りました。", "You left midway,\nso a life was used\nas a restart.", 6f);
                }
            }
            SaveNodes();
        }

        // 1 手打った(はじめて挑戦中のランダム面は、打ちかけの印をつける)
        void MarkDirty()
        {
            if (!LivesActive) return;
            RNode rn = RN(curNode);
            if (rn.dirty) return;
            rn.dirty = true;
            SaveNodes();
        }

        void ClearDirty()
        {
            if (!LivesActive) return;
            RNode rn = RN(curNode);
            if (!rn.dirty) return;
            rn.dirty = false;
            SaveNodes();
        }

        // クリアしたことを記録する
        void RecordNodeClear()
        {
            if (!IsRandomNode(curNode))
            {
                clearedSet.Add(curNode);
                SaveCleared();
                if (curNode == Stages.FixedCount - 1) SyncNodes();   // 最後の手作りの面をクリア → エキシビションマッチの最初の面が現れる
                return;
            }
            RNode rn = RN(curNode);
            rn.dirty = false;
            if (rn.state == StateNew)
            {
                rn.state = StateClear;
                OnExhibitionClear();   // 割らずにクリア: 連続記録が 1 増える
            }
            else if (rn.state == StateCracked)
            {
                rn.state = StateRepaired;
                rn.repairAnim = true;
                lastClearRepaired = true;
            }
            else if (rn.state == StateRepaired)
            {
                lastClearRepaired = true;
            }
            SaveNodes();
            SyncNodes();
            UpdateReached();
        }

        // 記録をすべて消す(「はじめから」)
        void ResetNodes()
        {
            rnodes.Clear();
            PlayerPrefs.DeleteKey(NodesKey);
            streak = 0;   // 今の連続記録は消す(自己ベストと到達ステージは、記録として残す)
            SaveRankStats();
            PlayerPrefs.DeleteKey(SeenKeyPrefix + "recap");
            PlayerPrefs.DeleteKey(SeenKeyPrefix + "exh");
        }

        // 画面の右側(盤の横)に出す、ひとこと
        void SideMessage(string ja, string en, float seconds)
        {
            sideJa = ja;
            sideEn = en;
            sideUntil = Time.time + seconds;
        }

        void DrawSideMessage(float vw)
        {
            if (Time.time >= sideUntil) return;
            float unit = StellarUi.Height / (2f * cam.orthographicSize);
            float x = vw * 0.5f + 4f * boardZoom * unit + 40f;
            float width = vw - x - 20f;
            float cy = StellarUi.Height * 0.5f - BoardCenterY * unit;
            float y = cy - 150f;
            if (!IsRandomNode(curNode) && stage >= 0 && stage < TutorialTexts.Length) y = cy + 130f;   // 1〜5 面の説明と重ならないように
            float a = Mathf.Clamp01((sideUntil - Time.time) / 0.6f) * hudAlpha;
            ui.Label(new Rect(x, y, width, 300f), Loc.T(sideJa, sideEn), 32f, TextAlignmentOptions.Left,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, a));
        }

        void BuildLives()
        {
            for (int i = 0; i < MaxLives; i++)
            {
                lifeSr[i] = MakeSr("Life" + i, transform, 12, MoonArt.Moon(MoonArt.FullFrame), mat);
                lifeSr[i].enabled = false;
            }
        }

        Sprite LifeSprite(bool full)
        {
            if (StellarArt.MoonsReady) return full ? StellarArt.Full() : StellarArt.New();
            return MoonArt.Moon(full ? MoonArt.FullFrame : 0);
        }

        // ステージ番号の下に、残りのライフ(満月 = 残っている、輪 = 失った)
        void UpdateLives()
        {
            lifePulse = Mathf.MoveTowards(lifePulse, 0f, Time.deltaTime * 2.5f);
            bool show = mode == Mode.Play && !creator && IsRandomNode(curNode) && !settingsOpen;
            if (!show)
            {
                for (int i = 0; i < MaxLives; i++) lifeSr[i].enabled = false;
                return;
            }
            RNode rn = RN(curNode);
            if (rn.state == StateClear)
            {
                for (int i = 0; i < MaxLives; i++) lifeSr[i].enabled = false;   // クリア済みの面を遊びなおすときは、ライフなし
                return;
            }
            int lives = rn.state == StateNew ? rn.lives : 0;
            float unit = StellarUi.Height / (2f * cam.orthographicSize);
            float fit = StellarArt.Fit(cam);
            Vector3 lp = leftArrow.transform.position;
            float right = lp.x - 55f / unit - 0.18f * fit;
            float y = lp.y - 66f / unit;
            for (int i = 0; i < MaxLives; i++)
            {
                SpriteRenderer sr = lifeSr[i];
                bool alive = i < lives;
                sr.enabled = true;
                sr.sprite = LifeSprite(alive);
                float s = 0.4f * fit;
                if (!alive && i == lives) s *= 1f + 0.6f * lifePulse;   // 失ったばかりの月が、ふくらむ
                sr.transform.position = new Vector3(right - (MaxLives - 1 - i) * 0.5f * fit, y, 0f);
                sr.transform.localScale = new Vector3(s, s, 1f);
                float a = (alive ? 1f : 0.4f) * Mathf.Min(boardAlpha, 0.95f) * hudAlpha;
                sr.color = new Color(1f, 1f, 1f, a);
            }
        }

        // ------------------------------------------------------------------
        // ヒント(設定でオンオフ、1 日 3 回まで)
        // ------------------------------------------------------------------

        static string TodayKey()
        {
            return System.DateTime.Now.ToString("yyyyMMdd");
        }

        int HintsUsedToday()
        {
            if (PlayerPrefs.GetString(HintDayKey, "") != TodayKey()) return 0;
            return PlayerPrefs.GetInt(HintUsedKey, 0);
        }

        int HintsLeft()
        {
            return Mathf.Max(0, HintsPerDay - HintsUsedToday());
        }

        void ConsumeHint()
        {
            PlayerPrefs.SetString(HintDayKey, TodayKey());
            PlayerPrefs.SetInt(HintUsedKey, HintsUsedToday() + 1);
            PlayerPrefs.Save();
        }

        void SetHintsOn(bool on)
        {
            hintsOn = on;
            hintCell = -1;
            PlayerPrefs.SetInt(HintOnKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        // 盤の横のヒントのボタン(押すと、次に打つ場所が光る)
        void UseHint()
        {
            if (mode != Mode.Play || busy || creator || settingsOpen || !hintsOn || boardAlpha < 0.5f) return;
            if (!DevMode.Active && HintsLeft() <= 0)
            {
                SideMessage("今日のヒントは\nもう使い切りました。\n(明日、また3回)", "You've used all of\ntoday's hints.\n(3 more tomorrow)", 5f);
                return;
            }
            if (board.IsSolved() || !board.HasMove(MoonBoard.New))
            {
                SideMessage("打てる場所が\nありません。\n戻るか、やり直して\nください。", "No moves left.\nUndo or restart.", 5f);
                return;
            }
            int move, depth, wins;
            bool truncated;
            if (DevMode.FindHint(board, out move, out depth, out wins, out truncated))
            {
                hintCell = move;
                if (!DevMode.Active) ConsumeHint();
                SideMessage("ここに置いてみましょう。\n(今日のヒント あと" + HintsLeft() + "回)",
                            "Try placing it here.\n(Hints left today: " + HintsLeft() + ")", 8f);
            }
            else
            {
                hintCell = -1;
                if (truncated)
                {
                    SideMessage("ヒントを探しきれませんでした。", "Couldn't find a hint.", 5f);
                }
                else
                {
                    SideMessage("この盤面からは\n解けません。\n戻るか、やり直して\nください。", "This position can't\nbe solved.\nUndo or restart.", 6f);
                }
            }
            PlaySe(clipTick);
        }

        // ヒントのボタンの下に、今日の残りの回数
        void DrawHintCount(float vw)
        {
            if (mode != Mode.Play || creator || settingsOpen || !hintsOn) return;
            float unit = StellarUi.Height / (2f * cam.orthographicSize);
            Vector3 p = HudPosition(3);
            float sx = vw * 0.5f + p.x * unit, sy = StellarUi.Height * 0.5f - p.y * unit;
            ui.Label(new Rect(sx - 60f, sy + 38f, 120f, 40f), HintsLeft() + "/" + HintsPerDay, 28f, TextAlignmentOptions.Center,
                     new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, 0.8f * Mathf.Min(boardAlpha, 1f) * hudAlpha));
        }

        // ------------------------------------------------------------------
        // フルスクリーン
        // ------------------------------------------------------------------

        void ApplyFullscreenOnStart()
        {
#if !UNITY_WEBGL && !UNITY_EDITOR
            if (Application.isMobilePlatform) return;
            if (PlayerPrefs.HasKey(FullscreenKey)) SetFullscreen(PlayerPrefs.GetInt(FullscreenKey, 0) == 1, false);
#endif
        }

        bool FullscreenNow
        {
            get { return Time.time - fsToggleTime < 0.8f ? fullscreenWanted : Screen.fullScreen; }
        }

        void ToggleFullscreen()
        {
            SetFullscreen(!FullscreenNow, true);
        }

        void SetFullscreen(bool on, bool save)
        {
            fullscreenWanted = on;
            fsToggleTime = Time.time;
#if UNITY_WEBGL && !UNITY_EDITOR
            Screen.fullScreen = on;   // ブラウザーでは、ボタンを押したそのときにだけ切り替えられる
#else
            if (on)
            {
                Resolution r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else
            {
                Resolution r = Screen.currentResolution;
                int h = Mathf.Min(720, Mathf.RoundToInt(r.height * 0.85f));
                int w = Mathf.RoundToInt(h * 16f / 9f);
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
#endif
            if (save)
            {
                PlayerPrefs.SetInt(FullscreenKey, on ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        static string OnOff(bool on)
        {
            return on ? "ON" : "OFF";
        }

        // ------------------------------------------------------------------
        // タイトル左下の「設定」「クレジット」
        // ------------------------------------------------------------------

        static readonly Rect TitleSettingsRect = new Rect(40f, 1080f - 110f, 230f, 64f);
        static readonly Rect TitleCreditsRect = new Rect(290f, 1080f - 110f, 290f, 64f);

        bool TitleMenuVisible
        {
            get { return mode == Mode.Title && !introOn && !creditsOn && !confirmReset && !settingsOpen && titleAlpha > 0.6f; }
        }

        // 押した位置が、左下のボタンの上か(タイトルの絵のボタンや「どこを押しても進む」に、同時に反応しないため)
        bool PointerOverTitleMenu()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null) return false;
            Rect view = StellarView.Pixels;
            float scale = Mathf.Max(0.01f, view.height / StellarUi.Height);
            Vector2 p = pointer.position.ReadValue();
            Vector2 q = new Vector2((p.x - view.x) / scale, (view.yMax - p.y) / scale);
            return false;   // 左下のボタンは無い
        }

        // タイトルの左下のボタンは廃止した(設定は右上の歯車、クレジットは設定の中)
        void DrawTitleMenu()
        {
        }

        // 基準の画面(1920x1080)の長方形を、文字を描く仮想画面の長方形にする
        Rect DesignToUi(Rect r, float vw)
        {
            float unit = StellarUi.Height / (2f * cam.orthographicSize);
            float f = StellarArt.Fit(cam) / StellarArt.PixelsPerUnit * unit;
            Vector3 c = StellarArt.FromCenter(cam, r.x + r.width * 0.5f, r.y + r.height * 0.5f);
            float cx = vw * 0.5f + c.x * unit, cy = StellarUi.Height * 0.5f - c.y * unit;
            return new Rect(cx - r.width * f * 0.5f, cy - r.height * f * 0.5f, r.width * f, r.height * f);
        }

        // 英語のとき: 絵に書いてある日本語の文字(つづきから・はじめから・ステージ)のかわりに、同じ場所に文字を置く
        bool dashOn;
        Rect dashRect;
        float dashAlpha;

        // タイトルの英語の文字: 黄色い丘の上に載るので、背景と同じ青にする
        static readonly Color TitleBlue = new Color(74f / 255f, 112f / 255f, 168f / 255f, 1f);

        void DrawEnglishOverlays(float vw)
        {
            if (mode == Mode.Title && !introOn && !creditsOn && StellarArt.TitleReady && titleAlpha > 0.01f)
            {
                if (Loc.En)
                {
                    for (int n = 0; n < 2; n++)
                    {
                        if (n == 0 && !HasProgress) continue;
                        string nm = n == 0 ? "title_continue" : "title_restart";
                        float a = titleAlpha * (confirmReset ? 0.4f : 1f);
                        float size = 50f * (1f - 0.06f * titleHover[n]);
                        ui.Label(DesignToUi(StellarArt.DesignRect(nm), vw), n == 0 ? "Continue" : "New Game", size,
                                 TextAlignmentOptions.Center, new Color(TitleBlue.r, TitleBlue.g, TitleBlue.b, a));
                    }
                    // ロゴは日本語の絵なので、英語の題を、その下に添える
                    ui.Label(DesignToUi(new Rect(900f, 640f, 948f, 80f), vw), "Starry Sky Reversi", 58f, TextAlignmentOptions.Center,
                             new Color(TitleBlue.r, TitleBlue.g, TitleBlue.b, titleAlpha));
                }
            }

            if (mode == Mode.Select && !settingsOpen && StellarArt.MoonsReady)
            {
                if (Loc.En)
                {
                    Rect r = StellarArt.DesignRect("stageselect_stage1");
                    ui.Label(DesignToUi(r, vw), "Stage", 76f, TextAlignmentOptions.Center,
                             new Color(ArtYellow.r, ArtYellow.g, ArtYellow.b, selectAlpha));
                }
                if (dashOn)
                {
                    ui.Fill(DesignToUi(dashRect, vw), new Color(244f / 255f, 235f / 255f, 142f / 255f, dashAlpha));
                }
            }
        }

        // ------------------------------------------------------------------
        // ステージ選択のノード(割れた月・修復された月)
        // ------------------------------------------------------------------

        // 月の道の上の位置。割れた月のあとの月は、右下に置く
        // 割れた月のあとの月(52-2 など)は、割れた月と同じ「場所」を分け合って、その右下に出る。
        // その月を選ぶと、道ぜんたいが左上へずれて、その月が手の場所に来る(ほかの月との位置関係は変わらない)。
        // RetryOffsetDesign: 割れた月から見た、次の挑戦の月の位置(絵の座標で、右・下へのピクセル)。調整用
        static readonly Vector2 RetryOffsetDesign = new Vector2(330f, 190f);

        int RetryDepth(int n)
        {
            return n >= 0 && n < NodeCount && IsRandomNode(n) && RN(n).attempt > 1 ? RN(n).attempt - 1 : 0;
        }

        // 道の上の番号(割れた月とそのやり直しの月は、同じ番号になる)
        int SlotIndex(int n)
        {
            int idx = n;
            for (int i = Stages.FixedCount; i <= n && i < NodeCount; i++) if (RetryDepth(i) > 0) idx--;
            return idx;
        }

        Vector3 RetryOffsetWorld()
        {
            float f = StellarArt.Fit(cam) / StellarArt.PixelsPerUnit;
            return new Vector3(RetryOffsetDesign.x * f, -RetryOffsetDesign.y * f, 0f);
        }

        // 月 k の、道の上での位置(手の場所からの、道の番号の差)
        float NodeU(int k, float pos)
        {
            return SlotIndex(k) - LerpAt(pos, SlotIndex);
        }

        float LerpAt(float pos, System.Func<int, int> f)
        {
            int last = Mathf.Max(0, NodeCount - 1);
            pos = Mathf.Clamp(pos, 0f, last);
            int i0 = Mathf.FloorToInt(pos), i1 = Mathf.Min(last, i0 + 1);
            return Mathf.Lerp(f(i0), f(i1), pos - i0);
        }

        Vector3 NodeWorld(int k, float pos)
        {
            Vector3 p = SlotWorld(NodeU(k, pos));
            float focusDepth = LerpAt(pos, RetryDepth);
            return p + RetryOffsetWorld() * (RetryDepth(k) - focusDepth);
        }

        bool NextIsRetry()
        {
            int n = selStage + 1;
            return n < NodeCount && IsRandomNode(n) && RN(n).attempt > 1;
        }

        // 割れた月の絵(輪を半分に切って、少しずらす)。元の絵ごとに作っておく
        readonly Dictionary<string, Sprite[]> halves = new Dictionary<string, Sprite[]>();

        Sprite[] Halves(Sprite src)
        {
            Sprite[] h;
            if (halves.TryGetValue(src.name, out h)) return h;
            Rect r = src.rect;
            float half = Mathf.Floor(r.width * 0.5f);
            Sprite left = Sprite.Create(src.texture, new Rect(r.x, r.y, half, r.height), new Vector2(1f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            Sprite right = Sprite.Create(src.texture, new Rect(r.x + half, r.y, r.width - half, r.height), new Vector2(0f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            left.name = src.name + "_L";
            right.name = src.name + "_R";
            h = new[] { left, right };
            halves[src.name] = h;
            return h;
        }

        // 修復の継ぎ目(金色のひび)。月の円がぴったり 1 ユニットの正方形
        static Sprite seamSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSeam()
        {
            seamSprite = null;
        }

        // 修復された月でクリアしたとき、クリア画面に出すひとこと
        void DrawClearNote(float vw, float vh)
        {
            if (!lastClearRepaired || hudAlpha > 0.5f) return;
            float a = Mathf.Clamp01((Time.time - clearReadyTime + 2.5f) / 1f) * 0.9f;
            ui.Label(new Rect(0f, vh * 0.08f, vw, 150f),
                     Loc.T("割れた月は、修復されました。\nただし、次の面へは進めません。", "The cracked moon has been repaired.\nBut you can't advance from it."),
                     44f, TextAlignmentOptions.Center, new Color(ArtYellow.r, ArtYellow.g, ArtYellow.b, a));
        }

        static Sprite SeamSprite()
        {
            if (seamSprite != null) return seamSprite;
            const int S = 256;
            var px = new Color32[S * S];
            Vector2[] pts =
            {
                new Vector2(0.60f, 0.02f), new Vector2(0.47f, 0.22f), new Vector2(0.58f, 0.40f),
                new Vector2(0.43f, 0.60f), new Vector2(0.55f, 0.79f), new Vector2(0.45f, 0.98f),
            };
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float d = 9f;
                    for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, SegDistance(u, v, pts[i], pts[i + 1]));
                    float core = Mathf.Clamp01((0.016f - d) / 0.010f);
                    float glow = Mathf.Clamp01(1f - d / 0.06f);
                    float a = Mathf.Max(core, glow * glow * 0.45f);
                    float rr = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                    a *= Mathf.Clamp01((0.5f - rr) / 0.02f);
                    px[y * S + x] = new Color32(255, 224, 130, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            seamSprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), S);
            seamSprite.name = "Seam";
            return seamSprite;
        }

        static float SegDistance(float px, float py, Vector2 a, Vector2 b)
        {
            float vx = b.x - a.x, vy = b.y - a.y;
            float t = Mathf.Clamp01(((px - a.x) * vx + (py - a.y) * vy) / (vx * vx + vy * vy));
            float dx = px - (a.x + vx * t), dy = py - (a.y + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        void HideFx(int k)
        {
            if (k < Stages.FixedCount || k >= fx.Length || fx[k] == null) return;
            fx[k].left.enabled = false;
            fx[k].right.enabled = false;
            fx[k].seam.enabled = false;
        }

        void HideAllFx()
        {
            for (int k = Stages.FixedCount; k < fx.Length; k++) HideFx(k);
        }

        // 割れる演出が終わったら、同じ面の次の挑戦の月(右下)を現す
        void ReleaseRetry(int k)
        {
            int n = k + 1;
            if (n < NodeCount && IsRandomNode(n) && RN(n).attempt > 1 && RN(n).waitReveal)
            {
                RN(n).waitReveal = false;
                RN(n).revealStart = Time.time;
            }
        }

        // ランダム面の月を描く(新月の輪 / クリア済みの満月 / 割れた月 / 修復された月)。a は全体の濃さ
        void DrawRandomNode(int k, Vector3 wp, float s, float a, Color tint)
        {
            RNode rn = RN(k);
            SpriteRenderer sr = stageIcons[k];
            NodeFx f = fx[k];
            float t = Time.time;
            float now = Time.time;

            // 演出の進み具合
            float crackP = 1f;     // 0 = 無傷、1 = 割れきった
            float repairQ = 0f;    // 0 = 割れたまま、1 = 直りきった
            if (rn.state == StateCracked || rn.state == StateRepaired)
            {
                if (rn.crackAnim)
                {
                    if (rn.animT0 < 0f) rn.animT0 = now + 0.9f;   // 画面が出きってから
                    crackP = Mathf.Clamp01((now - rn.animT0) / CrackSeconds);
                    if (crackP > 0.34f && !rn.crackFx)
                    {
                        rn.crackFx = true;
                        PlaySe(clipTick, 1f);
                        PlaySe(clipSparkle, 0.5f);
                        Burst(wp, 26);
                    }
                    if (now - rn.animT0 >= CrackSeconds)
                    {
                        rn.crackAnim = false;
                        rn.crackFx = false;
                        rn.animT0 = rn.repairAnim ? now + 0.7f : -1f;
                        ReleaseRetry(k);
                        crackP = 1f;
                    }
                }
                if (rn.state == StateRepaired && !rn.crackAnim)
                {
                    if (rn.repairAnim)
                    {
                        if (rn.animT0 < 0f) rn.animT0 = now + 0.9f;
                        repairQ = Mathf.Clamp01((now - rn.animT0) / RepairSeconds);
                        if (now - rn.animT0 >= RepairSeconds)
                        {
                            rn.repairAnim = false;
                            rn.animT0 = -1f;
                            PlaySe(clipSparkle, 0.7f);
                            Burst(wp, 18);
                            repairQ = 1f;
                        }
                    }
                    else
                    {
                        repairQ = 1f;
                    }
                }
            }

            bool whole = rn.state == StateNew || rn.state == StateClear;
            if (whole)
            {
                // 輪(まだ)か、満月(クリア済み)
                HideFx(k);
                sr.enabled = true;
                sr.transform.position = wp;
                sr.transform.localScale = new Vector3(s, s, 1f);
                if (rn.state == StateClear)
                {
                    sr.sprite = StellarArt.Full();
                    sr.color = new Color(tint.r, tint.g, tint.b, a);
                }
                else
                {
                    sr.sprite = StellarArt.New();
                    sr.color = new Color(tint.r, tint.g, tint.b, a * (0.85f + 0.15f * Mathf.Sin(t * 2f)));
                    sr.transform.localScale = new Vector3(s * (1f + 0.04f * Mathf.Sin(t * 2f)), s * (1f + 0.04f * Mathf.Sin(t * 2f)), 1f);
                }
                return;
            }

            // 直りきった月: 満月に、金色の継ぎ目
            bool repairedDone = rn.state == StateRepaired && !rn.crackAnim && repairQ >= 1f;
            if (repairedDone)
            {
                f.left.enabled = false;
                f.right.enabled = false;
                sr.enabled = true;
                sr.sprite = StellarArt.Full();
                sr.transform.position = wp;
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.color = new Color(tint.r, tint.g, tint.b, a);
                f.seam.enabled = true;
                f.seam.transform.position = wp + new Vector3(0f, 0f, -0.01f);
                f.seam.transform.localScale = new Vector3(s, s, 1f);
                f.seam.color = new Color(1f, 1f, 1f, a * (0.75f + 0.25f * Mathf.Sin(t * 2.4f)));
                return;
            }

            // 割れた月(演出中も含む): 満月が、真ん中から二つに割れて、V の字にずれる
            Sprite[] h = Halves(StellarArt.Full());
            float shakeAmp = crackP < 0.34f ? crackP / 0.34f * 0.035f * s : 0f;
            float open = Mathf.Clamp01((crackP - 0.34f) / 0.66f);
            open = 1f - (1f - open) * (1f - open) * (1f - open);   // 勢いよく開いて、止まる
            open *= 1f - repairQ;                                  // 修復中は、閉じていく
            float jitter = Mathf.Sin(t * 70f) * shakeAmp;
            Color dim = new Color(tint.r * 0.80f, tint.g * 0.82f, tint.b * 0.94f, 1f);
            // 割れ始めに、輪の月が満月に変わる(割れるのは、満ちた月)
            float fullQ = Mathf.Clamp01(crackP / 0.2f);
            float halfA = a * fullQ;

            f.left.enabled = halfA > 0.003f;
            f.right.enabled = f.left.enabled;
            f.left.sprite = h[0];
            f.right.sprite = h[1];
            f.left.transform.position = wp + new Vector3(-0.06f * open * s + jitter, -0.035f * open * s, 0f);
            f.right.transform.position = wp + new Vector3(0.06f * open * s - jitter, -0.015f * open * s, 0f);
            f.left.transform.rotation = Quaternion.Euler(0f, 0f, 9f * open);
            f.right.transform.rotation = Quaternion.Euler(0f, 0f, -9f * open);
            f.left.transform.localScale = new Vector3(s, s, 1f);
            f.right.transform.localScale = new Vector3(s, s, 1f);
            f.left.color = new Color(dim.r, dim.g, dim.b, halfA);
            f.right.color = new Color(dim.r, dim.g, dim.b, halfA);

            // 割れ始め: 輪の月がすっと消える
            sr.enabled = fullQ < 0.997f;
            if (sr.enabled)
            {
                sr.sprite = StellarArt.New();
                sr.transform.position = wp;
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.color = new Color(tint.r, tint.g, tint.b, a * (1f - fullQ));
            }
            f.seam.enabled = repairQ > 0.003f;
            if (f.seam.enabled)
            {
                f.seam.transform.position = wp + new Vector3(0f, 0f, -0.01f);
                f.seam.transform.localScale = new Vector3(s, s, 1f);
                f.seam.color = new Color(1f, 1f, 1f, a * repairQ);
            }
        }
    }
}
