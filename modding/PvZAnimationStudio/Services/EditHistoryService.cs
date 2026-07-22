using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed record EditorSnapshot(
    EditorProject Project,
    int CurrentFrame,
    int SelectedTrackIndex,
    int SelectedActionIndex);

public sealed class EditHistoryService
{
    public const int MaximumEntries = 100;
    private readonly List<(string Name, EditorSnapshot Snapshot)> _undo = [];
    private readonly List<(string Name, EditorSnapshot Snapshot)> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count == 0 ? null : _undo[^1].Name;
    public string? RedoName => _redo.Count == 0 ? null : _redo[^1].Name;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Record(string name, EditorSnapshot snapshot)
    {
        _undo.Add((name, snapshot));
        if (_undo.Count > MaximumEntries)
            _undo.RemoveRange(0, _undo.Count - MaximumEntries);
        _redo.Clear();
    }

    public (string Name, EditorSnapshot Snapshot)? Undo(EditorSnapshot current)
    {
        if (_undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((entry.Name, current));
        return entry;
    }

    public (string Name, EditorSnapshot Snapshot)? Redo(EditorSnapshot current)
    {
        if (_redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((entry.Name, current));
        if (_undo.Count > MaximumEntries)
            _undo.RemoveRange(0, _undo.Count - MaximumEntries);
        return entry;
    }
}
