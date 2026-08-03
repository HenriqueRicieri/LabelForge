using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Starters;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

/// <summary>One starter as the gallery shows it: what it is, and a picture of it.</summary>
public sealed partial class StarterCardViewModel : ViewModelBase
{
    public StarterCardViewModel(StarterLabel starter) => Starter = starter;

    public StarterLabel Starter { get; }

    public string Name => Starter.Name;

    public string Summary => Starter.Summary;

    public string SizeText => Starter.SizeText;

    /// <summary>The rendered label, or null until the render finishes. Null is a real
    /// state rather than a failure: the pictures arrive one at a time so the window opens
    /// at once instead of after every engine pass.</summary>
    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }
}

/// <summary>
/// The gallery behind "New from Sample": every starter, drawn rather than described.
///
/// The pictures are the feature. A list of names cannot tell somebody whether the shipping
/// label is the one with the big address block, and a starter that renders empty or runs
/// off the stock is exactly what a picture shows and a name hides.
///
/// Each is rendered through the same path as the canvas, preview ZPL with the sample values
/// substituted, at the density the designer is currently set to. So what the card shows is
/// what picking it produces, module rounding and all, rather than an artist's impression
/// stored beside the code.
/// </summary>
public sealed partial class StarterGalleryViewModel : ViewModelBase
{
    private readonly IZplRenderer _renderer = new BinaryKitsRenderer();
    private readonly int _dpmm;
    private bool _closed;

    public StarterGalleryViewModel(int dpmm)
    {
        _dpmm = dpmm;
        Cards = [.. StarterCatalog.All.Select(s => new StarterCardViewModel(s))];
        Selected = Cards.FirstOrDefault();
    }

    public IReadOnlyList<StarterCardViewModel> Cards { get; }

    [ObservableProperty]
    public partial StarterCardViewModel? Selected { get; set; }

    /// <summary>The density every card is drawn at, said out loud: a starter is built at
    /// whatever the designer is set to, and that is not a detail on a 600 dpi machine.</summary>
    public string DensityText
    {
        get
        {
            DensityOption? density = DensityOption.Standard.FirstOrDefault(d => d.Dpmm == _dpmm);
            return density is null
                ? $"Drawn at {_dpmm} dots per mm, the density the designer is set to."
                : $"Drawn at {density.Dpi} dpi, the density the designer is set to.";
        }
    }

    /// <summary>Renders the cards one at a time, off the UI thread, so the window is up
    /// and scrollable while the engine works through them.</summary>
    public async Task LoadPreviewsAsync()
    {
        foreach (StarterCardViewModel card in Cards)
        {
            StarterLabel starter = card.Starter;
            int dpmm = _dpmm;
            byte[] png = await Task.Run(() => Render(starter, dpmm)).ConfigureAwait(true);
            if (_closed || png.Length == 0)
            {
                continue;
            }

            using var stream = new MemoryStream(png);

            // Decoded down to card size rather than at full resolution: a 4 by 6 inch label
            // at 600 dpi is a 35 MB bitmap, and five of those to draw five thumbnails is
            // memory spent on pixels nothing will ever show.
            card.Preview = Bitmap.DecodeToWidth(stream, 640);
        }
    }

    /// <summary>Called when the dialog closes. Five decoded labels is real memory and the
    /// gallery is opened over and over, so the pictures go with the window; the flag also
    /// stops the renders still queued behind it from decoding more.</summary>
    public void ReleasePreviews()
    {
        _closed = true;
        foreach (StarterCardViewModel card in Cards)
        {
            Bitmap? preview = card.Preview;
            card.Preview = null;
            preview?.Dispose();
        }
    }

    private byte[] Render(StarterLabel starter, int dpmm)
    {
        try
        {
            LabelDocument document = starter.Create(dpmm);
            string zpl = new TemplateSubstitutor(document.Markers).Substitute(
                new ZplGenerator().GeneratePreview(document, 0),
                inner => VariableValues.ForPreview(document, inner, DateTime.Now));

            return _renderer.Render(zpl, document.WidthMm, document.HeightMm, dpmm).Png;
        }
        catch (Exception)
        {
            // A card with no picture still names its starter and still creates it. Taking
            // the gallery down because one render failed would be the worse trade.
            return [];
        }
    }
}
