using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelForge.App.Rendering;
using LabelForge.Core.Editing;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Templating;
using LabelForge.Core.Zpl;

namespace LabelForge.App.ViewModels;

public partial class DesignerViewModel
{
    // Two static layers can each retain 64 MB, in addition to the ordinary underlay.
    private const long MaxGesturePixels = 16_000_000;
    private GestureSession? _gestureSession;
    private int _previewEpoch;
    private readonly SemaphoreSlim _gesturePreparation = new(1, 1);
    private readonly RenderQueue<MovingLayerRequest, MovingLayerResult> _movingLayerQueue =
        new(RenderMovingLayer);

    [ObservableProperty]
    public partial CanvasGesturePreview? GesturePreview { get; set; }

    [ObservableProperty]
    public partial Vector GestureOffset { get; set; }

    private sealed class GestureSession(
        LabelDocument document, LabelDocument frozen, GestureKind kind,
        IReadOnlyList<Element> live, GestureLayerPlan plan, DotRect viewport,
        string snapshot, DateTime now)
    {
        public LabelDocument Document { get; } = document;
        public LabelDocument Frozen { get; } = frozen;
        public GestureKind Kind { get; } = kind;
        public IReadOnlyList<Element> Live { get; } = live;
        public GestureLayerPlan Plan { get; } = plan;
        public DotRect Viewport { get; } = viewport;
        public DateTime Now { get; } = now;
        public string LatestState { get; set; } = snapshot;
        public string RequestedState { get; set; } = snapshot;
        public bool Ready { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
    }

    private sealed record MovingLayerRequest(
        LabelDocument Document, IReadOnlyList<Element> Elements, DotRect Bounds, DateTime Now);

    private sealed record MovingLayerResult(Bitmap? Bitmap, DotRect Bounds);

    public void BeginGesturePreview(GestureKind kind, IReadOnlyList<Element> elements, string snapshot)
    {
        StopGesturePreview();
        if (Document.Dpmm != 24 || Underlay is null) return;

        GestureLayerPlan livePlan = GestureLayers.Split(Document, elements);
        if (!livePlan.CanComposite) return;

        int margin = Units.MmToDots(ElementPlacement.PasteboardMarginMm, Document.Dpmm);
        var viewport = new DotRect(-margin, -margin,
            Document.WidthDots + 2 * margin, Document.HeightDots + 2 * margin);
        if (!CanRenderLayer(viewport) || livePlan.Moving.Any(e => e.X < -margin || e.Y < -margin)) return;

        var frozen = LabelDocumentJson.Deserialize(LabelDocumentJson.Serialize(Document));
        ElementSnapshot.Restore(snapshot, frozen.Elements);
        var ids = livePlan.Moving.Select(e => e.Id).ToHashSet();
        GestureLayerPlan plan = GestureLayers.Split(frozen, frozen.Elements.Where(e => ids.Contains(e.Id)).ToList());
        var session = new GestureSession(Document, frozen, kind, livePlan.Moving, plan,
            viewport, snapshot, DateTime.Now);

        _previewEpoch++;
        _renderCts?.Cancel();
        _gestureSession = session;
        GestureOffset = default;
        PrepareGesturePreview(session);
    }

    private async void PrepareGesturePreview(GestureSession session)
    {
        Task<Bitmap?>[] tasks = [];
        bool transferred = false;
        bool preparing = false;
        try
        {
            CancellationToken token = session.Cancellation.Token;
            await _gesturePreparation.WaitAsync(token);
            preparing = true;
            MovingLayerRequest moving = CreateMovingRequest(session, session.RequestedState);
            tasks =
            [
                Task.Run(() => RenderGestureLayer(session.Frozen, session.Plan.Below,
                    session.Viewport, session.Now, RenderOutput.Pixels), token),
                Task.Run(() => RenderGestureLayer(moving.Document, moving.Elements,
                    moving.Bounds, moving.Now, RenderOutput.TransparentPixels), token),
                Task.Run(() => RenderGestureLayer(session.Frozen, session.Plan.Above,
                    session.Viewport, session.Now, RenderOutput.TransparentPixels), token),
            ];
            Bitmap?[] images = await Task.WhenAll(tasks);
            if (!ReferenceEquals(_gestureSession, session) || !ReferenceEquals(Document, session.Document)) return;

            GesturePreview = new CanvasGesturePreview(images[0], images[1], images[2],
                session.Viewport, moving.Bounds);
            transferred = true;
            session.Ready = true;
            CanvasRevision++;
            if (session.Kind != GestureKind.Move && session.LatestState != session.RequestedState)
                QueueMovingLayer(session, session.LatestState);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // The ordinary render owns diagnostics and remains available if a layer fails.
            if (ReferenceEquals(_gestureSession, session))
            {
                StopGesturePreview();
                ScheduleRender(delayMs: 0, live: true);
            }
        }
        finally
        {
            if (!transferred)
                foreach (var task in tasks)
                    if (task.IsCompletedSuccessfully) task.Result?.Dispose();
            if (preparing) _gesturePreparation.Release();
        }
    }

    private bool UpdateGesturePreview()
    {
        if (_gestureSession is not { } session) return false;
        if (!ReferenceEquals(Document, session.Document) ||
            Document.WidthDots != session.Frozen.WidthDots || Document.HeightDots != session.Frozen.HeightDots ||
            session.Live.Any(e => !Document.Elements.Contains(e) ||
                e.X < session.Viewport.X || e.Y < session.Viewport.Y))
        {
            StopGesturePreview();
            return false;
        }

        var bounds = new ElementBoundsCalculator();
        PlacementWarning = DescribePlacement(Document.Elements.Where(e => e.IsVisible)
            .Select(e => (Element: e, Status: ElementPlacement.Classify(e, bounds.GetBounds(e), Document)))
            .Where(e => e.Status != PlacementStatus.Inside).ToList());
        CanvasRevision++;

        if (session.Kind != GestureKind.Move)
        {
            string state = ElementSnapshot.Capture(session.Live);
            session.LatestState = state;
            if (session.Ready && state != session.RequestedState) QueueMovingLayer(session, state);
        }
        return true;
    }

    private async void QueueMovingLayer(GestureSession session, string state)
    {
        session.RequestedState = state;
        MovingLayerResult? result = null;
        try
        {
            MovingLayerRequest request = CreateMovingRequest(session, state);
            result = await _movingLayerQueue.RequestAsync(request, session.Cancellation.Token);
            if (result is null) return;
            if (!ReferenceEquals(_gestureSession, session) || GesturePreview is not { } preview) return;
            preview.ReplaceMoving(result.Bitmap, result.Bounds);
            result = null;
            CanvasRevision++;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ReferenceEquals(_gestureSession, session))
            {
                StopGesturePreview();
                ScheduleRender(delayMs: 0, live: true);
            }
        }
        finally
        {
            result?.Bitmap?.Dispose();
        }
    }

