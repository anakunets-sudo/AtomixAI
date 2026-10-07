namespace AtomixAI.UI.Infrastructure
{
    // Команда одного пункта контекстного меню (#). Диспетчер выбирает её по id пункта.
    public interface IMenuItemUiCommand : IWebViewUiCommand
    {
        bool CanHandle(string itemId);
    }
}
