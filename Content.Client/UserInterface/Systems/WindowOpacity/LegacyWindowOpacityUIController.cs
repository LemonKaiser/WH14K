using System.Collections.Generic;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.WindowOpacity;

[UsedImplicitly]
public sealed class LegacyWindowOpacityUIController : UIController
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;

    private float _windowOpacity = WindowOpacityHelper.MaxUiWindowOpacity;
    private bool _pendingApply = true;

    public override void Initialize()
    {
        base.Initialize();
        _uiManager.WindowRoot.OnChildAdded += OnWindowRootChildAdded;
        _cfg.OnValueChanged(CCVars.UiWindowOpacity, OnWindowOpacityChanged, true);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_pendingApply)
            return;

        ApplyLegacyWindowOpacity();
        _pendingApply = false;
    }

    private void OnWindowRootChildAdded(Control control)
    {
        if (control is DefaultWindow)
            _pendingApply = true;
    }

    private void OnWindowOpacityChanged(float opacity)
    {
        _windowOpacity = opacity;
        _pendingApply = true;
    }

    private void ApplyLegacyWindowOpacity()
    {
        foreach (var child in _uiManager.WindowRoot.Children)
        {
            if (child is DefaultWindow window)
                ApplyWindowOpacity(window);
        }
    }

    private void ApplyWindowOpacity(DefaultWindow window)
    {
        foreach (var control in EnumerateDescendants(window))
        {
            if (control is not PanelContainer panel)
                continue;

            if (panel.StyleClasses.Contains(DefaultWindow.StyleClassWindowPanel))
            {
                WindowOpacityHelper.ApplyPanelOpacity(panel, _windowOpacity);
                continue;
            }

            if (panel.StyleClasses.Contains(DefaultWindow.StyleClassWindowHeader) ||
                panel.StyleClasses.Contains(StyleClass.AlertWindowHeader))
            {
                WindowOpacityHelper.ApplyPanelOpacity(panel, _windowOpacity);
            }
        }
    }

    private static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            for (var i = current.ChildCount - 1; i >= 0; i--)
            {
                stack.Push(current.GetChild(i));
            }
        }
    }

}
