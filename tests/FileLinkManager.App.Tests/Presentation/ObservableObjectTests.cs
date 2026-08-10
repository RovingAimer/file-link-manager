using FileLinkManager.App.Presentation;

namespace FileLinkManager.App.Tests.Presentation;

public sealed class ObservableObjectTests
{
    [Fact]
    public void SetField_DoesNotNotifyForSameValue_AndNotifiesOnceForChangedValue()
    {
        var viewModel = new TestObservableObject();
        var propertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => propertyNames.Add(eventArgs.PropertyName);

        viewModel.Value = "initial";

        Assert.Empty(propertyNames);

        viewModel.Value = "changed";

        Assert.Equal(new[] { nameof(TestObservableObject.Value) }, propertyNames);
    }

    private sealed class TestObservableObject : ObservableObject
    {
        private string value = "initial";

        public string Value
        {
            get => value;
            set => SetField(ref this.value, value);
        }
    }
}
