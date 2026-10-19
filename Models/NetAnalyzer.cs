using System.Windows;

namespace WireCAD.Models;

/// <summary>Potenziale elettrico: insieme di fili e pin tra loro collegati.</summary>
public sealed class Net
{
    public int Index { get; init; }

    public List<WireEntity> Wires { get; } = new();

    public List<(EntityBase Owner, int Pin)> Pins { get; } = new();

    public string Name
    {
        get
        {
            foreach (WireEntity wire in Wires)
            {
                if (!string.IsNullOrWhiteSpace(wire.Signal))
                {
                    return wire.Signal.Trim();
                }
            }
            return string.Empty;
        }
    }
}

public sealed class NetResult
{
    private readonly HashSet<string> _connectedPins = new();
    private readonly HashSet<string> _connectedEnds = new();

    public List<Net> Nets { get; } = new();

    public Dictionary<WireEntity, Net> WireNets { get; } = new();

    internal void MarkPin(string node) => _connectedPins.Add(node);

    internal void MarkEnd(string endKey) => _connectedEnds.Add(endKey);

    public bool IsPinConnected(EntityBase owner, int pin) => _connectedPins.Contains(NetAnalyzer.PinNode(owner.Id, pin));

    public bool IsEndConnected(WireEntity wire, bool start) => _connectedEnds.Contains(NetAnalyzer.EndKey(wire, start));
}

/// <summary>
/// Calcola i potenziali del disegno. Due elementi sono collegati se hanno un punto di collegamento coincidente
/// (estremità filo, pin) oppure se una giunzione giace su un filo, oppure se i pin di un componente sono ponticellati.
/// </summary>
public static class NetAnalyzer
{
    private const double Tolerance = 0.01;

    public static string PinNode(Guid id, int pin) => $"P:{id}:{pin}";

    public static string EndKey(WireEntity wire, bool start) => $"{wire.Id}:{(start ? 0 : 1)}";

    private static string WireNode(WireEntity wire) => $"W:{wire.Id}";

    private sealed class UnionFind
    {
        private readonly Dictionary<string, string> _parent = new();

        public string Find(string item)
        {
            if (!_parent.ContainsKey(item))
            {
                _parent[item] = item;
                return item;
            }
            string root = item;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }
            string current = item;
            while (_parent[current] != root)
            {
                string next = _parent[current];
                _parent[current] = root;
                current = next;
            }
            return root;
        }

        public void Union(string a, string b)
        {
            string rootA = Find(a);
            string rootB = Find(b);
            if (rootA != rootB)
            {
                _parent[rootA] = rootB;
            }
        }
    }

    private static (long, long) Key(Point point)
    {
        return ((long)Math.Round(point.X / Tolerance), (long)Math.Round(point.Y / Tolerance));
    }

    public static NetResult Analyze(IReadOnlyList<EntityBase> entities)
    {
        NetResult result = new();
        UnionFind unions = new();
        Dictionary<(long, long), List<(string Node, string EndKey)>> byPoint = new();

        void Register(Point point, string node, string endKey)
        {
            (long, long) key = Key(point);
            if (!byPoint.TryGetValue(key, out List<(string Node, string EndKey)>? list))
            {
                list = new List<(string Node, string EndKey)>();
                byPoint[key] = list;
            }
            list.Add((node, endKey));
        }

        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();

        foreach (EntityBase entity in entities)
        {
            for (int i = 0; i < entity.PinCount; i++)
            {
                Register(entity.GetPinPosition(i), PinNode(entity.Id, i), string.Empty);
            }
            if (entity is SheathEntity shielded && shielded.ShieldPinIndices.Count == 2)
            {
                // la calza è un unico conduttore da un capo all'altro
                unions.Union(PinNode(shielded.Id, shielded.ShieldPinIndices[0]), PinNode(shielded.Id, shielded.ShieldPinIndices[1]));
            }
            if (entity is SheathEntity sheath && sheath.WireCount > 0)
            {
                // ogni ingresso è lo stesso conduttore della corrispondente uscita
                for (int k = 0; k < sheath.WireCount; k++)
                {
                    if (sheath.IsLinkBroken(k))
                    {
                        continue;
                    }
                    unions.Union(PinNode(sheath.Id, k), PinNode(sheath.Id, sheath.WireCount + k));
                }
            }
            if (entity is ComponentEntity component && component.Bridged && component.PinCount > 1)
            {
                for (int i = 1; i < component.PinCount; i++)
                {
                    unions.Union(PinNode(component.Id, 0), PinNode(component.Id, i));
                }
            }
        }

        foreach (WireEntity wire in wires)
        {
            unions.Find(WireNode(wire));
            Register(wire.StartPoint, WireNode(wire), EndKey(wire, true));
            Register(wire.EndPoint, WireNode(wire), EndKey(wire, false));
        }

        foreach (List<(string Node, string EndKey)> group in byPoint.Values)
        {
            List<string> distinct = group.Select(entry => entry.Node).Distinct().ToList();
            if (distinct.Count < 2)
            {
                continue;
            }
            for (int i = 1; i < distinct.Count; i++)
            {
                unions.Union(distinct[0], distinct[i]);
            }
            foreach (var entry in group)
            {
                if (entry.EndKey.Length > 0)
                {
                    result.MarkEnd(entry.EndKey);
                }
                else
                {
                    result.MarkPin(entry.Node);
                }
            }
        }

        foreach (JunctionEntity junction in entities.OfType<JunctionEntity>())
        {
            foreach (WireEntity wire in wires)
            {
                if (wire.PassesThrough(junction.Position))
                {
                    unions.Union(PinNode(junction.Id, 0), WireNode(wire));
                    result.MarkPin(PinNode(junction.Id, 0));
                }
            }
        }

        Dictionary<string, Net> byRoot = new();
        foreach (WireEntity wire in wires)
        {
            string root = unions.Find(WireNode(wire));
            if (!byRoot.TryGetValue(root, out Net? net))
            {
                net = new Net { Index = byRoot.Count };
                byRoot[root] = net;
                result.Nets.Add(net);
            }
            net.Wires.Add(wire);
            result.WireNets[wire] = net;
        }

        foreach (EntityBase entity in entities)
        {
            for (int i = 0; i < entity.PinCount; i++)
            {
                string root = unions.Find(PinNode(entity.Id, i));
                if (byRoot.TryGetValue(root, out Net? net))
                {
                    net.Pins.Add((entity, i));
                }
            }
        }

        return result;
    }
}
