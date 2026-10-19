using System.ComponentModel;

namespace WireCAD.Models;

public class EntityField : INotifyPropertyChanged
{
    private readonly Func<double> _getter;
    private readonly Action<double> _setter;

    public EntityField(string label, Func<double> getter, Action<double> setter)
    {
        Label = label;
        _getter = getter;
        _setter = setter;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    /// <summary>Oggetto a cui appartiene il campo (per la casella "mostra sul foglio").</summary>
    public EntityBase? Owner { get; set; }

    /// <summary>Se vero il valore del campo è mostrato sul foglio accanto all'oggetto.</summary>
    public bool IsShown
    {
        get => Owner != null && Owner.ShownFields.Contains(Label);
        set
        {
            Owner?.SetFieldShown(Label, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
        }
    }

    public double Value
    {
        get => _getter();
        set
        {
            _setter(value);
            Refresh();
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
