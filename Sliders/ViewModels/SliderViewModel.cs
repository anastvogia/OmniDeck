using System.Collections.ObjectModel;
using Sliders.Models;
using Sliders.ViewModels.Base;

namespace Sliders.ViewModels;

/// <summary>
/// Represents a single slider row in the UI.
/// Bound to the slider card displaying target mapping, invert toggle, and current value.
/// </summary>
public class SliderViewModel : ObservableObject
{
    private int _sliderIndex;
    private string _mappedTarget = "";
    private bool _isInverted;
    private int _displayOrder;
    private float _currentValue;

    /// <summary>0-based hardware channel from the Arduino.</summary>
    public int SliderIndex
    {
        get => _sliderIndex;
        set => SetProperty(ref _sliderIndex, value);
    }

    /// <summary>Target process/keyword this slider controls.</summary>
    public string MappedTarget
    {
        get => _mappedTarget;
        set
        {
            if (SetProperty(ref _mappedTarget, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                Sliders.Services.Logger.Info("SliderViewModel", $"Slider CH {SliderIndex} target changed to '{_mappedTarget}' ({DisplayLabel})");
            }
        }
    }

    /// <summary>Flip the slider direction (1023 = silent, 0 = max).</summary>
    public bool IsInverted
    {
        get => _isInverted;
        set => SetProperty(ref _isInverted, value);
    }

    /// <summary>Position in the UI list (lower = higher).</summary>
    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    /// <summary>Current normalized volume level 0.0–1.0 (for the visual bar).</summary>
    public float CurrentValue
    {
        get => _currentValue;
        set
        {
            if (SetProperty(ref _currentValue, value))
                OnPropertyChanged(nameof(CurrentValuePercent));
        }
    }

    /// <summary>Percentage string for display, e.g. "75%".</summary>
    public string CurrentValuePercent => $"{(int)(CurrentValue * 100)}%";

    /// <summary>Available targets to populate the dropdown.</summary>
    public ObservableCollection<string> AvailableTargets { get; } = new()
    {
        "",               // Unmapped
        "master",
        "active_window",
        "active_not_mapped",
    };

    /// <summary>User-friendly label shown next to the slider index.</summary>
    public string DisplayLabel => MappedTarget switch
    {
        "" => "Unmapped",
        "master" => "Master Volume",
        "active_window" => "Active Window",
        "active_not_mapped" => "Active Window (Unmapped Only)",
        var name => name
    };

    /// <summary>Creates a ViewModel from a persisted config.</summary>
    public static SliderViewModel FromConfig(SliderConfig config)
    {
        return new SliderViewModel
        {
            SliderIndex = config.SliderIndex,
            MappedTarget = config.MappedTarget,
            IsInverted = config.IsInverted,
            DisplayOrder = config.DisplayOrder,
        };
    }

    /// <summary>Exports back to a persistable config.</summary>
    public SliderConfig ToConfig()
    {
        return new SliderConfig
        {
            SliderIndex = SliderIndex,
            MappedTarget = MappedTarget,
            IsInverted = IsInverted,
            DisplayOrder = DisplayOrder,
        };
    }
}
