using UnityEngine;
using TMPro;

namespace StellarReversi
{
    /// <summary>
    /// カットイン: 画面の中央に、青ベースの黒っぽい帯(上と下はグラデーション)を出して、文字を見せる。
    /// 当たり判定は画面ぜんたいにあって、メニュー(設定)以外のどこを押しても次のページ(最後なら閉じる)へ進む。
    /// LUNATIC の面のはじめ(5 面目は、説明つき)と、15 面をクリアしたあとの言葉で使う。
    /// 文字の中の <c>&lt;color=#F0E682&gt;…&lt;/color&gt;</c> で、一部だけ黄色にできる。
    /// </summary>
    public partial class StellarGame
    {
        string[] cutPages;
        int cutIndex;
        float cutStart, cutPageStart;
        System.Action cutDone;

        const string CutYellow = "<color=#F0E682>";
        const string CutEnd = "</color>";

        bool CutinOn { get { return cutPages != null; } }

        void ShowCutin(string[] ja, string[] en, System.Action done)
        {
            cutPages = Loc.En ? en : ja;
            cutIndex = 0;
            cutStart = cutPageStart = Time.time;
            cutDone = done;
            PlaySe(clipSwell, 0.5f);
        }

        void HideCutin()
        {
            cutPages = null;
            cutDone = null;
        }

        // 押されたら、次のページへ。出はじめの一瞬は、まちがえて飛ばさないよう受けつけない
        void UpdateCutin(bool pressed)
        {
            if (!CutinOn || !pressed || Time.time - cutPageStart < 0.3f) return;
            PlaySe(clipTick, 0.7f);
            cutIndex++;
            cutPageStart = Time.time;
            if (cutIndex >= cutPages.Length)
            {
                System.Action done = cutDone;
                HideCutin();
                if (done != null) done();
            }
        }

        void DrawCutin(float vw, float vh)
        {
            if (!CutinOn || cutIndex >= cutPages.Length) return;
            float open = Mathf.Clamp01((Time.time - cutStart) / 0.28f);
            open = 1f - (1f - open) * (1f - open);
            float bandH = vh * 0.46f * open;
            float top = (vh - bandH) * 0.5f;
            Color core = new Color(0.035f, 0.07f, 0.16f, 0.94f);

            // 上と下は、すこしずつ透けるグラデーション(中の 60% は、まっすぐ濃い)
            const int steps = 18;
            float edge = bandH * 0.2f;
            float strip = edge / steps;
            for (int i = 0; i < steps; i++)
            {
                float a = core.a * (i + 1f) / steps;
                FillRect(new Rect(0f, top + i * strip, vw, strip + 1f), new Color(core.r, core.g, core.b, a));
                FillRect(new Rect(0f, top + bandH - (i + 1) * strip, vw, strip + 1f), new Color(core.r, core.g, core.b, a));
            }
            FillRect(new Rect(0f, top + edge, vw, bandH - 2f * edge), core);

            // 文字
            float ta = Mathf.Clamp01((Time.time - cutPageStart - 0.12f) / 0.4f) * open;
            string text = cutPages[cutIndex];
            int longest = 0;
            foreach (string line in text.Split('\n')) longest = Mathf.Max(longest, line.Length);
            float size = longest <= 10 ? 104f : (longest <= 16 ? 66f : 54f);
            ui.LabelRich(new Rect(0f, top, vw, bandH), text, size, TextAlignmentOptions.Center, new Color(0.96f, 0.97f, 1f, ta));

            // 次へ、の合図(ゆっくり点滅する小さな三角)
            float blink = 0.35f + 0.35f * Mathf.Sin(Time.time * 3f);
            ui.Label(new Rect(vw * 0.5f - 40f, top + bandH - edge - 60f, 80f, 60f), "▼", 36f, TextAlignmentOptions.Center,
                     new Color(0.96f, 0.97f, 1f, blink * ta));
        }

