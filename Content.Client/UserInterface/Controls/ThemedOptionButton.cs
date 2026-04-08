using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

public sealed class ThemedOptionButton : OptionButton
{
    private readonly List<Button> _popupButtons = new();
    private StyleBoxFlat? _popupButtonStyleOverride;
    private StyleBoxFlat? _popupSelectedButtonStyleOverride;
    private Color? _popupButtonFontColorOverride;
    private Color? _popupSelectedButtonFontColorOverride;

    public StyleBoxFlat? PopupButtonStyleOverride
    {
        get => _popupButtonStyleOverride;
        set
        {
            _popupButtonStyleOverride = value;
            RefreshPopupItemTheme();
        }
    }

    public StyleBoxFlat? PopupSelectedButtonStyleOverride
    {
        get => _popupSelectedButtonStyleOverride;
        set
        {
            _popupSelectedButtonStyleOverride = value;
            RefreshPopupItemTheme();
        }
    }

    public Color? PopupButtonFontColorOverride
    {
        get => _popupButtonFontColorOverride;
        set
        {
            _popupButtonFontColorOverride = value;
            RefreshPopupItemTheme();
        }
    }

    public Color? PopupSelectedButtonFontColorOverride
    {
        get => _popupSelectedButtonFontColorOverride;
        set
        {
            _popupSelectedButtonFontColorOverride = value;
            RefreshPopupItemTheme();
        }
    }

    public override void ButtonOverride(Button button)
    {
        _popupButtons.Add(button);
        button.HorizontalExpand = true;
        button.TextAlign = Label.AlignMode.Left;
        button.ClipText = false;
        RefreshPopupItemTheme();
    }

    public new void Clear()
    {
        base.Clear();
        _popupButtons.Clear();
    }

    public new void RemoveItem(int idx)
    {
        base.RemoveItem(idx);

        if (idx >= 0 && idx < _popupButtons.Count)
            _popupButtons.RemoveAt(idx);

        RefreshPopupItemTheme();
    }

    public new void Select(int idx)
    {
        base.Select(idx);
        RefreshPopupItemTheme();
    }

    public new bool TrySelect(int idx)
    {
        var result = base.TrySelect(idx);
        if (result)
            RefreshPopupItemTheme();

        return result;
    }

    public new void SelectId(int id)
    {
        base.SelectId(id);
        RefreshPopupItemTheme();
    }

    public new bool TrySelectId(int id)
    {
        var result = base.TrySelectId(id);
        if (result)
            RefreshPopupItemTheme();

        return result;
    }

    public void RefreshPopupItemTheme()
    {
        if (_popupButtons.Count == 0)
            return;

        var selectedIndex = ItemCount > 0 ? GetIdx(SelectedId) : -1;
        for (var i = 0; i < _popupButtons.Count; i++)
        {
            ApplyPopupButtonTheme(_popupButtons[i], i == selectedIndex);
        }
    }

    private void ApplyPopupButtonTheme(Button button, bool selected)
    {
        button.StyleBoxOverride = selected
            ? _popupSelectedButtonStyleOverride ?? _popupButtonStyleOverride
            : _popupButtonStyleOverride;

        button.Label.FontColorOverride = selected
            ? _popupSelectedButtonFontColorOverride ?? _popupButtonFontColorOverride
            : _popupButtonFontColorOverride;
    }
}
