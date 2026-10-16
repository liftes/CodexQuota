using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodexQuota
{
    public static class UiTheme
    {
        private static bool stylesInstalled;
        public static bool IsLight { get; private set; }
        public static event Action ThemeChanged;
        public static void Apply(string mode)
        {
            bool isLight_ = mode == "Light" || (mode != "Dark" && ReadApplicationLightTheme());
            IsLight = isLight_;
            ResourceDictionary resources_ = Application.Current.Resources;
            SetBrush(resources_,"QuotaSurface",isLight_ ? "#FFF5F5F5" : "#FF232323");
            SetBrush(resources_,"QuotaText",isLight_ ? "#FF202020" : "#FFF0F0F0");
            SetBrush(resources_,"QuotaControl",isLight_ ? "#FFFFFFFF" : "#FF343434");
            SetBrush(resources_,"QuotaBorder",isLight_ ? "#FF8C8C8C" : "#FF777777");
            SetBrush(resources_,"QuotaHover",isLight_ ? "#FFE3E8EE" : "#FF484F58");
            SetBrush(resources_,"QuotaSelected",isLight_ ? "#FFD4E4F6" : "#FF3C526A");
            SetBrush(resources_,"QuotaAccent",isLight_ ? "#FF276FAB" : "#FF8EBCE8");
            SetBrush(resources_,"QuotaPreview",isLight_ ? "#FFE7E7E7" : "#FF161616");
            SetBrush(resources_,"QuotaTooltipSurface",isLight_ ? "#FFF0F0F0" : "#FF2C2C2C");
            SetBrush(resources_,"QuotaTooltipText",isLight_ ? "#FF151515" : "#FFF7F7F7");
            SetBrush(resources_,"QuotaTooltipSecondary",isLight_ ? "#FF555555" : "#FFBDBDBD");
            SetBrush(resources_,"QuotaTooltipBorder",isLight_ ? "#FFCBCBCB" : "#FF535353");
            if (SystemParameters.HighContrast)
            {
                resources_["QuotaSurface"] = SystemColors.WindowBrush;
                resources_["QuotaText"] = SystemColors.WindowTextBrush;
                resources_["QuotaControl"] = SystemColors.WindowBrush;
                resources_["QuotaBorder"] = SystemColors.WindowTextBrush;
                resources_["QuotaHover"] = SystemColors.HighlightBrush;
                resources_["QuotaSelected"] = SystemColors.HighlightBrush;
                resources_["QuotaAccent"] = SystemColors.HighlightBrush;
            }
            if (!stylesInstalled)
            {
                resources_.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(styleMarkup));
                stylesInstalled = true;
            }
            Action changed_ = ThemeChanged;
            if (changed_ != null) changed_();
        }
        public static void ApplyWindow(Window window)
        {
            window.SetResourceReference(Control.ForegroundProperty,"QuotaText");
            window.SetResourceReference(Control.BackgroundProperty,"QuotaSurface");
        }
        private static void SetBrush(ResourceDictionary resources, string key, string color)
        {
            SolidColorBrush brush_ = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush_.Freeze(); resources[key] = brush_;
        }
        private static bool ReadApplicationLightTheme()
        {
            try
            {
                using (RegistryKey key_ = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                    return key_ == null || Convert.ToInt32(key_.GetValue("AppsUseLightTheme",1),CultureInfo.InvariantCulture) != 0;
            }
            catch { return true; }
        }
        private const string styleMarkup = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='{x:Type TextBlock}'><Setter Property='Foreground' Value='{DynamicResource QuotaText}'/></Style>
 <Style TargetType='{x:Type Button}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/><Setter Property='Background' Value='{DynamicResource QuotaControl}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource QuotaBorder}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='8,4'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
   <Border x:Name='ButtonBorder' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3'>
    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
   </Border><ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ButtonBorder' Property='Background' Value='{DynamicResource QuotaHover}'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ButtonBorder' Property='BorderBrush' Value='{DynamicResource QuotaAccent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type TextBox}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/><Setter Property='Background' Value='{DynamicResource QuotaControl}'/>
  <Setter Property='CaretBrush' Value='{DynamicResource QuotaText}'/><Setter Property='BorderBrush' Value='{DynamicResource QuotaBorder}'/>
  <Setter Property='SelectionBrush' Value='{DynamicResource QuotaAccent}'/><Setter Property='Padding' Value='4'/>
 </Style>
 <Style TargetType='{x:Type ComboBoxItem}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/><Setter Property='Background' Value='{DynamicResource QuotaControl}'/><Setter Property='Padding' Value='7,5'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBoxItem}'>
   <Border x:Name='ItemBorder' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource QuotaHover}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource QuotaSelected}'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type ComboBox}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/><Setter Property='Background' Value='{DynamicResource QuotaControl}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource QuotaBorder}'/><Setter Property='Padding' Value='7,5'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBox}'>
   <Grid>
    <ToggleButton x:Name='DropToggle' Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
     <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
      <Border x:Name='ToggleBorder' Background='{DynamicResource QuotaControl}' BorderBrush='{DynamicResource QuotaBorder}' BorderThickness='1' CornerRadius='3'>
       <TextBlock Text='&#x25BE;' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'/>
      </Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ToggleBorder' Property='Background' Value='{DynamicResource QuotaHover}'/></Trigger></ControlTemplate.Triggers>
     </ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='8,5,26,5' VerticalAlignment='Center' IsHitTestVisible='False'/>
    <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
     <Border Background='{DynamicResource QuotaControl}' BorderBrush='{DynamicResource QuotaBorder}' BorderThickness='1' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
      <ScrollViewer MaxHeight='320' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
     </Border>
    </Popup>
   </Grid><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type Expander}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Expander}'>
   <StackPanel><ToggleButton IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' HorizontalContentAlignment='Stretch'>
    <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
     <Border Background='Transparent' Padding='0,5'><DockPanel>
      <TextBlock x:Name='ExpanderGlyph' Text='+' Width='20'/><ContentPresenter/>
     </DockPanel></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='ExpanderGlyph' Property='Text' Value='&#x2212;'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></ToggleButton.Template><ContentPresenter Content='{TemplateBinding Header}'/>
   </ToggleButton><ContentPresenter x:Name='ExpandedBody' Visibility='Collapsed' ContentSource='Content'/></StackPanel>
   <ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='ExpandedBody' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type CheckBox}'>
  <Setter Property='Foreground' Value='{DynamicResource QuotaText}'/><Setter Property='Padding' Value='4,3'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'>
   <DockPanel Background='Transparent'><Border Width='15' Height='15' Margin='0,0,7,0' VerticalAlignment='Center' Background='{DynamicResource QuotaControl}' BorderBrush='{DynamicResource QuotaBorder}' BorderThickness='1' CornerRadius='2'>
    <TextBlock x:Name='CheckGlyph' Text='&#x2713;' Visibility='Collapsed' HorizontalAlignment='Center' VerticalAlignment='Center'/>
   </Border><ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></DockPanel>
   <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='CheckGlyph' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type Slider}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
   <Grid Height='24' Background='Transparent'>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
     <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='3' Background='{DynamicResource QuotaAccent}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='3' Background='{DynamicResource QuotaBorder}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
     <Track.Thumb><Thumb Width='14' Height='18'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='{DynamicResource QuotaControl}' BorderBrush='{DynamicResource QuotaAccent}' BorderThickness='1' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    </Track>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <ControlTemplate x:Key='QuotaHoverCardTemplate' TargetType='{x:Type ContentControl}'>
  <Border x:Name='TooltipSurface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'>
   <ContentPresenter RecognizesAccessKey='False'/>
  </Border>
 </ControlTemplate>
 <Style x:Key='QuotaHoverCardStyle' TargetType='{x:Type ContentControl}'>
  <Setter Property='Background' Value='{DynamicResource QuotaTooltipSurface}'/><Setter Property='Foreground' Value='{DynamicResource QuotaTooltipText}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource QuotaTooltipBorder}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,12'/>
  <Setter Property='Template' Value='{StaticResource QuotaHoverCardTemplate}'/>
 </Style>
 <Style TargetType='{x:Type ToolTip}' BasedOn='{StaticResource QuotaHoverCardStyle}'>
  <Setter Property='HasDropShadow' Value='False'/><Setter Property='MaxWidth' Value='280'/>
 </Style>
 <Style TargetType='{x:Type Separator}'><Setter Property='Background' Value='{DynamicResource QuotaBorder}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Height='1' Background='{TemplateBinding Background}'/></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>";
    }
}
