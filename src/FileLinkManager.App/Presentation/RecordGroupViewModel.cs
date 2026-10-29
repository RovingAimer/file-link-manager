namespace FileLinkManager.App.Presentation;

public sealed class RecordGroupViewModel(Guid? groupId, string name, IReadOnlyList<LinkRecordRowViewModel> records) : ObservableObject
{
    private bool isExpanded = true;
    public Guid? GroupId { get; } = groupId;
    public string Name { get; } = name;
    public IReadOnlyList<LinkRecordRowViewModel> Records { get; } = records;
    public int Count => Records.Count;
    public bool IsExpanded { get => isExpanded; set => SetField(ref isExpanded, value); }
}
