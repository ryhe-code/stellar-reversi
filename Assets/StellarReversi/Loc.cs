using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 言語(日本語 / English)。画面の文字は Loc.T("日本語", "English") で書く。
    /// 設定画面の「言語」ボタンで切り替わり、PlayerPrefs に保存される。初めての起動では、OS の言語が日本語なら日本語、それ以外は英語。
    /// 絵に文字が含まれているもの(「つづきから」「はじめから」「ステージ」など)は、英語のときだけ、同じ場所に文字で重ねる。
    /// </summary>
    public static class Loc
    {
        const string LangKey = "Stellar.Lang";

        /// <summary>0 = 日本語、1 = English。</summary>
        public static int Lang;

        public static bool En { get { return Lang == 1; } }

        public static void Load()
        {
            int def = Application.systemLanguage == SystemLanguage.Japanese ? 0 : 1;
            Lang = PlayerPrefs.GetInt(LangKey, def) == 1 ? 1 : 0;
        }

        public static void Toggle()
        {
            Lang = Lang == 1 ? 0 : 1;
            PlayerPrefs.SetInt(LangKey, Lang);
            PlayerPrefs.Save();
        }

        public static string T(string ja, string en)
        {
            return Lang == 1 ? en : ja;
        }
    }
}
