using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Demo.SharedUI.Services;

/// <summary>
/// Demo-only scoped state driving the shared layout <see cref="TmToastContainer"/> presentation from the
/// Feedback demo page: the page edits <see cref="Position"/>/<see cref="Stacked"/> here and the layout
/// container re-renders, so every toast position and the collapsed deck can be explored live without a
/// second container (one container per app — a second one would paint every toast twice). Registered
/// per circuit / per user (scoped), so two demo visitors do not steer each other's container.
/// </summary>
public sealed class DemoToastPresentationState
{
    private ToastPosition _position = ToastPosition.TopRight;
    private bool _stacked;

    /// <summary>Container position in effect. Defaults to the layout default (TopRight).</summary>
    public ToastPosition Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Whether the collapsed-deck presentation is in effect. Defaults to <c>false</c>.</summary>
    public bool Stacked
    {
        get => _stacked;
        set
        {
            if (_stacked != value)
            {
                _stacked = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Fired after a presentation change; the layout subscribes and re-renders the container.</summary>
    public event Action? Changed;
}
