using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>Independent Player Map export and rendering result contract.</summary>
public sealed class PlayerMapRenderReport
{
    public static readonly PlayerMapRenderReport Empty = new();

    public bool Attempted { get; init; }
    public bool Success { get; init; }
    public bool Exported { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int IncludedRooms { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string[] Errors { get; init; } = Array.Empty<string>();
    public string[] Warnings { get; init; } = Array.Empty<string>();

    internal static PlayerMapRenderReport Failure(string message, params string[] errors) => new()
    {
        Attempted = true,
        Success = false,
        Message = message ?? "Render failed.",
        Errors = errors ?? Array.Empty<string>()
    };
}
