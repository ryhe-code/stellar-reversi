// Assets/Scripts/Logic/BoardPosition.cs
using System;

[Serializable]
public readonly struct BoardPosition : IEquatable<BoardPosition>
{
    public readonly int X;
    public readonly int Y;

    public BoardPosition(int x, int y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(BoardPosition other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is BoardPosition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
