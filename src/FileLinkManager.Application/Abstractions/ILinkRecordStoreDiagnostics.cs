namespace FileLinkManager.Application.Abstractions;

public interface ILinkRecordStoreDiagnostics
{
    LinkRecordStoreLoadDiagnostics LastLoadDiagnostics { get; }
}

public sealed record LinkRecordStoreLoadDiagnostics(
    bool RecoveredFromBackup,
    bool IsReadOnly,
    string? Message)
{
    public static LinkRecordStoreLoadDiagnostics Clean { get; } = new(false, false, null);
}
