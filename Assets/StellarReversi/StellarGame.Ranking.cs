using UnityEngine;
using TMPro;

namespace StellarReversi
{
    /// <summary>
    /// エキシビションマッチの記録とランキング。
    ///   ・到達ステージ: いちばん先まで進んだステージの番号(下がらない)
    ///   ・無傷の連続クリア数: 月を割らずに続けてクリアした数。割れたら 0 に戻る。最高の数がランキングに載る
    /// 順位は設定の「ランキング」で見る。送り先は Ranking.Backend(Steam 版で差し替える)。
    /// </summary>
    public partial class StellarGame
    {
        const string BestStageKey = "Stellar.Rank.BestStage";
        const string StreakKey = "Stellar.Rank.Streak";
        const string BestStreakKey = "Stellar.Rank.BestStreak";

        int bestStage, streak, bestStreak;
        bool rankingOpen;
        int stageRank = -1, stageTotal, streakRank = -1, streakTotal;
        bool rankFetched;

        void LoadRankStats()
        {
            bestStage = PlayerPrefs.GetInt(BestStageKey, 0);
            streak = PlayerPrefs.GetInt(StreakKey, 0);
            bestStreak = PlayerPrefs.GetInt(BestStreakKey, 0);
            UpdateReached();
        }

        void SaveRankStats()
        {
            PlayerPrefs.SetInt(BestStageKey, bestStage);
            PlayerPrefs.SetInt(StreakKey, streak);
            PlayerPrefs.SetInt(BestStreakKey, bestStreak);
            PlayerPrefs.Save();
        }

        // 到達ステージを、いま並んでいる月から更新する(最後の月の面が、いちばん先)
        void UpdateReached()
        {
            int reached = 0;
            for (int i = 0; i < rnodes.Count; i++) reached = Mathf.Max(reached, rnodes[i].k + 1);
            if (reached > bestStage)
            {
                bestStage = reached;
                SaveRankStats();
                Ranking.Backend.Submit(Ranking.BoardStage, bestStage);
            }
        }

        // 月を割らずにクリアした(新しい月が、はじめてのクリアになった)
        void OnExhibitionClear()
        {
            streak++;
            if (streak > bestStreak)
            {
                bestStreak = streak;
                Ranking.Backend.Submit(Ranking.BoardStreak, bestStreak);
            }
            SaveRankStats();
        }

        // 月が割れた: 連続記録は、ここで終わり
        void OnExhibitionCrack()
        {
            streak = 0;
            SaveRankStats();
        }

        bool RankingUnlocked { get { return rnodes.Count > 0; } }

        void OpenRanking()
        {
            rankingOpen = true;
            rankFetched = false;
            stageRank = streakRank = -1;
            Ranking.Backend.Fetch(Ranking.BoardStage, (r, t) => { stageRank = r; stageTotal = t; });
            Ranking.Backend.Fetch(Ranking.BoardStreak, (r, t) => { streakRank = r; streakTotal = t; });
            rankFetched = true;
        }

        static string RankText(int rank, int total)
        {
            if (rank <= 0) return "—";
            return Loc.En ? "#" + rank + (total > 0 ? " / " + total : "") : rank + "位" + (total > 0 ? " / " + total + "人" : "");
        }

        void DrawRankingPanel(float vw, float vh)
        {
            FillRect(new Rect(0f, 0f, vw, vh), new Color(0.02f, 0.02f, 0.1f, 0.72f));
            float pw = 1000f, ph = 640f;
            var panel = new Rect((vw - pw) * 0.5f, (vh - ph) * 0.5f, pw, ph);
            FillRect(panel, new Color(Background.r, Background.g, Background.b, 0.98f));
            Color edge = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
            FillRect(new Rect(panel.x, panel.y, pw, 3f), edge);
            FillRect(new Rect(panel.x, panel.yMax - 3f, pw, 3f), edge);
            FillRect(new Rect(panel.x, panel.y, 3f, ph), edge);
            FillRect(new Rect(panel.xMax - 3f, panel.y, 3f, ph), edge);

            float x0 = panel.x + 70f, w = pw - 140f;
            ui.Label(new Rect(x0, panel.y + 40f, w, 70f), Loc.T("ランキング(エキシビションマッチ)", "Ranking (Exhibition Match)"), 44f,
                     TextAlignmentOptions.Left, Gold);

            float y = panel.y + 140f;
            ui.Label(new Rect(x0, y, w, 60f), Loc.T("到達ステージ", "Furthest stage"), 38f, TextAlignmentOptions.Left, PaleYellow);
            ui.Label(new Rect(x0, y, w, 60f), bestStage > 0 ? bestStage.ToString() : "—", 44f, TextAlignmentOptions.Right, PaleYellow);
            ui.Label(new Rect(x0 + 30f, y + 62f, w - 30f, 50f), Loc.T("順位", "Rank"), 32f, TextAlignmentOptions.Left, new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, 0.8f));
            ui.Label(new Rect(x0, y + 62f, w, 50f), RankText(stageRank, stageTotal), 36f, TextAlignmentOptions.Right, PaleYellow);

            y += 170f;
            ui.Label(new Rect(x0, y, w, 60f), Loc.T("無傷の連続クリア", "Flawless streak"), 38f, TextAlignmentOptions.Left, PaleYellow);
            ui.Label(new Rect(x0, y, w, 60f), Loc.T("現在 ", "Now ") + streak + Loc.T(" / 最高 ", " / Best ") + bestStreak, 40f, TextAlignmentOptions.Right, PaleYellow);
            ui.Label(new Rect(x0 + 30f, y + 62f, w - 30f, 50f), Loc.T("順位(最高の数)", "Rank (best streak)"), 32f, TextAlignmentOptions.Left, new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, 0.8f));
            ui.Label(new Rect(x0, y + 62f, w, 50f), RankText(streakRank, streakTotal), 36f, TextAlignmentOptions.Right, PaleYellow);

            ui.Label(new Rect(x0, panel.yMax - 120f, w, 50f),
                     Loc.T("月が割れると、連続記録は 0 に戻ります。順位は Steam 版で表示されます。", "Cracking a moon resets your streak. Ranks are shown in the Steam version."),
                     28f, TextAlignmentOptions.Left, new Color(PaleYellow.r, PaleYellow.g, PaleYellow.b, 0.7f));

            if (ui.Button(new Rect(panel.xMax - 90f, panel.y + 12f, 78f, 78f), "×", 52f) ||
                ui.Button(new Rect(panel.x + (pw - 300f) * 0.5f, panel.yMax - 70f - 10f, 300f, 60f), Loc.T("もどる", "Back"), 34f))
            {
                rankingOpen = false;
                PlaySe(clipTick);
            }
        }
    }
}
