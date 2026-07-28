using System.Net;
using System.Text;
using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// The Labelary compare mode (backlog E2).
///
/// **Nothing here touches the network, and that is a rule rather than an optimisation.**
/// Labelary must never become a test dependency: a suite that needs an internet service
/// fails on a train, and a suite that quietly skips when offline is worse, because it
/// reports green having checked nothing. Every request in this file is answered by a stub
/// handler, so what is tested is the request built and the response handled - which is
/// all of the renderer that is ours.
/// </summary>
public sealed class LabelaryCompareTests
{
    /// <summary>Answers every request from a script, and records what it was asked, so a
    /// test can assert the URL and body that would have gone out.</summary>
    private sealed class StubHandler(
        HttpStatusCode status, byte[] body, string? totalCount = null) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Sent { get; private set; }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Sent = request.Content?.ReadAsStringAsync(cancellationToken).Result;

            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (totalCount is not null)
            {
                response.Headers.Add("X-Total-Count", totalCount);
            }

            return response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private static byte[] Png(int width, int height, int inkPixels)
    {
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        for (int i = 0; i < inkPixels; i++)
        {
            bitmap.SetPixel(i % width, i / width, SKColors.Black);
        }

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        bitmap.Dispose();
        return data.ToArray();
    }

    /// <summary>
    /// The size asked for is the one that makes Labelary draw the same dots we do, and it
    /// is not the arithmetic conversion.
    ///
    /// Measured against the live service: it works at a WHOLE number of dots per inch,
    /// truncated, so 8 dpmm is 203 and not 203.2; and it FLOORS the resulting dot count,
    /// so 3.94 inches draws 799 dots where rounding 3.94 x 203 = 799.82 would give 800.
    /// The naive 100 / 25.4 = 3.937 therefore comes back a pixel narrow, and a label a
    /// pixel narrow cannot be compared pixel for pixel at all.
    ///
    /// 100 by 60 mm at 8 dpmm is 800 by 480 dots, so the request is (800 + 0.5) / 203 by
    /// (480 + 0.5) / 203. The half dot is what survives the flooring.
    /// </summary>
    [Fact]
    public void TheRequestAsksForTheSizeThatDrawsOurDotCount()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Png(8, 8, 4));
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        renderer.Render("^XA^FO10,10^A0N,30^FDhello^FS^XZ", 100, 60, 8);

        Assert.Equal("https://example.invalid/v1/printers/8dpmm/labels/3.94335x2.367/0/", handler.Url);
    }

    /// <summary>
    /// The dots-per-inch each density really means to Labelary, measured against the live
    /// service and pinned here so nobody "corrects" them to the round numbers.
    ///
    /// 12 dpmm is the one that surprises: Zebra prints 300 dpi on the box, Labelary draws
    /// 304, because it truncates 12 x 25.4. Asked for 4 by 2 inches it returns 1216 x 608.
    /// </summary>
    [Theory]
    [InlineData(6, 152)]
    [InlineData(8, 203)]
    [InlineData(12, 304)]
    [InlineData(24, 609)]
    public void TheRequestedInchesReproduceOurDotCountAtEveryDensity(int dpmm, int labelaryDpi)
    {
        var handler = new StubHandler(HttpStatusCode.OK, Png(4, 4, 1));
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        renderer.Render("^XA^XZ", 100, 50, dpmm);

        string inches = handler.Url!.Split('/')[^3].Split('x')[0];
        double asked = double.Parse(inches, System.Globalization.CultureInfo.InvariantCulture);

        // What Labelary would draw from it: floor, at its own truncated dpi.
        Assert.Equal(LabelForge.Core.Model.Units.MmToDots(100, dpmm), (int)(asked * labelaryDpi));
    }

    /// <summary>The bytes sent are the bytes a printer would get. Not a detail: a BOM in
    /// front of ^XA is three bytes nothing downstream has a reason to tolerate, and
    /// comparing a render of something other than the label would prove nothing.</summary>
    [Fact]
    public void TheZplGoesOutAsTheBytesAPrinterWouldGet()
    {
        const string zpl = "^XA^FO10,10^A0N,30^FDMinistério^FS^XZ";
        var handler = new StubHandler(HttpStatusCode.OK, Png(8, 8, 1));
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        renderer.Render(zpl, 100, 60, 8);

        Assert.Equal(zpl, handler.Sent);
        Assert.DoesNotContain('﻿', handler.Sent!);
    }

    [Fact]
    public void ALabelCountComesBackFromTheHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Png(4, 4, 2), totalCount: "7");
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        Assert.Equal(7, renderer.Render("^XA^XZ", 100, 60, 8).LabelCount);
    }

    /// <summary>
    /// A refusal is a rendering that did not happen, not an exception. The service explains
    /// itself in the body and that sentence is worth far more than the status code, so it
    /// is carried through: it names the command Labelary could not read.
    /// </summary>
    [Fact]
    public void ARefusalIsReportedRatherThanThrown()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest, Encoding.UTF8.GetBytes("ERROR: Invalid command '^QQ'"));
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        RenderResult result = renderer.Render("^XA^QQ^XZ", 100, 60, 8);

        Assert.Empty(result.Png);
        string error = Assert.Single(result.Errors);
        Assert.Contains("400", error, StringComparison.Ordinal);
        Assert.Contains("^QQ", error, StringComparison.Ordinal);
    }

    /// <summary>No network at all is the ordinary case for this renderer, not an
    /// exceptional one, so it degrades the way every other rendering failure does.</summary>
    [Fact]
    public void BeingOfflineIsReportedRatherThanThrown()
    {
        using var renderer = new LabelaryRenderer(
            new ThrowingHandler(), "https://example.invalid/v1/printers/");

        RenderResult result = renderer.Render("^XA^XZ", 100, 60, 8);

        Assert.Empty(result.Png);
        Assert.Contains("could not be reached", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage r, CancellationToken c) =>
            throw new HttpRequestException("no such host is known");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) =>
            throw new HttpRequestException("no such host is known");
    }

    /// <summary>Asked for something the service does not do, the answer is a sentence
    /// someone can act on and no request at all. A density Labelary has no printhead for
    /// has no comparison to draw, and saying so beats a 404.</summary>
    [Theory]
    [InlineData(10, 100, 60, "dots per mm")]
    [InlineData(8, 500, 60, "inches a side")]
    public void WhatTheServiceCannotDoIsSaidWithoutAsking(
        int dpmm, double widthMm, double heightMm, string expected)
    {
        var handler = new StubHandler(HttpStatusCode.OK, Png(4, 4, 1));
        using var renderer = new LabelaryRenderer(handler, "https://example.invalid/v1/printers/");

        RenderResult result = renderer.Render("^XA^XZ", widthMm, heightMm, dpmm);

        Assert.Contains(expected, Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Null(handler.Url);
    }

    // -- the comparison itself, which is where the value is and which needs no service --

    [Fact]
    public void IdenticalRendersAreReportedAsIdentical()
    {
        byte[] png = Png(20, 10, 30);

        RenderDifference difference = RenderComparison.Compare(png, png);

        Assert.True(difference.Comparable);
        Assert.Equal(0, difference.DisagreeingPixels);
        Assert.Equal(0, difference.InkDifference);
        Assert.Contains("Identical", difference.Summary, StringComparison.Ordinal);
    }

    /// <summary>Same size, different ink: both the count and the per-pixel disagreement
    /// are reported, because two ink counts that happen to agree are not proof the
    /// pictures do.</summary>
    [Fact]
    public void DifferentInkIsCountedBothWays()
    {
        RenderDifference difference = RenderComparison.Compare(Png(20, 10, 100), Png(20, 10, 80));

        Assert.True(difference.Comparable);
        Assert.Equal(100, difference.LeftInk);
        Assert.Equal(80, difference.RightInk);
        Assert.Equal(20, difference.DisagreeingPixels);
        Assert.Equal(0.2, difference.InkDifference, 3);
    }

    /// <summary>Different sizes is not a failure: the two engines round a label's dots
    /// independently, so a size a millimetre off whole dots comes back a pixel adrift. The
    /// ink comparison still stands and is still worth having.</summary>
    [Fact]
    public void DifferentSizesStillCompareTheInk()
    {
        RenderDifference difference = RenderComparison.Compare(Png(20, 10, 100), Png(21, 10, 100));

        Assert.False(difference.Comparable);
        Assert.Equal(100, difference.LeftInk);
        Assert.Equal(100, difference.RightInk);
        Assert.Contains("Canvas sizes differ", difference.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two questions a comparison has to keep apart: where the ink is, and how much of
    /// it there is. Same box, different weight is what the live service actually produces
    /// for text - the preview draws the same letters in the same place with lighter strokes,
    /// because it substitutes a typeface for a font that lives in the printer - and calling
    /// that "60 per cent apart" and stopping would say the label is wrong when it is right.
    /// </summary>
    [Fact]
    public void GeometryAndWeightAreReportedSeparately()
    {
        // Same 20-pixel-wide band of ink in both, but one fills it twice as densely.
        byte[] light = Striped(20, 10, rows: 4, everyOther: true);
        byte[] heavy = Striped(20, 10, rows: 4, everyOther: false);

        RenderDifference difference = RenderComparison.Compare(light, heavy);

        Assert.Equal(0, difference.EdgeDifferenceDots);
        Assert.Equal(new DotRect(0, 0, 20, 4), difference.LeftBounds);
        Assert.Equal(difference.LeftBounds, difference.RightBounds);
        Assert.True(difference.InkDifference > 0.4);
        Assert.StartsWith("Same size and position.", difference.Summary, StringComparison.Ordinal);
    }

    /// <summary>Ink shifted bodily is the case that must NOT read as "same size and
    /// position", since a field in the wrong place is the failure that matters.</summary>
    [Fact]
    public void InkInADifferentPlaceIsReportedAsSuch()
    {
        RenderDifference difference = RenderComparison.Compare(
            Block(30, 20, x: 2, y: 2, w: 10, h: 5),
            Block(30, 20, x: 9, y: 2, w: 10, h: 5));

        Assert.Equal(7, difference.EdgeDifferenceDots);
        Assert.Equal(difference.LeftInk, difference.RightInk);
        Assert.Contains("Ink box differs by 7 dot(s)", difference.Summary, StringComparison.Ordinal);
    }

    private static byte[] Striped(int width, int height, int rows, bool everyOther)
    {
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // The sparse one still reaches every edge, so the two occupy the same box
                // and differ only in how much of it they fill. That is the whole point:
                // it is the shape the live service produces for text.
                if (!everyOther || x % 2 == 0 || x == width - 1)
                {
                    bitmap.SetPixel(x, y, SKColors.Black);
                }
            }
        }

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        bitmap.Dispose();
        return data.ToArray();
    }

    private static byte[] Block(int width, int height, int x, int y, int w, int h)
    {
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        for (int j = y; j < y + h; j++)
        {
            for (int i = x; i < x + w; i++)
            {
                bitmap.SetPixel(i, j, SKColors.Black);
            }
        }

        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        bitmap.Dispose();
        return data.ToArray();
    }

    /// <summary>A missing image says which side is missing. "Nothing to compare" without
    /// saying which renderer failed sends someone looking in the wrong place.</summary>
    [Fact]
    public void AMissingImageNamesTheSideThatIsMissing()
    {
        Assert.Contains(
            "Labelary produced no image",
            RenderComparison.Compare(Png(4, 4, 2), []).Summary,
            StringComparison.Ordinal);

        Assert.Contains(
            "offline renderer produced no image",
            RenderComparison.Compare(null, Png(4, 4, 2)).Summary,
            StringComparison.Ordinal);
    }

    /// <summary>Two blank labels agree. Worth stating, because the ink difference is a
    /// ratio and blank against blank is the case that would divide by zero.</summary>
    [Fact]
    public void TwoBlankLabelsAgree()
    {
        RenderDifference difference = RenderComparison.Compare(Png(10, 10, 0), Png(10, 10, 0));

        Assert.True(difference.Comparable);
        Assert.Equal(0, difference.InkDifference);
        Assert.Equal(0, difference.DisagreeingPixels);
    }
}
