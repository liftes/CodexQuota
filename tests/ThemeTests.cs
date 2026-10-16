using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuota;

internal static class ThemeTests
{
    private static int count;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }
    private static void Pump()
    {
        DispatcherFrame frame_ = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame_.Continue = false; }));
        Dispatcher.PushFrame(frame_);
    }
    private static double Linear(byte channel)
    {
        double component_ = channel / 255.0;
        return component_ <= .04045 ? component_ / 12.92 : Math.Pow((component_ + .055) / 1.055, 2.4);
    }
    private static double Contrast(Brush first, Brush second)
    {
        SolidColorBrush first_ = first as SolidColorBrush;
        SolidColorBrush second_ = second as SolidColorBrush;
        if (first_ == null || second_ == null) throw new Exception("Expected a solid brush.");
        Color a_ = first_.Color, b_ = second_.Color;
        double firstL_ = .2126 * Linear(a_.R) + .7152 * Linear(a_.G) + .0722 * Linear(a_.B);
        double secondL_ = .2126 * Linear(b_.R) + .7152 * Linear(b_.G) + .0722 * Linear(b_.B);
        return (Math.Max(firstL_, secondL_) + .05) / (Math.Min(firstL_, secondL_) + .05);
    }
    private static bool Same(Brush first, Brush second)
    {
        return first is SolidColorBrush && second is SolidColorBrush && ((SolidColorBrush)first).Color == ((SolidColorBrush)second).Color;
    }
    private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        List<T> result_ = new List<T>();
        for (int index_ = 0; index_ < VisualTreeHelper.GetChildrenCount(root); index_++)
        {
            DependencyObject child_ = VisualTreeHelper.GetChild(root,index_);
            if (child_ is T) result_.Add((T)child_);
            result_.AddRange(Descendants<T>(child_));
        }
        return result_;
    }
    [STAThread]
    private static int Main()
    {
        Application application_ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window window_ = null;
        try
        {
            UiTheme.Apply("Light"); // Loads the real XAML, including all control templates.
            Check(UiTheme.IsLight, "Explicit Light mode applies.");
            StackPanel panel_ = new StackPanel { Margin = new Thickness(12) };
            TextBox textbox_ = new TextBox { Text = "Readable editor" };
            Button button_ = new Button { Content = "Readable button" };
            ComboBox combo_ = new ComboBox { ItemsSource = new[] { "Selected option", "Other option" }, SelectedIndex = 0 };
            Expander expander_ = new Expander { Header = "Readable header", IsExpanded = true, Content = new TextBlock { Text = "Readable body" } };
            CheckBox check_ = new CheckBox { Content = "Readable checkbox", IsChecked = true };
            Slider slider_ = new Slider { Minimum=0,Maximum=100,Value=50 };
            foreach (UIElement control_ in new UIElement[] { textbox_,button_,combo_,expander_,check_,slider_ }) panel_.Children.Add(control_);
            window_ = new Window { Content=panel_, Width=360,Height=360,Opacity=0,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.ToolWindow };
            UiTheme.ApplyWindow(window_);
            window_.Show(); Pump(); window_.UpdateLayout();
            foreach (Control control_ in new Control[] { textbox_,button_,combo_,expander_,check_,slider_ })
                Check(control_.Template != null && control_.ApplyTemplate() == false, control_.GetType().Name + " template has been applied by layout.");
            Popup popup_ = (Popup)combo_.Template.FindName("PART_Popup",combo_);
            Check(popup_ != null, "ComboBox exposes its required popup part.");
            popup_.Opacity = 0;
            foreach (string mode_ in new[] { "Light", "Dark", "Light" })
            {
                UiTheme.Apply(mode_); Pump(); window_.UpdateLayout();
                ResourceDictionary resources_ = application_.Resources;
                Brush text_ = (Brush)resources_["QuotaText"];
                Brush control_ = (Brush)resources_["QuotaControl"];
                Brush surface_ = (Brush)resources_["QuotaSurface"];
                Check(Same(window_.Foreground,text_) && Same(window_.Background,surface_),mode_ + " window follows dynamic resources.");
                Check(Same(textbox_.Foreground,text_) && Same(textbox_.Background,control_),mode_ + " TextBox follows resources.");
                Check(Same(textbox_.CaretBrush,text_),mode_ + " TextBox caret remains visible.");
                Check(Same(button_.Foreground,text_) && Same(button_.Background,control_),mode_ + " Button follows resources.");
                Check(Same(combo_.Foreground,text_) && Same(combo_.Background,control_),mode_ + " ComboBox selection follows resources.");
                Check(Same(expander_.Foreground,text_) && Same(check_.Foreground,text_),mode_ + " Expander and checkbox follow resources.");
                Check(Contrast(text_,control_) >= 4.5 && Contrast(text_,surface_) >= 4.5,mode_ + " normal text contrast exceeds 4.5.");
                Check(Contrast(text_,(Brush)resources_["QuotaHover"]) >= 4.5 && Contrast(text_,(Brush)resources_["QuotaSelected"]) >= 4.5,mode_ + " highlighted and selected text contrast exceeds 4.5.");
                combo_.IsDropDownOpen = true; Pump();
                Check(popup_.IsOpen && popup_.Child != null,mode_ + " dropdown opens with a visual child.");
                Border popupBorder_ = popup_.Child as Border;
                Check(popupBorder_ != null && Same(popupBorder_.Background,control_),mode_ + " dropdown surface follows resources.");
                ComboBoxItem selected_ = combo_.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;
                Check(selected_ != null && selected_.IsSelected && Same(selected_.Foreground,text_),mode_ + " selected dropdown item remains readable.");
                if (selected_ != null)
                {
                    Border itemBorder_ = selected_.Template.FindName("ItemBorder",selected_) as Border;
                    Check(itemBorder_ != null && Same(itemBorder_.Background,(Brush)resources_["QuotaSelected"]),mode_ + " selected popup background follows resources.");
                }
                ContentPresenter body_ = expander_.Template.FindName("ExpandedBody",expander_) as ContentPresenter;
                Check(body_ != null && body_.Visibility == Visibility.Visible,mode_ + " expanded body is visible.");
                TextBlock checkGlyph_ = check_.Template.FindName("CheckGlyph",check_) as TextBlock;
                Check(checkGlyph_ != null && checkGlyph_.Text == "\u2713" && checkGlyph_.Visibility == Visibility.Visible && Same(checkGlyph_.Foreground,text_),mode_ + " checked glyph remains readable.");
                bool arrowFound_ = false;
                foreach (TextBlock block_ in Descendants<TextBlock>(combo_)) if (block_.Text == "\u25BE") arrowFound_ = true;
                Check(arrowFound_,mode_ + " dropdown arrow is a real glyph rather than a question mark.");
                bool minusFound_ = false;
                foreach (TextBlock block_ in Descendants<TextBlock>(expander_)) if (block_.Text == "\u2212") minusFound_ = true;
                Check(minusFound_,mode_ + " expanded header uses the real minus glyph.");
                foreach (TextBlock block_ in Descendants<TextBlock>(expander_))
                    Check(Same(block_.Foreground,text_),mode_ + " expander text inherits readable foreground: " + block_.Text);
                combo_.IsDropDownOpen = false; Pump();
            }
            Console.WriteLine("PASS: " + count + " real control template and theme checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_);
            return 1;
        }
        finally
        {
            if (window_ != null) window_.Close();
            application_.Shutdown();
        }
    }
}
