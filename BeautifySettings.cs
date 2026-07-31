namespace QuickOneNote;

internal enum BackgroundKind { Solid, Gradient }

internal enum AspectPreset { Auto, R16x9, Square, Story }

/// <summary>"Stunning image" framing: padded gradient/solid background, rounded corners, shadow.</summary>
internal sealed class BeautifySettings
{
    public bool Enabled;
    public BackgroundKind Kind = BackgroundKind.Gradient;
    public Color Color1 = Color.FromArgb(131, 58, 180);   // gradient start / solid fill
    public Color Color2 = Color.FromArgb(253, 89, 166);   // gradient end
    public int Padding = 56;
    public int CornerRadius = 16;
    public bool Shadow = true;
    public AspectPreset Aspect = AspectPreset.Auto;

    /// <summary>Named gradient presets shown in the Beautify menu.</summary>
    public static readonly (string Name, Color C1, Color C2)[] Gradients =
    {
        ("Purple",  Color.FromArgb(131, 58, 180), Color.FromArgb(253, 89, 166)),
        ("Sunset",  Color.FromArgb(255, 94, 98),  Color.FromArgb(255, 195, 113)),
        ("Ocean",   Color.FromArgb(33, 147, 176), Color.FromArgb(109, 213, 237)),
        ("Mint",    Color.FromArgb(0, 176, 155),  Color.FromArgb(150, 201, 61)),
        ("Slate",   Color.FromArgb(44, 62, 80),   Color.FromArgb(96, 125, 150)),
        ("Peach",   Color.FromArgb(255, 153, 102),Color.FromArgb(255, 94, 98)),
    };

    public static readonly (string Name, Color C)[] Solids =
    {
        ("White",  Color.FromArgb(245, 245, 247)),
        ("Light",  Color.FromArgb(225, 228, 234)),
        ("Dark",   Color.FromArgb(32, 32, 36)),
        ("Black",  Color.FromArgb(12, 12, 14)),
    };
}
