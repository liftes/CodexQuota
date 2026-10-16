using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CodexQuota
{
    // Inline ContentControl shares its template and content with the popup.
    public sealed class HoverDesignControls : Expander, IDisposable
    {
        private readonly UserSettings settings;
        private readonly Action changed;
        private readonly List<NumberEditor> editors = new List<NumberEditor>();
        private readonly ContentControl preview;
        private bool disposed;
        private bool refreshing;

        public HoverDesignControls(UserSettings settings, Action changed)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            this.changed = changed;
            Name = "HoverDesignExpander";
            SetResourceReference(FrameworkElement.StyleProperty,typeof(Expander));
            Header = "悬浮窗设计";
            IsExpanded = false;
            Margin = new Thickness(0, 10, 0, 4);
            StackPanel panel_ = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            Content = panel_;
            TextBlock help_ = MakeText("字号和内容宽度以 DIP 为单位；输入或拖动后即时预览并保存。", 12);
            help_.Margin = new Thickness(0, 0, 0, 6);
            panel_.Children.Add(help_);
            AddEditor(panel_, "标题字号", "HoverTitle", 10, 20, 1,
                () => settings.HoverTitleFontSize, value_ => settings.HoverTitleFontSize = value_);
            AddEditor(panel_, "额度数字字号", "HoverValue", 16, 40, 1,
                () => settings.HoverValueFontSize, value_ => settings.HoverValueFontSize = value_);
            AddEditor(panel_, "明细字号", "HoverDetail", 10, 24, 1,
                () => settings.HoverDetailFontSize, value_ => settings.HoverDetailFontSize = value_);
            AddEditor(panel_, "内容宽度", "HoverWidth", 240, 480, 10,
                () => settings.HoverContentWidth, value_ => settings.HoverContentWidth = value_);
            WrapPanel presets_ = new WrapPanel { Margin = new Thickness(0, 3, 0, 8) };
            Button compact_ = new Button { Name = "HoverCompactPreset", Content = "紧凑（默认）", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 4) };
            Button relaxed_ = new Button { Name = "HoverRelaxedPreset", Content = "宽松", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 4) };
            compact_.Click += delegate { SetPreset(12, 22, 13, 300); };
            relaxed_.Click += delegate { SetPreset(14, 28, 18, 360); };
            presets_.Children.Add(compact_);
            presets_.Children.Add(relaxed_);
            panel_.Children.Add(presets_);
            TextBlock caption_ = MakeText("示例预览（不代表当前额度）", 12);
            caption_.Margin = new Thickness(0, 0, 0, 6);
            panel_.Children.Add(caption_);
            preview = new ContentControl { Name = "HoverDesignPreview", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Focusable = false };
            preview.SetResourceReference(FrameworkElement.StyleProperty,"QuotaHoverCardStyle");
            ScrollViewer viewport_ = new ScrollViewer
            {
                Name = "HoverPreviewViewport", Content = preview,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320, HorizontalContentAlignment = HorizontalAlignment.Left
            };
            panel_.Children.Add(viewport_);
            Expanded += delegate { Dispatcher.BeginInvoke(new Action(delegate { if (!disposed && IsLoaded) BringIntoView(); })); };
            RefreshPreview();
        }

        private static TextBlock MakeText(string text, double fontSize)
        {
            TextBlock block_ = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap };
            block_.SetResourceReference(TextBlock.ForegroundProperty, "QuotaText");
            return block_;
        }

        private void AddEditor(Panel panel, string label, string name, double minimum, double maximum, double tick, Func<double> read, Action<double> write)
        {
            TextBlock label_ = MakeText(label + "（" + minimum.ToString(CultureInfo.InvariantCulture) + "–" + maximum.ToString(CultureInfo.InvariantCulture) + "）", 12);
            panel.Children.Add(label_);
            DockPanel row_ = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
            TextBox input_ = new TextBox { Name = name + "Input", Text = read().ToString("0.#", CultureInfo.InvariantCulture), Width = 58, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Slider slider_ = new Slider { Name = name + "Slider", Minimum = minimum, Maximum = maximum, Value = read(), TickFrequency = tick, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(input_, Dock.Left);
            row_.Children.Add(input_);
            row_.Children.Add(slider_);
            panel.Children.Add(row_);
            NumberEditor editor_ = new NumberEditor { Input = input_, Slider = slider_, Read = read };
            editors.Add(editor_);
            input_.TextChanged += delegate
            {
                if (refreshing || disposed) return;
                double value_;
                if (!Double.TryParse(input_.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value_) || Double.IsNaN(value_) || Double.IsInfinity(value_) || value_ < minimum || value_ > maximum) return;
                if (Math.Abs(value_ - read()) < 0.000001) return;
                write(value_);
                refreshing = true;
                slider_.Value = value_;
                refreshing = false;
                NotifyChange();
            };
            slider_.ValueChanged += delegate
            {
                if (refreshing || disposed || Math.Abs(slider_.Value - read()) < 0.000001) return;
                write(slider_.Value);
                refreshing = true;
                input_.Text = read().ToString("0.#", CultureInfo.InvariantCulture);
                refreshing = false;
                NotifyChange();
            };
            input_.LostKeyboardFocus += delegate
            {
                refreshing = true;
                input_.Text = read().ToString("0.#", CultureInfo.InvariantCulture);
                refreshing = false;
            };
        }

        private void SetPreset(double title, double value, double detail, double width)
        {
            if (disposed) return;
            settings.HoverTitleFontSize = title;
            settings.HoverValueFontSize = value;
            settings.HoverDetailFontSize = detail;
            settings.HoverContentWidth = width;
            refreshing = true;
            foreach (NumberEditor editor_ in editors)
            {
                editor_.Input.Text = editor_.Read().ToString("0.#", CultureInfo.InvariantCulture);
                editor_.Slider.Value = editor_.Read();
            }
            refreshing = false;
            NotifyChange();
        }

        private void NotifyChange()
        {
            if (changed != null) changed();
            RefreshPreview();
        }

        public void RefreshPreview()
        {
            if (disposed || preview == null) return;
            HoverAppearance appearance_ = new HoverAppearance
            {
                TitleFontSize = settings.HoverTitleFontSize,
                ValueFontSize = settings.HoverValueFontSize,
                DetailFontSize = settings.HoverDetailFontSize,
                ContentWidth = settings.HoverContentWidth
            };
            appearance_.Normalize();
            DateTimeOffset now_ = DateTimeOffset.UtcNow;
            QuotaWindow quota_ = new QuotaWindow { UsedPercent = 32, WindowDurationMins = 300, ResetsAt = now_.AddMinutes(160) };
            preview.Content = QuotaHoverPresenter.BuildContent("五小时额度", quota_, false, now_, appearance_);
            preview.MaxWidth = appearance_.ContentWidth + 32;
            preview.SetResourceReference(Control.BackgroundProperty, "QuotaTooltipSurface");
            preview.SetResourceReference(Control.ForegroundProperty, "QuotaTooltipText");
            preview.SetResourceReference(Control.BorderBrushProperty, "QuotaTooltipBorder");
            preview.ApplyTemplate();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            preview.Content = null;
        }

        private sealed class NumberEditor
        {
            public TextBox Input;
            public Slider Slider;
            public Func<double> Read;
        }
    }
}
