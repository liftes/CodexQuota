using System;

namespace CodexQuota
{
    public sealed class HoverAppearance
    {
        public double TitleFontSize { get; set; }
        public double ValueFontSize { get; set; }
        public double DetailFontSize { get; set; }
        public double ContentWidth { get; set; }

        public HoverAppearance()
        {
            TitleFontSize = 12;
            ValueFontSize = 22;
            DetailFontSize = 13;
            ContentWidth = 300;
        }

        public HoverAppearance Normalize()
        {
            TitleFontSize = NormalizeNumber(TitleFontSize, 12, 10, 20);
            ValueFontSize = NormalizeNumber(ValueFontSize, 22, 16, 40);
            DetailFontSize = NormalizeNumber(DetailFontSize, 13, 10, 24);
            ContentWidth = NormalizeNumber(ContentWidth, 300, 240, 480);
            return this;
        }

        public HoverAppearance Clone()
        {
            return new HoverAppearance { TitleFontSize = TitleFontSize, ValueFontSize = ValueFontSize, DetailFontSize = DetailFontSize, ContentWidth = ContentWidth };
        }

        private static double NormalizeNumber(double value, double fallback, double minimum, double maximum)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
