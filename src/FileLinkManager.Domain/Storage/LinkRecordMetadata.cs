using System.Globalization;

namespace FileLinkManager.Domain.Storage;

public sealed record LinkRecordMetadata(string? CustomName = null, Guid? GroupId = null)
{
    public LinkRecordMetadata Normalize()
    {
        var name = string.IsNullOrWhiteSpace(CustomName) ? null : CustomName;
        if (name is not null && new StringInfo(name).LengthInTextElements > 20)
        {
            throw new ArgumentException("自定义名称最多为 20 个字。", nameof(CustomName));
        }

        if (GroupId == Guid.Empty)
        {
            throw new ArgumentException("请选择有效分组。", nameof(GroupId));
        }

        return this with { CustomName = name };
    }
}
