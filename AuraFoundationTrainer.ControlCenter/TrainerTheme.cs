using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        style.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty, 320d));
        style.Setters.Add(new Setter(Control.TemplateProperty, ComboBoxTemplate()));

        var focus = new Trigger
        {
            Property = UIElement.IsKeyboardFocusWithinProperty,
            Value = true
        };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        style.Triggers.Add(focus);

        var open = new Trigger
        {
            Property = ComboBox.IsDropDownOpenProperty,
            Value = true
        };
        open.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        style.Triggers.Add(open);

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabled.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, Muted));
        disabled.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        style.Triggers.Add(disabled);
        return style;
    }

    private static Style ComboBoxItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceRaised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(
            Control.HorizontalContentAlignmentProperty,
            HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.TemplateProperty, ComboBoxItemTemplate()));

        var highlighted = new Trigger
        {
            Property = ComboBoxItem.IsHighlightedProperty,
            Value = true
        };
        highlighted.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush("#2A383D")));
        style.Triggers.Add(highlighted);

        var selected = new Trigger
        {
            Property = Selector.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, AccentPressed));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Text));
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

    private static ControlTemplate ComboBoxTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));

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
            new CornerRadius(2));

        var selectedContent = new FrameworkElementFactory(typeof(ContentPresenter));
        selectedContent.SetValue(
            ContentPresenter.ContentProperty,
            new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        selectedContent.SetValue(
            ContentPresenter.ContentTemplateProperty,
            new TemplateBindingExtension(
                ComboBox.SelectionBoxItemTemplateProperty));
        selectedContent.SetValue(
            ContentPresenter.ContentTemplateSelectorProperty,
            new TemplateBindingExtension(
                ComboBox.ItemTemplateSelectorProperty));
        selectedContent.SetValue(
            ContentPresenter.MarginProperty,
            new Thickness(8, 2, 34, 2));
        selectedContent.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        selectedContent.SetValue(
            ContentPresenter.IsHitTestVisibleProperty,
            false);
        border.AppendChild(selectedContent);
        grid.AppendChild(border);

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(FrameworkElement.FocusableProperty, false);
        toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(ComboBox.IsDropDownOpen))
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });
        toggle.SetValue(Control.TemplateProperty, TransparentToggleTemplate());
        grid.AppendChild(toggle);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "\u25BE");
        arrow.SetValue(
            TextBlock.ForegroundProperty,
            new TemplateBindingExtension(Control.ForegroundProperty));
        arrow.SetValue(TextBlock.FontSizeProperty, 12d);
        arrow.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            HorizontalAlignment.Right);
        arrow.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(arrow);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetValue(Popup.StaysOpenProperty, false);
        popup.SetValue(FrameworkElement.FocusableProperty, false);
        popup.SetValue(
            Popup.IsOpenProperty,
            new Binding(nameof(ComboBox.IsDropDownOpen))
            {
                RelativeSource = RelativeSource.TemplatedParent
            });

        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(
            System.Windows.Controls.Border.BackgroundProperty,
            SurfaceRaised);
        popupBorder.SetValue(
            System.Windows.Controls.Border.BorderBrushProperty,
            BorderStrong);
        popupBorder.SetValue(
            System.Windows.Controls.Border.BorderThicknessProperty,
            new Thickness(1));
        popupBorder.SetValue(
            System.Windows.Controls.Border.CornerRadiusProperty,
            new CornerRadius(2));
        popupBorder.SetValue(
            FrameworkElement.MinWidthProperty,
            new Binding(FrameworkElement.ActualWidthProperty.Name)
            {
                RelativeSource = RelativeSource.TemplatedParent
            });
        popupBorder.SetValue(
            FrameworkElement.MaxHeightProperty,
            new Binding(nameof(ComboBox.MaxDropDownHeight))
            {
                RelativeSource = RelativeSource.TemplatedParent
            });

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        scroll.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        grid.AppendChild(popup);

        return new ControlTemplate(typeof(ComboBox))
        {
            VisualTree = grid
        };
    }

    private static ControlTemplate TransparentToggleTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(
            System.Windows.Controls.Border.BackgroundProperty,
            Brushes.Transparent);
        return new ControlTemplate(typeof(ToggleButton))
        {
            VisualTree = border
        };
    }

    private static ControlTemplate ComboBoxItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(
            System.Windows.Controls.Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            ContentPresenter.MarginProperty,
            new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            new TemplateBindingExtension(
                Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(ComboBoxItem))
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
