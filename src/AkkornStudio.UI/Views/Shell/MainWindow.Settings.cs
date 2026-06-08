using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AkkornStudio.UI.Controls;
using Avalonia.Styling;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.Theming;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI;

public partial class MainWindow
{
    private bool _isSyncingEditorSafetySettings;
    private bool _isSyncingEditorResultDateTimeSettings;
    private bool _isSyncingProjectConventionSettings;

    private void SettingsBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        GetSettingsModule().CloseSettings();
    }

    private void SettingsDialog_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void SettingsCloseBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        GetSettingsModule().CloseSettings();
        e.Handled = true;
    }

    private void OnStartOpenSettingsRequested()
    {
        _globalModalManager.RequestSettings(keepStartVisible: true);
    }

    private void OpenSettings(bool keepStartVisible)
    {
        GetSettingsModule().OpenSettings(keepStartVisible);
        SyncLanguageComboSelection();
        SyncEditorSafetySettingsToggles();
        SyncEditorResultDateTimeSettingsControls();
        SyncProjectConventionSettingsControls();
        EnsureKeyboardShortcutsSettingsPanel();
    }

    private void SettingsNavBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string sectionRaw)
        {
            e.Handled = true;
            return;
        }

        if (Enum.TryParse<ShellViewModel.ESettingsSection>(sectionRaw, ignoreCase: true, out var section))
            CurrentShell.SelectSettingsSection(section);

        e.Handled = true;
    }

    private void SettingsThemeDarkBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;

        AppSettingsStore.SaveThemeVariant("Dark");
        SetSettingsStatus(L("settings.status.darkApplied", "Tema escuro aplicado."), isError: false);

        e.Handled = true;
    }

    private void SettingsThemeLightBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;

        AppSettingsStore.SaveThemeVariant("Light");
        SetSettingsStatus(L("settings.status.lightApplied", "Tema claro aplicado."), isError: false);

        e.Handled = true;
    }

    private void SettingsToggleSnapBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        EnsureCanvasInitialized();
        CanvasViewModel activeCanvas = ResolveActiveDiagramCanvasStrict();
        activeCanvas.ToggleSnapCommand.Execute(null);
        SetSettingsStatus(LF("settings.status.snapUpdated", "Snap atualizado: {0}.", activeCanvas.SnapToGridLabel), isError: false);
        e.Handled = true;
    }

    private void SettingsLanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item || item.Tag is not string culture)
            return;

        if (LocalizationService.Instance.SetCulture(culture))
        {
            _titleMenu = null;
            SetSettingsStatus(LF("settings.status.languageSelected", "Idioma selecionado: {0}.", culture), isError: false);
        }

        e.Handled = true;
    }

    private void SyncLanguageComboSelection()
    {
        ComboBox? combo = this.FindControl<ComboBox>("SettingsLanguageCombo");
        if (combo is null)
            return;

        foreach (object? option in combo.Items)
        {
            if (option is ComboBoxItem item && item.Tag is string culture &&
                string.Equals(culture, LocalizationService.Instance.CurrentCulture, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void SettingsApplyThemeJsonBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TextBox? jsonEditor = this.FindControl<TextBox>("SettingsThemeJsonTextBox");
        if (jsonEditor is null)
        {
            e.Handled = true;
            return;
        }

        string rawJson = jsonEditor.Text ?? string.Empty;
        ThemeJsonOperationResult result = _themeJsonSettings.ApplyAndPersist(rawJson);
        string warningSuffix = result.Warnings.Count > 0
            ? $" (avisos: {string.Join(" | ", result.Warnings)})"
            : string.Empty;
        SetSettingsStatus(result.Message + warningSuffix, isError: !result.Success);
        e.Handled = true;
    }

    private void SettingsRestoreDefaultThemeBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ThemeJsonOperationResult result = _themeJsonSettings.RestoreDefault();
        TextBox? jsonEditor = this.FindControl<TextBox>("SettingsThemeJsonTextBox");
        if (jsonEditor is not null)
            jsonEditor.Text = _themeJsonSettings.GetEditorJsonOrTemplate();

        SetSettingsStatus(result.Message, isError: !result.Success);
        e.Handled = true;
    }

    private void PopulateSettingsThemeJsonEditor()
    {
        TextBox? jsonEditor = this.FindControl<TextBox>("SettingsThemeJsonTextBox");
        if (jsonEditor is null)
            return;

        jsonEditor.Text = _themeJsonSettings.GetEditorJsonOrTemplate();
        SetSettingsStatus(L("settings.status.themeEditorReady", "Editor de tema pronto. Aplique para salvar e usar o tema."), isError: false);
    }

    private void EnsureKeyboardShortcutsSettingsPanel()
    {
        GetKeyboardShortcutsViewModel().Refresh();

        ContentControl? host = this.FindControl<ContentControl>("SettingsKeyboardShortcutsHost");
        if (host is null)
            return;

        if (host.Content is KeyboardShortcutsEditorControl)
            return;

        host.Content = new KeyboardShortcutsEditorControl(
            GetKeyboardShortcutsViewModel(),
            showHeader: false,
            statusCallback: (message, isError) => SetSettingsStatus(message, isError));
    }

    private void SyncEditorSafetySettingsToggles()
    {
        CheckBox? top1000Toggle = this.FindControl<CheckBox>("SettingsEditorTop1000Toggle");
        CheckBox? mutationGuardToggle = this.FindControl<CheckBox>("SettingsEditorMutationGuardToggle");
        if (top1000Toggle is null || mutationGuardToggle is null)
            return;

        (bool top1000WithoutWhereEnabled, bool protectMutationWithoutWhereEnabled) = AppSettingsStore.LoadSqlEditorSafetySettings();

        _isSyncingEditorSafetySettings = true;
        top1000Toggle.IsChecked = top1000WithoutWhereEnabled;
        mutationGuardToggle.IsChecked = protectMutationWithoutWhereEnabled;
        _isSyncingEditorSafetySettings = false;

        CurrentShell.SetSqlEditorExecutionSafetyOptions(top1000WithoutWhereEnabled, protectMutationWithoutWhereEnabled);
    }

    private void SettingsEditorTop1000Toggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyEditorSafetySettingsFromToggles();
        e.Handled = true;
    }

    private void SettingsEditorMutationGuardToggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyEditorSafetySettingsFromToggles();
        e.Handled = true;
    }

    private void ApplyEditorSafetySettingsFromToggles()
    {
        if (_isSyncingEditorSafetySettings)
            return;

        bool top1000WithoutWhereEnabled = this.FindControl<CheckBox>("SettingsEditorTop1000Toggle")?.IsChecked == true;
        bool protectMutationWithoutWhereEnabled = this.FindControl<CheckBox>("SettingsEditorMutationGuardToggle")?.IsChecked == true;

        AppSettingsStore.SaveSqlEditorSafetySettings(top1000WithoutWhereEnabled, protectMutationWithoutWhereEnabled);
        CurrentShell.SetSqlEditorExecutionSafetyOptions(top1000WithoutWhereEnabled, protectMutationWithoutWhereEnabled);

        string status = string.Format(
            "Editor atualizado: TOP 1000 sem WHERE {0}; protecao de mutacao sem WHERE {1}.",
            top1000WithoutWhereEnabled ? "ON" : "OFF",
            protectMutationWithoutWhereEnabled ? "ON" : "OFF");
        SetSettingsStatus(status, isError: false);
    }

    private void SyncEditorResultDateTimeSettingsControls()
    {
        ComboBox? dateOrderCombo = this.FindControl<ComboBox>("SettingsEditorResultDateOrderCombo");
        ComboBox? separatorCombo = this.FindControl<ComboBox>("SettingsEditorResultDateSeparatorCombo");
        CheckBox? preferRawToggle = this.FindControl<CheckBox>("SettingsEditorResultPreferRawToggle");
        if (dateOrderCombo is null || separatorCombo is null || preferRawToggle is null)
            return;

        SqlEditorResultDateTimeDisplaySettings settings = AppSettingsStore.LoadSqlEditorResultDateTimeDisplaySettings();

        _isSyncingEditorResultDateTimeSettings = true;
        SelectComboBoxItemByTag(dateOrderCombo, settings.DateOrder);
        SelectComboBoxItemByTag(separatorCombo, settings.DateSeparator);
        preferRawToggle.IsChecked = settings.PreferRawValues;
        _isSyncingEditorResultDateTimeSettings = false;
    }

    private void SettingsEditorResultDateTime_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyEditorResultDateTimeSettingsFromControls();
        e.Handled = true;
    }

    private void ApplyEditorResultDateTimeSettingsFromControls()
    {
        if (_isSyncingEditorResultDateTimeSettings)
            return;

        ComboBox? dateOrderCombo = this.FindControl<ComboBox>("SettingsEditorResultDateOrderCombo");
        ComboBox? separatorCombo = this.FindControl<ComboBox>("SettingsEditorResultDateSeparatorCombo");
        CheckBox? preferRawToggle = this.FindControl<CheckBox>("SettingsEditorResultPreferRawToggle");
        if (dateOrderCombo is null || separatorCombo is null || preferRawToggle is null)
            return;

        string dateOrder = (dateOrderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "YMD";
        string separator = (separatorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "-";

        var settings = new SqlEditorResultDateTimeDisplaySettings
        {
            DateOrder = dateOrder,
            DateSeparator = separator,
            PreferRawValues = preferRawToggle.IsChecked == true,
        };

        AppSettingsStore.SaveSqlEditorResultDateTimeDisplaySettings(settings);
        SetSettingsStatus(
            $"Resultados do editor atualizados: ordem {dateOrder}, separador '{separator}', modo {(settings.PreferRawValues ? "raw" : "formatado")}.",
            isError: false);
    }

    private void SyncProjectConventionSettingsControls()
    {
        ComboBox? namingCombo = this.FindControl<ComboBox>("SettingsProjectNamingConventionCombo");
        ComboBox? wireCombo = this.FindControl<ComboBox>("SettingsProjectWireCurveModeCombo");
        CheckBox? enforceToggle = this.FindControl<CheckBox>("SettingsProjectEnforceAliasToggle");
        CheckBox? warnReservedToggle = this.FindControl<CheckBox>("SettingsProjectWarnReservedToggle");
        TextBox? maxAliasTextBox = this.FindControl<TextBox>("SettingsProjectMaxAliasLengthTextBox");
        if (namingCombo is null || wireCombo is null || enforceToggle is null || warnReservedToggle is null || maxAliasTextBox is null)
            return;

        ProjectConventionSettings settings = CurrentShell.CurrentProjectConventionSettings;
        _isSyncingProjectConventionSettings = true;
        SelectComboBoxItemByTag(namingCombo, settings.NamingConvention);
        SelectComboBoxItemByTag(wireCombo, settings.DefaultWireCurveMode);
        enforceToggle.IsChecked = settings.EnforceAliasNaming;
        warnReservedToggle.IsChecked = settings.WarnOnReservedKeywords;
        maxAliasTextBox.Text = settings.MaxAliasLength.ToString();
        _isSyncingProjectConventionSettings = false;

        CurrentShell.ApplyProjectConventionSettings(settings);
    }

    private void SelectComboBoxItemByTag(ComboBox comboBox, string? tagValue)
    {
        if (comboBox.Items is null || string.IsNullOrWhiteSpace(tagValue))
            return;

        foreach (object? option in comboBox.Items)
        {
            if (option is ComboBoxItem item
                && item.Tag is string itemTag
                && string.Equals(itemTag, tagValue, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SettingsProjectNamingConventionCombo_Changed(object? sender, SelectionChangedEventArgs e)
    {
        ApplyProjectConventionSettingsFromControls();
        e.Handled = true;
    }

    private void SettingsProjectWireCurveModeCombo_Changed(object? sender, SelectionChangedEventArgs e)
    {
        ApplyProjectConventionSettingsFromControls();
        e.Handled = true;
    }

    private void SettingsProjectEnforceAliasToggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyProjectConventionSettingsFromControls();
        e.Handled = true;
    }

    private void SettingsProjectWarnReservedToggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyProjectConventionSettingsFromControls();
        e.Handled = true;
    }

    private void SettingsProjectMaxAliasLengthTextBox_Changed(object? sender, TextChangedEventArgs e)
    {
        ApplyProjectConventionSettingsFromControls();
        e.Handled = true;
    }

    private void ApplyProjectConventionSettingsFromControls()
    {
        if (_isSyncingProjectConventionSettings)
            return;

        ComboBox? namingCombo = this.FindControl<ComboBox>("SettingsProjectNamingConventionCombo");
        ComboBox? wireCombo = this.FindControl<ComboBox>("SettingsProjectWireCurveModeCombo");
        CheckBox? enforceToggle = this.FindControl<CheckBox>("SettingsProjectEnforceAliasToggle");
        CheckBox? warnReservedToggle = this.FindControl<CheckBox>("SettingsProjectWarnReservedToggle");
        TextBox? maxAliasTextBox = this.FindControl<TextBox>("SettingsProjectMaxAliasLengthTextBox");
        if (namingCombo is null || wireCombo is null || enforceToggle is null || warnReservedToggle is null || maxAliasTextBox is null)
            return;

        string namingConvention = (namingCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "snake_case";
        string defaultWireCurveMode = (wireCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Bezier";
        bool enforceAliasNaming = enforceToggle.IsChecked == true;
        bool warnOnReservedKeywords = warnReservedToggle.IsChecked == true;
        int maxAliasLength = int.TryParse(maxAliasTextBox.Text, out int parsedMaxAliasLength)
            ? Math.Max(0, parsedMaxAliasLength)
            : 64;

        var settings = new ProjectConventionSettings
        {
            NamingConvention = namingConvention,
            EnforceAliasNaming = enforceAliasNaming,
            WarnOnReservedKeywords = warnOnReservedKeywords,
            MaxAliasLength = maxAliasLength,
            DefaultWireCurveMode = defaultWireCurveMode,
        };

        AppSettingsStore.SaveProjectConventionSettings(settings);
        CurrentShell.ApplyProjectConventionSettings(settings);
        SetSettingsStatus("Projeto atualizado: convenções e wire style aplicados globalmente.", isError: false);
    }

    private void SetSettingsStatus(string message, bool isError)
    {
        TextBlock? statusText = this.FindControl<TextBlock>("SettingsThemeStatusText");
        if (statusText is null)
            return;

        statusText.Text = message;
        statusText.Foreground = isError
            ? ResourceBrush("StatusErrorBrush", UiColorConstants.C_F87171)
            : ResourceBrush("StatusOkBrush", UiColorConstants.C_34D399);
    }

    private SettingsWorkspaceModule GetSettingsModule()
    {
        _settingsModule ??= new SettingsWorkspaceModule(
            getShell: () => CurrentShell,
            enterCanvas: EnterCanvasMode
        );

        return _settingsModule;
    }
}
