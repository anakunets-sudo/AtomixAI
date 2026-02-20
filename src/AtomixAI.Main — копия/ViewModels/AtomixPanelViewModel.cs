using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtomixAI.Main.ViewModels
{
    public partial class AtomixPanelViewModel : ObservableObject
    {
        [ObservableProperty] private string _userRequest = string.Empty;
        [ObservableProperty] private bool _isProcessing; // Для кнопки Стоп
        [ObservableProperty] private bool _isListening;  // Микрофон
        [ObservableProperty] private int _rating; // Toolkit сам создаст публичное свойство Rating (с большой буквы)

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        [RelayCommand]
        private async Task SendAsync()
        {
            if (string.IsNullOrWhiteSpace(UserRequest)) return;

            Messages.Add(new ChatMessage { Text = UserRequest, Type = MessageType.User });
            var currentQuery = UserRequest;
            UserRequest = string.Empty;
            IsProcessing = true;

            // Здесь будет вызов McpHost через Messenger или событие
            await Task.Delay(500); // Имитация
        }

        [RelayCommand]
        private void EmergencyStop()
        {
            IsProcessing = false;
            Messages.Add(new ChatMessage { Text = "Выполнение прервано", Type = MessageType.AiError });
        }

        [RelayCommand]
        private void SetRating(string score)
        {
            if (int.TryParse(score, out int s))
            {
                // Попробуйте использовать приватное поле, если Rating еще "не виден"
                _rating = s;
                // Или вызывайте сгенерированный метод уведомления (если нужно)
                OnPropertyChanged(nameof(Rating));
            }
        }

        [RelayCommand]
        private void ToggleMic() => IsListening = !IsListening;
    }
}
