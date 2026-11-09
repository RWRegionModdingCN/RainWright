namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Allocation-free, game-neutral render preparation accumulator. All enabled rooms
/// must have either Pending or Ready bakes before the scheduler may wait; missing,
/// failed, invalid and empty inputs fail closed instead of producing partial output.
/// </summary>
internal struct PlayerMapBakeReadiness
{
    internal int EnabledRooms { get; private set; }
    internal int ReadyRooms { get; private set; }
    internal int PendingRooms { get; private set; }
    internal bool HasFailedRoom { get; private set; }

    internal bool HardFailure => HasFailedRoom || EnabledRooms == 0;
    internal bool NeedsPreparation => !HardFailure && PendingRooms > 0;

    internal void Observe(RoomMapBakeStatus status)
    {
        EnabledRooms++;
        switch (status)
        {
            case RoomMapBakeStatus.Ready:
                ReadyRooms++;
                break;
            case RoomMapBakeStatus.Pending:
                PendingRooms++;
                break;
            default:
                // Missing, Failed and unknown future enum values must not
                // allow a partial render to replace known-good map output.
                HasFailedRoom = true;
                break;
        }
    }
}
