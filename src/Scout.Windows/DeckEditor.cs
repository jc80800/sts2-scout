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
        Title = "Scout — Edit deck"; Width = 780; Height = Math.Min(900, SystemParameters.WorkArea.Height - 40); MinWidth = 600; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var entries = Deck.Entries(context).ToList();
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "Your deck", FontSize = 26, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Add the cards you own. Set the total copies for each card, then Save deck.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
        var character = new ComboBox { ItemsSource = new[] { "ironclad", "silent", "defect", "necrobinder", "regent" }, SelectedItem = context.Character };
        var version = new TextBox { Text = context.GameVersion }; var act = new TextBox { Text = context.Act.ToString() }; var ascension = new TextBox { Text = context.Ascension.ToString() };
        void Field(string label, Control control) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold }); panel.Children.Add(control); }
        Field("Character", character); Field($"Your game version (supported: {pack.GameVersion})", version); Field("Act (1–4)", act); Field("Ascension (0–10 for researched patch)", ascension);
        var search = new TextBox(); Field("Find a card — all characters", search);
        var found = new ListBox { Height = 130, DisplayMemberPath = "Label" }; panel.Children.Add(found);
        void Search() => found.ItemsSource = pack.Entities.Where(e => e.Kind == EntityKind.Card && e.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name).Select(e => new CatalogItem(e)).ToArray();
        search.TextChanged += (_, _) => Search(); Search();
        var copies = new TextBox { Text = "1" }; Field("Number of copies", copies);
        var upgraded = new CheckBox { Content = "Upgraded (+)" }; panel.Children.Add(upgraded);
        var addActions = new StackPanel(); panel.Children.Add(addActions);
        panel.Children.Add(new TextBlock { Text = "Current deck", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
        var deckList = new ListBox { Height = 170 }; panel.Children.Add(deckList);
        var deckActions = new WrapPanel(); panel.Children.Add(deckActions);
        string Label(DeckEntry e) => $"{e.Copies} × {pack.Entities.SingleOrDefault(c => c.Id == e.EntityId)?.Name ?? e.EntityId}{(e.Upgraded ? "+" : "")}";
        void Refresh() => deckList.ItemsSource = entries.Select(Label).ToArray();
        deckList.SelectionChanged += (_, _) => { if (deckList.SelectedIndex >= 0) { var entry = entries[deckList.SelectedIndex]; copies.Text = entry.Copies.ToString(); upgraded.IsChecked = entry.Upgraded; search.Text = pack.Entities.SingleOrDefault(c => c.Id == entry.EntityId)?.Name ?? entry.EntityId; found.SelectedItem = ((CatalogItem[])found.ItemsSource).SingleOrDefault(c => c.Card.Id == entry.EntityId); } };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) }; footer.Children.Add(message);
        void Button(string label, Action action)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 5, 0, 0) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException) { message.Text = ex.Message; } };
            if (label == "Save deck") footer.Children.Add(b);
            else if (label == "Add card") addActions.Children.Add(b);
            else { b.Margin = new Thickness(0, 4, 12, 4); deckActions.Children.Add(b); }
        }
        Button("Add card", () =>
        {
            if (found.SelectedItem is not CatalogItem item) { message.Text = "Select a card from the search results first."; return; }
            var e = item.Card;
            var count = int.Parse(copies.Text); if (count is < 1 or > 99) throw new InvalidDataException("Copies must be 1–99");
            var entry = new DeckEntry(e.Id, count, upgraded.IsChecked == true);
            entries.RemoveAll(c => c.EntityId == entry.EntityId && c.Upgraded == entry.Upgraded); entries.Add(entry); Refresh(); message.Text = $"Set {count} × {e.Name}{(entry.Upgraded ? "+" : "")}.";
        });
        Button("Remove card", () => { if (deckList.SelectedIndex >= 0) { entries.RemoveAt(deckList.SelectedIndex); Refresh(); } });
        Button("Clear deck", () => { entries.Clear(); Refresh(); });
        Button("Save deck", () =>
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
