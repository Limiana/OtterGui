using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using ImGuiNET;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Filesystem;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Text;

namespace OtterGui.FileSystem.Selector;

public record struct ModSelectorSettings(float CurrentWidth, float MinimumScale, float MaximumScale, bool Resizable, bool UseScaling);

public partial class FileSystemSelector<T, TStateStorage> where T : class where TStateStorage : struct
{
    public delegate void SelectionChangeDelegate(T? oldSelection, T? newSelection, in TStateStorage state);

    protected readonly HashSet<FileSystem<T>.IPath> _selectedPaths = [];

    // The currently selected leaf, if any.
    protected FileSystem<T>.Leaf? SelectedLeaf;

    // The currently selected value, if any.
    public T? Selected
        => SelectedLeaf?.Value;

    public IReadOnlySet<FileSystem<T>.IPath> SelectedPaths
        => _selectedPaths;

    // Fired after the selected leaf changed.
    public event SelectionChangeDelegate? SelectionChanged;
    private FileSystem<T>.Leaf? _jumpToSelection;

    public void ClearSelection()
        => Select(null, AllowMultipleSelection);

    public void RemovePathFromMultiSelection(FileSystem<T>.IPath path)
    {
        _selectedPaths.Remove(path);
        if (_selectedPaths.Count == 1 && _selectedPaths.First() is FileSystem<T>.Leaf leaf)
            Select(leaf, true, GetState(leaf));
    }

    private void Select(FileSystem<T>.IPath? path, in TStateStorage storage, bool additional, bool all)
    {
        if (path == null)
        {
            Select(null, AllowMultipleSelection, storage);
        }
        else if (all && AllowMultipleSelection && SelectedLeaf != path)
        {
            var idxTo = _state.IndexOf(s => s.Path == path);
            var depth = _state[idxTo].Depth;
            if (SelectedLeaf != null && _selectedPaths.Count == 0)
            {
                var idxFrom = _state.IndexOf(s => s.Path == SelectedLeaf);
                (idxFrom, idxTo) = idxFrom > idxTo ? (idxTo, idxFrom) : (idxFrom, idxTo);
                if (_state.Skip(idxFrom).Take(idxTo - idxFrom + 1).All(s => s.Depth == depth))
                {
                    foreach (var p in _state.Skip(idxFrom).Take(idxTo - idxFrom + 1))
                        _selectedPaths.Add(p.Path);
                    Select(null, false);
                }
            }
        }
        else if (additional && AllowMultipleSelection)
        {
            if (SelectedLeaf != null && _selectedPaths.Count == 0)
                _selectedPaths.Add(SelectedLeaf);
            if (!_selectedPaths.Add(path))
                RemovePathFromMultiSelection(path);
            else
                Select(null, false);
        }
        else if (path is FileSystem<T>.Leaf leaf)
        {
            Select(leaf, AllowMultipleSelection, storage);
        }
    }

    protected virtual void Select(FileSystem<T>.Leaf? leaf, bool clear, in TStateStorage storage = default)
    {
        if (clear)
            _selectedPaths.Clear();

        var oldV = SelectedLeaf?.Value;
        var newV = leaf?.Value;
        if (oldV == newV)
            return;

        SelectedLeaf = leaf;
        SelectionChanged?.Invoke(oldV, newV, storage);
    }

    protected readonly FileSystem<T> FileSystem;

    public virtual ISortMode<T> SortMode
        => ISortMode<T>.Lexicographical;

    // Used by Add and AddFolder buttons.
    private string _newName = string.Empty;

    private readonly string _label = string.Empty;

    public string Label
    {
        get => _label;
        init
        {
            _label = value;
            MoveLabel = $"{value}Move";
        }
    }

    // Default color for tree expansion lines.
    protected virtual uint FolderLineColor
        => 0xFFFFFFFF;

    // Default color for folder names.
    protected virtual uint ExpandedFolderColor
        => 0xFFFFFFFF;

    protected virtual uint CollapsedFolderColor
        => 0xFFFFFFFF;

    // Whether all folders should be opened by default or closed.
    protected virtual bool FoldersDefaultOpen
        => false;

