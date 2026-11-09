namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Direction of one explicit world connection edge.
/// A/B are stable edge endpoints, not implied source/destination names.
/// </summary>
public enum WorldConnectionDirection
{
    Bidirectional,
    AToB,
    BToA
}

