using System.Collections.Generic;
using System.Linq;
using AtomixAI.Core;

namespace AtomixAI.Bridge
{
    public class RecentSelectionService
    {
        private readonly List<AtomicMenuNode> _items = new List<AtomicMenuNode>();
        private const int MaxItems = 5;

        public void Add(AtomicMenuNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) return;

            // Удаляем старый такой же (по ID), чтобы поднять его наверх
            _items.RemoveAll(x => x.Id == node.Id);

            // Вставляем в начало
            _items.Insert(0, node);

            // Ограничиваем список
            if (_items.Count > MaxItems) _items.RemoveAt(MaxItems);
        }

        public List<AtomicMenuNode> GetRecent() => _items;
    }
}