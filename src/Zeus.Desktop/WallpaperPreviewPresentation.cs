using System.Windows;
using System.Windows.Media;
using Zeus.Windows;

namespace Zeus.Desktop;

/// <summary>Builds a bounded, non-mutating illustration of a Windows wallpaper placement mode.</summary>
public sealed record WallpaperPreviewPresentation(
    Stretch Stretch,
    TileMode TileMode,
    Rect Viewport,
    double Width,
    double Height,
    double TargetAspectRatio,
    string Description)
{
    public static WallpaperPreviewPresentation Create(
        WallpaperPosition? position,
        WallpaperPosition? currentPosition,
        WallpaperMonitorBounds? targetBounds,
        double sourceAspectRatio)
    {
        var aspect = targetBounds is { Width: > 0, Height: > 0 }
            ? (double)targetBounds.Width / targetBounds.Height
            : 16d / 9d;
        if (!double.IsFinite(aspect) || aspect is < 0.25 or > 8) aspect = 16d / 9d;
        if (!double.IsFinite(sourceAspectRatio) || sourceAspectRatio is < 0.05 or > 20) sourceAspectRatio = 16d / 9d;

        var effectivePosition = position ?? currentPosition;
        var (stretch, tileMode, description) = effectivePosition switch
        {
            WallpaperPosition.Fill => (Stretch.UniformToFill, TileMode.None, "Preencher: proporção mantida, com possível corte."),
            WallpaperPosition.Fit => (Stretch.Uniform, TileMode.None, "Ajustar: imagem inteira, com possíveis faixas."),
            WallpaperPosition.Stretch => (Stretch.Fill, TileMode.None, "Esticar: ocupa a área e pode distorcer a imagem."),
            WallpaperPosition.Center => (Stretch.Uniform, TileMode.None, "Centralizar: imagem centralizada na prévia; a escala real depende da resolução original."),
            WallpaperPosition.Tile => (Stretch.Fill, TileMode.Tile, "Lado a lado: repetição ilustrativa; o tamanho final depende dos pixels originais."),
            WallpaperPosition.Span => (Stretch.UniformToFill, TileMode.None, "Estender: imagem ilustrada sobre a proporção conjunta dos monitores."),
            _ => (Stretch.Uniform, TileMode.None, "Ajuste atual desconhecido; a imagem inteira é mostrada sem simulação.")
        };

        const double maxWidth = 640;
        const double maxHeight = 260;
        var width = Math.Min(maxWidth, maxHeight * aspect);
        var height = width / aspect;
        var viewport = Rect.Empty;
        if (tileMode == TileMode.Tile)
        {
            const double tileWidthFraction = 0.24;
            var tileHeightFraction = Math.Clamp(tileWidthFraction * aspect / sourceAspectRatio, 0.06, 0.8);
            viewport = new Rect(0, 0, tileWidthFraction, tileHeightFraction);
        }

        return new WallpaperPreviewPresentation(stretch, tileMode, viewport, width, height, aspect, description);
    }

    public ImageBrush CreateBrush(ImageSource source)
    {
        var brush = new ImageBrush(source)
        {
            Stretch = Stretch,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode
        };
        if (TileMode == TileMode.Tile)
        {
            brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
            brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            brush.Viewport = Viewport;
            brush.Viewbox = new Rect(0, 0, 1, 1);
        }
        brush.Freeze();
        return brush;
    }
}
