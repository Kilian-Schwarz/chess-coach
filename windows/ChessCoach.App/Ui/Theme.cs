using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace ChessCoach.App.Ui;

internal static class Theme
{
    public static readonly Brush PanelBg = F(new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1F, 0x24)));
    public static readonly Brush PanelBorder = F(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush CardBg = F(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Text = F(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF5)));
    public static readonly Brush Muted = F(new SolidColorBrush(Color.FromRgb(0xA0, 0xA3, 0xAD)));
    public static readonly Brush FieldBg = F(new SolidColorBrush(Color.FromRgb(0x2B, 0x2C, 0x33)));

    private static Brush F(SolidColorBrush b) { b.Freeze(); return b; }

    public static Color RatingColor(ChessCoach.Core.MoveRating r) => r switch
    {
        ChessCoach.Core.MoveRating.Best => Color.FromRgb(0x34, 0xC7, 0x59),
        ChessCoach.Core.MoveRating.Good => Color.FromRgb(0x66, 0xD4, 0xCF),
        ChessCoach.Core.MoveRating.Inaccuracy => Color.FromRgb(0xFF, 0xD6, 0x0A),
        ChessCoach.Core.MoveRating.Mistake => Color.FromRgb(0xFF, 0x9F, 0x0A),
        _ => Color.FromRgb(0xFF, 0x45, 0x3A),
    };

    public static string RatingSymbol(ChessCoach.Core.MoveRating r) => r switch
    {
        ChessCoach.Core.MoveRating.Best => "★",
        ChessCoach.Core.MoveRating.Good => "✓",
        ChessCoach.Core.MoveRating.Inaccuracy => "?!",
        ChessCoach.Core.MoveRating.Mistake => "?",
        _ => "??",
    };

    private static Style? _button, _primary, _small;

    private static Style Make(string bg, string hover, string fg, string padding, string fontSize)
    {
        string xaml = $@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
  <Setter Property='Foreground' Value='{fg}'/>
  <Setter Property='FontSize' Value='{fontSize}'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='b' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{bg}' CornerRadius='7' Padding='{padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{hover}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>";
        var s = (Style)XamlReader.Parse(xaml);
        s.Seal();
        return s;
    }

    public static Style PanelButton => _button ??= Make("#3A3B44", "#4A4B56", "#F2F2F5", "10,5", "13");
    public static Style PrimaryButton => _primary ??= Make("#0A84FF", "#3395FF", "#FFFFFF", "10,5", "13");
    public static Style SmallButton => _small ??= Make("#2F3038", "#43444F", "#C8CAD2", "7,2", "11");
}
