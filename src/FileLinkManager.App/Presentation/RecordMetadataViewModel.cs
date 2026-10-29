using System.Globalization;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.App.Presentation;

public sealed record RecordGroupOptionViewModel(Guid? GroupId, string DisplayName, bool IsAll = false);

public sealed class RecordMetadataViewModel : ObservableObject
{
    private string customName = "";
    private RecordGroupOptionViewModel? selectedGroup;
    private bool isEnabled = true;

    public RecordMetadataViewModel() => SetGroups(Array.Empty<LinkRecordGroup>(), null);

    public string CustomName
    {
        get => customName;
        set
        {
            if (!IsEnabled || !SetField(ref customName, value ?? "")) return;
            OnPropertyChanged(nameof(TextElementCount));
            OnPropertyChanged(nameof(NameError));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public int TextElementCount => new StringInfo(CustomName).LengthInTextElements;
    public string? NameError => TextElementCount > 20 ? "自定义名称最多为 20 个字。" : null;
    public bool CanSave => NameError is null;
    public bool IsEnabled { get => isEnabled; set => SetField(ref isEnabled, value); }
    public IReadOnlyList<RecordGroupOptionViewModel> Groups { get; private set; } = Array.Empty<RecordGroupOptionViewModel>();
    public RecordGroupOptionViewModel? SelectedGroup
    {
        get => selectedGroup;
        set { if (IsEnabled) SetField(ref selectedGroup, value); }
    }

    public void SetGroups(IEnumerable<LinkRecordGroup> groups, Guid? groupId)
    {
        Groups = new[] { new RecordGroupOptionViewModel(null, "未分组") }
            .Concat(groups.OrderBy(group => group.SortOrder).Select(group => new RecordGroupOptionViewModel(group.Id, group.Name))).ToArray();
        selectedGroup = Groups.FirstOrDefault(group => group.GroupId == groupId) ?? Groups[0];
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(SelectedGroup));
    }

    public LinkRecordMetadata ToMetadata() => new LinkRecordMetadata(CustomName, SelectedGroup?.GroupId).Normalize();
}
