using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// Pins the grouped ComboBox structure that makes provider rows visible and selectable.
/// </summary>
public class ProviderPickerMarkupTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Group_template_hosts_the_selectable_service_rows()
    {
        var styles = XDocument.Load(Path.Combine(
            StringsParityTests.ProjectDirectory(), "Themes", "SharedStyles.xaml"));

        var template = styles.Descendants()
            .Single(element => (string?)element.Attribute(X + "Key") == "ComboBoxGroupTemplate");

        Assert.Contains(
            template.Descendants(),
            element => element.Name.LocalName == "ContentPresenter" &&
                       (string?)element.Attribute("Content") == "{TemplateBinding Content}" &&
                       (string?)element.Attribute("ContentTemplate") == "{TemplateBinding ContentTemplate}");
        Assert.Contains(template.Descendants(), element => element.Name.LocalName == "ItemsPresenter");
    }

    [Theory]
    [InlineData("Views/Translation/TranslationPage.xaml")]
    [InlineData("Views/Capture/ToolbarWindow.xaml")]
    [InlineData("Views/QuickLookup/QuickLookupWindow.xaml")]
    [InlineData("Views/Realtime/RealtimePage.xaml")]
    public void Every_grouped_provider_picker_uses_the_selectable_group_container(string relativePath)
    {
        var path = Path.Combine(
            StringsParityTests.ProjectDirectory(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(path);
        var provider = document.Descendants()
            .Single(element => (string?)element.Attribute(X + "Name") == "ProviderBox");
        var groupStyle = provider.Descendants()
            .Single(element => element.Name.LocalName == "GroupStyle");

        Assert.Equal(
            "{StaticResource ComboBoxGroupItemStyle}",
            (string?)groupStyle.Attribute("ContainerStyle"));
    }

    [Fact]
    public void Grouped_picker_generates_selectable_service_items()
    {
        OnUiThread(() =>
        {
            var resources = SharedStyles();
            var options = new List<ServiceOption>
            {
                new("Microsoft", "Microsoft", false, false),
                new("OpenAI", "OpenAI", false, false, GroupKey: "S.Provider.Group.AI"),
            };
            var view = CollectionViewSource.GetDefaultView(options);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServiceOption.Group)));

            var picker = new ComboBox
            {
                Style = (Style)resources["ModernComboBox"],
                ItemsSource = view,
                SelectedValuePath = nameof(ServiceOption.Value),
                Width = 180,
            };
            picker.GroupStyle.Add(new GroupStyle
            {
                ContainerStyle = (Style)resources["ComboBoxGroupItemStyle"],
            });

            var window = new Window { Content = picker, Width = 240, Height = 160, ShowInTaskbar = false };
            try
            {
                window.Show();
                picker.IsDropDownOpen = true;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                var popup = (Popup)picker.Template.FindName("Popup", picker);
                Assert.NotNull(popup.Child);
                var items = Descendants<ComboBoxItem>(popup.Child).ToList();
                Assert.Equal(2, items.Count);

                items[1].IsSelected = true;
                Assert.Equal("OpenAI", picker.SelectedValue);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ResourceDictionary SharedStyles()
    {
        var path = Path.Combine(
            StringsParityTests.ProjectDirectory(), "Themes", "SharedStyles.xaml");
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
