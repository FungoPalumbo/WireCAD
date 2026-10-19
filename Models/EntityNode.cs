using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace WireCAD.Models;

public class EntityNode : INotifyPropertyChanged
{
    private string _groupName = "";
    private bool _isExpanded;
    private bool _isMarked;
    private bool _isModified;
    private string? _baseline;

    public EntityNode(string groupName)
    {
        _groupName = groupName;
        Children.CollectionChanged += (_, _) => Raise(nameof(Summary));
    }

    public EntityNode(EntityBase entity)
    {
        Entity = entity;
        entity.PropertyChanged += Entity_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EntityBase? Entity { get; }

    public EntityNode? Parent { get; set; }

    public ObservableCollection<EntityNode> Children { get; } = new();

    public bool IsGroup => Entity == null;

    public string Name => Entity?.Name ?? _groupName;

    public string FilePath { get; set; } = "";

    public string DisplayName => Entity != null
        ? Entity.Name
        : IsModified ? $"{_groupName} (modificato)" : _groupName;

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
            {
                return;
            }
            _isModified = value;
            Raise();
            Raise(nameof(DisplayName));
        }
    }

    public string Summary => Entity?.Summary ?? (Children.Count == 1 ? "1 forma" : $"{Children.Count} forme");

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }
            _isExpanded = value;
            Raise();
        }
    }

    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value)
            {
                return;
            }
            _isMarked = value;
            Raise();
        }
    }

    /// <summary>Cambia il nome di un gruppo.</summary>
    public void Rename(string name)
    {
        _groupName = name;
        Raise(nameof(Name));
        Raise(nameof(DisplayName));
    }

    public void CaptureBaseline()
    {
        _baseline = ComputeSignature();
        IsModified = false;
    }

    public void UpdateModifiedState()
    {
        if (_baseline == null)
        {
            return;
        }
        IsModified = ComputeSignature() != _baseline;
    }

    private string ComputeSignature()
    {
        StringBuilder builder = new();
        foreach (EntityNode child in Children)
        {
            EntityBase entity = child.Entity!;
            builder.Append(entity.TypeKey).Append('|').Append(entity.Name);
            foreach (System.Windows.Point point in entity.GetDefinitionPoints())
            {
                builder.Append('|').Append(Normalize(point.X)).Append(';').Append(Normalize(point.Y));
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string Normalize(double value)
    {
        return (Math.Round(value, 4) + 0.0).ToString("R", CultureInfo.InvariantCulture);
    }

    public void Detach()
    {
        if (Entity != null)
        {
            Entity.PropertyChanged -= Entity_PropertyChanged;
        }
    }

    private void Entity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Name))
        {
            Raise(nameof(Name));
            Raise(nameof(DisplayName));
            Parent?.UpdateModifiedState();
        }
        else if (e.PropertyName == nameof(Summary))
        {
            Raise(nameof(Summary));
        }
    }

    private void Raise([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
