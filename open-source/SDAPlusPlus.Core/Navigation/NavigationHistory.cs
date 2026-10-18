namespace SDAPlusPlus.Core.Navigation;

public sealed class NavigationHistory<T>(T initial) where T : notnull
{
    private readonly List<T> _entries = [initial];
    private int _index;
    public T Current => _entries[_index];
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index + 1 < _entries.Count;

    public void Visit(T value)
    {
        if (EqualityComparer<T>.Default.Equals(Current, value)) return;
        _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(value);
        if (_entries.Count > 100) _entries.RemoveAt(0);
        _index = _entries.Count - 1;
    }
    public T Back() { if (CanGoBack) _index--; return Current; }
    public T Forward() { if (CanGoForward) _index++; return Current; }
}
