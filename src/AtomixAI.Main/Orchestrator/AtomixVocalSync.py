import os
import json
import socket
import threading
import pyaudio
import win32file
from vosk import Model, KaldiRecognizer

# --- CONFIG ---
UDP_CMD_PORT = 5006
PIPE_NAME = r'\\.\pipe\AtomixAI_Vocal_Pipe'
# Убедись, что путь к модели верный
MODEL_PATH = os.path.join(os.path.dirname(__file__), "model", "vosk-model-small-ru-0.22")

is_listening = False  # Флаг от UDP (кнопка вкл/выкл)

def command_listener():
    """Слушает быстрые UDP команды на включение/выключение"""
    global is_listening
    cmd_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    cmd_sock.bind(("127.0.0.1", UDP_CMD_PORT))
    while True:
        data, _ = cmd_sock.recvfrom(1024)
        msg = data.decode('utf-8').strip()
        is_listening = (msg == "1")
        state_str = "RECORDING..." if is_listening else "IDLE"
        print(f"[*] Mic Status Changed: {state_str}")

def main():
    global is_listening
    
    # 1. Инициализация Pipe (ждем подключения Revit)
    print(f"[VocalSync] Ожидание подключения Revit к пайпу {PIPE_NAME}...")
    handle = None
    while not handle:
        try:
            handle = win32file.CreateFile(PIPE_NAME, 0x40000000, 0, None, 3, 0, None)
        except Exception:
            import time; time.sleep(0.5)

    # 2. Инициализация Vosk
    if not os.path.exists(MODEL_PATH):
        print(f"[ERROR] Модель не найдена по пути: {MODEL_PATH}")
        return

    model = Model(MODEL_PATH)
    rec = KaldiRecognizer(model, 16000)
    
    p = pyaudio.PyAudio()
    stream = p.open(format=pyaudio.paInt16, 
                    channels=1, 
                    rate=16000, 
                    input=True, 
                    frames_per_buffer=2000) # Оптимальный буфер для захвата

    # 3. Запуск потока команд (UDP)
    threading.Thread(target=command_listener, daemon=True).start()

    print("[VocalSync] Готов к работе. Нажмите кнопку в интерфейсе...")

    was_listening = False # Состояние на предыдущей итерации

    try:
        while True:
            if is_listening:
                # Режим записи: читаем поток и "кормим" модель
                data = stream.read(1000, exception_on_overflow=False)
                rec.AcceptWaveform(data)
                was_listening = True
            else:
                # Если только что выключили кнопку
                if was_listening:
                    # Принудительно финализируем результат БЕЗ ожидания паузы
                    result_json = json.loads(rec.Result())
                    text = result_json.get('text', '').strip()
                    
                    if text:
                        print(f"[DEBUG] Распознано: {text}")
                        # Отправляем в Revit через Named Pipe
                        try:
                            win32file.WriteFile(handle, (text + "\n").encode('utf-8'))
                        except Exception as e:
                            print(f"[ERROR] Ошибка отправки в Pipe: {e}")
                    
                    # Сброс состояния модели для новой фразы
                    rec.Reset()
                    was_listening = False
                
                # КРИТИЧНО: Очищаем системный буфер PyAudio, пока микрофон "выключен"
                # Это убирает задержку (лаг) при следующем включении
                if stream.get_read_available() > 0:
                    stream.read(stream.get_read_available(), exception_on_overflow=False)
                
                import time; time.sleep(0.05) # Не грузим CPU в простое

    except KeyboardInterrupt:
        print("[!] Остановка...")
    finally:
        stream.stop_stream()
        stream.close()
        p.terminate()

if __name__ == "__main__":
    main()