    private static MovingLayerRequest CreateMovingRequest(GestureSession session, string state)
    {
        List<Element> elements = LabelDocumentJson.DeserializeElements(state);
        List<Element> measured = LabelDocumentJson.DeserializeElements(state);
        var substitutor = new TemplateSubstitutor();
        string Resolve(string text) => substitutor.Substitute(text,
            inner => VariableValues.ForPreview(session.Frozen, inner, session.Now));
        foreach (Element element in measured)
        {
            switch (element)
            {
                case TextElement text: text.Text = Resolve(text.Text); break;
                case BarcodeElement barcode: barcode.Data = Resolve(barcode.Data); break;
                case QrCodeElement qr: qr.Data = Resolve(qr.Data); break;
                case DataMatrixElement matrix: matrix.Data = Resolve(matrix.Data); break;
                case Pdf417Element pdf: pdf.Data = Resolve(pdf.Data); break;
            }
        }
        DotRect viewport = GestureLayers.GetMovingViewport(measured);
        if (!CanRenderLayer(viewport)) throw new InvalidOperationException("Gesture layer exceeds the pixel budget.");
        return new MovingLayerRequest(session.Frozen, elements, viewport, session.Now);
    }

    private static MovingLayerResult RenderMovingLayer(MovingLayerRequest request, CancellationToken token) =>
        new(RenderGestureLayer(request.Document, request.Elements, request.Bounds,
            request.Now, RenderOutput.TransparentPixels), request.Bounds);

    private static Bitmap? RenderGestureLayer(LabelDocument document, IReadOnlyList<Element> elements,
        DotRect viewport, DateTime now, RenderOutput output)
    {
        if (elements.Count == 0) return null;
        string zpl = new ZplGenerator().GeneratePreviewLayer(document, elements, viewport);
        zpl = new TemplateSubstitutor().Substitute(zpl,
            inner => VariableValues.ForPreview(document, inner, now));
        RenderResult result = new BinaryKitsRenderer().Render(zpl,
            Units.DotsToMm(viewport.Width, document.Dpmm), Units.DotsToMm(viewport.Height, document.Dpmm),
            document.Dpmm, output: output);
        if (!result.HasImage || result.Errors.Count > 0)
            throw new InvalidOperationException("Could not render the gesture layer.");
        return ToBitmap(result);
    }

    private static bool CanRenderLayer(DotRect viewport) =>
        viewport.Width > 0 && viewport.Height > 0 && (long)viewport.Width * viewport.Height <= MaxGesturePixels;

    public void EndGesturePreview(bool committed)
    {
        if (_gestureSession is not { } session) return;
        _gestureSession = null;
        _previewEpoch++;
        session.Cancellation.Cancel();
        session.Cancellation.Dispose();
        if (committed) return;
        ClearGestureImages();
        ScheduleRender(delayMs: 0, live: true);
    }

    private void ClearGestureImages()
    {
        CanvasGesturePreview? previous = GesturePreview;
        GesturePreview = null;
        GestureOffset = default;
        previous?.Dispose();
    }

    private void StopGesturePreview()
    {
        bool hadPreview = _gestureSession is not null || GesturePreview is not null;
        if (_gestureSession is { } session)
        {
            _gestureSession = null;
            session.Cancellation.Cancel();
            session.Cancellation.Dispose();
        }
        if (hadPreview) _previewEpoch++;
        ClearGestureImages();
    }

    partial void OnDocumentChanged(LabelDocument value)
    {
        _previewEpoch++;
        StopGesturePreview();
        _renderCts?.Cancel();
    }
}
