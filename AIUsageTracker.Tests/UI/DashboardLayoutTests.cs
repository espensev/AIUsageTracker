// <copyright file="DashboardLayoutTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

[Collection("WpfState")]
public sealed class DashboardLayoutTests
{
    [Fact]
    public Task CreateProviderCard_MinimumWindowWidth_KeepsProviderNameAndQuotaVisible()
    {
        return RunInStaAsync(() =>
        {
            var fixture = MainWindowDeterministicFixture.Create();
            var renderer = CreateRenderer(fixture.Preferences);

            foreach (var usage in fixture.Usages)
            {
                // Reserve the window border, list padding and vertical scrollbar at 320 DIP.
                var card = renderer.CreateProviderCard(usage, showUsed: false);
                Arrange(card, 296);
                var name = Descendants<TextBlock>(card).Single(block =>
                    block.Inlines.Cast<Inline>().Any(inline => inline is Run run && string.Equals(run.Text, usage.ProviderName, StringComparison.Ordinal)));
                Assert.True(name.ActualWidth >= 64, $"Provider name has only {name.ActualWidth} DIP: {usage.ProviderName}");

                var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false, fixture.Preferences.EnablePaceAdjustment);
                var status = Descendants<TextBlock>(card).Single(block => string.Equals(block.Text, presentation.StatusText, StringComparison.Ordinal));
                Assert.True(status.ActualWidth > 0);
                Assert.True(status.TranslatePoint(new Point(status.ActualWidth, 0), card).X <= 296);
            }
        });
    }

    [Fact]
    public Task CreateProviderCard_LongName_TrimsNameWithoutDisplacingQuota()
    {
        return RunInStaAsync(() =>
        {
            var fixture = MainWindowDeterministicFixture.Create();
            var usage = fixture.Usages[0];
            usage.ProviderName += " — a workspace with a much longer display name";
            var card = CreateRenderer(fixture.Preferences).CreateProviderCard(usage, showUsed: false);
            Arrange(card, 296);

            var name = Descendants<TextBlock>(card).Single(block =>
                block.Inlines.Cast<Inline>().Any(inline => inline is Run run && string.Equals(run.Text, usage.ProviderName, StringComparison.Ordinal)));
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            Assert.InRange(name.ActualWidth, 64, 140);
            Assert.Contains(usage.ProviderName, AutomationProperties.GetName(card), StringComparison.Ordinal);
        });
    }

    [Fact]
    public Task CreateProviderCard_EmptyStatusSlot_UsesAvailableHeaderWidth()
    {
        return RunInStaAsync(() =>
        {
            var fixture = MainWindowDeterministicFixture.Create();
            fixture.Preferences.CardCompactMode = false;
            fixture.Preferences.CardStatusLine = CardSlotContent.None;
            var usage = fixture.Usages[0];
            usage.ProviderName += " with a long account name";
            var card = CreateRenderer(fixture.Preferences).CreateProviderCard(usage, showUsed: false);
            Arrange(card, 296);

            var name = Descendants<TextBlock>(card).Single(block =>
                block.Inlines.Cast<Inline>().Any(inline => inline is Run run && string.Equals(run.Text, usage.ProviderName, StringComparison.Ordinal)));
            Assert.InRange(name.ActualWidth, 220, 296);
            Assert.True(name.TranslatePoint(new Point(name.ActualWidth, 0), card).X <= 296);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CreateProviderCard_RecentReading_PreservesFreshnessAndStaleState(bool isStale)
    {
        return RunInStaAsync(() =>
        {
            var fixture = MainWindowDeterministicFixture.Create();
            var usage = fixture.Usages[0];
            usage.FetchedAt = DateTime.UtcNow;
            usage.IsStale = isStale;
            var card = CreateRenderer(fixture.Preferences).CreateProviderCard(usage, showUsed: false);
            Arrange(card, 296);

            var labels = Descendants<TextBlock>(card).Select(block => block.Text).ToList();
            Assert.Equal(isStale, labels.Contains("Stale", StringComparer.Ordinal));
            Assert.Equal(isStale, labels.Contains("just now", StringComparer.Ordinal));
            Assert.Equal(1, card.Opacity);
            Assert.Contains("Last updated:", AutomationProperties.GetHelpText(card), StringComparison.Ordinal);
        });
    }

    [Fact]
    public Task Footer_MinimumWindowWidth_KeepsActionsVisibleAndNamed()
    {
        return RunInStaAsync(() =>
        {
            var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "UI", "MainWindow.xaml"));
            XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
            var footerSource = new XElement(source.Descendants().Single(element => string.Equals((string?)element.Attribute(xamlNamespace + "Name"), "FooterBorder", StringComparison.Ordinal)));

            // Load the actual footer without launching MainWindow or attaching its live service handlers.
            foreach (var attribute in footerSource.DescendantsAndSelf().Attributes().Where(attribute =>
                         attribute.Name.LocalName is "Click" or "Checked" or "Unchecked").ToList())
            {
                attribute.Remove();
            }

            var footer = Assert.IsType<Border>(XamlReader.Parse(footerSource.ToString()));
            Arrange(footer, 318);
            var buttons = Descendants<Button>(footer).ToList();
            Assert.Equal(4, buttons.Count);
            foreach (var button in buttons)
            {
                var position = button.TranslatePoint(default, footer);
                Assert.InRange(position.X, 0, 318 - button.ActualWidth);
                Assert.True(button.ActualHeight >= 32);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            }

            var toggles = Descendants<CheckBox>(footer).ToList();
            Assert.Equal(2, toggles.Count);
            Assert.True(toggles.Max(toggle => toggle.TranslatePoint(new Point(toggle.ActualWidth, 0), footer).X) <=
                        buttons.Min(button => button.TranslatePoint(default, footer).X));
        });
    }

    private static ProviderCardRenderer CreateRenderer(AppPreferences preferences) =>
        new(
            preferences,
            isPrivacyMode: true,
            (_, fallback) => fallback,
            _ => new Border(),
            (_, content) => new ToolTip { Content = content },
            _ => { },
            UsageMath.FormatRelativeTime);

    private static void Arrange(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static Task RunInStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
