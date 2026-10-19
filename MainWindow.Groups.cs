using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WireCAD.Controls;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata ai gruppi dell'elenco entità: raggruppa, separa, rinomina.
public partial class MainWindow
{
    private bool _selectingFromTree;

    /// <summary>
    /// Durante la gestione di un clic sull'albero la selezione non deve aprire o chiudere i gruppi da sola:
    /// l'espansione dipende solo dalla freccia o dal doppio clic.
    /// </summary>
    private void BeginTreeSelection()
    {
        _selectingFromTree = true;
        Dispatcher.BeginInvoke(new Action(() => _selectingFromTree = false), System.Windows.Threading.DispatcherPriority.Input);
    }

    private string UniqueGroupName(string baseName, EntityNode? except = null)
    {
        HashSet<string> names = _roots
            .Where(root => root.IsGroup && !ReferenceEquals(root, except))
            .Select(root => root.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }
        int number = 2;
        while (names.Contains($"{baseName} ({number})"))
        {
            number++;
        }
        return $"{baseName} ({number})";
    }

    private string NextGroupName()
    {
        HashSet<string> names = _roots
            .Where(root => root.IsGroup)
            .Select(root => root.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int number = 1;
        while (names.Contains($"Gruppo {number}"))
        {
            number++;
        }
        return $"Gruppo {number}";
    }

    /// <summary>Sposta le entità nel gruppo indicato (null = fuori da ogni gruppo); i gruppi rimasti vuoti spariscono.</summary>
    private void MoveToGroup(IEnumerable<EntityBase> items, EntityNode? target)
    {
        foreach (EntityBase entity in items.ToList())
        {
            if (!_nodes.TryGetValue(entity, out EntityNode? node))
            {
                continue;
            }
            EntityNode? previous = node.Parent;
            if (ReferenceEquals(previous, target))
            {
                continue;
            }
            if (previous == null)
            {
                _roots.Remove(node);
            }
            else
            {
                previous.Children.Remove(node);
                if (previous.Children.Count == 0)
                {
                    _roots.Remove(previous);
                }
                else
                {
                    previous.UpdateModifiedState();
                }
            }
            node.Parent = target;
            if (target == null)
            {
                _roots.Add(node);
            }
            else
            {
                target.Children.Add(node);
            }
        }
    }

    private void GroupSelection()
    {
        List<EntityBase> items = _entities.Where(_selection.Contains).ToList();
        if (items.Count < 2)
        {
            StatusHint.Text = "Seleziona almeno due oggetti per raggrupparli.";
            return;
        }
        string? name = Dialogs.Prompt(this, "Raggruppa", $"Nome del gruppo ({items.Count} oggetti):", NextGroupName());
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        EntityNode group = new(UniqueGroupName(name.Trim())) { IsExpanded = true };
        _roots.Add(group);
        MoveToGroup(items, group);
        SetSelection(items);
        CommitChange();
        StatusHint.Text = $"Creato il gruppo \"{group.Name}\" con {items.Count} oggetti.";
    }

    private void UngroupSelection()
    {
        List<EntityBase> items = _entities
            .Where(item => _selection.Contains(item) && _nodes.TryGetValue(item, out EntityNode? node) && node.Parent != null)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }
        MoveToGroup(items, null);
        UpdateTreeMarks();
        CommitChange();
        StatusHint.Text = $"{items.Count} oggetti separati dal gruppo.";
    }

    private void RenameGroup(EntityNode group)
    {
        string? name = Dialogs.Prompt(this, "Rinomina gruppo", "Nuovo nome del gruppo:", group.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        group.Rename(UniqueGroupName(name.Trim(), group));
        CommitChange();
    }

    private void DissolveGroup(EntityNode group)
    {
        List<EntityBase> items = GroupEntities(group);
        MoveToGroup(items, null);
        SetSelection(items);
        CommitChange();
        StatusHint.Text = "Gruppo sciolto: gli oggetti restano sul foglio.";
    }

    /// <summary>Menu del tasto destro su una entità dell'elenco.</summary>
    private void ShowEntityTreeMenu(EntityNode node)
    {
        ContextMenu menu = new() { Placement = PlacementMode.MousePoint };

        MenuItem group = new()
        {
            Header = _selection.Count >= 2
                ? $"Raggruppa selezione ({_selection.Count} oggetti)..."
                : "Raggruppa selezione (seleziona almeno due oggetti)",
            IsEnabled = _selection.Count >= 2
        };
        group.Click += (_, _) => GroupSelection();
        menu.Items.Add(group);

        MenuItem ungroup = new()
        {
            Header = "Separa dal gruppo",
            IsEnabled = _selection.Any(item => _nodes.TryGetValue(item, out EntityNode? member) && member.Parent != null)
        };
        ungroup.Click += (_, _) => UngroupSelection();
        menu.Items.Add(ungroup);

        menu.Items.Add(new Separator());

        MenuItem delete = new() { Header = "Elimina selezione", InputGestureText = "Canc" };
        delete.Click += (_, _) => DeleteSelected();
        menu.Items.Add(delete);

        menu.IsOpen = true;
    }
}
