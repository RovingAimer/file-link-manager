using System.Globalization;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed class LinkRecordRowViewModel
{
    public LinkRecordRowViewModel(PersistedLinkRecord record, string currentDeviceKey)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        IsOfflineSnapshot = !string.Equals(record.DeviceKey, currentDeviceKey, StringComparison.Ordinal);
    }

    public PersistedLinkRecord Record { get; }

    public Guid Id => Record.Id;

    public string DeviceDisplayName => Record.DeviceDisplayNameAtCreation;

    public string LinkPath => Record.LinkRecord.Request.LinkPath;

    public string SourcePath => Record.LinkRecord.Request.SourcePath;

    public string TypeDisplayName => GetTypeDisplayName(Record.LinkRecord.Request.Type);

    public string StatusDisplayName => IsDeleted
        ? "已删除"
        : GetStatusDisplayName(Record.Status);

    public string DeleteActionDisplayName => IsDeleted
        ? "删除记录"
        : "删除链接";

    public string StatusReasonDisplay => string.IsNullOrWhiteSpace(Record.StatusReason)
        ? "—"
        : Record.StatusReason!;

    public string CreatedAtDisplay => FormatLocalTime(Record.CreatedUtc);

    public string LastCheckedAtDisplay => FormatLocalTime(Record.LastCheckedUtc);

    public bool IsOfflineSnapshot { get; }

    public bool IsDeleted => Record.DeletedUtc is not null || Record.Status == LinkStatus.Deleted;

    public bool CanOperate => !IsOfflineSnapshot && !IsDeleted;

    internal static string GetTypeDisplayName(FileLinkType type) => type switch
    {
        FileLinkType.FileSymbolicLink => "文件符号链接",
        FileLinkType.DirectorySymbolicLink => "目录符号链接",
        FileLinkType.FileHardLink => "文件硬链接",
        FileLinkType.DirectoryJunction => "目录联接",
        _ => type.ToString()
    };

    internal static string GetStatusDisplayName(LinkStatus status) => status switch
    {
        LinkStatus.Valid => "有效",
        LinkStatus.Missing => "失效（路径缺失）",
        LinkStatus.Conflict => "冲突",
        LinkStatus.Deleted => "已删除",
        LinkStatus.Unknown => "未知",
        _ => status.ToString()
    };

    private static string FormatLocalTime(DateTimeOffset utcTime) =>
        utcTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
