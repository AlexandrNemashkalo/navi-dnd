namespace NaviDnD;

public interface IGridSymbolProvider
{
    char Vertical { get; }

    char Horizontal { get; }

    char GetCorner(string cornerCode);
}

public enum CornerType
{
    TopLeft,        // ┌
    TopRight,       // ┐
    BottomLeft,     // └
    BottomRight,    // ┘
    TopMiddle,      // ┬
    BottomMiddle,   // ┴
    LeftMiddle,     // ├
    RightMiddle,    // ┤
    Cross,          // ┼
    None            // нет угла
}