    public readonly Action<Exception> ExceptionHandler;

    public readonly bool AllowMultipleSelection;

    protected readonly Logger Log;

    protected virtual void SetSize(Vector2 size)
    {
        _currentWidth = size.X;
    }

    protected virtual float CurrentWidth
        => MathF.Round(ImGui.GetContentRegionAvail().X);

    protected virtual float MinimumAbsoluteSize
        => 0;

    protected virtual float MinimumAbsoluteRemainder
        => 0;

    protected virtual float MinimumScaling
        => 0.1f;

    protected virtual float MaximumScaling
        => 0.9f;

    protected virtual bool UseScaling
        => true;

    protected virtual bool Resizable
        => true;

    public FileSystemSelector(FileSystem<T> fileSystem, IKeyState keyState, Logger log, Action<Exception>? exceptionHandler = null,
        string label = "##FileSystemSelector", bool allowMultipleSelection = false)
    {
        FileSystem = fileSystem;
        _state = new List<StateStruct>(FileSystem.Root.TotalDescendants);
        _keyState = keyState;
        Label = label;
        AllowMultipleSelection = allowMultipleSelection;
        Log = log;

        InitDefaultContext();
        InitDefaultButtons();
        EnableFileSystemSubscription();
        ExceptionHandler = exceptionHandler ?? (e => Log.Warning(e.ToString()));
    }

    private void GetSizeInternal()
    {
        var window = ImGui.GetContentRegionAvail().X;

        var minimumButtons = ButtonCount * ImGui.GetFrameHeight();
        var minimumAbsolute = MathF.Max(minimumButtons, MinimumAbsoluteSize);

        var maximumAbsolute = MathF.Max(minimumAbsolute, window - MinimumAbsoluteRemainder);
        (_minWidth, _maxWidth) = UseScaling
            ? (MathF.Round(MathF.Max(minimumAbsolute, MinimumScaling * window)), MathF.Min(maximumAbsolute, MaximumScaling * window))
            : (minimumAbsolute, maximumAbsolute);

        _currentWidth = Math.Clamp(CurrentWidth, _minWidth, _maxWidth);
    }

    // Default flags to use for custom leaf nodes.
    protected const ImGuiTreeNodeFlags LeafFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Bullet | ImGuiTreeNodeFlags.NoTreePushOnOpen;

    // Customization point: Should always create a tree node using LeafFlags (with possible selection.)
    // But can add additional icons or buttons if wanted.
    // Everything drawn in here is wrapped in a group.
    protected virtual void DrawLeafName(FileSystem<T>.Leaf leaf, in TStateStorage state, bool selected)
    {
        var flag = selected ? ImGuiTreeNodeFlags.Selected | LeafFlags : LeafFlags;
        using var _ = ImRaii.TreeNode(leaf.Name, flag);
    }

    public void Draw()
    {
        try
        {
            DrawPopups();
            using var group = ImRaii.Group();
            GetSizeInternal();
            if (DrawList())
                DrawButtons();
        }
        catch (Exception e)
        {
            throw new Exception("Exception during FileSystemSelector rendering:\n"
              + $"{_currentIndex} Current Index\n"
              + $"{_currentDepth} Current Depth\n"
              + $"{_currentEnd} Current End\n"
              + $"{_state.Count} Current State Count\n"
              + $"{_filterDirty} Filter Dirty", e);
        }
    }

    // Select a specific leaf in the file system by its value.
    // If a corresponding leaf can be found, also expand its ancestors.
    public void SelectByValue(T value)
    {
        var leaf = FileSystem.Root.GetAllDescendants(ISortMode<T>.Lexicographical).OfType<FileSystem<T>.Leaf>()
            .FirstOrDefault(l => l.Value == value);
        if (leaf != null)
            EnqueueFsAction(() =>
            {
                _filterDirty |= ExpandAncestors(leaf);
                Select(leaf, AllowMultipleSelection, GetState(leaf));
                _jumpToSelection = leaf;
            });
    }

