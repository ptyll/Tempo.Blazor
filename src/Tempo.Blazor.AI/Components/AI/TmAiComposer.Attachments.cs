using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Attachment half of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6): the browser file picker, the metadata
/// chips, and the two limits — <see cref="MaxAttachments"/> and <see cref="MaxFileSize"/> — enforced before a
/// file reaches the host. A rejected file is never added and never raised; the reason goes through the
/// component's single localized live region, and the limit message is a plural family so the count agrees with
/// the noun ("1 soubor", "2 soubory", "5 souborů").
/// </summary>
public partial class TmAiComposer
{
    private IReadOnlyList<AiComposerAttachment> EffectiveAttachments => _attachments;

    /// <summary>
    /// Adopts the host's seeded attachments. Echoing the list this component raised through
    /// <see cref="AttachmentsChanged"/> (same ids in the same order) is not a replacement, so a bound host does
    /// not undo a removal by re-rendering with its previous copy.
    /// </summary>
    private void SyncAttachmentsFromParameter()
    {
        if (ReferenceEquals(Attachments, _lastAttachmentsParameter))
        {
            return;
        }

        _lastAttachmentsParameter = Attachments;
        var incoming = Attachments ?? Array.Empty<AiComposerAttachment>();
        var echo = incoming.Count == _attachments.Count
            && incoming.Zip(_attachments, (host, current) => ReferenceEquals(host, current)).All(same => same);

        if (echo)
        {
            return;
        }

        _attachments.Clear();
        _attachments.AddRange(incoming);
    }

    private async Task OnFilesSelectedAsync(InputFileChangeEventArgs args)
    {
        if (Disabled || !EnableAttachments)
        {
            return;
        }

        IReadOnlyList<IBrowserFile> files;
        try
        {
            // Read the whole batch instead of a capped count: a capped GetMultipleFiles throws, and the reader
            // deserves the localized "at most N" message rather than a framework exception in the console.
            files = args.GetMultipleFiles(int.MaxValue);
        }
        catch (Exception)
        {
            return;
        }

        var accepted = false;
        var limit = Math.Max(0, MaxAttachments);

        foreach (var file in files)
        {
            if (_attachments.Count >= limit)
            {
                Announce(Loc.Plural("TmAiComposer_TooManyAttachments", limit));
                break;
            }

            if (MaxFileSize > 0 && file.Size > MaxFileSize)
            {
                Announce(Loc["TmAiComposer_AttachmentTooLarge", file.Name, FormatSize(MaxFileSize)]);
                continue;
            }

            _attachments.Add(new AiComposerAttachment
            {
                Name = file.Name,
                SizeBytes = file.Size,
                ContentType = file.ContentType,
                Content = file,
            });
            accepted = true;
        }

        if (accepted)
        {
            await RaiseAttachmentsChangedAsync();
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Removes one chip. The Remove control leaves the DOM with its chip, so the focus goes back to the prompt
    /// instead of <c>&lt;body&gt;</c>.
    /// </summary>
    private async Task RemoveAttachmentAsync(AiComposerAttachment attachment)
    {
        if (!_attachments.Remove(attachment))
        {
            return;
        }

        _focusTextareaAfterRender = true;
        await RaiseAttachmentsChangedAsync();
        await InvokeAsync(StateHasChanged);
    }

    private Task RaiseAttachmentsChangedAsync()
        => AttachmentsChanged.HasDelegate
            ? AttachmentsChanged.InvokeAsync(_attachments.ToArray())
            : Task.CompletedTask;

    /// <summary>
    /// Human-readable size for a chip and for the limit message. The B/KB/MB units follow the rest of the
    /// library's file surfaces (<c>TmAttachmentManager</c>), so the same file reads the same everywhere.
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024} KB";
        }

        return $"{bytes / (1024L * 1024)} MB";
    }
}
