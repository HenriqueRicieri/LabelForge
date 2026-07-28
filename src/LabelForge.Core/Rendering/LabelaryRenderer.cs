using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using LabelForge.Core.Io;

namespace LabelForge.Core.Rendering;

/// <summary>
/// Renders ZPL through the Labelary web service, for comparing against the offline
/// engine (backlog E2).
///
/// **This sends the label over the internet**, which is the whole reason it exists and
/// the whole reason it is never reached for on its own. It is not the default renderer,
/// nothing constructs it as a fallback, and no test may touch the network through it:
/// the tests inject a handler and answer themselves. Labelary must not become a runtime
/// or a test dependency, so a caller has to ask for this by name, every time, on the
/// user's say-so.
///
/// What it is worth is that Labelary renders what a printer prints. It has already
/// settled three questions this project could not answer from its own renderer: the cell
/// metrics of fonts A to H (B14), which typeface to pin for font 0 (G4), and - by being
/// tested and found wanting for that font's punctuation - which source to trust for the
/// scalable font's advances (B15). This makes that comparison repeatable instead of
/// something rebuilt by hand each time.
///
/// The contract is <see cref="IZplRenderer"/>'s: PNG bytes plus diagnostics, and never a
/// throw. A refused request, a timeout or no network at all is a rendering that did not
/// happen, reported in <see cref="RenderResult.Errors"/> like any other.
/// </summary>
public sealed class LabelaryRenderer : IZplRenderer, IDisposable
{
    /// <summary>Labelary states its label size in INCHES, and caps it. Anything past this
    /// is refused by the service, so it is refused here with a sentence someone can act on
    /// rather than an HTTP status.</summary>
    public const double MaxSideInches = 15.0;

    /// <summary>Densities the service offers. Ours are the same four, which is not a
    /// coincidence - they are the printheads Zebra makes - but a label at some other dpmm
    /// has no comparison to draw and should say so.</summary>
    public static IReadOnlyList<int> SupportedDpmm { get; } = [6, 8, 12, 24];

    private const string DefaultBaseAddress = "https://api.labelary.com/v1/printers/";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <param name="handler">Injected by tests so they answer themselves and never reach
    /// the network. Null uses a real client.</param>
    /// <param name="baseAddress">Overridden only by tests.</param>
    public LabelaryRenderer(HttpMessageHandler? handler = null, string? baseAddress = null)
    {
        _ownsClient = true;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(baseAddress ?? DefaultBaseAddress);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>
    /// Renders one label through the service.
    ///
    /// Synchronous, because <see cref="IZplRenderer"/> is, and a network call on the UI
    /// thread would freeze the window. Callers already run rendering off it; this one has
    /// to. <see cref="HttpClient.Send(HttpRequestMessage)"/> is a real synchronous send
    /// rather than a blocking wait on an async one, which is what keeps that safe.
    /// </summary>
    public RenderResult Render(string zpl, double widthMm, double heightMm, int dpmm, int labelIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        double widthInches = Inches(widthMm, dpmm);
        double heightInches = Inches(heightMm, dpmm);

        if (!SupportedDpmm.Contains(dpmm))
        {
            return Failed(
                $"Labelary renders at {string.Join(", ", SupportedDpmm)} dots per mm and this "
                + $"label is {dpmm}, so there is nothing to compare against.");
        }

        if (widthInches > MaxSideInches || heightInches > MaxSideInches)
        {
            return Failed(
                $"Labelary renders labels up to {MaxSideInches} inches a side and this one is "
                + $"{widthInches:0.#} by {heightInches:0.#}.");
        }

        string url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}dpmm/labels/{1:0.#####}x{2:0.#####}/{3}/",
            dpmm,
            widthInches,
            heightInches,
            Math.Max(labelIndex, 0));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            // The same bytes a printer would get. ZplTextFile is the one place that
            // decision lives: UTF-8, and no BOM, because three bytes in front of ^XA are
            // bytes nothing downstream has a reason to tolerate.
            request.Content = new ByteArrayContent(ZplTextFile.ToBytes(zpl));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));

            using HttpResponseMessage response = _http.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                // Labelary explains a refusal in the body, and that sentence is far more
                // use than the status code: it names the command it could not read.
                string detail = SafeRead(response);
                return Failed(
                    $"Labelary refused the label ({(int)response.StatusCode} {response.StatusCode})"
                    + (detail.Length > 0 ? $": {detail}" : "."));
            }

            using var buffer = new MemoryStream();
            response.Content.ReadAsStream().CopyTo(buffer);
            byte[] png = buffer.ToArray();

            int labelCount = response.Headers.TryGetValues("X-Total-Count", out IEnumerable<string>? counts)
                             && int.TryParse(
                                 counts.FirstOrDefault(), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out int total)
                ? total
                : 1;

            // Labelary reports nothing about commands it did not understand, so the
            // unknown-command list is empty rather than falsely reassuring. Ours is the
            // renderer that says; this one is the one that is right.
            return new RenderResult(
                png,
                (int)Math.Round(widthMm * dpmm),
                (int)Math.Round(heightMm * dpmm),
                [],
                [],
                Math.Max(labelCount, 1));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or IOException)
        {
            return Failed($"Labelary could not be reached: {ex.Message}");
        }
    }

    /// <summary>
    /// The label size Labelary has to be asked for so it draws the same number of dots we
    /// do. Measured against the service rather than derived, because two of its three
    /// steps are not what arithmetic would suggest.
    ///
    /// It states sizes in inches and turns them back into dots, and it does that at a
    /// WHOLE number of dots per inch, truncated: 6 dpmm is 152 and not 152.4, 8 is 203,
    /// 12 is **304** and not the 300 Zebra prints on the box, 24 is 609. Then it floors
    /// the dot count rather than rounding it - asked for 3.94 inches at 8 dpmm it draws
    /// 799 dots, where rounding 3.94 x 203 = 799.82 would give 800.
    ///
    /// So the conversion goes through dots rather than through millimetres: take the dot
    /// count this app would use, then ask for the inches that produce it. The extra half
    /// dot is what survives the flooring and the five decimal places in the URL.
    ///
    /// Getting this wrong is not cosmetic. A label one pixel narrower cannot be compared
    /// pixel for pixel at all, which throws away the half of the comparison that says
    /// where the two engines disagree rather than merely by how much.
    /// </summary>
    private static double Inches(double mm, int dpmm) =>
        (Model.Units.MmToDots(mm, dpmm) + 0.5) / DotsPerInch(dpmm);

    private static int DotsPerInch(int dpmm) => (int)(dpmm * 25.4);

    private static string SafeRead(HttpResponseMessage response)
    {
        try
        {
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return reader.ReadToEnd().Trim();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private static RenderResult Failed(string reason) =>
        new([], 0, 0, [], [reason], 0);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