    public void Draw(float width)
    {
        try
        {
            DrawPopups();
            using var group = ImRaii.Group();
            width = MathF.Round(width);
            if (DrawList(width))
            {
                if (width < 0)
                    width = ImGui.GetWindowWidth() - width;
                DrawButtons(width);
            }
        }
        catch (Exception e)
        {
            throw new Exception("Exception during FileSystemSelector rendering:\n"
              + $"{_currentIndex} Current Index\n"
              + $"{_currentDepth} Current Depth\n"
              + $"{_currentEnd} Current End\n"
              + $"{_state.Count} Current State Count\n"
              + $"{_filterDirty} Filter Dirty", e);
        }
    }
    private void DrawButtons(float width)
    {
        var buttonWidth = new Vector2(width / Math.Max(_buttons.Count, 1), ImGui.GetFrameHeight());
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 0f)
            .Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        foreach (var button in _buttons)
        {
            button.Item1.Invoke(buttonWidth);
            ImGui.SameLine();
        }

        ImGui.NewLine();
    }
    // Draw the whole list.
    private bool DrawList(float width)
    {
        // Filter row is outside the child for scrolling.
        DrawFilterRow(width);

        using (var outerStyle = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
                   .Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            using var child = ImRaii.Child(Label, new Vector2(width, -ImGui.GetFrameHeight()), true);
            outerStyle.Pop(2);
            MainContext();
            if (!child)
                return false;

            ImGui.SetScrollX(0);
            _stateStorage = ImGui.GetStateStorage();
            using (var innerStyle = ImRaii.PushStyle(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale)
                       .Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, ImGuiHelpers.GlobalScale))
                       .Push(ImGuiStyleVar.FramePadding, new Vector2(ImGuiHelpers.GlobalScale, ImGui.GetStyle().FramePadding.Y)))
            {
                //// Check if filters are dirty and recompute them before the draw iteration if necessary.
                ApplyFilters();
                if (_jumpToSelection != null)
                {
                    var idx = _state.FindIndex(s => s.Path == _jumpToSelection);
                    if (idx >= 0)
                        ImGui.SetScrollFromPosY(ImGui.GetTextLineHeightWithSpacing() * idx - ImGui.GetScrollY());

                    _jumpToSelection = null;
                }

                // TODO: do this right.
                //HandleKeyNavigation();
                using (var clipper = ImUtf8.ListClipper(_state.Count, ImGui.GetTextLineHeightWithSpacing()))
                {
                    // Draw the clipped list.

                    while (clipper.Step())
                    {
                        _currentIndex = clipper.DisplayStart;
                        _currentEnd = Math.Min(_state.Count, clipper.DisplayEnd);
                        if (_currentIndex >= _currentEnd)
                            continue;

                        if (_state[_currentIndex].Depth != 0)
                            DrawPseudoFolders();
                        _currentEnd = Math.Min(_state.Count, _currentEnd);
                        for (; _currentIndex < _currentEnd; ++_currentIndex)
                            DrawStateStruct(_state[_currentIndex]);
                    }
                }
            }

            //// Handle all queued actions at the end of the iteration.
            HandleActions();
            outerStyle.Push(ImGuiStyleVar.WindowPadding, Vector2.Zero)
                .Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        }

        return true;
    }

    // Draw the default filter row of a given width.
    private void DrawFilterRow(float width)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero).Push(ImGuiStyleVar.FrameRounding, 0);
        (width, var clear) = CustomFilters(width);
        ImGui.SetNextItemWidth(width);
        var tmp = FilterValue;
        using var id = ImRaii.PushId(0, clear);
        var change = ImGui.InputTextWithHint("##Filter", "Filter...", ref tmp, 128);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && !ImGui.IsItemFocused())
        {
            try
            {
                var x = ImGui.GetClipboardText();
                if (x.Length > 0)
                {
                    tmp = x;
                    change = true;
                }
            }
            catch
            {
                // ignored
            }
        }

        if (clear)
            tmp = string.Empty;

        if (clear || change)
        {
            if (ChangeFilterInternal(tmp) && ChangeFilter(tmp))
            {
                SetFilterDirty();
            }
        }

        style.Pop();
        if (FilterTooltip.Length > 0)
            ImGuiUtil.HoverTooltip(FilterTooltip);
    }
}