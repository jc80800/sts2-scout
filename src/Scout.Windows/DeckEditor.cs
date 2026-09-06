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
        Title = "Scout — current run deck"; Width = 700; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var entries = Deck.Entries(context).ToList();
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = new ScrollViewer { Content = panel };
        panel.Children.Add(new TextBlock { Text = "Enter the complete current deck. Save after every pick, upgrade, removal, or new run.\nScout never infers that a recommended card was taken.", TextWrapping = TextWrapping.Wrap });
        var character = new ComboBox { ItemsSource = new[] { "ironclad", "silent", "defect", "necrobinder", "regent" }, SelectedItem = context.Character };
        var version = new TextBox { Text = context.GameVersion }; var act = new TextBox { Text = context.Act.ToString() }; var ascension = new TextBox { Text = context.Ascension.ToString() };
        void Field(string label, Control control) { panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(control); }
        Field("Character", character); Field($"Installed game version (catalog: {pack.GameVersion})", version); Field("Act (1–4)", act); Field("Ascension (0–10 for researched patch)", ascension);
        var search = new TextBox(); Field("Search canonical catalog (all cards, including curses/status)", search);
        var found = new ListBox { Height = 130, DisplayMemberPath = "Label" }; panel.Children.Add(found);
        void Search() => found.ItemsSource = pack.Entities.Where(e => e.Kind == EntityKind.Card && e.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name).Select(e => new CatalogItem(e)).ToArray();
        search.TextChanged += (_, _) => Search(); Search();
        var copies = new TextBox { Text = "1" }; Field("Copies for selected entry", copies);
        var upgraded = new CheckBox { Content = "Upgraded copies (+)" }; panel.Children.Add(upgraded);
        var deckList = new ListBox { Height = 170 }; panel.Children.Add(deckList);
        string Label(DeckEntry e) => $"{e.Copies} × {pack.Entities.SingleOrDefault(c => c.Id == e.EntityId)?.Name ?? e.EntityId}{(e.Upgraded ? "+" : "")}";
        void Refresh() => deckList.ItemsSource = entries.Select(Label).ToArray();
        deckList.SelectionChanged += (_, _) => { if (deckList.SelectedIndex >= 0) { var entry = entries[deckList.SelectedIndex]; copies.Text = entry.Copies.ToString(); upgraded.IsChecked = entry.Upgraded; search.Text = pack.Entities.SingleOrDefault(c => c.Id == entry.EntityId)?.Name ?? entry.EntityId; found.SelectedItem = ((CatalogItem[])found.ItemsSource).SingleOrDefault(c => c.Card.Id == entry.EntityId); } };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(message);
        void Button(string label, Action action)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 5, 0, 0) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException) { message.Text = ex.Message; } }; panel.Children.Add(b);
        }
        Button("Add card / set copies for base or upgraded entry", () =>
        {
            if (found.SelectedItem is not CatalogItem item) return;
            var e = item.Card;
            var count = int.Parse(copies.Text); if (count is < 1 or > 99) throw new InvalidDataException("Copies must be 1–99");
            var entry = new DeckEntry(e.Id, count, upgraded.IsChecked == true);
            entries.RemoveAll(c => c.EntityId == entry.EntityId && c.Upgraded == entry.Upgraded); entries.Add(entry); Refresh();
        });
        Button("Remove selected deck entry", () => { if (deckList.SelectedIndex >= 0) { entries.RemoveAt(deckList.SelectedIndex); Refresh(); } });
        Button("New run — clear entered deck", () => { entries.Clear(); Refresh(); });
        Button("Save current run", () =>
        {
            var candidate = context with { Character = character.SelectedItem as string ?? "unconfigured", GameVersion = version.Text.Trim(), Act = int.Parse(act.Text), Ascension = int.Parse(ascension.Text), Cards = entries.ToArray(), Deck = entries.SelectMany(e => Enumerable.Repeat(e.EntityId, e.Copies)).ToArray() };
            Deck.Validate(candidate, pack);
            if (candidate.Character == "unconfigured" || candidate.Ascension > 10) throw new InvalidDataException("Select character and ascension 0–10");
            Saved = candidate; DialogResult = true;
        }); Refresh();
    }
}

public sealed class RewardCorrection : Window
{
    public Observation? Corrected { get; private set; }
    public RewardCorrection(StrategyPack pack, RunContext context, Observation observation)
    {
        Title = "Confirm the three visible reward cards"; Width = 520; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Confirm all three cards and upgrades from the game. These corrections apply only to this paused reward.", TextWrapping = TextWrapping.Wrap });
        var boxes = new List<ComboBox>(); var checks = new List<CheckBox>();
        var pool = CatalogValidation.Pool(pack, context.Character).OrderBy(e => e.Name).ToArray();
        for (var slot = 0; slot < 3; slot++)
        {
            panel.Children.Add(new TextBlock { Text = $"Slot {slot + 1} — type to search" });
            var box = new ComboBox { ItemsSource = pool, DisplayMemberPath = "Name", IsEditable = true, IsTextSearchEnabled = true };
            box.SelectedItem = pool.SingleOrDefault(e => e.Id == observation.Choices.SingleOrDefault(c => c.Slot == slot)?.EntityId);
            var check = new CheckBox { Content = "Upgraded (+)", IsChecked = observation.Choices.SingleOrDefault(c => c.Slot == slot)?.Upgraded == true };
            boxes.Add(box); checks.Add(check); panel.Children.Add(box); panel.Children.Add(check);
        }
        var message = new TextBlock(); panel.Children.Add(message);
        var save = new Button { Content = "Confirm this reward", Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (boxes.Any(b => b.SelectedItem is not Entity e || b.Text != e.Name)) { message.Text = "Choose each card from the catalog list"; return; }
            Corrected = observation with { Choices = boxes.Select((b, slot) => new Choice(slot, ((Entity)b.SelectedItem).Id, 1, Upgraded: checks[slot].IsChecked == true)).ToArray(), ProfileVersion = observation.ProfileVersion + "/user-confirmed" };
            DialogResult = true;
        };
    }
}
