using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using LabelForge.Core.Io;
using LabelForge.Core.Model;

namespace LabelForge.App.Services;

public interface IElementClipboard
{
    Task WriteAsync(string json);
    Task<string?> ReadAsync();
}

public sealed class ElementClipboard(Func<IClipboard?> getClipboard) : IElementClipboard
{
    public static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("LabelForge.Elements.v1");

    public async Task WriteAsync(string json)
    {
        DataTransfer? transfer = null;
        try
        {
            if (getClipboard() is not { } clipboard) return;
            var item = new DataTransferItem();
            item.Set(Format, json);
            item.Set(DataFormat.Text, json);
            transfer = new DataTransfer();
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
            transfer = null; // The clipboard owns a successfully published transfer.
            await clipboard.FlushAsync();
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            // The designer retains its own copy when the OS clipboard is busy or absent.
        }
        finally
        {
            (transfer as IDisposable)?.Dispose();
        }
    }

    public async Task<string?> ReadAsync()
    {
        try
        {
            if (getClipboard() is not { } clipboard) return null;
            using var transfer = await clipboard.TryGetDataAsync();
            if (transfer is null) return null;
            string? json = await transfer.TryGetValueAsync(Format);
            if (IsElementList(json)) return json;
            json = await transfer.TryGetTextAsync();
            return IsElementList(json) ? json : null;
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return null;
        }
    }

    private static bool IsUnavailable(Exception ex) =>
        ex is ExternalException or IOException or NotSupportedException or InvalidOperationException;

    private static bool IsElementList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var elements = LabelDocumentJson.DeserializeElements(json);
            return elements.Count > 0 && elements.All(e => e is not null && e.Name is not null && (e switch
            {
                TextElement text => text.Text is not null,
                BarcodeElement barcode => barcode.Data is not null,
                QrCodeElement qr => qr.Data is not null,
                DataMatrixElement matrix => matrix.Data is not null,
                Pdf417Element pdf => pdf.Data is not null,
                ImageElement image => image.ImageData is not null,
                _ => true,
            }));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }
}
