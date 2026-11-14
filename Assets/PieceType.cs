// Assets/Scripts/Logic/PieceType.cs
public enum PieceType
{
    None = 0,
    Black = 1,
    White = 2
}

public static class PieceTypeExtensions
{
    /// <summary>
    /// ‘Šè‚ÌÎ‚ÌF‚ğæ“¾‚·‚é
    /// </summary>
    public static PieceType GetOpponent(this PieceType piece)
    {
        return piece switch
        {
            PieceType.Black => PieceType.White,
            PieceType.White => PieceType.Black,
            _ => PieceType.None
        };
    }
}