using System.Collections.Generic;

namespace StellarReversi
{
    /// <summary>
    /// 星空リバーシの盤面ロジック(Unity には依存しない)。
    /// プレイヤーは新月、相手(CPU)は満月。普通のオセロと同じく、相手の色を挟むとその石が自分の色になる。
    /// 勝利条件は「全てのマスが新月になる」ことだけ。
    /// マスの番号は index = row * 8 + col(左上が 0、右下が 63)。
    /// </summary>
    public sealed class MoonBoard
    {
        public const int Size = 8;
        public const int Count = Size * Size;

        public const sbyte Empty = 0;
        public const sbyte New = 1;   // 新月(プレイヤーの駒)
        public const sbyte Full = 2;  // 満月(相手の駒)

        static readonly int[] Dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] Dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        readonly sbyte[] cells = new sbyte[Count];

        public sbyte this[int index] => cells[index];

        /// <summary>64文字(B=新月, W=満月, .=空き)から盤面を読み込む。</summary>
        public void Load(string stage)
        {
            for (int i = 0; i < Count; i++)
            {
                char ch = i < stage.Length ? stage[i] : '.';
                cells[i] = ch == 'B' ? New : ch == 'W' ? Full : Empty;
            }
        }

        public void CopyCellsTo(sbyte[] dest)
        {
            System.Array.Copy(cells, dest, Count);
        }

        public void SetCells(sbyte[] src)
        {
            System.Array.Copy(src, cells, Count);
        }

        static sbyte Other(sbyte side)
        {
            return side == New ? Full : New;
        }

        /// <summary>side が index に置いたとき、裏返る石の数(0 なら置けない)。</summary>
        public int CountFlips(int index, sbyte side)
        {
            if (index < 0 || index >= Count || cells[index] != Empty) return 0;
            int cx = index % Size, cy = index / Size;
            int total = 0;
            for (int d = 0; d < 8; d++)
            {
                total += RunLength(cx, cy, d, side);
            }
            return total;
        }

        public bool IsLegal(int index, sbyte side = New)
        {
            return CountFlips(index, side) > 0;
        }

        public bool HasMove(sbyte side = New)
        {
            for (int i = 0; i < Count; i++)
            {
                if (IsLegal(i, side)) return true;
            }
            return false;
        }

        /// <summary>全てのマスが新月なら true(これが唯一の勝利条件)。</summary>
        public bool IsSolved()
        {
            for (int i = 0; i < Count; i++)
            {
                if (cells[i] != New) return false;
            }
            return true;
        }

        static readonly System.Random Rng = new System.Random();

        /// <summary>
        /// 相手(満月)の手。打てる所の中から、ランダムに選ぶ。打てなければ -1。
        /// (解けるかどうかの検証では、どの手を選ばれても解けるかを調べる)
        /// </summary>
        public int ChooseOpponentMove()
        {
            int count = 0, chosen = -1;
            for (int i = 0; i < Count; i++)
            {
                if (!IsLegal(i, Full)) continue;
                count++;
                if (Rng.Next(count) == 0) chosen = i;   // 一様に選ぶ(貯水池サンプリング)
            }
            return chosen;
        }

        /// <summary>
        /// index に side の石を置き、挟んだ相手の石を裏返す。裏返した枚数を返す(0 なら置けず、盤面は変わらない)。
        /// flipped が null でなければ、裏返したマスの番号が追加される。
        /// </summary>
        public int Play(int index, sbyte side = New, List<int> flipped = null)
        {
            if (index < 0 || index >= Count || cells[index] != Empty) return 0;

            int cx = index % Size, cy = index / Size;
            int total = 0;
            for (int d = 0; d < 8; d++)
            {
                int run = RunLength(cx, cy, d, side);
                if (run == 0) continue;

                int x = cx + Dx[d], y = cy + Dy[d];
                for (int k = 0; k < run; k++)
                {
                    int i = y * Size + x;
                    cells[i] = side;
                    if (flipped != null) flipped.Add(i);
                    x += Dx[d];
                    y += Dy[d];
                }
                total += run;
            }

            if (total > 0) cells[index] = side;
            return total;
        }

        // (cx, cy) から方向 d に進んだとき、side の石で挟める相手の石の数。挟めなければ 0。
        int RunLength(int cx, int cy, int d, sbyte side)
        {
            sbyte other = Other(side);
            int x = cx + Dx[d], y = cy + Dy[d];
            int run = 0;
            while (x >= 0 && x < Size && y >= 0 && y < Size && cells[y * Size + x] == other)
            {
                run++;
                x += Dx[d];
                y += Dy[d];
            }
            if (run > 0 && x >= 0 && x < Size && y >= 0 && y < Size && cells[y * Size + x] == side)
            {
                return run;
            }
            return 0;
        }
    }
}
