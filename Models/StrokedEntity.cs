using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Primitiva di disegno con tratto selezionabile: continuo, tratteggiato, punteggiato o tratto-punto.</summary>
public abstract class StrokedEntity : EntityBase
{
    public const string DashSolid = "Continuo";
    public const string DashDashed = "Tratteggio";
    public const string DashDotted = "Punteggiato";
    public const string DashDashDot = "Tratto-punto";

    public static readonly string[] DashChoices = { DashSolid, DashDashed, DashDotted, DashDashDot };

    private string _dash = DashSolid;

    public string Dash
    {
        get => _dash;
        set => SetField(ref _dash, value);
    }

    public override DoubleCollection? DashPattern
    {
        get
        {
            switch (_dash)
            {
                case DashDashed:
                    return new DoubleCollection { 8, 4 };
                case DashDotted:
                    return new DoubleCollection { 2, 4 };
                case DashDashDot:
                    return new DoubleCollection { 8, 4, 2, 4 };
                default:
                    return null;
            }
        }
    }

    protected TextField DashField()
    {
        return new TextField("Tratto", () => string.IsNullOrEmpty(Dash) ? DashSolid : Dash, v => Dash = v, DashChoices);
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[] { DashField() };
    }

    public override void Export(Dictionary<string, string> data)
    {
        if (_dash != DashSolid)
        {
            data["dash"] = _dash;
        }
    }

    public override void Import(Dictionary<string, string> data)
    {
        string dash = GetText(data, "dash");
        _dash = dash.Length == 0 ? DashSolid : dash;
    }
}
