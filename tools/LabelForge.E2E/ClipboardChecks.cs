using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using LabelForge.App.Services;
using LabelForge.App.ViewModels;
using LabelForge.Core.Io;
using LabelForge.Core.Model;
using SkiaSharp;

internal static class ClipboardChecks
{
    public static void Run(Avalonia.Controls.Window window, string scratch, Action<string, bool> check)
    {
        var clipboard = new TestClipboard(window.Clipboard!);
        var source = Create("source");
        var target = Create("target");
        try
        {
            using var bitmap = new SKBitmap(8, 8);
            bitmap.Erase(SKColors.Black);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            byte[] imageBytes = png.ToArray();
            Guid group = Guid.NewGuid();
            var text = new TextElement { Text = "Clipboard", X = 60, Y = 70, ZOrder = 5, GroupId = group };
            var image = new ImageElement
            {
                ImageData = imageBytes, SourcePixelWidth = 8, SourcePixelHeight = 8,
                WidthDots = 40, HeightDots = 40, X = 140, Y = 80, ZOrder = 9, GroupId = group,
            };
            source.Document.Elements.Add(text);
            source.Document.Elements.Add(image);
            source.NotifyDocumentEdited();
            source.Selection.SetMany([image, text]);
            string before = source.SerializeDocument();
            Run(source.CopyCommand.ExecuteAsync(null));
            check("Copy publishes private JSON and matching text", clipboard.Private is not null && clipboard.Private == clipboard.Text);
            check("Copy leaves the source document unchanged", source.SerializeDocument() == before);
            check("A fresh designer can paste another window's clipboard", target.PasteCommand.CanExecute(null));
            string copied = clipboard.Private!;
            Run(target.PasteCommand.ExecuteAsync(null));
            check("Cross-window paste transfers both fields", target.Document.Elements.Count == 2);
            check("Paste preserves draw order and relative placement", target.Document.Elements is [TextElement { X: 80, Y: 90, ZOrder: 0 }, ImageElement { X: 160, Y: 100, ZOrder: 1 }]);
            check("Paste retains embedded image bytes", target.Document.Elements.OfType<ImageElement>().SingleOrDefault()?.ImageData.SequenceEqual(imageBytes) == true);
            check("Paste assigns new field and group identities", target.Document.Elements.Count == 2 &&
                target.Document.Elements.All(e => e.Id != text.Id && e.Id != image.Id && e.GroupId != group && e.GroupId is not null) &&
                target.Document.Elements.Select(e => e.GroupId).Distinct().Count() == 1);
            target.UndoCommand.Execute(null);
            check("One undo removes the entire pasted group", target.Document.Elements.Count == 0 && !target.CanUndo);
            target.RedoCommand.Execute(null);
            check("Redo restores the pasted image and group", target.Document.Elements.Count == 2 && target.Document.Elements[0].GroupId == target.Document.Elements[1].GroupId);
            Run(target.PasteCommand.ExecuteAsync(null));
            check("Repeated system paste keeps the local cascade", target.Selection.Primary?.X == 180 && target.Selection.Items.Min(e => e.X) == 100);
            check("Paste does not rewrite the system clipboard", clipboard.Private == copied && clipboard.Writes == 1);
            Run(target.PasteInPlaceCommand.ExecuteAsync(null));
            check("Paste in place retains the current local clipboard position", target.Selection.Items.Min(e => e.X) == 100);
            Run(source.CopyCommand.ExecuteAsync(null));
            Run(source.PasteInPlaceCommand.ExecuteAsync(null));
            check("Source paste in place retains original coordinates", source.Selection.Items.Min(e => e.X) == 60);
            Run(target.PasteAt(400, 300));
            check("Context paste uses the requested point", target.Selection.Items.Min(e => e.X) == 400 && target.Selection.Items.Min(e => e.Y) == 300);

            string negative = LabelDocumentJson.SerializeElements([new BoxElement { X = -40, Y = -25 }]);
            clipboard.Private = negative;
            clipboard.Text = "unrelated text";
            target.NewDocumentCommand.Execute(null);
            Run(target.PasteInPlaceCommand.ExecuteAsync(null));
            check("Private format takes precedence and preserves pasteboard positions", target.SelectedElement is BoxElement { X: -40, Y: -25 });

            clipboard.Private = "not JSON";
            clipboard.Text = LabelDocumentJson.SerializeElements([new TextElement { Text = "Text fallback", X = 15, Y = 25 }]);
            target.NewDocumentCommand.Execute(null);
            Run(target.PasteInPlaceCommand.ExecuteAsync(null));
            check("Valid text JSON survives an invalid private format", target.SelectedElement is TextElement { Text: "Text fallback", X: 15, Y: 25 });
            clipboard.Private = clipboard.Text = "ordinary text";
            Run(target.PasteInPlaceCommand.ExecuteAsync(null));
            check("Unrelated system content preserves the internal clipboard", target.Selection.Primary is TextElement { Text: "Text fallback" } && target.Document.Elements.Count == 2);

            clipboard.Fail = true;
            source.Selection.Set(text);
            Run(source.CopyCommand.ExecuteAsync(null));
            source.NewDocumentCommand.Execute(null);
            Run(source.PasteInPlaceCommand.ExecuteAsync(null));
            check("Unavailable clipboard retains local copy and paste", source.SelectedElement is TextElement { Text: "Clipboard", X: 60 });
            Run(source.CutCommand.ExecuteAsync(null));
            check("Cut still deletes once when the OS clipboard is unavailable", source.Document.Elements.Count == 0);
            source.UndoCommand.Execute(null);
            check("One undo restores a cut with a failed clipboard write", source.Document.Elements.Count == 1);
            clipboard.Fail = false;

            foreach (string invalid in new[] { "ordinary text", "{", "[]", "[null]", "[{}]", "[{\"$type\":\"future\"}]", "[{\"$type\":\"text\",\"Text\":null}]", "[{\"$type\":\"image\",\"ImageData\":\"!\"}]" })
            {
                var empty = Create("invalid");
                try
                {
                    clipboard.Private = clipboard.Text = invalid;
                    Run(empty.PasteCommand.ExecuteAsync(null));
                    check($"Invalid clipboard adds no elements or undo: {invalid}", empty.Document.Elements.Count == 0 && !empty.CanUndo);
                }
                finally { empty.ShutDown(); }
            }

            clipboard.Private = copied;
            clipboard.Text = null;
            clipboard.ReadGate = new TaskCompletionSource();
            Task pending = target.PasteCommand.ExecuteAsync(null);
            target.NewDocumentCommand.Execute(null);
            clipboard.ReadGate.SetResult();
            Run(pending);
            check("A pending paste cannot land in a replacement document", target.Document.Elements.Count == 0 && !target.CanUndo);
            clipboard.ReadGate = new TaskCompletionSource();
            pending = target.PasteCommand.ExecuteAsync(null);
            var newer = new TextElement { Text = "Newer copy", X = 30, Y = 40 };
            target.Document.Elements.Add(newer);
            target.NotifyDocumentEdited();
            target.Selection.Set(newer);
            Run(target.CopyCommand.ExecuteAsync(null));
            clipboard.ReadGate.SetResult();
            Run(pending);
            check("A stale clipboard read cannot overwrite a newer copy", target.Document.Elements.Count == 1);
            clipboard.ReadGate = null;
            clipboard.Private = clipboard.Text = null;
            Run(target.PasteInPlaceCommand.ExecuteAsync(null));
            check("The newer local copy survives a stale read", target.Selection.Primary is TextElement { Text: "Newer copy" });

            var unavailable = new ElementClipboard(() => null);
            Run(unavailable.WriteAsync(copied));
            Task<string?> noClipboard = unavailable.ReadAsync();
            Run(noClipboard);
            check("A platform without clipboard support falls back cleanly", noClipboard.Result is null);

            source.NewDocumentCommand.Execute(null);
            source.Document.Elements.Add(new BoxElement { X = 10, Y = 20 });
            source.NotifyDocumentEdited();
            source.Selection.Set(source.Document.Elements[0]);
            clipboard.WriteGate = new TaskCompletionSource();
            Task cut = source.CutCommand.ExecuteAsync(null);
            check("Cut captures and removes its selection before an asynchronous write", source.Document.Elements.Count == 0);
            source.NewDocumentCommand.Execute(null);
            source.Document.Elements.Add(new TextElement { Text = "Keep me" });
            source.NotifyDocumentEdited();
            source.Selection.Set(source.Document.Elements[0]);
            clipboard.WriteGate.SetResult();
            Run(cut);
            check("A completing cut cannot delete a later document's selection", source.Document.Elements.Single() is TextElement { Text: "Keep me" });
            clipboard.WriteGate = null;
        }
        finally
        {
            source.ShutDown();
            target.ShutDown();
        }

        DesignerViewModel Create(string name)
        {
            string dir = Path.Combine(scratch, name + "-" + Guid.NewGuid().ToString("N"));
            return new DesignerViewModel(
                new LabelForge.Core.Media.UserMediaStore(Path.Combine(dir, "media.json")),
                new LabelForge.Core.Fields.FieldCatalogStore(Path.Combine(dir, "fields.json")),
                new RecoveryStore(Path.Combine(dir, "recovery"), "test"),
                new LabelForge.Core.Settings.UserSettingsStore(Path.Combine(dir, "settings.json")),
                clipboard);
        }
    }

