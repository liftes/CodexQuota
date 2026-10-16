using System;

namespace CodexQuota
{
    public static class RingLayout
    {
        public static double GroupWidth(double ringSize, double ringGap, int ringCount)
        {
            return ringCount * ringSize + Math.Max(0,ringCount - 1) * ringGap;
        }
        public static double WindowWidth(double ringSize, double ringGap, int ringCount)
        {
            // Preserve the default 78/42 DIP window footprint and calibrated X offset.
            return GroupWidth(ringSize,ringGap,ringCount) + (ringCount > 1 ? 32 : 22);
        }
        public static double RingLeft(double width, double ringSize, double ringGap, int ringCount, int index)
        {
            return (width - GroupWidth(ringSize,ringGap,ringCount)) / 2 + index * (ringSize + ringGap);
        }
    }
}
