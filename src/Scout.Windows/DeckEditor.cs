using System.IO;
using System.Windows;
using System.Windows.Controls;
using Scout.Core;

namespace Scout.Windows;

public sealed class DeckEditor : Window
{
    private sealed record CatalogItem(Entity Card) { public string Label => $"{Card.Name} [{Card.Card?.Color ?? Card.Id}]"; }
    public RunContext? Saved { get; private set; }
    public DeckEditor(StrategyPack pack, RunContext context)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Scout — Edit deck"; Width = 900; Height = Math.Min(780, SystemParameters.WorkArea.Height - 40); MinWidth = 760; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var entries = Deck.Entries(context).ToList();
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Your deck", FontSize = 26, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Add the cards you own. Set the total copies for each card, then Save deck.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16) });
        var character = new ComboBox { ItemsSource = new[] { "ironclad", "silent", "defect", "necrobinder", "regent" }, SelectedItem = context.Character };
        var version = new TextBox { Text = context.GameVersion == "unconfigured" ? "" : context.GameVersion };
        var act = new TextBox { Text = context.Act.ToString() }; var ascension = new TextBox { Text = context.Ascension.ToString() };
        var fields = new Grid { Margin = new Thickness(0, 0, 0, 20) }; header.Children.Add(fields);
        foreach (var width in new[] { 2d, 2d, 1d, 1d }) fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        void Field(int column, string label, Control control)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, column == 3 ? 0 : 12, 0) };
            field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }); field.Children.Add(control);
            Grid.SetColumn(field, column); fields.Children.Add(field);
        }
        Field(0, "Character", character); Field(1, $"Game version ({pack.GameVersion})", version); Field(2, "Act (1–4)", act); Field(3, "Ascension", ascension);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) }; footer.Children.Add(message);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var findPanel = new DockPanel { Margin = new Thickness(0, 0, 16, 0) }; columns.Children.Add(findPanel);
        var deckPanel = new DockPanel(); Grid.SetColumn(deckPanel, 1); columns.Children.Add(deckPanel);
        var searchArea = new StackPanel(); DockPanel.SetDock(searchArea, Dock.Top); findPanel.Children.Add(searchArea);
        searchArea.Children.Add(new TextBlock { Text = "Find a card", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var search = new TextBox { ToolTip = "Search all characters, curses, and status cards" }; searchArea.Children.Add(search);
        var addArea = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(addArea, Dock.Bottom); findPanel.Children.Add(addArea);
        addArea.Children.Add(new TextBlock { Text = "Total copies" });
        var copies = new TextBox { Text = "1" }; addArea.Children.Add(copies);
        var upgraded = new CheckBox { Content = "Upgraded (+)" }; addArea.Children.Add(upgraded);
        var found = new ListBox { DisplayMemberPath = "Label", Margin = new Thickness(0, 6, 0, 0) }; findPanel.Children.Add(found);
        void Search() => found.ItemsSource = pack.Entities.Where(e => e.Kind == EntityKind.Card && e.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name).Select(e => new CatalogItem(e)).ToArray();
        search.TextChanged += (_, _) => Search(); Search();
        var heading = new TextBlock { Text = "Current deck", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }; DockPanel.SetDock(heading, Dock.Top); deckPanel.Children.Add(heading);
        var deckActions = new WrapPanel(); DockPanel.SetDock(deckActions, Dock.Bottom); deckPanel.Children.Add(deckActions);
        var deckList = new ListBox(); deckPanel.Children.Add(deckList);
        string Label(DeckEntry e) => $"{e.Copies} × {pack.Entities.SingleOrDefault(c => c.Id == e.EntityId)?.Name ?? e.EntityId}{(e.Upgraded ? "+" : "")}";
        void Refresh() => deckList.ItemsSource = entries.Select(Label).ToArray();
        deckList.SelectionChanged += (_, _) => { if (deckList.SelectedIndex >= 0) { var entry = entries[deckList.SelectedIndex]; copies.Text = entry.Copies.ToString(); upgraded.IsChecked = entry.Upgraded; search.Text = pack.Entities.SingleOrDefault(c => c.Id == entry.EntityId)?.Name ?? entry.EntityId; found.SelectedItem = ((CatalogItem[])found.ItemsSource).SingleOrDefault(c => c.Card.Id == entry.EntityId); } };
        void Button(Panel parent, string label, Action action)
        {
            var button = new Button { Content = label }; button.Click += (_, _) => { try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException) { message.Text = ex.Message; } }; parent.Children.Add(button);
        }
        Button(addArea, "Add card", () =>
        {
            if (found.SelectedItem is not CatalogItem item) { message.Text = "Select a card from the search results first."; return; }
            var count = int.Parse(copies.Text); if (count is < 1 or > 99) throw new InvalidDataException("Copies must be 1–99");
            var entry = new DeckEntry(item.Card.Id, count, upgraded.IsChecked == true);
            entries.RemoveAll(c => c.EntityId == entry.EntityId && c.Upgraded == entry.Upgraded); entries.Add(entry); Refresh();
            message.Text = $"Set {count} × {item.Card.Name}{(entry.Upgraded ? "+" : "")}.";
        });
        Button(deckActions, "Remove card", () => { if (deckList.SelectedIndex >= 0) { entries.RemoveAt(deckList.SelectedIndex); Refresh(); } });
        Button(deckActions, "Clear deck", () => { entries.Clear(); Refresh(); message.Text = "Deck cleared. Add your new run's cards, then Save deck."; });
        Button(footer, "Save deck", () =>
        {
            var candidate = context with { Character = character.SelectedItem as string ?? "unconfigured", GameVersion = GameVersions.Normalize(version.Text), Act = int.Parse(act.Text), Ascension = int.Parse(ascension.Text), Cards = entries.ToArray(), Deck = entries.SelectMany(e => Enumerable.Repeat(e.EntityId, e.Copies)).ToArray() };
            Deck.Validate(candidate, pack);
            if (candidate.Character == "unconfigured" || candidate.Ascension > 10) throw new InvalidDataException("Select character and ascension 0–10");
            if (candidate.GameVersion != pack.GameVersion) throw new InvalidDataException($"This catalog supports {pack.GameVersion}. Your actual game version must match before advice is available.");
            Saved = candidate; DialogResult = true;
        }); Refresh();
    }

}