        // ------------------------------------------------------------------
        // 使うところ
        // ------------------------------------------------------------------

        // LUNATIC の面に入ったとき。5 面目(最初の LUNATIC)だけ、説明がつく
        void ShowLunaticCutin()
        {
            if (stage == 4)
            {
                ShowCutin(new[] { "LUNATIC", "赤い月のステージは\n難易度が高くなっています" },
                          new[] { "LUNATIC", "Stages with red moons\nare much harder" }, null);
            }
            else
            {
                ShowCutin(new[] { "LUNATIC" }, new[] { "LUNATIC" }, null);
            }
        }

        // 15 面をクリアしたあとの言葉。そのあと、クレジットへ
        void ShowStage15Cutin()
        {
            int lastFixed = Stages.RandomFromStage - 1;
            ShowCutin(
                new[]
                {
                    "ここまでプレイしていただき\nありがとうございます",
                    "ゲームに慣れてきましたか？",
                    "ステージ" + lastFixed + "まで進んだら\n" + CutYellow + "エキシビションマッチ" + CutEnd + "に\n挑戦する権利を差し上げます",
                },
                new[]
                {
                    "Thank you so much\nfor playing this far",
                    "Getting used to the game?",
                    "Reach Stage " + lastFixed + " and you'll earn the right\nto challenge the " + CutYellow + "Exhibition Match" + CutEnd,
                },
                ShowCredits);
        }

        // ------------------------------------------------------------------
        // 16 面の前(ルールの振り返り)と、エキシビションマッチの最初の面の前(説明)。どちらも、はじめて入るときの 1 回だけ
        // ------------------------------------------------------------------

        const string SeenKeyPrefix = "Stellar.Seen.";

        static bool Seen(string id) { return PlayerPrefs.GetInt(SeenKeyPrefix + id, 0) != 0; }

        static void MarkSeen(string id)
        {
            PlayerPrefs.SetInt(SeenKeyPrefix + id, 1);
            PlayerPrefs.Save();
        }

        void ShowRuleCutins()
        {
            if (IsRandomNode(curNode) && curNode == Stages.FixedCount && !Seen("exh"))
            {
                MarkSeen("exh");
                int n = Stages.RandomFromStage;
                // RankingEnabled が true のとき、7 ページ目(順位の案内)が出る
                string rankJa = RankingEnabled ? "今の自分の順位を、\n設定から見ることができます" : null;
                string rankEn = RankingEnabled ? "You can check your current rank\nfrom the settings menu" : null;
                ShowCutin(
                    Pages(
                        "ここからは\n" + CutYellow + "エキシビションマッチ" + CutEnd,
                        "ステージは、たくさんの中から\nランダムに選ばれます",
                        "ライフは3つ。\n「ひとつ戻る」「やり直す」を\n押すたびに1つ減ります",
                        "ライフがなくなると月が割れます。\n割れた月でもクリアできますが、\n次のステージには進めません",
                        "割れたらステージセレクトの右下に、\n新しいステージ（" + n + "-2）が現れます。\nそれを割らずにクリアすれば、\n次のステージへ進めます",
                        "慎重に置いてクリアしましょう",
                        rankJa),
                    Pages(
                        "From here on:\n" + CutYellow + "Exhibition Match" + CutEnd,
                        "Stages are chosen at random\nfrom a huge pool",
                        "You have 3 lives.\nUndo and Reset each cost 1 life",
                        "When your lives run out, the moon cracks.\nYou can still clear it,\nbut you can't advance",
                        "On the stage select, a new stage (" + n + "-2)\nappears at the lower right.\nClear it without cracking\nto move on",
                        "Place your moons carefully!",
                        rankEn),
                    null);
            }
        }

        const bool RankingEnabled = true;

        // null を除いて並べる
        static string[] Pages(params string[] p)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (string t in p) if (t != null) list.Add(t);
            return list.ToArray();
        }
    }
}
