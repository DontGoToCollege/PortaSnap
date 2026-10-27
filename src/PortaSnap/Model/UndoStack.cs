using System.Windows.Media.Imaging;

namespace PortaSnap.Model;

/// <summary>Snapshot-based undo: each entry stores the base image reference plus serialized annotations/effects.</summary>
public sealed class UndoStack
{
    const int Max = 80;

    public sealed record State(BitmapSource Base, string Annotations, DocumentEffects Effects);

    readonly List<State> _undo = new();
    readonly List<State> _redo = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public static State Snapshot(SnapDocument d) => new(d.Base, SnapJson.SerializeAnnotations(d.Annotations), d.Effects.Clone());

    public void Checkpoint(SnapDocument d) => Push(Snapshot(d));

    public void Push(State s)
    {
        _undo.Add(s);
        if (_undo.Count > Max) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DropLast()
    {
        if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo(SnapDocument d)
    {
        if (_undo.Count == 0) return false;
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Snapshot(d));
        Restore(d, s);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo(SnapDocument d)
    {
        if (_redo.Count == 0) return false;
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Snapshot(d));
        Restore(d, s);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void Restore(SnapDocument d, State s)
    {
        if (!ReferenceEquals(d.Base, s.Base)) d.Base = s.Base;
        d.Annotations.Clear();
        d.Annotations.AddRange(SnapJson.DeserializeAnnotations(s.Annotations));
        d.Effects = s.Effects.Clone();
    }
}
