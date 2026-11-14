using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// ステージは Assets/Resources/Stellar/Stages.txt から読み込む(面の数は決まっていない)。
    /// 書き方は Stages.txt の冒頭に書いてある。面を足したり直したりしたら、再生しなおすだけで反映される。
    /// ステージ選択の月の数は、ここで読み込んだ面の数で決まる。
    /// </summary>
    public static class Stages
    {
        const string ResourcePath = "Stellar/Stages";

        static string[] data;

        public static int Count => Data.Length;

        /// <summary>この面(1 始まり)から先は、ライフのあるランダム面(エキシビションマッチ)になる。手作りの面は、その手前まで。</summary>
        public const int RandomFromStage = 26;

        /// <summary>手作りの面として並べる数(ステージ選択の固定の月)。</summary>
        public static int FixedCount { get { return Mathf.Min(Count, RandomFromStage - 1); } }

        /// <summary>各ステージ 64 文字(B = 新月, W = 満月, . = 空き。左上から右へ、上から下へ)。</summary>
        public static string[] Data
        {
            get
            {
                if (data == null) data = Load();
                return data;
            }
        }

        static string[] Load()
        {
            var list = new List<string>();
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[星空リバーシ] Assets/Resources/Stellar/Stages.txt が見つかりません。ステージは 0 面です。");
                return list.ToArray();
            }

            var sb = new StringBuilder();
            foreach (string raw in asset.text.Split('\n'))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);   // # から後ろはコメント
                int slashes = line.IndexOf("//");
                if (slashes >= 0) line = line.Substring(0, slashes);

                foreach (char ch in line)
                {
                    char c = char.ToUpperInvariant(ch);
                    if (c == 'B' || c == 'W' || c == '.') sb.Append(c);
                    if (sb.Length == MoonBoard.Count)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
            }

            if (sb.Length > 0)
            {
                Debug.LogWarning("[星空リバーシ] Stages.txt の最後に、64文字に満たない端があります(" + sb.Length +
                                 " 文字)。その分は無視しました。1面は 8 行 x 8 文字です。");
            }

            Debug.Log("[星空リバーシ] ステージを " + list.Count + " 面、読み込みました。");
            return list.ToArray();
        }
    }
}
