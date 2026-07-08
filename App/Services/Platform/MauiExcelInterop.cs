using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>Desktop <see cref="IExcelInterop"/> using the MAUI file picker / file saver.</summary>
[UsedImplicitly]
public sealed class MauiExcelInterop : IExcelInterop
{
    public async Task<Stream?> PickXlsxForReadAsync()
    {
        PickOptions options = new PickOptions
        {
            PickerTitle = "Select exported .xlsx file",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".xlsx"] },
                // Mac Catalyst reports DevicePlatform.MacCatalyst (not macOS) and expects a UTI, not
                // a bare extension. org.openxmlformats.spreadsheetml.sheet is the UTI for .xlsx.
                { DevicePlatform.MacCatalyst, ["org.openxmlformats.spreadsheetml.sheet"] },
            }),
        };

        FileResult? picked = await FilePicker.Default.PickAsync(options);
        return picked is null ? null : await picked.OpenReadAsync();
    }

    public async Task SaveXlsxAsync(string suggestedFileName, Stream content) =>
        await FileSaver.Default.SaveAsync(suggestedFileName, content);
}
