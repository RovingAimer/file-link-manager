using FileLinkManager.Application.Abstractions;

namespace FileLinkManager.App.Presentation;

public sealed class StorageAccessViewModel : ObservableObject
{
    private bool isReadOnly;
    private string? message;

    public bool IsReadOnly
    {
        get => isReadOnly;
        private set => SetField(ref isReadOnly, value);
    }

    public string? Message
    {
        get => message;
        private set => SetField(ref message, value);
    }

    public void Apply(LinkRecordStoreLoadDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        IsReadOnly = diagnostics.IsReadOnly;
        Message = diagnostics.IsReadOnly
            ? diagnostics.Message ?? "记录存储当前处于只读模式。"
            : null;
    }

    public void MarkReadOnly(string message)
    {
        IsReadOnly = true;
        Message = string.IsNullOrWhiteSpace(message)
            ? "记录存储当前处于只读模式。"
            : message;
    }

}
