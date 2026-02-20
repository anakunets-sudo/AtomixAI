using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtomixAI.Main.ViewModels
{
    public enum MessageType { User, AiInfo, AiError, AiSuccess }

    public partial class ChatMessage : ObservableObject
    {
        public string Text { get; set; } = string.Empty;
        public MessageType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [ObservableProperty]
        private int _rating = 0; // Генерирует свойство Rating

        public bool IsAi => Type != MessageType.User;

        [RelayCommand]
        private void SetRating(string score)
        {
            if (int.TryParse(score, out int s))
            {
                Rating = s; // Устанавливаем через сгенерированное свойство
            }
        }
    }
}