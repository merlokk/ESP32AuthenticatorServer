namespace AuthServer.Firmware.Domain;

/// <summary>A board the firmware is built for (firmware devices/config.h BoardId): one image fits one board.</summary>
internal sealed record Board(string Id, string Name);

internal static class Boards
{
    public static readonly IReadOnlyList<Board> All =
    [
        new("x4pro", "XTEINK X4 Pro"),
        new("dev", "ESP32-S3 dev board"),
    ];

    public static bool IsKnown(string? id) => All.Any(board => board.Id == id);
}