    private static void Run(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        if (!task.IsCompleted) throw new TimeoutException("Clipboard operation did not finish.");
        task.GetAwaiter().GetResult();
    }

    private sealed class TestClipboard(IClipboard platform) : IElementClipboard
    {
        public string? Private;
        public string? Text;
        public bool Fail;
        public int Writes;
        public TaskCompletionSource? ReadGate;
        public TaskCompletionSource? WriteGate;

        private ElementClipboard Adapter => new(() => Fail
            ? throw new System.Runtime.InteropServices.COMException("Clipboard busy") : platform);

        public async Task WriteAsync(string json)
        {
            if (WriteGate is not null) await WriteGate.Task;
            await Adapter.WriteAsync(json);
            if (Fail) return;
            using var transfer = await platform.TryGetDataAsync();
            Private = transfer is null ? null : await transfer.TryGetValueAsync(ElementClipboard.Format);
            Text = transfer is null ? null : await transfer.TryGetTextAsync();
            Writes++;
        }

        public async Task<string?> ReadAsync()
        {
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            if (Private is not null) item.Set(ElementClipboard.Format, Private);
            if (Text is not null) item.Set(DataFormat.Text, Text);
            transfer.Add(item);
            await platform.SetDataAsync(transfer);
            string? result = await Adapter.ReadAsync();
            if (ReadGate is not null) await ReadGate.Task;
            return result;
        }
    }
}
