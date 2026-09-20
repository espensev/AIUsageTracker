// <copyright file="SettingsWindow.Providers.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Helpers;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow
{
    private const string ResourceKeyTertiaryText = "TertiaryText";
    private const string ResourceKeySecondaryText = "SecondaryText";
    private const string ResourceKeyProgressBarGreen = "ProgressBarGreen";
    private const string ResourceKeyStatusTextWarning = "StatusTextWarning";

    private sealed record StatusPanelPresentation(
        bool UseHorizontalLayout,
        string PrimaryText,
        string PrimaryResourceKey,
        bool PrimaryItalic,
        IReadOnlyList<StatusSecondaryLine> SecondaryLines);

    internal readonly record struct StatusSecondaryLine(
        string Text,
        string? ResourceKey = null,
        bool Wrap = false,
        bool ExtraTopMargin = false);

    private void PopulateProviders()
    {
        this.ProvidersStack.Children.Clear();

        var displayItems = CreateProviderDisplayItems(this._configs, this._usages);
        var dashboardCards = CreateProviderDashboardGroups(this._usages);
        var displayedIds = displayItems.Select(item => item.Config.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additionalGroups = dashboardCards.Where(group => !displayedIds.Contains(group.Key)).ToList();
        var disambiguatedProviderIds = GetProviderIdsRequiringDisambiguation(
            displayItems.Select(item => item.Config.ProviderId).Concat(additionalGroups.Select(group => group.Key)));
        foreach (var item in displayItems)
        {
            var usage = this._usages.FirstOrDefault(u =>
                string.Equals(u.ProviderId, item.Config.ProviderId, StringComparison.OrdinalIgnoreCase))
                ?? dashboardCards[item.Config.ProviderId].FirstOrDefault();
            this.AddProviderCard(
                item.Config,
                usage,
                dashboardCards[item.Config.ProviderId].ToList(),
                item.IsDerived,
                disambiguatedProviderIds.Contains(item.Config.ProviderId));
        }

        // Cards whose owner has no settings row still need an accessible visibility control.
        foreach (var group in additionalGroups)
        {
            this.AddProviderCard(
                CreateDefaultDisplayConfig(group.Key),
                group.First(),
                group.ToList(),
                isDerived: true,
                disambiguateDisplayName: disambiguatedProviderIds.Contains(group.Key));
        }

        this.ApplyProviderFilter();
    }

    internal static ILookup<string, ProviderUsage> CreateProviderDashboardGroups(IReadOnlyCollection<ProviderUsage> usages)
    {
        // Deliberately omit the hidden-item filter so hidden and unavailable cached cards remain editable.
        return MainWindowRuntimeLogic.BuildMainWindowUsageList(usages)
            .ToLookup(usage => ResolveProviderOwnerId(usage.ProviderId), StringComparer.OrdinalIgnoreCase);
    }

    internal static bool MatchesProviderSearch(string providerId, string? searchText)
    {
        var query = searchText?.Trim();
        return string.IsNullOrEmpty(query) ||
               providerId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               ProviderMetadataCatalog.GetConfiguredDisplayName(providerId).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlySet<string> GetProviderIdsRequiringDisambiguation(IEnumerable<string> providerIds)
    {
        return providerIds
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(
                ProviderMetadataCatalog.GetConfiguredDisplayName,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string GetProviderSettingsDisplayLabel(string providerId, bool disambiguateDisplayName)
    {
        var displayName = ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
        return disambiguateDisplayName ? $"{displayName} ({providerId})" : displayName;
    }

    private void ProviderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        this.ApplyProviderFilter();
    }

    private void ApplyProviderFilter()
    {
        if (this.ProvidersStack == null || this.ProviderSearchBox == null || this.ProviderSearchEmptyText == null)
        {
            return;
        }

        var visibleCount = 0;
        foreach (var child in this.ProvidersStack.Children)
        {
            if (child is FrameworkElement { Tag: string providerId } card)
            {
                var matches = MatchesProviderSearch(providerId, this.ProviderSearchBox.Text);
                card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                visibleCount += matches ? 1 : 0;
            }
        }

        this.ProviderSearchEmptyText.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddProviderCard(
        ProviderConfig config,
        ProviderUsage? usage,
        IReadOnlyList<ProviderUsage> dashboardCards,
        bool isDerived,
        bool disambiguateDisplayName)
    {
        var card = new Border
        {
            Tag = config.ProviderId,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var settingsBehavior = ResolveProviderSettingsBehavior(config, usage, isDerived);
        var displayLabel = GetProviderSettingsDisplayLabel(config.ProviderId, disambiguateDisplayName);
        var panel = new StackPanel();
        panel.Children.Add(this.BuildProviderHeader(config, usage, settingsBehavior, dashboardCards, displayLabel));
        if (dashboardCards.Count != 1)
        {
            panel.Children.Add(this.BuildProviderVisibilitySettings(dashboardCards));
        }

        if (!isDerived)
        {
            var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            details.Children.Add(this.BuildProviderCredentials(config, usage, settingsBehavior));
            details.Children.Add(this.BuildProviderOptions(config, settingsBehavior));
            var expander = new Expander
            {
                Header = settingsBehavior.InputMode == ProviderInputMode.StandardApiKey ? "Credentials and options" : "Connection details and options",
                Content = details,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
            };
            expander.SetResourceReference(Control.ForegroundProperty, ResourceKeySecondaryText);
            AutomationProperties.SetName(expander, $"{displayLabel} connection details and options");
            panel.Children.Add(expander);
        }

        card.Child = panel;
        this.ProvidersStack.Children.Add(card);
    }

    private FrameworkElement BuildProviderCredentials(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        keyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyContent = this.BuildProviderInputContent(config, usage, settingsBehavior);
        Grid.SetColumn(keyContent, 0);
        keyPanel.Children.Add(keyContent);

        if (settingsBehavior.InputMode == ProviderInputMode.StandardApiKey)
        {
            var testButton = this.BuildTestConnectionButton(config, keyContent);
            Grid.SetColumn(testButton, 1);
            keyPanel.Children.Add(testButton);
            if (testButton.Tag is TextBlock resultText)
            {
                Grid.SetRow(resultText, 1);
                Grid.SetColumnSpan(resultText, 2);
                keyPanel.Children.Add(resultText);
            }
        }

        return keyPanel;
    }

    internal static IReadOnlyList<ProviderSettingsDisplayItem> CreateProviderDisplayItems(
        IReadOnlyCollection<ProviderConfig> configs,
        IReadOnlyCollection<ProviderUsage> usages)
    {
        var displayItems = configs
            .Where(config => ProviderMetadataCatalog.Find(config.ProviderId)?.ShowInSettings ?? false)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false))
            .ToList();
        var configuredProviderIds = displayItems
            .Select(item => item.Config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultProviderIds = ProviderMetadataCatalog.GetDefaultSettingsProviderIds()
            .Where(providerId => !configuredProviderIds.Contains(providerId))
            .ToList();

        var defaultItems = defaultProviderIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateDefaultDisplayConfig)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false));

        displayItems.AddRange(defaultItems);

        return displayItems
            .OrderBy(item => ProviderMetadataCatalog.GetConfiguredDisplayName(item.Config.ProviderId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Config.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static ProviderSettingsBehavior ResolveProviderSettingsBehavior(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isDerived)
    {
        var resolvedProviderId = ResolveProviderOwnerId(config.ProviderId);
        var hasSessionToken = IsSessionToken(config.ApiKey);
        var inputMode = isDerived
            ? ProviderInputMode.DerivedReadOnly
            : ResolveProviderInputMode(resolvedProviderId, usage, hasSessionToken);
        var isInactive = !isDerived && inputMode switch
        {
            ProviderInputMode.AutoDetectedStatus => usage == null || !usage.IsAvailable,
            ProviderInputMode.SessionAuthStatus => string.IsNullOrWhiteSpace(config.ApiKey) && usage?.IsAvailable != true,
            _ => string.IsNullOrWhiteSpace(config.ApiKey),
        };
        var sessionProviderLabel = inputMode == ProviderInputMode.SessionAuthStatus
            ? ProviderMetadataCatalog.Find(resolvedProviderId)?.SessionStatusLabel
            : null;

        return new ProviderSettingsBehavior(
            InputMode: inputMode,
            IsInactive: isInactive,
            SessionProviderLabel: sessionProviderLabel);
    }

    internal static bool IsSessionToken(string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProviderOwnerId(string providerId)
    {
        return ProviderMetadataCatalog.GetProviderOwnerId(providerId);
    }

    private static ProviderConfig CreateDefaultDisplayConfig(string providerId)
    {
        if (ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config))
        {
            return config;
        }

        return new ProviderConfig
        {
            ProviderId = providerId,
        };
    }

    private static ProviderConfig CreateDerivedConfig(ProviderUsage usage)
    {
        return new ProviderConfig
        {
            ProviderId = usage.ProviderId,
        };
    }

    private static ProviderInputMode ResolveProviderInputMode(string providerId, ProviderUsage? usage, bool hasSessionToken)
    {
        var settingsDef = ProviderMetadataCatalog.Find(providerId);
        var settingsMode = settingsDef?.SettingsMode ?? ProviderSettingsMode.StandardApiKey;
        if (settingsMode == ProviderSettingsMode.SessionAuthStatus &&
            (settingsDef?.UseSessionAuthStatusWhenQuotaBasedOrSessionToken ?? false) &&
            (usage is not QuotaProviderUsage qu || !qu.IsQuotaBased) &&
            !hasSessionToken)
        {
            settingsMode = ProviderSettingsMode.StandardApiKey;
        }

        return settingsMode switch
        {
            ProviderSettingsMode.AutoDetectedStatus => ProviderInputMode.AutoDetectedStatus,
            ProviderSettingsMode.ExternalAuthStatus => ProviderInputMode.ExternalAuthStatus,
            ProviderSettingsMode.SessionAuthStatus => ProviderInputMode.SessionAuthStatus,
            _ => ProviderInputMode.StandardApiKey,
        };
    }

    private FrameworkElement BuildProviderInputContent(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly
                or ProviderInputMode.AutoDetectedStatus
                or ProviderInputMode.ExternalAuthStatus
                or ProviderInputMode.SessionAuthStatus
                => this.BuildStatusPanel(config, usage, settingsBehavior),
            _ => this.BuildApiKeyEditor(config),
        };
    }

    private StackPanel BuildStatusPanel(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        var presentation = CreateStatusPresentation(
            config,
            usage,
            settingsBehavior,
            this._isPrivacyMode);

        var panel = new StackPanel
        {
            Orientation = presentation.UseHorizontalLayout
                ? Orientation.Horizontal
                : Orientation.Vertical,
        };

        var statusText = new TextBlock
        {
            Text = presentation.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontStyle = presentation.PrimaryItalic ? FontStyles.Italic : FontStyles.Normal,
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, presentation.PrimaryResourceKey);
        panel.Children.Add(statusText);

        foreach (var line in presentation.SecondaryLines)
        {
            var secondaryText = this.CreateSecondaryStatusText(line.Text);
            secondaryText.SetResourceReference(TextBlock.ForegroundProperty, line.ResourceKey ?? ResourceKeySecondaryText);
            secondaryText.TextWrapping = line.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (line.ExtraTopMargin)
            {
                secondaryText.Margin = new Thickness(0, 4, 0, 0);
            }

            panel.Children.Add(secondaryText);
        }

        return panel;
    }

    private static StatusPanelPresentation CreateStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderSettingsBehavior settingsBehavior,
        bool isPrivacyMode)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly => CreateDerivedStatusPresentation(config, usage),
            ProviderInputMode.AutoDetectedStatus => CreateAutoDetectedStatusPresentation(usage, isPrivacyMode),
            ProviderInputMode.ExternalAuthStatus => CreateExternalAuthStatusPresentation(config, usage, isPrivacyMode),
            ProviderInputMode.SessionAuthStatus => CreateSessionAuthStatusPresentation(config, usage, settingsBehavior, isPrivacyMode),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settingsBehavior),
                settingsBehavior.InputMode,
                "Status presentation is only valid for status-based provider modes."),
        };
    }

    private static StatusPanelPresentation CreateDerivedStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage)
    {
        var secondaryLines = new List<StatusSecondaryLine>();
        var sourceLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId ?? string.Empty);
        string primaryText;
        string primaryResourceKey;

        if (usage?.IsAvailable == true)
        {
            primaryText = $"Derived from {sourceLabel} usage (read-only)";
            primaryResourceKey = ResourceKeyProgressBarGreen;
        }
        else if (usage != null && !string.IsNullOrWhiteSpace(usage.Description))
        {
            primaryText = usage.Description;
            primaryResourceKey = ResourceKeyTertiaryText;
        }
        else
        {
            primaryText = "Derived provider (waiting for usage data)";
            primaryResourceKey = ResourceKeyTertiaryText;
        }

        if (usage is QuotaProviderUsage derivedQ && derivedQ.NextResetTime is DateTime derivedReset)
        {
            secondaryLines.Add(new StatusSecondaryLine(BuildSettingsResetText(usage, derivedReset)));
        }

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: primaryText,
            PrimaryResourceKey: primaryResourceKey,
            PrimaryItalic: false,
            SecondaryLines: secondaryLines);
    }

    private static StatusPanelPresentation CreateAutoDetectedStatusPresentation(
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var isConnected = usage?.IsAvailable == true;
        var accountInfo = usage?.AccountName;
        var hasAccountInfo = !string.IsNullOrWhiteSpace(accountInfo) && accountInfo is not ("Unknown" or "User");
        string displayAccount;
        if (hasAccountInfo)
        {
            displayAccount = isPrivacyMode ? PrivacyHelper.MaskAccountIdentifier(accountInfo!) : accountInfo!;
        }
        else
        {
            displayAccount = "No account detected";
        }

        var secondaryLines = new List<StatusSecondaryLine>();

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
            PrimaryResourceKey: isConnected ? ResourceKeyProgressBarGreen : ResourceKeyTertiaryText,
            PrimaryItalic: !isConnected,
            SecondaryLines: secondaryLines);
    }

    private static StatusPanelPresentation CreateExternalAuthStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var username = usage?.AccountName;
        var hasUsername = !string.IsNullOrWhiteSpace(username) && username is not ("Unknown" or "User");
        var isAuthenticated = !string.IsNullOrWhiteSpace(config.ApiKey) ||
                              usage?.IsAvailable == true ||
                              hasUsername;
        string displayText;
        if (!isAuthenticated)
        {
            displayText = "Not Authenticated";
        }
        else if (!hasUsername)
        {
            displayText = "Authenticated";
        }
        else if (isPrivacyMode)
        {
            displayText = $"Authenticated ({PrivacyHelper.MaskAccountIdentifier(username!)})";
        }
        else
        {
            displayText = $"Authenticated ({username})";
        }

        return new StatusPanelPresentation(
            UseHorizontalLayout: true,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? ResourceKeyProgressBarGreen : ResourceKeyTertiaryText,
            PrimaryItalic: false,
            SecondaryLines: Array.Empty<StatusSecondaryLine>());
    }

    private static StatusPanelPresentation CreateSessionAuthStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderSettingsBehavior settingsBehavior,
        bool isPrivacyMode)
    {
        var providerSessionLabel = settingsBehavior.SessionProviderLabel ??
                                   ProviderMetadataCatalog.GetConfiguredDisplayName(
                                       config.ProviderId ?? string.Empty);
        var hasSessionToken = IsSessionToken(config.ApiKey);
        var isAuthenticated = hasSessionToken || usage?.IsAvailable == true;
        var accountName = usage?.AccountName;

        var displayText = ResolveSessionAuthDisplayText(
            isAuthenticated,
            hasSessionToken,
            accountName,
            providerSessionLabel,
            usage?.IsAvailable,
            isPrivacyMode);

        var secondaryLines = new List<StatusSecondaryLine>();
        var resolvedReset = (usage as QuotaProviderUsage)?.NextResetTime;
        if (resolvedReset is DateTime nextReset)
        {
            secondaryLines.Add(BuildSettingsResetStatusLine(usage!, nextReset));
        }
        else if (isAuthenticated)
        {
            secondaryLines.Add(BuildSettingsResetLoadingStatusLine());
        }

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? ResourceKeyProgressBarGreen : ResourceKeyTertiaryText,
            PrimaryItalic: false,
            SecondaryLines: secondaryLines);
    }

    private static string ResolveSessionAuthDisplayText(
        bool isAuthenticated,
        bool hasSessionToken,
        string? accountName,
        string providerSessionLabel,
        bool? isUsageAvailable,
        bool isPrivacyMode)
    {
        if (!isAuthenticated)
        {
            return "Not Authenticated";
        }

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            return isPrivacyMode
                ? $"Authenticated ({PrivacyHelper.MaskAccountIdentifier(accountName)})"
                : $"Authenticated ({accountName})";
        }

        return hasSessionToken && isUsageAvailable != true
            ? $"Authenticated via {providerSessionLabel} - refresh to load quota"
            : $"Authenticated via {providerSessionLabel}";
    }

    internal static StatusSecondaryLine BuildSettingsResetStatusLine(ProviderUsage usage, DateTime nextReset)
    {
        return new StatusSecondaryLine(
            BuildSettingsResetText(usage, nextReset),
            ResourceKeyStatusTextWarning);
    }

    internal static StatusSecondaryLine BuildSettingsResetLoadingStatusLine()
    {
        return new StatusSecondaryLine(
            "Next reset: loading...",
            ResourceKeyStatusTextWarning);
    }

    private static string BuildSettingsResetText(ProviderUsage usage, DateTime nextReset)
    {
        var resetLabel = MainWindowRuntimeLogic.ResolveResetWindowLabel(usage);
        var resetText = MainWindowRuntimeLogic.IsMinimaxCodingPlanUsage(usage)
            ? MainWindowRuntimeLogic.FormatUtcResetDateTime(nextReset)
            : nextReset.ToString("g", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(resetLabel)
            ? $"Next reset: {resetText}"
            : $"Next {resetLabel} reset: {resetText}";
    }

    internal static string GetProviderConnectionStatus(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        if (usage != null)
        {
            return usage.IsAvailable ? "Connected" : "Unavailable";
        }

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return "Configured · awaiting refresh";
        }

        return settingsBehavior.InputMode == ProviderInputMode.AutoDetectedStatus ? "Waiting for local app" : "Not connected";
    }

    private Grid BuildProviderHeader(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderSettingsBehavior settingsBehavior,
        IReadOnlyList<ProviderUsage> dashboardCards,
        string displayLabel)
    {
        var headerPanel = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = this.CreateProviderIcon(config.ProviderId);
        icon.Width = 16;
        icon.Height = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(icon);

        var title = new TextBlock
        {
            Text = displayLabel,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var identity = new StackPanel();
        identity.Children.Add(title);
        var status = new TextBlock
        {
            Text = GetProviderConnectionStatus(config, usage, settingsBehavior),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, usage != null && !usage.IsAvailable ? ResourceKeyStatusTextWarning : ResourceKeySecondaryText);
        identity.Children.Add(status);
        Grid.SetColumn(identity, 1);
        headerPanel.Children.Add(identity);
        if (dashboardCards.Count == 1)
        {
            var visibility = this.BuildProviderVisibilitySettings(dashboardCards);
            visibility.Margin = new Thickness(12, 0, 0, 0);
            visibility.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(visibility, 2);
            headerPanel.Children.Add(visibility);
        }

        return headerPanel;
    }

    private WrapPanel BuildProviderOptions(ProviderConfig config, ProviderSettingsBehavior settingsBehavior)
    {
        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        options.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Show in tray",
            isChecked: config.ShowInTray,
            margin: new Thickness(0, 4, 16, 4),
            isEnabled: true,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ShowInTray = isChecked;
                this.MarkSettingsChanged(refreshTrayIcons: true);
            }));

        options.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Notifications",
            isChecked: config.EnableNotifications,
            margin: new Thickness(0, 4, 16, 4),
            isEnabled: true,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.EnableNotifications = isChecked;
                this.MarkSettingsChanged();
            }));

        var definition = ProviderMetadataCatalog.Find(config.ProviderId);
        if (settingsBehavior.InputMode == ProviderInputMode.AutoDetectedStatus &&
            definition?.FamilyMode == ProviderFamilyMode.FlatWindowCards)
        {
            options.Children.Add(this.CreateProviderHeaderCheckBox(
                content: "Show cached models offline",
                isChecked: config.ShowCachedModelsWhenOffline,
                margin: new Thickness(0, 4, 0, 4),
                isEnabled: true,
                onCheckedChanged: isChecked =>
                {
                    var trackedConfig = this.GetOrCreateTrackedConfig(config);
                    trackedConfig.ShowCachedModelsWhenOffline = isChecked;
                    this.MarkSettingsChanged();
                }));
        }

        return options;
    }

    private CheckBox CreateProviderHeaderCheckBox(
        string content,
        bool isChecked,
        Thickness margin,
        bool isEnabled,
        Action<bool> onCheckedChanged)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            IsChecked = isChecked,
            FontSize = 11,
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = margin,
            IsEnabled = isEnabled,
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, ResourceKeySecondaryText);
        checkBox.Checked += (_, _) => onCheckedChanged(true);
        checkBox.Unchecked += (_, _) => onCheckedChanged(false);
        return checkBox;
    }

    private FrameworkElement BuildApiKeyEditor(ProviderConfig config)
    {
        var keyBox = new TextBox
        {
            Text = GetDisplayApiKey(config.ApiKey, this._isPrivacyMode),
            Tag = config,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            IsReadOnly = this._isPrivacyMode,
        };
        AutomationProperties.SetName(keyBox, $"{ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId)} API key");

        if (!this._isPrivacyMode)
        {
            keyBox.TextChanged += (s, e) =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ApiKey = keyBox.Text;
                this.MarkSettingsChanged();
            };
        }

        var authSourcePanel = BuildAuthSourcePanel(config.AuthSource);
        if (authSourcePanel == null)
        {
            return keyBox;
        }

        var panel = new StackPanel();
        panel.Children.Add(keyBox);
        panel.Children.Add(authSourcePanel);
        return panel;
    }

    private Button BuildTestConnectionButton(ProviderConfig config, FrameworkElement keyContent)
    {
        var resultText = new TextBlock
        {
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        resultText.SetResourceReference(TextBlock.ForegroundProperty, ResourceKeySecondaryText);

        var button = new Button
        {
            Content = "Test",
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 50,
            Tag = resultText,
        };
        button.SetResourceReference(Button.ForegroundProperty, ResourceKeySecondaryText);

        button.Click += async (_, _) =>
        {
            var trackedConfig = this.GetOrCreateTrackedConfig(config);
            var apiKey = trackedConfig.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                resultText.Text = "Enter an API key first";
                resultText.Visibility = Visibility.Visible;
                return;
            }

            button.IsEnabled = false;
            button.Content = "...";
            resultText.Visibility = Visibility.Collapsed;

            try
            {
                var result = await this._monitorService
                    .TestProviderConnectionAsync(config.ProviderId, apiKey)
                    .ConfigureAwait(true);

                resultText.Text = result.Success ? "Connected" : result.Message;
                resultText.Foreground = result.Success
                    ? new SolidColorBrush(Color.FromRgb(106, 168, 79))
                    : new SolidColorBrush(Color.FromRgb(205, 92, 92));
                resultText.Visibility = Visibility.Visible;
            }
            catch (InvalidOperationException ex)
            {
                resultText.Text = $"Error: {ex.Message}";
                resultText.Foreground = new SolidColorBrush(Color.FromRgb(205, 92, 92));
                resultText.Visibility = Visibility.Visible;
            }
            finally
            {
                button.Content = "Test";
                button.IsEnabled = true;
            }
        };

        return button;
    }

    private static FrameworkElement? BuildAuthSourcePanel(string? authSource)
    {
        var (sourceLabel, removalHint, paths) = ResolveAuthSourceDisplay(authSource);
        if (sourceLabel == null)
        {
            return null;
        }

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var sourceLine = new TextBlock
        {
            FontSize = 9,
            Margin = new Thickness(0, 0, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        sourceLine.SetResourceReference(TextBlock.ForegroundProperty, ResourceKeySecondaryText);
        sourceLine.Inlines.Add(new System.Windows.Documents.Run("Source: ") { FontWeight = FontWeights.SemiBold });
        sourceLine.Inlines.Add(new System.Windows.Documents.Run(sourceLabel));
        if (!string.IsNullOrEmpty(removalHint))
        {
            sourceLine.ToolTip = $"To remove: {removalHint}";
        }

        panel.Children.Add(sourceLine);

        // File path lines (one per path, selectable for copy + edit/folder buttons)
        foreach (var path in paths)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = path,
                FontSize = 8,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                ToolTip = path,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pathBox.SetResourceReference(TextBox.ForegroundProperty, ResourceKeySecondaryText);
            Grid.SetColumn(pathBox, 0);
            row.Children.Add(pathBox);

            var capturedPath = path;
            var fileExists = File.Exists(path);

            var editButton = new Button
            {
                Content = "Edit",
                FontSize = 9,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = fileExists ? $"Open in Notepad: {path}" : "File does not exist yet",
                IsEnabled = fileExists,
            };
            editButton.SetResourceReference(Button.BackgroundProperty, "AccentColor");
            editButton.SetResourceReference(Button.ForegroundProperty, "AccentForeground");
            editButton.Click += (_, _) => OpenInNotepad(capturedPath);
            Grid.SetColumn(editButton, 1);
            row.Children.Add(editButton);

            var folderButton = new Button
            {
                Content = "Folder",
                FontSize = 9,
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Show in Explorer: {Path.GetDirectoryName(path)}",
            };
            folderButton.Click += (_, _) => OpenPathInExplorer(capturedPath);
            Grid.SetColumn(folderButton, 2);
            row.Children.Add(folderButton);

            panel.Children.Add(row);
        }

        return panel;
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Intentionally ignored - notepad launch failure is non-critical
        }
    }

    private static void OpenPathInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true,
                    });
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // If shell open fails, silently ignore — the path is still selectable for manual navigation.
        }
    }

    private static (string? SourceLabel, string? RemovalHint, IReadOnlyList<string> Paths) ResolveAuthSourceDisplay(string? authSource)
    {
        if (string.IsNullOrWhiteSpace(authSource) ||
            string.Equals(authSource, AuthSource.None, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, Array.Empty<string>());
        }

        // Environment variable
        if (AuthSource.TryParseEnvironmentVariable(authSource, out var varName))
        {
            return (
                $"Environment variable {varName}",
                $"Delete the {varName} environment variable from System Properties → Environment Variables, then restart.",
                Array.Empty<string>());
        }

        // Roo Code
        if (AuthSource.TryParseRooPath(authSource, out var rooPath))
        {
            return (
                "Roo Code",
                "Edit or delete the file below to remove the key from Roo Code.",
                new[] { rooPath });
        }

        // Kilo Code
        if (AuthSource.IsRooOrKilo(authSource))
        {
            return (
                "Kilo Code",
                "Remove the key from Kilo Code settings.",
                Array.Empty<string>());
        }

        // Config file(s) — show full paths
        var configPaths = AuthSource.ParseConfigFilePaths(authSource);
        if (configPaths.Count > 0)
        {
            // Determine human-readable source application from paths
            var appName = ResolveConfigSourceAppName(configPaths);
            return (
                appName,
                "Edit or delete the file(s) below, or clear the key field above and save.",
                configPaths);
        }

        // Fallback for other known constants (OpenCode Session, Codex Native, etc.)
        return (authSource, null, Array.Empty<string>());
    }

    private static string ResolveConfigSourceAppName(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (path.Contains("opencode", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenCode";
            }

            if (path.Contains("roo", StringComparison.OrdinalIgnoreCase))
            {
                return "Roo Code";
            }

            if (path.Contains("kilo", StringComparison.OrdinalIgnoreCase))
            {
                return "Kilo Code";
            }

            if (path.Contains("AIUsageTracker", StringComparison.OrdinalIgnoreCase))
            {
                return "AI Usage Tracker";
            }
        }

        return "Config file";
    }

    private static string GetDisplayApiKey(string? apiKey, bool isPrivacyMode)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return apiKey ?? string.Empty;
        }

        if (!isPrivacyMode)
        {
            return apiKey;
        }

        if (apiKey.Length > 8)
        {
            return apiKey[..4] + "****" + apiKey[^4..];
        }

        return "****";
    }

    private TextBlock CreateSecondaryStatusText(string text)
    {
        var statusText = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0),
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, ResourceKeySecondaryText);
        return statusText;
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        return this._providerIconService.CreateIcon(providerId);
    }

    private ProviderConfig GetOrCreateTrackedConfig(ProviderConfig config)
    {
        var existing = this._configs.FirstOrDefault(current =>
            current.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var tracked = this.CloneConfig(config);
        this._configs.Add(tracked);
        return tracked;
    }

    private ProviderConfig CloneConfig(ProviderConfig config)
    {
        return new ProviderConfig
        {
            ProviderId = config.ProviderId,
            ApiKey = config.ApiKey,
            Limit = config.Limit,
            BaseUrl = config.BaseUrl,
            ShowInTray = config.ShowInTray,
            ShowCachedModelsWhenOffline = config.ShowCachedModelsWhenOffline,
            EnableNotifications = config.EnableNotifications,
            EnabledSubTrays = config.EnabledSubTrays.ToList(),
            AuthSource = config.AuthSource,
            Description = config.Description,
            Models = config.Models
                .Select(model => new AIModelConfig
                {
                    Id = model.Id,
                    Name = model.Name,
                    Matches = model.Matches.ToList(),
                    Color = model.Color,
                })
                .ToList(),
        };
    }

    private FrameworkElement BuildProviderVisibilitySettings(IReadOnlyList<ProviderUsage> cards)
    {
        if (cards.Count == 0)
        {
            var waiting = this.CreateSecondaryStatusText("Dashboard cards appear after the first usage refresh.");
            waiting.TextWrapping = TextWrapping.Wrap;
            return waiting;
        }

        var panel = new StackPanel();
        foreach (var usage in cards.DistinctBy(card => card.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var label = cards.Count == 1 ? "Show on dashboard" : usage.ProviderName ?? usage.ProviderId;
            var checkBox = new CheckBox
            {
                Content = label,
                Tag = usage.ProviderId,
                IsChecked = !this._preferences.HiddenProviderItemIds.Contains(usage.ProviderId, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 2, 0, 2),
                MinHeight = 24,
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            checkBox.SetResourceReference(Control.ForegroundProperty, ResourceKeySecondaryText);
            AutomationProperties.SetName(checkBox, $"Show {usage.ProviderName ?? usage.ProviderId} on dashboard");
            checkBox.Checked += this.ProviderVisibility_Changed;
            checkBox.Unchecked += this.ProviderVisibility_Changed;
            panel.Children.Add(checkBox);
        }

        if (cards.Count == 1)
        {
            return panel;
        }

        var expander = new Expander
        {
            Header = "Dashboard cards",
            Content = panel,
            FontSize = 11,
        };
        expander.SetResourceReference(Control.ForegroundProperty, ResourceKeySecondaryText);
        return expander;
    }

    private void ProviderVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!this.IsInitialized || sender is not CheckBox { Tag: string itemId } cb)
        {
            return;
        }

        this.SetHiddenProviderItemId(itemId, !(cb.IsChecked ?? true));
        this.ScheduleAutoSave();
    }

    private void SetHiddenProviderItemId(string id, bool hidden)
    {
        var list = this._preferences.HiddenProviderItemIds;
        if (hidden)
        {
            if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(id);
            }
        }
        else
        {
            foreach (var item in list.Where(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                list.Remove(item);
            }
        }
    }
}
