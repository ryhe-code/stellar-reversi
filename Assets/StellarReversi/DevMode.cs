using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StellarReversi
{
    /// <summary>コルーチンを走らせるためだけの入れ物(StopAllCoroutines で止まらないよう、本体とは別のコンポーネントにしてある)。</summary>
    public sealed class DevRunner : MonoBehaviour { }

    /// <summary>
    /// 【デベロッパーモード】開発の間だけ使う道具。開発が終わったら撤去する。
    ///   ・背景の明るさ、月の輝度を、設定画面のスライダーで変えられる
    ///   ・クリアしていないステージにも、ステージ選択や矢印で行ける
    ///   ・設定画面の「ステージを検証」で、Stages.txt の全ての面が解けるか(最悪の場合の手数と最善手つき)を Console に出力する
///   ・プレイ中に「ヒント」ボタン(または H キー)で、いまの盤面から相手がどう打っても勝てる最善の一手を光らせる
    ///
    /// 撤去のしかた: Available を false にすれば全て無効になる。ファイルごと消すときは、
    /// StellarGame.cs の "DevMode" と書いてある箇所(数か所)と、DevRunner の行も一緒に消す。
    /// </summary>
    public static class DevMode
    {
        public const bool Available = true;   // true = デベロッパーモードを使える(公開するときは false にする)

        const string ActiveKey = "Stellar.Dev";
        const string BackgroundKey = "Stellar.DevBackground";
        const string MoonKey = "Stellar.DevMoon";
        const string WinSpeedKey = "Stellar.DevWinSpeed";
        const string TitleMoveKey = "Stellar.DevTitleMove";
        const string TitleSpeedKey = "Stellar.DevTitleSpeed";

        static bool active = true;

        public static bool Active
        {
            get => Available && active;
            set => active = value;
        }

        /// <summary>0..1。0.5 が元の色。</summary>
        public static float BackgroundLevel = 0.5f;

        /// <summary>0..1。0.5 が元の輝度。</summary>
        public static float MoonLevel = 0.5f;

        /// <summary>背景の色に掛ける倍率(0.25 倍〜4 倍)。</summary>
        public static float BackgroundScale => Active ? Mathf.Pow(4f, BackgroundLevel * 2f - 1f) : 1f;

        /// <summary>月の色に掛ける倍率(0〜2 倍)。</summary>
        public static float MoonScale => Active ? MoonLevel * 2f : 1f;

        /// <summary>0..1。0 が等速、1 が 2 倍速。クリア演出の見た目の長さだけを縮める(音は倍速にしない)。</summary>
        public static float WinSpeedLevel = 0f;

        /// <summary>クリア演出の倍速(1〜2 倍、0.05 刻み)。</summary>
        public static float WinSpeed => Active ? Mathf.Round(Mathf.Lerp(1f, 2f, WinSpeedLevel) * 20f) * 0.05f : 1f;

        /// <summary>0..1。0.5 が元の強さ(0 で止まり、1 で 2 倍)。タイトルの動きの大きさ。</summary>
        public static float TitleMoveLevel = 0.5f;

        /// <summary>0..1。0.5 が元の速さ(0 で止まり、1 で 2 倍)。タイトルの動きの速さ。</summary>
        public static float TitleSpeedLevel = 0.5f;

        public static float TitleMove => Active ? TitleMoveLevel * 2f : 1f;
        public static float TitleSpeed => Active ? TitleSpeedLevel * 2f : 1f;

        public static void Load()
        {
            active = PlayerPrefs.GetInt(ActiveKey, 1) != 0;
            BackgroundLevel = PlayerPrefs.GetFloat(BackgroundKey, 0.5f);
            MoonLevel = PlayerPrefs.GetFloat(MoonKey, 0.5f);
            WinSpeedLevel = PlayerPrefs.GetFloat(WinSpeedKey, 0f);
            TitleMoveLevel = PlayerPrefs.GetFloat(TitleMoveKey, 0.5f);
            TitleSpeedLevel = PlayerPrefs.GetFloat(TitleSpeedKey, 0.5f);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(ActiveKey, active ? 1 : 0);
            PlayerPrefs.SetFloat(BackgroundKey, BackgroundLevel);
            PlayerPrefs.SetFloat(MoonKey, MoonLevel);
            PlayerPrefs.SetFloat(WinSpeedKey, WinSpeedLevel);
            PlayerPrefs.SetFloat(TitleMoveKey, TitleMoveLevel);
            PlayerPrefs.SetFloat(TitleSpeedKey, TitleSpeedLevel);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------
        // ステージの検証とヒント(ゲームと同じ決まりで、全マス新月にできる打ち方を探す)
        //   ・相手の手はランダムなので、相手が打てる全ての手に対して勝てる打ち方だけを「解ける」とする
        //   ・同じ盤面は一度しか調べない(メモ化)。調べた結果は「勝つまでの最短の手数」で持つ
        //   ・探索は 実行時間(秒)と盤面数、両方の上限つき。超えたら「判定できず」になる
        // ------------------------------------------------------------------

        const int Inf = 1000;

        sealed class Search
        {
            public readonly Dictionary<string, int> memo = new Dictionary<string, int>();
            public int budget = 1500000;
            public bool truncated;
            public readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            public double seconds = 4.0;
            public int nodes;
        }

        /// <summary>手の名前。列 A〜H、行 1〜8(左上が A1)。</summary>
        public static string CellName(int i)
        {
            return ((char)('A' + i % MoonBoard.Size)).ToString() + (i / MoonBoard.Size + 1);
        }

        /// <summary>
        /// 盤面 b(あなたの番)から全マス新月にする最短の手数を調べる。解けなければ Inf。
        /// move には最短の勝ち筋の最初の一手(無ければ -1)。winningFirstMoves は、勝てる最初の手の数。
        /// </summary>
        public static bool FindHint(MoonBoard b, out int move, out int depth, out int winningFirstMoves, out bool truncated)
        {
            var s = new Search();
            move = -1;
            winningFirstMoves = 0;
            int best = Inf;
            if (!b.IsSolved() && b.HasMove(MoonBoard.New))
            {
                for (int i = 0; i < MoonBoard.Count; i++)
                {
                    if (!b.IsLegal(i, MoonBoard.New)) continue;
                    int d = AfterMove(b, i, s);
                    if (d >= Inf) continue;
                    winningFirstMoves++;
                    if (d < best)
                    {
                        best = d;
                        move = i;
                    }
                }
            }
            depth = best;
            truncated = s.truncated;
            return best < Inf;
        }

        // b で i に打った場合に、どんな相手の返しでも勝てるとして、必要な手数(i を含む。最悪の場合)。勝てない返しがあれば Inf。
        static int AfterMove(MoonBoard b, int i, Search s)
        {
            MoonBoard nb = Clone(b);
            nb.Play(i, MoonBoard.New);
            if (nb.IsSolved()) return 1;
            int d = Replies(nb, s);
            return d >= Inf ? Inf : d + 1;
        }

        // 相手の番。打てる全ての手(ランダムに選ばれるので、全部)を調べ、最悪の場合の手数を返す。
        static int Replies(MoonBoard nb, Search s)
        {
            bool any = false;
            int worst = 0;
            for (int j = 0; j < MoonBoard.Count; j++)
            {
                if (!nb.IsLegal(j, MoonBoard.Full)) continue;
                any = true;
                MoonBoard n2 = Clone(nb);
                n2.Play(j, MoonBoard.Full);

                int d;
                if (n2.HasMove(MoonBoard.New)) d = Depth(n2, s);
                else if (n2.HasMove(MoonBoard.Full)) d = Replies(n2, s);   // あなたに打てる所が無いので、相手がもう一度打つ
                else d = Inf;                                               // 行き詰まり
                if (d >= Inf) return Inf;
                if (d > worst) worst = d;
            }
            if (!any) return nb.HasMove(MoonBoard.New) ? Depth(nb, s) : Inf;
            return worst;
        }

        // あなたの番の盤面 b から、勝つまでの最短手数(解けなければ Inf)
        static int Depth(MoonBoard b, Search s)
        {
            string key = Key(b);
            int known;
            if (s.memo.TryGetValue(key, out known)) return known;
            if (--s.budget < 0 || ((++s.nodes & 1023) == 0 && s.clock.Elapsed.TotalSeconds > s.seconds))
            {
                s.truncated = true;
                return Inf;
            }

            int best = Inf;
            for (int i = 0; i < MoonBoard.Count; i++)
            {
                if (!b.IsLegal(i, MoonBoard.New)) continue;
                int d = AfterMove(b, i, s);
                if (d < best) best = d;
                if (best == 1) break;
            }

            if (!s.truncated) s.memo[key] = best;
            return best;
        }

        public static IEnumerator CheckStages()
        {
            string[] data = Stages.Data;
            Debug.Log("[星空リバーシ] ステージの検証を始めます(" + data.Length + " 面)");
            int bad = 0;
            for (int k = 0; k < data.Length; k++)
            {
                var board = new MoonBoard();
                board.Load(data[k]);
                string name = "ステージ " + (k + 1);

                if (board.IsSolved())
                {
                    Debug.LogWarning("[星空リバーシ] " + name + ": 最初から全マスが新月です");
                    bad++;
                }
                else if (!board.HasMove(MoonBoard.New))
                {
                    Debug.LogWarning("[星空リバーシ] " + name + ": 最初に打てる場所がありません");
                    bad++;
                }
                else
                {
                    int move, depth, wins;
                    bool truncated;
                    bool ok = FindHint(board, out move, out depth, out wins, out truncated);
                    if (ok)
                    {
                        Debug.Log("[星空リバーシ] " + name + ": 解けます。相手がどう打っても勝てます。最悪でも " + depth + " 手 / 勝てる最初の手 " + wins +
                                  " 通り(最善手: " + CellName(move) + ")");
                    }
                    else if (truncated)
                    {
                        Debug.LogWarning("[星空リバーシ] " + name + ": 探索が大きすぎて判定できませんでした");
                        bad++;
                    }
                    else
                    {
                        Debug.LogWarning("[星空リバーシ] " + name + ": 解けません(どう打っても全マス新月にできません)");
                        bad++;
                    }
                }
                yield return null;
            }
            Debug.Log("[星空リバーシ] 検証が終わりました。問題のある面: " + bad + " / " + data.Length);
        }

        static MoonBoard Clone(MoonBoard b)
        {
            var cells = new sbyte[MoonBoard.Count];
            b.CopyCellsTo(cells);
            var c = new MoonBoard();
            c.SetCells(cells);
            return c;
        }

        static string Key(MoonBoard b)
        {
            var chars = new char[MoonBoard.Count];
            for (int i = 0; i < MoonBoard.Count; i++) chars[i] = (char)('0' + b[i]);
            return new string(chars);
        }
    }
}
