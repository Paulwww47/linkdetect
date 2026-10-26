using System.ComponentModel;

namespace LinkDetect.Models;

public sealed class TokenChip(string text, int start, int length) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Text { get; } = text;

    public int Start { get; } = start;

    public int Length { get; } = length;

    public int End => Start + Length;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
