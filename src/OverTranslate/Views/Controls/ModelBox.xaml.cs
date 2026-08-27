using System.Windows;
using System.Windows.Controls;
using OverTranslate.Services;
using OverTranslate.Services.Providers;
// UseWindowsForms puts System.Windows.Forms in the implicit usings, so this name collides
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;

namespace OverTranslate.Views.Controls;

/// <summary>
/// A model-name input box with a fetch button: the endpoint's own /models listing drops out of
/// the field, so a name like “Qwen/Qwen2.5-7B-Instruct” is picked from a list rather than typed
/// from memory.
/// </summary>
/// <remarks>
/// The listing is fetched fresh on every click — an Ollama user who just pulled a new model gets
/// it on the next press, with no cache to explain. Loading and failures are shown in the same
/// popup the list occupies, so the button never fails silently.
/// </remarks>
public partial class ModelBox : UserControl
{
    public ModelBox()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Where the fetch button gets the endpoint to list: the host supplies a callback so the
    /// listing always answers the values currently on that host's form, saved or not.
    /// </summary>
    public Func<(string BaseUrl, string ApiKey, int TimeoutSeconds)>? Source { get; set; }

    /// <summary>The model name being edited.</summary>
    public string Text
    {
        get => ModelInput.Text;
        set => ModelInput.Text = value;
    }

    /// <summary>What an empty box falls back to, shown in place rather than described elsewhere.</summary>
    public string Placeholder
    {
        get => HintBlock.Text;
        set
        {
            HintBlock.Text = value;
            UpdateHint();
        }
    }

    /// <summary>Raised when the user edits the name, after either typing or picking from the list.</summary>
    public event EventHandler? ModelTextChanged;

    /// <summary>True while a listing is in flight, so a second click cannot stack a second popup.</summary>
    private bool _fetching;

    private void ModelInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateHint();
        ModelTextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHint() =>
        HintBlock.Visibility = ModelInput.Text.Length == 0 && HintBlock.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        if (_fetching || Source is null) return;

        _fetching = true;
        FetchBtn.IsEnabled = false;
        try
        {
            ShowMessage(LocalizationService.Get("S.Models.Loading"));
            ListPopup.IsOpen = true;

            var (baseUrl, apiKey, timeout) = Source();
            var models = await OpenAiCompatibleProvider.FetchModelsAsync(baseUrl, apiKey, timeout);

            BuildList(models);
        }
        catch (Exception ex)
        {
            // The endpoint's own message (status + body summary, or the bad-URL wording) is the
            // actionable one; this control has nothing to add to it.
            ShowMessage(ex.Message);
        }
        finally
        {
            _fetching = false;
            FetchBtn.IsEnabled = true;
        }
    }

    private void BuildList(List<string> models)
    {
        ModelList.Children.Clear();
        foreach (var model in models)
        {
            var row = new Button
            {
                Style = (Style)FindResource("ModelRow"),
                Content = model,
            };
            row.Click += (_, _) =>
            {
                ModelInput.Text = model;
                // To the end, so the caret sits where the next keystroke would expect it.
                ModelInput.CaretIndex = model.Length;
                ListPopup.IsOpen = false;
            };
            ModelList.Children.Add(row);
        }

        MessageText.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Visible;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        ListHost.Visibility = Visibility.Collapsed;
    }
}
