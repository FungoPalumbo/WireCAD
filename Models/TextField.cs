using System.ComponentModel;

namespace WireCAD.Models;

/// <summary>Campo di testo modificabile nel pannello proprietà (attributi elettrici).</summary>
public class TextField : INotifyPropertyChanged
{
    private readonly Func<string> _getter;
    private readonly Action<string> _setter;

    public TextField(string label, Func<string> getter, Action<string> setter, IReadOnlyList<string>? choices = null)
    {
        Label = label;
        _getter = getter;
        _setter = setter;
        Choices = choices;
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

    /// <summary>Valori proposti in un menu a tendina (il campo resta comunque modificabile a mano).</summary>
    public IReadOnlyList<string>? Choices { get; }

    public bool HasChoices => Choices != null;

    public string Value
    {
        get => _getter();
        set
        {
            _setter(value ?? string.Empty);
            Refresh();
        }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
