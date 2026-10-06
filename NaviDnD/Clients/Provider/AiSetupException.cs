namespace NaviDnD.Clients;

// Нейронку нельзя запустить из-за настройки (CLI не установлен, нет входа): «повтори действие» тут не поможет —
// игроку показывается сам текст ошибки, даже без отладки (GameAiClient.RecordError).
public class AiSetupException(string message) : Exception(message);
