using System.ComponentModel;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class ShellViewModelDisposalTests
{
    [Fact]
    public void Dispose_UnsubscribesFromLocalizationPropertyChanged()
    {
        var localization = new CountingLocalizationService();
        var shell = new ShellViewModel(
            localization: localization,
            connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        Assert.Equal(1, localization.SubscriberCount);

        shell.Dispose();

        Assert.Equal(0, localization.SubscriberCount);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromOutputAndQuickPreviewPropertyChanged()
    {
        var shell = new ShellViewModel(
            localization: new CountingLocalizationService(),
            connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        int notifications = 0;
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.IsOutputPreviewModalVisible)
                or nameof(ShellViewModel.IsQuickDataPreviewModalVisible))
            {
                notifications++;
            }
        };

        shell.OutputPreview.IsVisible = true;
        Assert.True(notifications > 0);

        notifications = 0;
        shell.Dispose();

        shell.OutputPreview.IsVisible = false;
        shell.OutputPreview.IsVisible = true;
        await shell.QuickDataPreview.OpenSqlPreviewAsync(
            title: "Preview",
            subtitle: string.Empty,
            sql: "SELECT 1",
            connection: null,
            provider: DatabaseProvider.Postgres,
            metadata: null,
            focusTableFullName: null,
            sourceDocumentType: null,
            cancellationToken: new CancellationToken(canceled: true));

        Assert.Equal(0, notifications);
    }

    private sealed class CountingLocalizationService : ILocalizationService
    {
        private PropertyChangedEventHandler? _propertyChanged;

        public int SubscriberCount { get; private set; }

        public string CurrentCulture => "pt-BR";

        public string CurrentLanguageLabel => "PT-BR";

        public string this[string key] => key;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                _propertyChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _propertyChanged -= value;
                if (SubscriberCount > 0)
                    SubscriberCount--;
            }
        }

        public bool ToggleCulture() => false;

        public bool SetCulture(string culture) => false;
    }
}
