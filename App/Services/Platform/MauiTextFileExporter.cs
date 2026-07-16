using System.Text;
using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>Desktop <see cref="ITextFileExporter"/> using the MAUI file saver.</summary>
[UsedImplicitly]
public sealed class MauiTextFileExporter : ITextFileExporter
{
    public async Task SaveTextAsync(string suggestedFileName, string content)
    {
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await FileSaver.Default.SaveAsync(suggestedFileName, stream);
    }
}
