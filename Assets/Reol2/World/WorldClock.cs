using System;

namespace Reol2.World
{
    internal sealed class WorldClock
    {
        internal const int Hour = 3600;
        internal const int Day = 24 * Hour;
        internal const int Year = 120 * Day;
        internal long Tick { get; private set; } = 9 * Hour;
        internal float DayFraction => (Tick % Day) / (float)Day;
        internal bool Paused { get; set; }

        internal void Advance(long seconds)
        {
            if (seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Paused)
                Tick = checked(Tick + seconds);
        }
    }
}
