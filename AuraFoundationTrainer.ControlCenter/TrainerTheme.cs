using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AuraFoundationTrainer.ControlCenter;

internal enum TrainerButtonTone
{
    Primary,
    Secondary,
    Danger
}

internal static class TrainerTheme
{
    public static readonly Brush Window = Brush("#101416");
    public static readonly Brush Header = Brush("#151B1E");
    public static readonly Brush Surface = Brush("#192125");
    public static readonly Brush SurfaceRaised = Brush("#202A2F");
    public static readonly Brush Input = Brush("#11181B");
    public static readonly Brush Border = Brush("#35444A");
    public static readonly Brush BorderStrong = Brush("#4A5B61");
    public static readonly Brush Text = Brush("#EAF0F1");
    public static readonly Brush Muted = Brush("#9EADB1");
    public static readonly Brush Accent = Brush("#3DB7A2");
    public static readonly Brush AccentHover = Brush("#52C8B3");
    public static readonly Brush AccentPressed = Brush("#2E9282");
    public static readonly Brush Warning = Brush("#E0A54B");
    public static readonly Brush Danger = Brush("#D76F67");
    public static readonly Brush DangerHover = Brush("#E4847C");
    public static readonly Brush Success = Brush("#70C995");

    public static void Apply(Window window)
    {
        window.Background = Window;
        window.Foreground = Text;
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        window.Resources[SystemColors.WindowBrushKey] = Input;
        window.Resources[SystemColors.WindowTextBrushKey] = Text;
        window.Resources[SystemColors.ControlBrushKey] = SurfaceRaised;
        window.Resources[SystemColors.ControlTextBrushKey] = Text;
        window.Resources[SystemColors.HighlightBrushKey] = AccentPressed;
        window.Resources[SystemColors.HighlightTextBrushKey] = Text;
        window.Resources[typeof(TextBox)] = TextBoxStyle();
        window.Resources[typeof(ComboBox)] = ComboBoxStyle();
        window.Resources[typeof(ComboBoxItem)] = ComboBoxItemStyle();
        window.Resources[typeof(CheckBox)] = CheckBoxStyle();
        window.Resources[typeof(ProgressBar)] = ProgressBarStyle();
        window.Resources[typeof(TabControl)] = TabControlStyle();
        window.Resources[typeof(TabItem)] = TabItemStyle();
    }

    public static Style ButtonStyle(TrainerButtonTone tone)
    {
        var background = tone switch
        {
            TrainerButtonTone.Primary => Accent,
            TrainerButtonTone.Danger => Danger,
            _ => SurfaceRaised
        };
        var foreground = tone == TrainerButtonTone.Primary
            ? Brush("#071512")
            : Text;
        var hover = tone switch
        {
            TrainerButtonTone.Primary => AccentHover,
            TrainerButtonTone.Danger => DangerHover,
            _ => Brush("#29363B")
        };
        var pressed = tone switch
        {
            TrainerButtonTone.Primary => AccentPressed,
            TrainerButtonTone.Danger => Brush("#B85A54"),
            _ => Brush("#151D20")
        };

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderStrong));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 0, 14, 0)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34d));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate()));

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hover));
        style.Triggers.Add(hoverTrigger);

        var pressedTrigger = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };
        pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, pressed));
        style.Triggers.Add(pressedTrigger);

        var disabledTrigger = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38d));
        disabledTrigger.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
        style.Triggers.Add(disabledTrigger);
        return style;
    }

    public static Border ContentSurface(UIElement child)
    {
        return new Border
        {
            Background = Window,
            BorderBrush = Border,
            BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(20, 16, 20, 18),
            Child = child
        };
    }

    private static Style TextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 3, 9, 3)));
        style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Accent));
        style.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, AccentPressed));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var focus = new Trigger
        {
            Property = UIElement.IsKeyboardFocusWithinProperty,
            Value = true
        };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        style.Triggers.Add(focus);
        return style;
    }

    private static Style CheckBoxStyle()
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28d));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return style;
    }

    private static Style ComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 2, 8, 2)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var focus = new Trigger
        {
            Property = UIElement.IsKeyboardFocusWithinProperty,
            Value = true
        };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        style.Triggers.Add(focus);
        return style;
    }

    private static Style ComboBoxItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        var selected = new Trigger
        {
            Property = Selector.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, AccentPressed));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style ProgressBarStyle()
    {
        var style = new Style(typeof(ProgressBar));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Input));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    private static Style TabControlStyle()
    {
        var style = new Style(typeof(TabControl));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Window));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        return style;
    }

    private static Style TabItemStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Header));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 10, 18, 9)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.TemplateProperty, TabItemTemplate()));

        var selected = new Trigger
        {
            Property = Selector.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 2)));
        style.Triggers.Add(selected);

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Triggers.Add(hover);
        return style;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(
            System.Windows.Controls.Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(
            System.Windows.Controls.Border.BorderBrushProperty,
            new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(
            System.Windows.Controls.Border.BorderThicknessProperty,
            new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(
            System.Windows.Controls.Border.CornerRadiusProperty,
            new CornerRadius(5));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        presenter.SetValue(
            ContentPresenter.MarginProperty,
            new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };
    }

    private static ControlTemplate TabItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(
            System.Windows.Controls.Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(
            System.Windows.Controls.Border.BorderBrushProperty,
            new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(
            System.Windows.Controls.Border.BorderThicknessProperty,
            new TemplateBindingExtension(Control.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        presenter.SetValue(
            ContentPresenter.MarginProperty,
            new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(TabItem))
        {
            VisualTree = border
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