public sealed class RewardCorrection : Window
{
    public Observation? Corrected { get; private set; }
    public RewardCorrection(StrategyPack pack, RunContext context, Observation observation)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Scout — Enter cards"; Width = 620; Height = 600; MinWidth = 480; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Choose the three cards shown in the game. Mark any upgrades, then Get advice. This does not add cards to your deck.", TextWrapping = TextWrapping.Wrap });
        var boxes = new List<ComboBox>(); var checks = new List<CheckBox>();
        var pool = CatalogValidation.Pool(pack, context.Character).OrderBy(e => e.Name).ToArray();
        panel.Children.Add(new TextBlock { Text = $"{context.Character} + colorless • {pool.Length} available cards", Margin = new Thickness(0, 12, 0, 12) });
        for (var slot = 0; slot < 3; slot++)
        {
            panel.Children.Add(new TextBlock { Text = $"Card {slot + 1} — type its name", Margin = new Thickness(0, 8, 0, 4) });
            var box = new ComboBox { ItemsSource = pool, DisplayMemberPath = "Name", IsEditable = true, IsTextSearchEnabled = true };
            box.SelectedItem = pool.SingleOrDefault(e => e.Id == observation.Choices.SingleOrDefault(c => c.Slot == slot)?.EntityId);
            var check = new CheckBox { Content = "Upgraded (+)", IsChecked = observation.Choices.SingleOrDefault(c => c.Slot == slot)?.Upgraded == true };
            boxes.Add(box); checks.Add(check); panel.Children.Add(box); panel.Children.Add(check);
        }
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(message);
        var save = new Button { Content = "Get advice", Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (boxes.Any(b => b.SelectedItem is not Entity e || b.Text != e.Name)) { message.Text = "Choose each card from the catalog list"; return; }
            Corrected = observation with { Choices = boxes.Select((b, slot) => new Choice(slot, ((Entity)b.SelectedItem).Id, 1, Upgraded: checks[slot].IsChecked == true)).ToArray(), ProfileVersion = observation.ProfileVersion + "/user-confirmed" };
            DialogResult = true;
        };
    }
}
