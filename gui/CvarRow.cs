using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CSGORestore;

/// <summary>指令面板里的一行：名称 + 说明 + 滑块 / 开关 + 当前值。</summary>
public sealed class CvarRow : Grid
{
    public Cvar Def { get; }

    private readonly Slider? _slider;
    private readonly ToggleButton? _toggle;
    private readonly TextBlock _valueText;
    private bool _sync;

    public event EventHandler? ValueChanged;

    public CvarRow(Cvar def)
    {
        Def = def;
        Margin = new Thickness(0, 0, 0, 15);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = def.Label,
            FontSize = 13,
            Foreground = Brush("Text"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var code = new TextBlock
        {
            Text = def.Name,
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
            FontSize = 10.5,
            Foreground = Brush("Muted"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(title);
        left.Children.Add(code);

        _valueText = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
            FontSize = 12.5,
            Foreground = Brush("Accent"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 46,
            TextAlignment = TextAlignment.Right
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(left);

        if (def.Kind == CvarKind.Toggle)
        {
            _toggle = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["Pill"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = def.Tip
            };
            _toggle.Checked += (_, _) => ToggleChanged();
            _toggle.Unchecked += (_, _) => ToggleChanged();
            Grid.SetColumn(_toggle, 1);
            header.Children.Add(_toggle);
        }
        else
        {
            Grid.SetColumn(_valueText, 1);
            header.Children.Add(_valueText);
        }

        Children.Add(header);

        if (def.Kind == CvarKind.Numeric)
        {
            _slider = new Slider
            {
                Style = (Style)Application.Current.Resources["DarkSlider"],
                Minimum = def.Min,
                Maximum = def.Max,
                SmallChange = def.Step,
                LargeChange = def.Step * 5,
                Margin = new Thickness(0, 7, 0, 0),
                ToolTip = def.Tip
            };
            _slider.ValueChanged += (_, e) => SliderChanged(e.NewValue);
            SetRow(_slider, 1);
            Children.Add(_slider);
        }
        else
        {
            ToolTip = def.Tip;
        }

        Sync();
    }

    private void SliderChanged(double raw)
    {
        if (_sync) return;
        double v = Math.Round(raw / Def.Step) * Def.Step;
        v = Math.Clamp(v, Def.Min, Def.Max);
        Def.Value = v;
        _valueText.Text = v.ToString(Def.Format, CultureInfo.InvariantCulture);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleChanged()
    {
        if (_sync) return;
        Def.Value = _toggle!.IsChecked == true ? 1 : 0;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把 Def.Value 同步到控件上（不触发 ValueChanged）。</summary>
    public void Sync()
    {
        _sync = true;
        if (_toggle != null)
            _toggle.IsChecked = Def.Value > 0.5;
        if (_slider != null)
            _slider.Value = Def.Value;
        _valueText.Text = Def.Value.ToString(Def.Format, CultureInfo.InvariantCulture);
        _sync = false;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
