using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MyDock.App.Controls;

/// <summary>
/// A button that needs two clicks: the first arms it (pseudo-class <c>:armed</c>), the second runs the command.
/// It disarms by itself after a few seconds.
/// </summary>
public sealed class ConfirmButton : Button
{
    public static readonly DirectProperty<ConfirmButton, bool> IsArmedProperty =
        AvaloniaProperty.RegisterDirect<ConfirmButton, bool>(nameof(IsArmed), o => o.IsArmed);

    private readonly DispatcherTimer _disarmTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _isArmed;

    public ConfirmButton() => _disarmTimer.Tick += (_, _) => IsArmed = false;

    public bool IsArmed
    {
        get => _isArmed;
        private set
        {
            if (!SetAndRaise(IsArmedProperty, ref _isArmed, value))
                return;

            PseudoClasses.Set(":armed", value);
            _disarmTimer.Stop();
            if (value)
                _disarmTimer.Start();
        }
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        if (!IsArmed)
        {
            IsArmed = true;
            return;
        }

        IsArmed = false;
        base.OnClick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        IsArmed = false;
        base.OnDetachedFromVisualTree(e);
    }
}
