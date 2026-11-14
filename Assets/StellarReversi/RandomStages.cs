using System.Collections.Generic;
using UnityEngine;

namespace StellarReversi
{
    /// <summary>
    /// 30 面以降の「ランダム面」のもと。Assets/Resources/Stellar/Pool.txt にある面(どの相手の手でも必ず解けると検証ずみ)から抽選する。
    /// 盤面は 8 通りの向き(回転・反転)で使えるので、実際に出る面の数は、Pool.txt の面の数の 8 倍になる。
    /// Pool.txt には、通常の面([NORMAL])と LUNATIC の面([LUNATIC])が、64 文字ずつ並んでいる。
    /// 抽選の結果は pick(= もとの面の番号 * 8 + 向き)で保存する。
    /// </summary>
    public static class RandomStages
    {
        const string ResourcePath = "Stellar/Pool";

        static List<string> normal, lunatic;

        static void Load()
        {
            if (normal != null) return;
            normal = new List<string>();
            lunatic = new List<string>();

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null)
            {
                List<string> target = normal;
                var sb = new System.Text.StringBuilder();
                foreach (string raw in asset.text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[NORMAL]")) { target = normal; sb.Length = 0; continue; }
                    if (line.StartsWith("[LUNATIC]")) { target = lunatic; sb.Length = 0; continue; }
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line.Substring(0, hash);
                    foreach (char ch in line)
                    {
                        char c = char.ToUpperInvariant(ch);
                        if (c == 'B' || c == 'W' || c == '.') sb.Append(c);
                        if (sb.Length == MoonBoard.Count)
                        {
                            target.Add(sb.ToString());
                            sb.Length = 0;
                        }
                    }
                }
            }

            // 30 面以降に回る手作りの面(FixedCount 以降)も、抽選のもとに加える
            for (int i = Stages.FixedCount; i < Stages.Count; i++)
            {
                string st = Stages.Data[i];
                bool lun = (i + 1) % 5 == 0;
                List<string> t = lun ? lunatic : normal;
                if (!t.Contains(st)) t.Add(st);
            }

            // Pool.txt が無いときは、ふつうの面を使い回す(動かなくなるのを避けるための保険)
            if (normal.Count == 0) normal.AddRange(Stages.Data);
            if (lunatic.Count == 0) lunatic.AddRange(normal);
            Debug.Log("[星空リバーシ] ランダム面のもと: 通常 " + normal.Count + " 面 / LUNATIC " + lunatic.Count + " 面(× 8 通りの向き)");
        }

        static List<string> Pool(bool isLunatic)
        {
            Load();
            return isLunatic ? lunatic : normal;
        }

        public static int Count(bool isLunatic)
        {
            return Pool(isLunatic).Count * 8;
        }

        /// <summary>抽選する。avoid(最近使ったもとの面の番号)は、なるべく避ける。</summary>
        public static int Choose(bool isLunatic, System.Random rng, HashSet<int> avoid)
        {
            List<string> pool = Pool(isLunatic);
            int baseIndex = rng.Next(pool.Count);
            for (int tries = 0; tries < 40 && avoid != null && avoid.Contains(baseIndex) && avoid.Count < pool.Count; tries++)
            {
                baseIndex = rng.Next(pool.Count);
            }
            return baseIndex * 8 + rng.Next(8);
        }

        public static int BaseIndex(int pick)
        {
            return pick / 8;
        }

        public static string Board(bool isLunatic, int pick)
        {
            List<string> pool = Pool(isLunatic);
            int baseIndex = Mathf.Clamp(pick / 8, 0, pool.Count - 1);
            return Transform(pool[baseIndex], pick & 7);
        }

        // 向き: bit0 = 縦横を入れ替える、bit1 = 左右反転、bit2 = 上下反転(ルールは、どの向きでも同じ)
        static string Transform(string s, int sym)
        {
            var chars = new char[MoonBoard.Count];
            for (int r = 0; r < MoonBoard.Size; r++)
            {
                for (int c = 0; c < MoonBoard.Size; c++)
                {
                    int rr = r, cc = c;
                    if ((sym & 4) != 0) rr = MoonBoard.Size - 1 - rr;
                    if ((sym & 2) != 0) cc = MoonBoard.Size - 1 - cc;
                    if ((sym & 1) != 0) { int t = rr; rr = cc; cc = t; }
                    chars[r * MoonBoard.Size + c] = s[rr * MoonBoard.Size + cc];
                }
            }
            return new string(chars);
        }
    }
}
