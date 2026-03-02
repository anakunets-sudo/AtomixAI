import { Editor } from '@tiptap/core'; // Само "тело" редактора
import StarterKit from '@tiptap/starter-kit'; // Базовый набор (жирный, курсив, параграфы)
import Mention from '@tiptap/extension-mention'; // Основа для наших "Smart Tags"
import tippy from 'tippy.js'; // Движок для всплывающего меню (как в IntelliJ)
import Fuse from 'fuse.js'; // Движок для мгновенного поиска по Revit ID/Именам

// 1. Описываем, как работает выпадающее меню
const suggestionLogic = {
  char: '#',
  render: () => {
    let popup;
    return {
      onStart: (props) => {
        popup = tippy('body', {
          getReferenceClientRect: props.clientRect,
          content: 'Меню Revit...', // Тут будет ваш список элементов
          showOnCreate: true,
          interactive: true,
          trigger: 'manual',
        });
      },
      onExit() { popup[0].destroy(); },
    };
  },
};

// 2. Создаем сам редактор (ТО, О ЧЕМ ВЫ СПРАШИВАЛИ)
const editor = new Editor({
  element: document.querySelector('#app'), // ID блока в вашем index.html
  extensions: [
    StarterKit,
    Mention.configure({
      suggestion: suggestionLogic,
    }),
  ],
  content: '<p>Введите # для выбора элемента из Revit...</p>',
});