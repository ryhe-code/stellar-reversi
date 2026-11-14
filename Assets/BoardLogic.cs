// Assets/Scripts/Logic/BoardLogic.cs
using System.Collections.Generic;

public class BoardLogic
{
    public const int BoardSize = 8;

    // 盤面データ [x, y]
    private readonly PieceType[,] _board = new PieceType[BoardSize, BoardSize];

    // 8方向のベクトル走査用 (上、下、左、右、斜め4方向)
    private static readonly (int dx, int dy)[] Directions = new[]
    {
        (0, 1),   // 上
        (0, -1),  // 下
        (-1, 0),  // 左
        (1, 0),   // 右
        (-1, 1),  // 左上
        (1, 1),   // 右上
        (-1, -1), // 左下
        (1, -1)   // 右下
    };

    public BoardLogic()
    {
        Initialize();
    }

    /// <summary>
    /// 盤面を初期状態（中央に白黒2個ずつ）にリセット
    /// </summary>
    public void Initialize()
    {
        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                _board[x, y] = PieceType.None;
            }
        }

        // オセロの標準初期配置
        // (3, 3): 白, (4, 4): 白
        // (3, 4): 黒, (4, 3): 黒
        _board[3, 3] = PieceType.White;
        _board[4, 4] = PieceType.White;
        _board[3, 4] = PieceType.Black;
        _board[4, 3] = PieceType.Black;
    }

    /// <summary>
    /// 指定座標の石を取得
    /// </summary>
    public PieceType GetPieceAt(int x, int y)
    {
        if (!IsWithinBoard(x, y)) return PieceType.None;
        return _board[x, y];
    }

    public PieceType GetPieceAt(BoardPosition pos) => GetPieceAt(pos.X, pos.Y);

    /// <summary>
    /// 盤面内かどうか
    /// </summary>
    public static bool IsWithinBoard(int x, int y)
    {
        return x >= 0 && x < BoardSize && y >= 0 && y < BoardSize;
    }

    /// <summary>
    /// 指定の場所に石を置けるかどうか
    /// </summary>
    public bool CanPlace(int x, int y, PieceType player)
    {
        if (!IsWithinBoard(x, y) || _board[x, y] != PieceType.None)
        {
            return false;
        }

        // いずれかの方向で1つ以上挟めれば置ける
        foreach (var (dx, dy) in Directions)
        {
            if (CountFlippablePiecesInDirection(x, y, dx, dy, player) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool CanPlace(BoardPosition pos, PieceType player) => CanPlace(pos.X, pos.Y, player);

    /// <summary>
    /// 石を配置し、挟んだ石を反転する。
    /// 返り値として「反転した石の座標リスト」を返す（アニメーション演出等で利用）。
    /// 置けなかった場合は null を返す。
    /// </summary>
    public List<BoardPosition> PlacePiece(int x, int y, PieceType player)
    {
        if (!CanPlace(x, y, player))
        {
            return null;
        }

        List<BoardPosition> flippedPositions = new List<BoardPosition>();

        // 8方向それぞれで挟める石を収集
        foreach (var (dx, dy) in Directions)
        {
            GetFlippablePiecesInDirection(x, y, dx, dy, player, flippedPositions);
        }

        // 石を配置
        _board[x, y] = player;

        // 挟んだ石をすべて反転
        foreach (var pos in flippedPositions)
        {
            _board[pos.X, pos.Y] = player;
        }

        return flippedPositions;
    }

    /// <summary>
    /// プレイヤーが置けるマス（合法手）の全一覧を取得
    /// </summary>
    public List<BoardPosition> GetValidMoves(PieceType player)
    {
        List<BoardPosition> validMoves = new List<BoardPosition>();

        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                if (CanPlace(x, y, player))
                {
                    validMoves.Add(new BoardPosition(x, y));
                }
            }
        }

        return validMoves;
    }

    /// <summary>
    /// プレイヤーに打てる手があるか（パス判定に使用）
    /// </summary>
    public bool HasValidMove(PieceType player)
    {
        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                if (CanPlace(x, y, player)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ゲームが終了しているか（双方が打てない状態）
    /// </summary>
    public bool IsGameOver()
    {
        return !HasValidMove(PieceType.Black) && !HasValidMove(PieceType.White);
    }

    /// <summary>
    /// 現在の石の数を集計
    /// </summary>
    public (int blackCount, int whiteCount) GetScore()
    {
        int black = 0;
        int white = 0;

        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                if (_board[x, y] == PieceType.Black) black++;
                else if (_board[x, y] == PieceType.White) white++;
            }
        }

        return (black, white);
    }

    #region 内部探索アルゴリズム

    /// <summary>
    /// 指定方向で挟める相手の石の数をカウント（高速判定用）
    /// </summary>
    private int CountFlippablePiecesInDirection(int startX, int startY, int dx, int dy, PieceType player)
    {
        PieceType opponent = player.GetOpponent();
        int count = 0;

        int curX = startX + dx;
        int curY = startY + dy;

        while (IsWithinBoard(curX, curY))
        {
            PieceType currentPiece = _board[curX, curY];

            if (currentPiece == opponent)
            {
                count++;
            }
            else if (currentPiece == player)
            {
                // 自分の石に到達：1個以上の相手の石を挟んでいれば成立
                return count;
            }
            else
            {
                // 空白マスに突き当たった：挟めない
                return 0;
            }

            curX += dx;
            curY += dy;
        }

        // 盤面外に出た場合も不成立
        return 0;
    }

    /// <summary>
    /// 指定方向で挟める相手の石の座標をリストに追加
    /// </summary>
    private void GetFlippablePiecesInDirection(int startX, int startY, int dx, int dy, PieceType player, List<BoardPosition> results)
    {
        PieceType opponent = player.GetOpponent();
        List<BoardPosition> potentialFlips = new List<BoardPosition>();

        int curX = startX + dx;
        int curY = startY + dy;

        while (IsWithinBoard(curX, curY))
        {
            PieceType currentPiece = _board[curX, curY];

            if (currentPiece == opponent)
            {
                potentialFlips.Add(new BoardPosition(curX, curY));
            }
            else if (currentPiece == player)
            {
                // 自分の石で挟めたので、蓄積した座標を本番リストに追加
                results.AddRange(potentialFlips);
                return;
            }
            else
            {
                // 空白マスなら終了
                return;
            }

            curX += dx;
            curY += dy;
        }
    }

    #endregion
}