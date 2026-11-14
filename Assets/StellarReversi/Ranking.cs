namespace StellarReversi
{
    /// <summary>
    /// ランキングの送り先。ゲームは「記録(到達ステージ・無傷の連続クリア数)」を作って Submit するだけで、
    /// 送り先は Backend を差し替えて決める。今は何も送らない LocalBackend(順位は出ない)。
    /// Steam 版では、Steamworks の Leaderboards に送る IBackend を作って、Ranking.Backend に入れる:
    ///   ・Submit: SteamUserStats.FindOrCreateLeaderboard(board, ...) で見つけて、UploadLeaderboardScore(KeepBest)
    ///   ・Fetch : DownloadLeaderboardEntries(GlobalAroundUser) で、自分の順位と全体の人数を取る
    /// </summary>
    public static class Ranking
    {
        /// <summary>エキシビションマッチで、いちばん先まで進んだステージの番号。</summary>
        public const string BoardStage = "ExhibitionStage";

        /// <summary>エキシビションマッチで、月を割らずに続けてクリアした最高の数。</summary>
        public const string BoardStreak = "ExhibitionStreak";

        public interface IBackend
        {
            /// <summary>自己ベストの更新を送る(送り先は、高いほうだけを残す)。</summary>
            void Submit(string board, int value);

            /// <summary>自分の順位と全体の人数を取る。順位がわからないときは rank = -1。</summary>
            void Fetch(string board, System.Action<int, int> onResult);
        }

        sealed class LocalBackend : IBackend
        {
            public void Submit(string board, int value) { }
            public void Fetch(string board, System.Action<int, int> onResult) { if (onResult != null) onResult(-1, 0); }
        }

        public static IBackend Backend = new LocalBackend();
    }
}
