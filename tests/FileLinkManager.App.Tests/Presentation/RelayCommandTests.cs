using FileLinkManager.App.Presentation.Commands;

namespace FileLinkManager.App.Tests.Presentation;

public sealed class RelayCommandTests
{
    [Fact]
    public void CanExecute_UsesCurrentPredicateAndRaisesWhenRequested()
    {
        var canExecute = false;
        var raised = 0;
        var command = new RelayCommand(
            _ => { },
            _ => canExecute);
        command.CanExecuteChanged += (_, _) => raised++;

        Assert.False(command.CanExecute(null));
        canExecute = true;
        command.RaiseCanExecuteChanged();

        Assert.True(command.CanExecute(null));
        Assert.Equal(1, raised);
    }
}
